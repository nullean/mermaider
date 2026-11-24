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
				default:
					diagnostics.Add(new(start + 1, $"Unknown option '{word}' after animate. Expected loop, once or scrub, and calm, crisp or still."));
					break;
			}
		}

		var steps = new List<ScoreStep>();
		var tokens = new Dictionary<string, TokenDeclaration>(StringComparer.Ordinal);
		string? stepCaption = null;
		var stepLine = 0;
		var stepIndent = -1;
		List<Statement>? stepStatements = null;

		void CloseStep()
		{
			if (stepStatements is null)
				return;
			if (stepStatements.Count == 0)
				diagnostics.Add(new(stepLine, "This step is empty. Indent the statements that belong to it under the step line."));
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

			if (stepStatements is not null)
				stepStatements.Add(statement);
			else if (statement.With && steps.Count > 0 && steps[^1].Caption is null)
				((List<Statement>)steps[^1].Statements).Add(statement);
			else
				steps.Add(new ScoreStep(null, [statement], lineNo));
		}
		CloseStep();

		return new ScoreDocument(mode, motion, steps, tokens, start + 1);
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
			"tour" => ParseTour(reader, tokens),
			"run" => new RunStatement(reader.Chain(min: 2), line),
			"try" => ParseTry(reader),
			"ping" => ParsePing(reader),
			"caption" => reader.Quoted() is { } text ? new CaptionStatement(text, line) : reader.Fail("caption takes a quoted text: caption \"…\"."),
			"build" => new BuildStatement(line),
			"play" => ParsePlay(reader),
			_ =>
				reader.Fail(
					$"Unknown verb '{verb}'. The verbs are show, hide, focus, pulse, mark, draw, erase, cut, restore, send, stream, tour, run, try, ping, caption, build and play."
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
			var begin = i;
			while (i < line.Length)
			{
				var ch = line[i];
				if (
					char.IsWhiteSpace(ch) || ch is ',' or '=' or '"' || (ch == '-' && i + 1 < line.Length && line[i + 1] == '>') ||
					(ch == '<' && i + 1 < line.Length && line[i + 1] == '-')
				)
					break;
				i++;
			}
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
			if (edges.Count > 0)
				_ = Fail("This verb takes nodes or groups, not edges.");
			return targets;
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

		private static bool IsKeyword(Word word) =>
			!word.Quoted && word.Text is "each" or "close" or "ok" or "fail" or "warn" or "info" or "clear" or "success" or "failure" or "warning";
	}
}
