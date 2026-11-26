using System.Text;

namespace Mermaider.Animate.Score;

/// <summary>
/// Parses the <c>animate</c> block: a header line, then steps and statements. Layout is by indentation: a statement
/// indented deeper than the <c>step</c> above it belongs to that step; a statement outside any step is a step of its own.
/// Every problem is collected with its line, so one pass reports them all.
/// </summary>
internal static class ScoreParser
{
	/// <summary>Finds the line the <c>animate</c> block starts on (0-based), or -1 when the source has none.</summary>
	internal static int FindStart(string[] lines)
	{
		for (var i = 0; i < lines.Length; i++)
		{
			var line = lines[i];
			if (line.StartsWith("animate", StringComparison.Ordinal) && (line.Length == 7 || char.IsWhiteSpace(line[7])))
				return i;
		}
		return -1;
	}

	/// <param name="lines">The whole source split into lines.</param>
	/// <param name="start">The 0-based line the <c>animate</c> header is on.</param>
	/// <param name="diagnostics">Problems are appended here.</param>
	internal static ScoreDocument Parse(string[] lines, int start, List<AnimationDiagnostic> diagnostics)
	{
		var header = Tokenize(lines[start], start + 1, diagnostics);
		PlaybackMode? mode = null;
		MotionPreset? motion = null;
		PlayerContents? contents = null;
		for (var i = 1; i < header.Count; i++)
		{
			var word = header[i].Text;
			switch (word)
			{
				case "loop":
					mode = PlaybackMode.Loop;
					break;
				case "once":
					mode = PlaybackMode.Once;
					break;
				case "scrub":
					mode = PlaybackMode.Scrub;
					break;
				case "calm":
					motion = MotionPreset.Calm;
					break;
				case "crisp":
					motion = MotionPreset.Crisp;
					break;
				case "still":
					motion = MotionPreset.None;
					break;
				case "contents":
					contents = PlayerContents.Always;
					break;
				case "compact":
					contents = PlayerContents.Never;
					break;
				default:
					diagnostics.Add(
						new(start + 1, $"Unknown option '{word}' after animate. Expected loop, once or scrub; calm, crisp or still; and contents or compact.")
					);
					break;
			}
		}

		// steps go to the shared beginning until a scenario line, then to that scenario
		var shared = new List<ScoreStep>();
		var steps = shared;
		var scenarios = new List<ScoreScenario>();
		var tokens = new Dictionary<string, TokenDeclaration>(StringComparer.Ordinal);
		var highlights = new List<HighlightStatement>();
		var misplaced = false;
		string? stepCaption = null;
		var stepLine = 0;
		var stepIndent = -1;
		List<Statement>? stepStatements = null;

		void CloseStep()
		{
			if (stepStatements is null)
				return;
			// a step whose only line was a misplaced highlight already has its problem reported
			if (stepStatements.Count == 0 && !misplaced)
				diagnostics.Add(new(stepLine, "This step is empty. Indent the statements that belong to it under the step line."));
			misplaced = false;
			steps.Add(new ScoreStep(stepCaption, stepStatements, stepLine));
			stepStatements = null;
			stepIndent = -1;
		}

		for (var i = start + 1; i < lines.Length; i++)
		{
			var raw = lines[i];
			var lineNo = i + 1;
			var trimmed = raw.Trim();
			if (trimmed.Length == 0 || trimmed.StartsWith("%%", StringComparison.Ordinal))
				continue;

			var indent = IndentOf(raw);

			// becomes: the lines indented under it are the next version of the diagram, in the diagram's own language
			if (trimmed == "becomes" || trimmed.StartsWith("becomes ", StringComparison.Ordinal))
			{
				// becomes [A -> B, C | A, B -> C]: a split or a merge, named so the morph can show it
				IReadOnlyList<TargetRef> from = [], into = [];
				if (trimmed != "becomes")
				{
					var reader = new Reader(Tokenize(raw, lineNo, diagnostics), lineNo, diagnostics);
					_ = reader.Next();
					from = ReadNames(reader);
					if (!reader.Accept("->"))
						_ = reader.Fail("becomes names a split or a merge: becomes app -> orders, billing, or becomes api1, api2 -> api.");
					else
						into = ReadNames(reader);
					reader.ExpectEnd();
					if (from.Count > 1 && into.Count > 1)
						diagnostics.Add(new(lineNo, "becomes names one node splitting into several, or several merging into one, not both."));
				}
				var body = new List<string>();
				var first = i + 2;
				while (i + 1 < lines.Length && (lines[i + 1].Trim().Length == 0 || IndentOf(lines[i + 1]) > indent))
					body.Add(lines[++i]);
				while (body.Count > 0 && body[^1].Trim().Length == 0)
					body.RemoveAt(body.Count - 1);
				var leading = body.TakeWhile(l => l.Trim().Length == 0).Count();
				first += leading;
				body = [.. body.Skip(leading)];
				if (body.Count == 0)
				{
					diagnostics.Add(new(lineNo, "becomes needs the next version of the diagram, indented under it."));
					continue;
				}
				var margin = body.Where(l => l.Trim().Length > 0).Min(IndentOf);
				var becomes = new BecomesStatement(
					string.Join("\n", body.Select(l => l.Trim().Length == 0 ? "" : l[Math.Min(margin, l.Length - l.TrimStart().Length)..])),
					first,
					lineNo
				)
				{ From = from, Into = into, };
				if (stepStatements is not null && indent <= stepIndent)
					CloseStep();
				if (stepStatements is not null)
					stepStatements.Add(becomes);
				else
					steps.Add(new ScoreStep(null, [becomes], lineNo));
				continue;
			}

			var words = Tokenize(raw, lineNo, diagnostics);
			if (words.Count == 0)
				continue;

			if (words[0].Is("step"))
			{
				CloseStep();
				stepCaption = words.Count > 1 && words[1].Quoted ? words[1].Text : null;
				if (words.Count > 2 || (words.Count == 2 && !words[1].Quoted))
					diagnostics.Add(new(lineNo, "A step line takes one quoted caption: step \"What happens\"."));
				stepLine = lineNo;
				stepIndent = indent;
				stepStatements = [];
				continue;
			}

			if (words[0].Is("scenario"))
			{
				CloseStep();
				// scenario "Name" [as ROLE]
				var named = words.Count > 1 && words[1].Quoted && words[1].Text.Trim().Length > 0;
				var roled = words.Count == 4 && words[2].Is("as");
				if (!named || (words.Count != 2 && !roled))
					diagnostics.Add(new(lineNo, "A scenario line takes one quoted name, and how it ends if you like: scenario \"API 1 times out\" as fail."));
				var name = words.Count > 1 ? words[1].Text.Trim() : "";
				var role = roled ? ParseRole(words[3], lineNo, diagnostics) : null;
				if (scenarios.Any(s => s.Name == name))
					diagnostics.Add(new(lineNo, $"There is already a scenario called \"{name}\"."));
				steps = [];
				scenarios.Add(new ScoreScenario(name, steps, lineNo) { Role = role });
				continue;
			}

			if (words[0].Is("token"))
			{
				CloseStep();
				if (ParseToken(words, lineNo, diagnostics) is { } declaration)
				{
					if (tokens.ContainsKey(declaration.Name))
						diagnostics.Add(new(lineNo, $"Token '{declaration.Name}' is declared twice."));
					tokens[declaration.Name] = declaration;
				}
				continue;
			}

			if (stepStatements is not null && indent <= stepIndent)
				CloseStep();

			var statement = ParseStatement(words, lineNo, tokens, diagnostics);
			if (statement is null)
				continue;

			// a highlight holds for the whole animation, so it belongs to the score, not to a step
			if (statement is HighlightStatement highlight)
			{
				if (stepStatements is not null)
				{
					diagnostics.Add(new(lineNo, "highlight holds for the whole animation: write it before the steps, not inside one."));
					misplaced = true;
				}
				else
					highlights.Add(highlight);
				continue;
			}

			if (stepStatements is not null)
				stepStatements.Add(statement);
			else if (statement.With && steps.Count > 0 && steps[^1].Caption is null)
				steps[^1] = steps[^1] with { Statements = [.. steps[^1].Statements, statement] };
			else
				steps.Add(new ScoreStep(null, [statement], lineNo));
		}
		CloseStep();

		foreach (var scenario in scenarios.Where(s => s.Steps.Count == 0))
			diagnostics.Add(new(scenario.Line, $"The scenario \"{scenario.Name}\" has no steps. Indent its steps under it, or write them after it."));
		if (scenarios.Count == 1)
			diagnostics.Add(new(scenarios[0].Line, "A score with one scenario tells one story: give it a second scenario, or write its steps without one."));
		return new ScoreDocument(mode, motion, shared, tokens, start + 1) { Scenarios = scenarios, Contents = contents, Highlights = highlights };
	}

	/// <summary>Node names separated by commas, by id or by label in quotes; stops at '->' or the end.</summary>
	private static List<TargetRef> ReadNames(Reader reader)
	{
		var names = new List<TargetRef>();
		do
		{
			if (reader.Peek(0) is { Quoted: true })
				names.Add(new TargetRef(reader.Quoted()!, IsGroup: false, reader.Line) { ByLabel = true });
			else if (reader.Id() is { } id)
				names.Add(new TargetRef(id, IsGroup: false, reader.Line));
			else
			{
				_ = reader.Fail("Expected a node name.");
				break;
			}
		}
		while (reader.Accept(","));
		return names;
	}

	private static int IndentOf(string line)
	{
		var width = 0;
		foreach (var c in line)
		{
			if (c == ' ')
				width++;
			else if (c == '\t')
				width += 4;
			else
				break;
		}
		return width;
	}

	private static TokenDeclaration? ParseToken(List<Word> words, int line, List<AnimationDiagnostic> diagnostics)
	{
		// token NAME = "label" [as ROLE]
		if (words.Count < 4 || words[1].Quoted || !words[2].Is("=") || !words[3].Quoted)
		{
			diagnostics.Add(new(line, "Declare a token as: token NAME = \"label\" [as ok|fail|warn|info]."));
			return null;
		}
		Role? role = null;
		if (words.Count >= 6 && words[4].Is("as"))
			role = ParseRole(words[5], line, diagnostics);
		else if (words.Count > 4)
			diagnostics.Add(new(line, $"Unexpected '{words[4].Text}' after the token's label."));
		return new TokenDeclaration(words[1].Text, words[3].Text, role, line);
	}

	private static Statement? ParseStatement(List<Word> words, int line, Dictionary<string, TokenDeclaration> tokens, List<AnimationDiagnostic> diagnostics)
	{
		var with = false;
		if (words[0].Is("with"))
		{
			with = true;
			words.RemoveAt(0);
			if (words.Count == 0)
			{
				diagnostics.Add(new(line, "'with' needs a statement after it, as in: with pulse api."));
				return null;
			}
		}

		var reader = new Reader(words, line, diagnostics);
		var verb = reader.Next()!.Text;
		Statement? statement = verb switch
		{
			"show" => ParseShowHide(reader, show: true),
			"hide" => ParseShowHide(reader, show: false),
			"focus" => ParseFocus(reader),
			"pulse" => ParsePulse(reader),
			"mark" => ParseMark(reader),
			"draw" => ParseDraw(reader),
			"erase" => ParseErase(reader),
			"cut" => new CutStatement(reader.Edges(), line),
			"restore" => new RestoreStatement(reader.Edges(), line),
			"send" => ParseSend(reader, tokens),
			"stream" => ParseStream(reader),
			"highlight" => ParseHighlight(reader),
			"tour" => ParseTour(reader, tokens),
			"run" => new RunStatement(reader.Chain(min: 2), line),
			"try" => ParseTry(reader),
			"ping" => ParsePing(reader),
			"caption" => reader.Quoted() is { } text ? new CaptionStatement(text, line) : reader.Fail("caption takes a quoted text: caption \"…\"."),
			"build" => new BuildStatement(line),
			"play" => ParsePlay(reader),
			"fill" => ParseFill(reader),
			"sweep" => ParseSweep(reader),
			"move" => ParseMove(reader),
			"set" => ParseSet(reader),
			"trace" => new TraceStatement(reader.Chain(min: 2), line),
			"expand" => new ExpandStatement(reader.Targets(), line),
			"badge" => ParseBadge(reader),
			"count" => ParseCount(reader),
			"hang" => new HangStatement(reader.Targets(), line),
			"note" => ParseNote(reader),
			"callout" => ParseCallout(reader),
			"fail" => new FailStatement(reader.Targets(), reader.Quoted(), line),
			"recover" => new RecoverStatement(reader.Targets(), line),
			"ghost" => ParseGhost(reader),
			"group" => ParseGroup(reader),
			"because" => ParseBecause(reader),
			"retry" => ParseRetry(reader),
			"scale" => ParseScale(reader),
			"cascade" => new CascadeStatement(reader.Chain(min: 2), reader.Quoted(), line),
			_ =>
				reader.Fail(
					$"Unknown verb '{verb}'. The verbs are show, hide, focus, pulse, mark, badge, fill, hang, draw, erase, cut, restore, send, stream, tour, run, try, ping, play, sweep, move, expand, set, trace, note, callout, fail, recover, cascade, ghost, group, because, retry, scale, count, highlight, caption and build."
				),
		};
		if (statement is null)
			return null;
		reader.ExpectEnd();
		return statement with { With = with };
	}

	private static Statement? ParseShowHide(Reader reader, bool show)
	{
		var (targets, edges) = reader.TargetsOrEdges();
		var each = reader.Flag("each");
		return show ? new ShowStatement(targets, edges, each, reader.Line) : new HideStatement(targets, edges, each, reader.Line);
	}

	private static Statement? ParseFocus(Reader reader)
	{
		if (reader.Flag("all"))
			return new FocusStatement([], Close: false, All: true, reader.Line);
		var targets = reader.Targets();
		var close = reader.Flag("close");
		return new FocusStatement(targets, close, All: false, reader.Line);
	}

	private static Statement? ParsePulse(Reader reader)
	{
		var targets = reader.Targets();
		return new PulseStatement(targets, reader.Flag("each"), reader.Line);
	}

	private static Statement? ParseMark(Reader reader)
	{
		var (targets, edges) = reader.TargetsOrEdges();
		var word = reader.Next();
		if (word is null)
			return reader.Fail("mark needs a role after its targets: ok, fail, warn, info or clear.");
		if (word.Is("clear"))
			return new MarkStatement(targets, edges, null, reader.Line);
		var role = ParseRole(word, reader.Line, reader.Diagnostics);
		return role is null ? null : new MarkStatement(targets, edges, role, reader.Line);
	}

	private static Statement? ParseDraw(Reader reader)
	{
		var edges = reader.Edges();
		return new DrawStatement(edges, reader.Flag("each"), reader.Line);
	}

	private static Statement? ParseErase(Reader reader)
	{
		var edges = reader.Edges();
		return new EraseStatement(edges, reader.Flag("each"), reader.Line);
	}

	private static Statement? ParseSend(Reader reader, Dictionary<string, TokenDeclaration> tokens)
	{
		// send [TOKEN] A -> B[, C …] ["label"] [as ROLE] [becomes "label"] [slow|fast] [lost|stall] [each]
		string? token = null;
		if (reader.Peek(0) is { Quoted: false } first && reader.Peek(1) is { Quoted: false } second && !second.Is("->") && reader.Peek(2)?.Is("->") == true)
		{
			token = first.Text;
			reader.Next();
			if (!tokens.ContainsKey(token))
				reader.Diagnostics.Add(new(reader.Line, $"Token '{token}' is not declared. Declare it first: token {token} = \"label\"."));
		}
		var from = reader.Id();
		if (from is null || !reader.Expect("->"))
			return reader.Fail("send needs a route: send A -> B.");
		var to = new List<string>();
		do
		{
			if (reader.Id() is { } id)
				to.Add(id);
			else
				return reader.Fail("send needs a target after '->'.");
		}
		while (reader.Accept(","));

		string? label = null, becomes = null;
		Role? role = null;
		var speed = Speed.Normal;
		var fate = Fate.Arrives;
		var each = false;
		while (reader.Peek(0) is { } word)
		{
			if (word.Quoted && label is null)
			{
				label = word.Text;
				reader.Next();
			}
			else if (word.Is("as"))
			{
				reader.Next();
				role = reader.Next() is { } r ? ParseRole(r, reader.Line, reader.Diagnostics) : null;
			}
			else if (word.Is("becomes"))
			{
				reader.Next();
				becomes = reader.Quoted();
				if (becomes is null)
					return reader.Fail("becomes takes a quoted label: becomes \"…\".");
			}
			else if (word.Is("slow") || word.Is("fast"))
			{
				speed = word.Is("slow") ? Speed.Slow : Speed.Fast;
				reader.Next();
			}
			else if (word.Is("lost") || word.Is("stall"))
			{
				fate = word.Is("lost") ? Fate.Lost : Fate.Stalls;
				reader.Next();
			}
			else if (word.Is("each"))
			{
				each = true;
				reader.Next();
			}
			else
				break;
		}
		return new SendStatement(token, from, to, label, role, becomes, speed, fate, each, reader.Line);
	}

	private static Statement? ParseHighlight(Reader reader)
	{
		// highlight path A -> B -> C [as ROLE]  |  highlight flow A -> B -> C  |  highlight A[, B] [as ROLE]
		var kind = reader.Accept("path") ? HighlightKind.Path : reader.Accept("flow") ? HighlightKind.Flow : HighlightKind.Node;
		IReadOnlyList<TargetRef> targets = [];
		IReadOnlyList<string> route = [];
		// highlight biggest | lowest: the mark the data picks (the biggest slice, the lowest score)
		var pick = kind == HighlightKind.Node ? reader.Accept("biggest") ? "biggest" : reader.Accept("lowest") ? "lowest" : null : null;
		if (pick is not null)
		{
			Role? picked = null;
			if (reader.Accept("as"))
				picked = reader.Next() is { } first ? ParseRole(first, reader.Line, reader.Diagnostics) : null;
			return new HighlightStatement(kind, [], [], picked, reader.Line) { Pick = pick };
		}
		if (kind == HighlightKind.Node)
		{
			targets = reader.Targets();
			if (targets.Count == 0)
				return reader.Fail("highlight takes nodes, or a route: highlight api as fail, highlight path a -> b -> c, highlight flow a -> b.");
		}
		else
			route = reader.Chain(min: 2);
		Role? role = null;
		if (reader.Accept("as"))
			role = reader.Next() is { } r ? ParseRole(r, reader.Line, reader.Diagnostics) : null;
		return new HighlightStatement(kind, targets, route, role, reader.Line);
	}

	private static Statement? ParseStream(Reader reader)
	{
		var path = reader.Chain(min: 2);
		var rate = StreamRate.Steady;
		var always = false;
		while (reader.Peek(0) is { Quoted: false } word)
		{
			if (word.Is("trickle"))
				rate = StreamRate.Trickle;
			else if (word.Is("steady"))
				rate = StreamRate.Steady;
			else if (word.Is("busy"))
				rate = StreamRate.Busy;
			else if (word.Is("always"))
				always = true;
			else
				break;
			reader.Next();
		}
		return new StreamStatement(path, rate, always, reader.Line);
	}

	private static Statement? ParseTour(Reader reader, Dictionary<string, TokenDeclaration> tokens)
	{
		string? token = null;
		if (reader.Peek(0) is { Quoted: false } first && reader.Peek(1) is { Quoted: false } second && !second.Is("->") && tokens.ContainsKey(first.Text))
		{
			token = first.Text;
			reader.Next();
		}
		var path = reader.Chain(min: 2);
		var label = reader.Quoted();
		Role? role = null;
		if (reader.Accept("as"))
			role = reader.Next() is { } r ? ParseRole(r, reader.Line, reader.Diagnostics) : null;
		return new TourStatement(token, path, label, role, reader.Line);
	}

	private static Statement? ParseTry(Reader reader)
	{
		var from = reader.Id();
		if (from is null || !reader.Expect("->") || reader.Id() is not { } to)
			return reader.Fail("try needs a transition: try A -> B rejected \"reason\".");
		_ = reader.Accept("rejected");
		return new TryStatement(from, to, reader.Quoted(), reader.Line);
	}

	private static Statement? ParsePing(Reader reader)
	{
		var from = reader.Id();
		if (from is null || !(reader.Accept("<->") || reader.Accept("->")) || reader.Id() is not { } to)
			return reader.Fail("ping needs two ends: ping A <-> B \"request\" \"reply\".");
		return new PingStatement(from, to, reader.Quoted(), reader.Quoted(), reader.Line);
	}

	private static Statement? ParsePlay(Reader reader)
	{
		int? first = null, last = null;
		if (reader.Peek(0) is { Quoted: false } word && word.Text.StartsWith('#'))
		{
			reader.Next();
			var text = word.Text[1..];
			var range = text.IndexOf("..", StringComparison.Ordinal);
			if (range >= 0)
			{
				first = ParseIndex(text[..range], reader);
				last = ParseIndex(text[(range + 2)..].TrimStart('#'), reader);
			}
			else
				first = last = ParseIndex(text, reader);
		}
		return new PlayStatement(first, last, reader.Line);
	}

	private static Statement? ParseFill(Reader reader)
	{
		// fill TARGET COUNT[/CAPACITY]
		var id = reader.Id();
		var amount = reader.Peek(0) is { Quoted: false } word ? reader.Next()!.Text : null;
		if (id is null || amount is null)
			return reader.Fail("fill takes a node and an amount: fill topic 3/8, or fill topic 5 after its capacity is known.");
		var slash = amount.IndexOf('/');
		var countText = slash >= 0 ? amount[..slash] : amount;
		var capacityText = slash >= 0 ? amount[(slash + 1)..] : null;
		if (
			!int.TryParse(countText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count) ||
			(capacityText is not null &&
				(!int.TryParse(capacityText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var cap) || cap < 1))
		)
			return reader.Fail($"'{amount}' is not an amount. Write a count, or count/capacity: 3/8.");
		int? capacity = capacityText is null ? null : int.Parse(capacityText, System.Globalization.CultureInfo.InvariantCulture);
		if (capacity is { } c && count > c)
			return reader.Fail($"{count} is more than the capacity of {c}.");
		return new FillStatement(new TargetRef(id, IsGroup: false, reader.Line), count, capacity, reader.Line);
	}

	private static Statement? ParseSet(Reader reader)
	{
		// set BAR VALUE: a bar named #2, by id, or by label
		var targets = reader.Targets();
		var number = reader.Peek(0) is { Quoted: false } word ? reader.Next()!.Text : null;
		if (
			targets.Count != 1 || number is null ||
			!double.TryParse(number, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
		)
			return reader.Fail("set takes one bar and a number: set #2 75.");
		return new SetStatement(targets[0], value, reader.Line);
	}

	private static Statement? ParseMove(Reader reader)
	{
		// move A[, B] to COLUMN [each]
		var targets = new List<TargetRef>();
		do
		{
			if (reader.Peek(0) is { Quoted: true })
				targets.Add(new TargetRef(reader.Quoted()!, false, reader.Line) { ByLabel = true });
			else if (reader.Id() is { } id && id != "to")
				targets.Add(new TargetRef(id, false, reader.Line));
			else
				return reader.Fail("move takes cards, then to and a column: move t1 to Done.");
		}
		while (reader.Accept(","));
		if (!reader.Accept("to") || (reader.Quoted() ?? reader.Id()) is not { } column)
			return reader.Fail("move takes cards, then to and a column: move t1 to Done.");
		return new MoveStatement(targets, column, reader.Flag("each"), reader.Line);
	}

	private static Statement? ParseSweep(Reader reader)
	{
		// sweep [to "date"]
		if (!reader.Accept("to"))
			return new SweepStatement(null, reader.Line);
		var text = reader.Quoted();
		if (text is null || !DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date))
			return reader.Fail("sweep to takes a quoted date: sweep to \"2024-03-01\".");
		return new SweepStatement(date, reader.Line);
	}

	private static Statement? ParseBadge(Reader reader)
	{
		// badge A[, B] "text" [as ROLE]  |  badge A clear
		var targets = new List<TargetRef>();
		do
		{
			// a label in quotes names a target when the badge text, clear or another target follows it
			if (
				reader.Peek(0) is { Quoted: true } &&
				(reader.Peek(1) is { Quoted: true } || reader.Peek(1)?.Is("clear") == true || reader.Peek(1)?.Is(",") == true)
			)
				targets.Add(new TargetRef(reader.Quoted()!, IsGroup: false, reader.Line) { ByLabel = true });
			else if (reader.Id() is { } id && id != "clear")
				targets.Add(new TargetRef(id, IsGroup: false, reader.Line));
			else
				return reader.Fail("badge takes nodes, then a quoted text or clear: badge api \"timeout\" as fail.");
		}
		while (reader.Accept(","));
		if (reader.Accept("clear"))
			return new BadgeStatement(targets, null, null, reader.Line);
		var text = reader.Quoted();
		if (text is null)
			return reader.Fail("badge needs a quoted text after its nodes, or clear.");
		Role? role = null;
		if (reader.Accept("as"))
			role = reader.Next() is { } r ? ParseRole(r, reader.Line, reader.Diagnostics) : null;
		return new BadgeStatement(targets, text, role, reader.Line);
	}

	private static Statement? ParseGhost(Reader reader)
	{
		// ghost A[, B -> C] [clear]
		var (targets, edges) = reader.TargetsOrEdges();
		return new GhostStatement(targets, edges, reader.Accept("clear"), reader.Line);
	}

	private static Statement? ParseGroup(Reader reader)
	{
		// group A, B "Label" [as ROLE]
		var targets = new List<TargetRef>();
		do
		{
			if (reader.Peek(0) is { Quoted: true } && (reader.Peek(1) is { Quoted: true } || reader.Peek(1)?.Is(",") == true))
				targets.Add(new TargetRef(reader.Quoted()!, IsGroup: false, reader.Line) { ByLabel = true });
			else if (reader.Peek(0) is { Quoted: false } && reader.Id() is { } id)
				targets.Add(new TargetRef(id, IsGroup: false, reader.Line));
			else
				break;
		}
		while (reader.Accept(","));
		var label = reader.Quoted();
		if (targets.Count == 0 || label is null)
			return reader.Fail("group takes nodes and a quoted label: group api, db \"Payments\".");
		Role? role = null;
		if (reader.Accept("as"))
			role = reader.Next() is { } r ? ParseRole(r, reader.Line, reader.Diagnostics) : null;
		return new GroupStatement(targets, label, role, reader.Line);
	}

	private static Statement? ParseBecause(Reader reader)
	{
		// because A -> B "reason" [as ROLE]
		const string usage = "because takes a cause, an effect and the reason: because db -> api \"pool exhausted\".";
		var chain = reader.Chain(min: 2);
		if (chain.Count != 2)
			return reader.Fail(usage);
		var reason = reader.Quoted();
		if (reason is null)
			return reader.Fail(usage);
		Role? role = null;
		if (reader.Accept("as"))
			role = reader.Next() is { } r ? ParseRole(r, reader.Line, reader.Diagnostics) : null;
		return new BecauseStatement(chain[0], chain[1], reason, role, reader.Line);
	}

	private static Statement? ParseRetry(Reader reader)
	{
		// retry A -> B N [backoff] [succeeds|fails]
		const string usage = "retry takes an edge and how many attempts: retry lb -> api 3 backoff succeeds.";
		var chain = reader.Chain(min: 2);
		if (chain.Count != 2)
			return reader.Fail(usage);
		if (Count(reader.Next()) is not { } attempts)
			return reader.Fail("retry makes 1 to 9 attempts: retry lb -> api 3.");
		var backoff = reader.Accept("backoff");
		var succeeds = reader.Accept("succeeds");
		if (!succeeds)
			_ = reader.Accept("fails");
		return new RetryStatement(chain[0], chain[1], attempts, backoff, succeeds, reader.Line);
	}

	private static Statement? ParseScale(Reader reader)
	{
		// scale A to N
		TargetRef? target = reader.Peek(0) is { Quoted: true }
			? new TargetRef(reader.Quoted()!, IsGroup: false, reader.Line) { ByLabel = true }
			: reader.Id() is { } id ? new TargetRef(id, IsGroup: false, reader.Line) : null;
		if (target is null || !reader.Accept("to") || Count(reader.Next()) is not { } count)
			return reader.Fail("scale takes a node and 1 to 9 replicas: scale api to 3, and scale api to 1 to fold them back.");
		return new ScaleStatement(target, count, reader.Line);
	}

	/// <summary>A small whole number, 1 to 9: attempts, replicas.</summary>
	private static int? Count(Word? word) =>
		word is { Quoted: false } && int.TryParse(
			word.Text,
			System.Globalization.NumberStyles.None,
			System.Globalization.CultureInfo.InvariantCulture,
			out var n
		) && n is >= 1 and <= 9 ? n : null;

	private static Statement? ParseCallout(Reader reader)
	{
		// callout A "text" [as ROLE]  |  callout "Label" "text"  |  callout A -> B "text"  |  callout #3 "text"
		const string usage = "callout takes one node or edge, then a quoted text: callout api \"this is where it times out\".";
		var (targets, edges) = reader.TargetsOrEdges();
		if (targets.Count + edges.Count != 1 || targets.Any(t => t.IsGroup))
			return reader.Fail(usage);
		var text = reader.Quoted();
		if (text is null)
			return reader.Fail(usage);
		Role? role = null;
		if (reader.Accept("as"))
			role = reader.Next() is { } r ? ParseRole(r, reader.Line, reader.Diagnostics) : null;
		return new CalloutStatement(targets.FirstOrDefault(), edges.FirstOrDefault(), text, role, reader.Line);
	}

	private static Statement? ParseCount(Reader reader)
	{
		// count A [FROM ->] TO ["unit"] [as ROLE]
		const string usage = "count takes a node and a number, from another if given: count api 38 -> 1200 \"ms\".";
		TargetRef? target = reader.Peek(0) is { Quoted: true }
			? new TargetRef(reader.Quoted()!, IsGroup: false, reader.Line) { ByLabel = true }
			: reader.Id() is { } id ? new TargetRef(id, IsGroup: false, reader.Line) : null;
		if (target is null || Number(reader) is not { } first)
			return reader.Fail(usage);
		double? from = null;
		var to = first;
		if (reader.Accept("->"))
		{
			if (Number(reader) is not { } second)
				return reader.Fail(usage);
			(from, to) = (first, second);
		}
		var unit = reader.Peek(0) is { Quoted: true } ? reader.Quoted() : null;
		Role? role = null;
		if (reader.Accept("as"))
			role = reader.Next() is { } r ? ParseRole(r, reader.Line, reader.Diagnostics) : null;
		return new CountStatement(target, from, to, unit, role, reader.Line);
	}

	private static double? Number(Reader reader) =>
		reader.Peek(0) is { Quoted: false } word && double.TryParse(
			word.Text,
			System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture,
			out var value
		) && !double.IsNaN(value) && !double.IsInfinity(value) ? (reader.Next() is not null ? value : null) : null;

	private static Statement? ParseNote(Reader reader)
	{
		// note #FIRST[..#LAST] "text" [as ROLE]
		if (reader.Peek(0) is not { Quoted: false } word || !word.Text.StartsWith('#'))
			return reader.Fail("note takes a message range and a text: note #4..#5 \"38 ms\".");
		_ = reader.Next();
		var text = word.Text[1..];
		var range = text.IndexOf("..", StringComparison.Ordinal);
		var first = ParseIndex(range >= 0 ? text[..range] : text, reader);
		var last = range >= 0 ? ParseIndex(text[(range + 2)..].TrimStart('#'), reader) : first;
		var label = reader.Quoted();
		if (first is null || last is null || label is null)
			return reader.Fail("note takes a message range and a text: note #4..#5 \"38 ms\".");
		Role? role = null;
		if (reader.Accept("as"))
			role = reader.Next() is { } r ? ParseRole(r, reader.Line, reader.Diagnostics) : null;
		return new NoteStatement(Math.Min(first.Value, last.Value), Math.Max(first.Value, last.Value), label, role, reader.Line);
	}

	private static int? ParseIndex(string text, Reader reader)
	{
		if (int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= 1)
			return value;
		reader.Diagnostics.Add(new(reader.Line, $"'#{text}' is not a message number. Messages count from #1."));
		return null;
	}

	private static Role? ParseRole(Word word, int line, List<AnimationDiagnostic> diagnostics)
	{
		switch (word.Text)
		{
			case "ok":
			case "success":
				return Role.Ok;
			case "fail":
			case "failure":
				return Role.Fail;
			case "warn":
			case "warning":
				return Role.Warn;
			case "info":
				return Role.Info;
			default:
				diagnostics.Add(new(line, $"Unknown role '{word.Text}'. The roles are ok, fail, warn and info."));
				return null;
		}
	}

	internal sealed record Word(string Text, bool Quoted)
	{
		public bool Is(string text) => !Quoted && Text == text;
	}

	/// <summary>Splits a line into words, quoted strings and the punctuation <c>-&gt;</c>, <c>&lt;-&gt;</c>, <c>,</c> and <c>=</c>.</summary>
	internal static List<Word> Tokenize(string line, int lineNo, List<AnimationDiagnostic> diagnostics)
	{
		var words = new List<Word>();
		var i = 0;
		while (i < line.Length)
		{
			var c = line[i];
			if (char.IsWhiteSpace(c))
			{
				i++;
				continue;
			}
			if (c == '%' && i + 1 < line.Length && line[i + 1] == '%')
				break;
			if (c == '"')
			{
				var sb = new StringBuilder();
				i++;
				var closed = false;
				while (i < line.Length)
				{
					if (line[i] == '\\' && i + 1 < line.Length)
					{
						_ = sb.Append(line[i + 1]);
						i += 2;
						continue;
					}
					if (line[i] == '"')
					{
						closed = true;
						i++;
						break;
					}
					_ = sb.Append(line[i]);
					i++;
				}
				if (!closed)
					diagnostics.Add(new(lineNo, "A quoted text is not closed."));
				words.Add(new Word(sb.ToString(), Quoted: true));
				continue;
			}
			if (c == '<' && i + 2 < line.Length && line[i + 1] == '-' && line[i + 2] == '>')
			{
				words.Add(new Word("<->", false));
				i += 3;
				continue;
			}
			if (c == '-' && i + 1 < line.Length && line[i + 1] == '>')
			{
				words.Add(new Word("->", false));
				i += 2;
				continue;
			}
			if (c is ',' or '=')
			{
				words.Add(new Word(c.ToString(), false));
				i++;
				continue;
			}
			// a word runs to the next space or token. It ends before "<->" only, which the branch above takes: a lone "<-" stays
			// part of a word (a name the reader then reports), never a word of no characters, which would never end
			var begin = i;
			while (i < line.Length)
			{
				var ch = line[i];
				if (
					char.IsWhiteSpace(ch) || ch is ',' or '=' or '"' || (ch == '-' && i + 1 < line.Length && line[i + 1] == '>') ||
					(ch == '<' && i + 2 < line.Length && line[i + 1] == '-' && line[i + 2] == '>')
				)
					break;
				i++;
			}
			// whatever the rules above say, every word takes a character, so no line can keep the tokenizer in place
			if (i == begin)
				i++;
			words.Add(new Word(line[begin..i], false));
		}
		return words;
	}

	/// <summary>A cursor over a statement's words with helpers for the shapes the verbs share.</summary>
	private sealed class Reader(List<Word> words, int line, List<AnimationDiagnostic> diagnostics)
	{
		private int _position;
		private bool _failed;

		public int Line { get; } = line;
		public List<AnimationDiagnostic> Diagnostics { get; } = diagnostics;

		public Word? Peek(int offset) => _position + offset < words.Count ? words[_position + offset] : null;

		public Word? Next() => _position < words.Count ? words[_position++] : null;

		public bool Accept(string text)
		{
			if (Peek(0)?.Is(text) != true)
				return false;
			_position++;
			return true;
		}

		public bool Flag(string text) => Accept(text);

		public bool Expect(string text)
		{
			if (Accept(text))
				return true;
			_ = Fail($"Expected '{text}' here.");
			return false;
		}

		public string? Quoted() => Peek(0) is { Quoted: true } word ? Next()!.Text : null;

		public string? Id()
		{
			if (Peek(0) is { Quoted: false } word && word.Text is not ("->" or "<->" or "," or "="))
			{
				_position++;
				return word.Text;
			}
			return null;
		}

		public Statement? Fail(string message)
		{
			if (!_failed)
				Diagnostics.Add(new(Line, message));
			_failed = true;
			return null;
		}

		public void ExpectEnd()
		{
			if (!_failed && Peek(0) is { } extra)
				Diagnostics.Add(new(Line, $"Unexpected '{extra.Text}' at the end of the statement."));
		}

		/// <summary>A → B → C …</summary>
		public List<string> Chain(int min)
		{
			var path = new List<string>();
			if (Id() is { } first)
				path.Add(first);
			while (Accept("->"))
			{
				if (Id() is { } next)
					path.Add(next);
				else
				{
					_ = Fail("Expected a name after '->'.");
					break;
				}
			}
			if (path.Count < min)
				_ = Fail("Expected a route of at least two names joined by '->'.");
			return path;
		}

		public List<TargetRef> Targets()
		{
			var (targets, edges) = TargetsOrEdges();
			if (edges.Any(e => !e.IsMessage))
				_ = Fail("This verb takes nodes or groups, not edges.");
			// #n here names the n-th element: a gantt task, a commit, a timeline period
			return [.. targets, .. edges.Where(e => e.IsMessage).Select(e => new TargetRef($"#{e.Index}", false, Line))];
		}

		public List<EdgeRef> Edges()
		{
			var (targets, edges) = TargetsOrEdges();
			if (targets.Count > 0)
				_ = Fail($"This verb takes edges (A -> B), but '{targets[0]}' is a node.");
			return edges;
		}

		/// <summary>A comma-separated list of ids, <c>group</c> ids and edges; an edge chain A -> B -> C names two edges.</summary>
		public (List<TargetRef> Targets, List<EdgeRef> Edges) TargetsOrEdges()
		{
			var targets = new List<TargetRef>();
			var edges = new List<EdgeRef>();
			do
			{
				if (Accept("group"))
				{
					if (Id() is { } groupId)
						targets.Add(new TargetRef(groupId, IsGroup: true, Line));
					else
						_ = Fail("Expected a group id after 'group'.");
					continue;
				}
				if (Peek(0) is { } word && IsKeyword(word))
					break;
				if (Peek(0) is { Quoted: true } labelled)
				{
					_position++;
					targets.Add(new TargetRef(labelled.Text, IsGroup: false, Line) { ByLabel = true });
					continue;
				}
				if (Peek(0) is { Quoted: false } message && message.Text.StartsWith('#'))
				{
					_position++;
					var body = message.Text[1..];
					var dots = body.IndexOf("..", StringComparison.Ordinal);
					var first = Number(dots >= 0 ? body[..dots] : body);
					var last = dots >= 0 ? Number(body[(dots + 2)..].TrimStart('#')) : first;
					if (first is null || last is null)
						_ = Fail($"'{message.Text}' is not a message number. Messages count from #1.");
					else
						for (var n = Math.Min(first.Value, last.Value); n <= Math.Max(first.Value, last.Value); n++)
							edges.Add(EdgeRef.Message(n, Line));
					continue;
				}
				var id = Id();
				if (id is null)
				{
					_ = Fail("Expected a name here.");
					break;
				}
				if (Peek(0)?.Is("->") != true)
				{
					targets.Add(new TargetRef(id, IsGroup: false, Line));
					continue;
				}
				var from = id;
				while (Accept("->"))
				{
					if (Id() is not { } to)
					{
						_ = Fail("Expected a name after '->'.");
						break;
					}
					int? index = null;
					if (
						Peek(0) is { Quoted: false } maybeIndex && maybeIndex.Text.StartsWith('#') && int.TryParse(
							Digits(maybeIndex.Text),
							System.Globalization.NumberStyles.None,
							System.Globalization.CultureInfo.InvariantCulture,
							out var n
						) && n >= 1
					)
					{
						index = n;
						_position++;
					}
					edges.Add(new EdgeRef(from, to, index, Line));
					from = to;
				}
			}
			while (Accept(","));
			if (targets.Count == 0 && edges.Count == 0)
				_ = Fail("This verb needs at least one target.");
			return (targets, edges);
		}

		private static string Digits(string text) => text[1..];

		private static int? Number(string text) =>
			int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 1 ? n : null;

		private static bool IsKeyword(Word word) =>
			!word.Quoted && word.Text is "each" or "close" or "ok" or "fail" or "warn" or "info" or "clear" or "success" or "failure" or "warning";
	}
}
