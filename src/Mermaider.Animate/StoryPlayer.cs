using System.Globalization;
using System.Net;
using System.Text;
using Mermaider.Animate.Score;

namespace Mermaider.Animate;

/// <summary>
/// The player: the animated diagram as a figure, what is happening now as its caption, and beside it the story's
/// contents, drawn in the diagram's own design language. No script: every control is a label for a radio, and
/// <c>:has()</c> applies the state.
/// <list type="bullet">
/// <item>The contents are a story map: the steps on a trunk, lit in the accent as they play, the current one ringed as
/// <c>run</c> rings a state. A score with endings forks: each ending is a node in its role's colours with the mark icon,
/// and the chosen one opens on its own branch. The list keeps its height however long the story: past nine rows it is a
/// window that follows the playback.</item>
/// <item>Each ending is a unit of its own (its diagram, caption and contents), shown while its radio is checked; showing
/// one starts it afresh, shared beginning included.</item>
/// <item>A unit's radio group holds its state: play, pause, and playing or holding from a step. Clicking a step in the
/// contents jumps there and plays on: a negative delay puts every animation, in the SVG and beside it, at that step.</item>
/// <item>Below 640px the contents fold into one line under the caption, which opens them in place.</item>
/// </list>
/// The caption and the contents animate on the story's own clock, so they never drift from the diagram.
/// </summary>
internal static class StoryPlayer
{
	// one rhythm: every row is 32px; an ending is a 28px box centred in its row
	private const double RowHeight = 32;
	private const double EndingHeight = 32;
	private const int WindowRows = 9;

	/// <summary>One ending (or the only story): its animation, name and role.</summary>
	private sealed record Ending(AnimatedDiagram Animation, string? Name, Role? Role, int Shared);

	internal static string Render(AnimatedDiagram animation)
	{
		var endings = animation.Scenarios.Count > 1
			? animation.Scenarios.Select(s => new Ending(s.Animation, s.Name, s.Animation.ScenarioRole, s.Animation.SharedSteps)).ToList()
			: [new Ending(animation, null, null, 0)];
		var first = endings[0].Animation;
		if (first.Timeline is null || first.Mode == PlaybackMode.Scrub || !first.Steps.Any(s => s.Caption is not null))
			return $"<figure>\n{first.Svg}\n</figure>";

		var root = first.Prefix + "-s";
		// the contents show when asked for, or when there is something to find: endings, or three steps and more
		var captioned = first.Steps.Count(s => s.Caption is not null);
		var contents = endings.Count > 1 || first.Contents == PlayerContents.Always || (first.Contents == PlayerContents.Auto && captioned >= 3);
		// the diagram's own title, unless the diagram already draws it (a journey's heading)
		var title = Between(first.Svg, "<title>", "</title>") is { Length: > 0 } named && !first.Svg.Contains(">" + named + "</text>", StringComparison.Ordinal)
			? named
			: null;
		var sb = new StringBuilder();
		_ = sb.Append("<div class=\"").Append(root).Append("\" style=\"").Append(Tokens(first)).Append("\">\n<style>\n");
		// the container the units' layout asks about: beside the figure when wide, folded under it when narrow
		Rule(
			sb,
			$".{root}",
			$"container-type: inline-size; font-family: {Font(first)}; color: var(--_text); margin: 0 auto; width: min(100%, {Px(first.Timeline.ViewBox.Width + (contents ? 284 : 0))})"
		);
		Rule(sb, $".{root} > input", "position: absolute; opacity: 0; width: 1px; height: 1px; pointer-events: none");
		if (endings.Count > 1)
		{
			Rule(sb, $".{root} > .{root}-end", "display: none");
			for (var k = 0; k < endings.Count; k++)
				Rule(sb, $".{root}:has(#{root}-{k}:checked) > .{root}-end-{k}", "display: block");
		}
		// the figure's title: the diagram's own (front matter title or accTitle), with the title mark the diagrams use
		Rule(
			sb,
			$".{root}-title",
			"display: flex; align-items: center; gap: 8px; margin: 0 0 12px; font-size: 18px; font-weight: 700; line-height: 24px; color: var(--_text)"
		);
		Rule(sb, $".{root}-title::before", "content: ''; width: 10px; height: 10px; border-radius: 2px; background: var(--accent); flex: none");
		_ = sb.Append("</style>\n");
		if (title is { Length: > 0 })
			_ = sb.Append("<div class=\"").Append(root).Append("-title\">").Append(WebUtility.HtmlEncode(WebUtility.HtmlDecode(title))).Append("</div>\n");
		if (endings.Count > 1)
			for (var k = 0; k < endings.Count; k++)
				_ = sb
					.Append("<input type=\"radio\" name=\"")
					.Append(root)
					.Append("\" id=\"")
					.Append(root)
					.Append('-')
					.Append(k)
					.Append('"')
					.Append(k == 0 ? " checked" : "")
					.Append(">\n");
		for (var k = 0; k < endings.Count; k++)
		{
			_ = sb.Append("<div class=\"").Append(root).Append("-end ").Append(root).Append("-end-").Append(k).Append("\">\n");
			Unit(sb, endings, k, root, contents);
			_ = sb.Append("</div>\n");
		}
		_ = sb.Append("</div>");
		return sb.ToString();
	}

	/// <summary>An element beside the SVG that animates: its class, the CSS property it moves and its keyframes on the story clock.</summary>
	private sealed record Track(string Class, List<(double T, string Css)> Frames, bool Cut)
	{
		public string At(double t)
		{
			var value = Frames[0].Css;
			foreach (var (time, css) in Frames)
				if (time <= t)
					value = css;
			return value;
		}
	}

	private static void Unit(StringBuilder sb, IReadOnlyList<Ending> endings, int chosen, string root, bool contents)
	{
		var ending = endings[chosen];
		var animation = ending.Animation;
		var timeline = animation.Timeline!;
		var prefix = animation.Prefix;
		var p = prefix + "-p";
		var steps = animation.Steps.Where(s => s.Caption is not null).ToList();
		var captions = timeline.Captions;
		var shared = endings.Count > 1 ? animation.Steps.Take(ending.Shared).Count(s => s.Caption is not null) : steps.Count;

		// the story clock: in a loop it starts where the intro ends and repeats; otherwise it is the whole timeline
		var clockStart = timeline.Loops ? timeline.StoryStart : 0;
		var clockLength = timeline.Duration - clockStart;
		var timing = timeline.Loops ? $"{Ms(clockLength)} linear {Ms(clockStart)} infinite backwards" : $"{Ms(clockLength)} linear 0s 1 both";

		double Local(TimeSpan t) => t.TotalMilliseconds - clockStart;

		var starts = steps.Select(s => Local(s.Start)).ToList();
		var ends = steps.Select(s => Local(s.End)).ToList();

		var tracks = new List<Track>();

		Track Add(string cls, List<(double, string)> frames, bool cut = true)
		{
			var track = new Track($"{p}-h{tracks.Count}", frames, cut);
			tracks.Add(track);
			return track;
		}

		// the caption: each one shown from when it appears until the next, its counter with it
		var captionTracks = new List<Track>();
		for (var i = 0; i < captions.Count; i++)
		{
			var from = captions[i].At - clockStart;
			var to = i + 1 < captions.Count ? captions[i + 1].At - clockStart : clockLength;
			captionTracks.Add(Add("", [(0, "opacity:0"), (from, "opacity:1"), (to, "opacity:0")]));
		}

		// the progress line fills as each step plays
		var progressFrames = new List<(double, string)> { (0, "transform:scaleX(0)") };
		for (var k = 0; k < steps.Count; k++)
		{
			progressFrames.Add((starts[k], "transform:scaleX(0)"));
			progressFrames.Add((ends[k], "transform:scaleX(1)"));
		}
		var progress = Add("", progressFrames, cut: false);

		// each row: a ring while its step plays, a lit dot and trunk from when it starts
		var rings = new List<Track>();
		var lit = new List<Track>();
		for (var k = 0; k < steps.Count; k++)
		{
			var end = k + 1 < steps.Count ? starts[k + 1] : clockLength;
			rings.Add(Add("", [(0, "opacity:0"), (starts[k], "opacity:1"), (end, "opacity:0")]));
			lit.Add(Add("", [(0, "opacity:0"), (starts[k], "opacity:1")]));
		}

		// the rows, top to bottom: the shared steps, then every ending, the chosen one open with its steps
		var rows = new List<(string Kind, int Index, double Y)>();
		var y = 0.0;
		for (var k = 0; k < shared; k++)
		{
			rows.Add(("step", k, y));
			y += RowHeight;
		}
		// the fork: one row where the shared steps end, a junction every ending leaves from
		var junction = y + (RowHeight / 2);
		if (endings.Count > 1)
			y += RowHeight;
		if (endings.Count > 1)
			for (var e = 0; e < endings.Count; e++)
			{
				rows.Add(("ending", e, y));
				y += EndingHeight;
				if (e == chosen)
					for (var k = shared; k < steps.Count; k++)
					{
						rows.Add(("step", k, y));
						y += RowHeight;
					}
			}
		var height = y;
		var window = Math.Min(height, WindowRows * RowHeight);
		Track? slide = null;
		if (height > window + 1)
		{
			var slideFrames = new List<(double, string)> { (0, "transform:translateY(0px)") };
			foreach (var (kind, index, rowY) in rows.Where(r => r.Kind == "step"))
			{
				var offset = Math.Max(0, Math.Min(height - window, rowY + (RowHeight / 2) - (window / 2)));
				slideFrames.Add((starts[index], $"transform:translateY({Px(-offset)})"));
			}
			slide = Add("", slideFrames);
		}

		var all = string.Join(", ", $".{p} svg [class*=\"{prefix}-a\"]", $".{p} [class*=\"{p}-h\"]");
		_ = sb.Append("<div class=\"").Append(p).Append("\">\n<style>\n");
		Layout(sb, p, timeline.ViewBox.Width);

		// keyframes and timing for everything beside the SVG, on the story clock
		foreach (var track in tracks)
		{
			_ = sb.Append("@keyframes ").Append(track.Class).Append(" {");
			foreach (var (t, css) in track.Frames)
				_ = sb
					.Append(' ')
					.Append(Pct(t / clockLength * 100))
					.Append(" { ")
					.Append(css)
					.Append(track.Cut ? "; animation-timing-function: steps(1, jump-end)" : "")
					.Append(" }");
			_ = sb.Append(" }\n");
			Rule(sb, $".{track.Class}", $"{track.Frames[0].Css}; animation: {track.Class} {timing}");
		}
		_ = sb.Append("@media (prefers-reduced-motion: reduce) { .").Append(progress.Class).Append(" { animation: none; } }\n");
		// where supported, the unit waits until it is on screen, the SVG with it: one trigger for both
		_ = sb
			.Append("@supports (timeline-trigger: --t view()) {\n  .")
			.Append(p)
			.Append(" { timeline-trigger: --")
			.Append(prefix)
			.Append(" view() entry 30% exit 0%; }\n  .")
			.Append(p)
			.Append(" svg { timeline-trigger: none; }\n  ")
			.Append(string.Join(", ", tracks.Select(t => "." + t.Class)))
			.Append(" { animation-trigger: --")
			.Append(prefix)
			.Append(" play-forwards; }\n}\n");

		// the states
		Rule(sb, $".{p}:has(#{p}-pause:checked) :is({all})", "animation-play-state: paused");
		var classes = animation.AnimatedClasses();
		for (var k = 0; k < steps.Count; k++)
		{
			var from = $"{p}-from{k}";
			var hold = $"{p}-hold{k}";
			var at = steps[k].Start.TotalMilliseconds;
			foreach (var group in classes.GroupBy(c => c.Starts))
				Rule(
					sb,
					string.Join(
						", ",
						group.SelectMany(c => new[] { $".{p}:has(#{from}:checked) svg .{c.Class}", $".{p}:has(#{hold}:checked) svg .{c.Class}" })
					),
					"animation-delay: " + string.Join(", ", group.Key.Split(',').Select(s => Ms(double.Parse(s, CultureInfo.InvariantCulture) - at)))
				);
			Rule(sb, $".{p}:has(#{from}:checked) [class*=\"{p}-h\"], .{p}:has(#{hold}:checked) [class*=\"{p}-h\"]", $"animation-delay: {Ms(clockStart - at)}");
			Rule(sb, $".{p}:has(#{hold}:checked) :is({all})", "animation-play-state: paused");
		}

		// the controls: each shows only in the states it acts in
		var buttons = new List<(string State, string Target, string Glyph, string Name)>
		{
			($"{p}-play", $"{p}-pause", "❚❚", "Pause"),
			($"{p}-pause", $"{p}-play", "▶", "Play"),
		};
		for (var k = 0; k < steps.Count; k++)
		{
			buttons.Add(($"{p}-from{k}", $"{p}-hold{k}", "❚❚", "Pause"));
			buttons.Add(($"{p}-hold{k}", $"{p}-from{k}", "▶", "Play"));
		}
		foreach (var state in buttons.Select(b => b.State).Distinct())
			Rule(sb, $".{p}:has(#{state}:checked) .{p}-ctl .when-{state}", "display: inline-flex");
		foreach (var target in buttons.Select(b => b.Target).Distinct())
			Rule(sb, $".{p}:has(#{target}:focus-visible) label[for=\"{target}\"]", "outline: 2px solid var(--accent); outline-offset: 2px");
		for (var k = 0; k < steps.Count; k++)
			Rule(sb, $".{p}:has(#{p}-from{k}:focus-visible) label[for=\"{p}-from{k}\"]", "outline: 2px solid var(--accent); outline-offset: 2px");
		_ = sb.Append("</style>\n");

		// one radio group: play first and checked, then playing from each step in order, so arrow keys walk the steps
		var radios = new List<string> { $"{p}-play", $"{p}-pause" };
		radios.AddRange(Enumerable.Range(0, steps.Count).Select(k => $"{p}-from{k}"));
		radios.AddRange(Enumerable.Range(0, steps.Count).Select(k => $"{p}-hold{k}"));
		foreach (var id in radios)
			_ = sb
				.Append("<input type=\"radio\" name=\"")
				.Append(p)
				.Append("\" id=\"")
				.Append(id)
				.Append('"')
				.Append(id == $"{p}-play" ? " checked" : "")
				.Append(">\n");
		_ = sb.Append("<input type=\"checkbox\" id=\"").Append(p).Append("-toc\">\n");

		// the figure: the diagram, and what is happening now as its caption
		_ = sb.Append("<figure class=\"").Append(p).Append("-fig\">\n<div class=\"").Append(p).Append("-stage\">\n").Append(animation.Svg).Append("\n</div>\n");
		// the caption's lines are hidden from screen readers (the SVG's description tells the story); its controls are not
		_ = sb.Append("<figcaption class=\"").Append(p).Append("-cap\">\n<span class=\"").Append(p).Append("-lines\" aria-hidden=\"true\">");
		for (var i = 0; i < captions.Count; i++)
			_ = sb
				.Append("<span class=\"")
				.Append(p)
				.Append("-line ")
				.Append(captionTracks[i].Class)
				.Append("\"><span class=\"")
				.Append(p)
				.Append("-n\">")
				.Append(captions[i].Number)
				.Append('/')
				.Append(steps.Count)
				.Append("</span>")
				.Append(WebUtility.HtmlEncode(captions[i].Text))
				.Append("</span>");
		_ = sb.Append("</span>");
		if (ending.Name is not null)
			_ = sb
				.Append("<span class=\"")
				.Append(p)
				.Append("-chip\" style=\"--r: ")
				.Append(RoleColor(ending))
				.Append("\">")
				.Append(WebUtility.HtmlEncode(ending.Name))
				.Append("</span>");

		void Controls()
		{
			foreach (var (state, target, glyph, name) in buttons)
				_ = sb
					.Append("<label class=\"b when-")
					.Append(state)
					.Append("\" for=\"")
					.Append(target)
					.Append("\" title=\"")
					.Append(name)
					.Append("\" aria-label=\"")
					.Append(name)
					.Append("\">")
					.Append(glyph)
					.Append("</label>");
		}

		// compact: the controls end the caption line, and there is no list
		if (!contents)
		{
			_ = sb.Append("<span class=\"").Append(p).Append("-ctl\">");
			Controls();
			_ = sb.Append("</span>");
		}
		_ = sb.Append("<span class=\"").Append(p).Append("-prog\"><i class=\"").Append(progress.Class).Append("\"></i></span>\n</figcaption>\n</figure>\n");
		if (!contents)
		{
			_ = sb.Append("</div>\n");
			return;
		}

		// the contents
		_ = sb.Append("<nav class=\"").Append(p).Append("-map\" aria-label=\"Story\">\n<div class=\"").Append(p).Append("-ctl\">");
		Controls();
		_ = sb.Append("<span class=\"").Append(p).Append("-head\">Story</span>");
		_ = sb.Append("<label class=\"").Append(p).Append("-more\" for=\"").Append(p).Append("-toc\">contents</label></div>\n");
		_ = sb
			.Append("<div class=\"")
			.Append(p)
			.Append(slide is null ? "-win" : "-win slides")
			.Append("\" style=\"height: ")
			.Append(Px(window))
			.Append("\"><div class=\"")
			.Append(p)
			.Append("-list")
			.Append(slide is null ? "" : " " + slide.Class)
			.Append("\">\n");
		if (endings.Count > 1)
			Forks(sb, p, rows, chosen, junction, shared > 0 ? junction - RowHeight : junction, height, lit.Count > shared ? lit[shared].Class : null);
		for (var r = 0; r < rows.Count; r++)
		{
			var (kind, index, rowY) = rows[r];
			if (kind == "step")
			{
				var onBranch = endings.Count > 1 && index >= shared;
				var after = r + 1 < rows.Count && rows[r + 1].Kind == "step" && (endings.Count == 1 || (rows[r + 1].Index >= shared) == onBranch);
				_ = sb
					.Append("<label class=\"")
					.Append(p)
					.Append("-row")
					.Append(onBranch ? " branch" : "")
					.Append("\" for=\"")
					.Append(p)
					.Append("-from")
					.Append(index)
					.Append("\" title=\"Play from step ")
					.Append(index + 1)
					.Append("\">");
				_ = sb.Append("<span class=\"").Append(p).Append("-dot\">");
				if (after)
					_ = sb.Append("<i class=\"t\"></i><i class=\"t lit ").Append(lit[index + 1].Class).Append("\"></i>");
				_ = sb
					.Append("<i class=\"d\"></i><i class=\"d lit ")
					.Append(lit[index].Class)
					.Append("\"></i><i class=\"ring ")
					.Append(rings[index].Class)
					.Append("\"></i></span>");
				_ = sb
					.Append("<span class=\"")
					.Append(p)
					.Append("-i\">")
					.Append(index + 1)
					.Append("</span><span class=\"")
					.Append(p)
					.Append("-c\">")
					.Append(WebUtility.HtmlEncode(steps[index].Caption!))
					.Append("</span></label>\n");
			}
			else
			{
				// the junction's own row: how many ways the story can go, beside the diamond
				if (index == 0)
					_ = sb.Append("<span class=\"").Append(p).Append("-junction\">").Append(endings.Count).Append(" ways it can go</span>\n");
				var other = endings[index];
				var open = index == chosen;
				var count = other.Animation.Steps.Count(s => s.Caption is not null) - shared;
				_ = sb
					.Append("<label class=\"")
					.Append(p)
					.Append("-ending")
					.Append(open ? " open" : "")
					.Append("\" for=\"")
					.Append(root)
					.Append('-')
					.Append(index)
					.Append("\" style=\"--r: ")
					.Append(RoleColor(other))
					.Append("\" title=\"Play this ending\">")
					.Append(Icon(other.Role))
					.Append("<span class=\"")
					.Append(p)
					.Append("-name\">")
					.Append(WebUtility.HtmlEncode(other.Name!))
					.Append("</span><span class=\"")
					.Append(p)
					.Append("-count\">")
					.Append(count)
					.Append("</span></label>\n");
			}
		}
		_ = sb.Append("</div></div>\n</nav>\n</div>\n");
	}

	/// <summary>
	/// The unit's layout and paint, every colour from the diagram's tokens. One spacing scale (4, 8, 12, 24) and one row
	/// rhythm (32px). The figure's column is the diagram's own width, so the caption and its line share the diagram's
	/// edges. The contents keep three columns: the trunk (x 14), the branch (x 44, the ending icons' centre) and the text
	/// (x 60, step captions and ending names alike); a branch's steps indent their text by the same 30px.
	/// </summary>
	private static void Layout(StringBuilder sb, string p, double width)
	{
		Rule(sb, $".{p}", "position: relative");
		Rule(sb, $".{p} > input", "position: absolute; opacity: 0; width: 1px; height: 1px; pointer-events: none");
		Rule(sb, $".{p}-fig", "margin: 0; min-width: 0");
		Rule(sb, $".{p}-stage svg", "display: block; width: 100%; height: auto");
		Rule(sb, $".{p}-cap", "display: flex; align-items: center; gap: 8px; margin-top: 12px; flex-wrap: wrap");
		Rule(sb, $".{p}-lines", "display: grid; flex: 1 1 auto; min-width: 0");
		Rule(sb, $".{p}-line", "grid-area: 1 / 1; display: flex; align-items: center; gap: 8px; font-size: 15px; font-weight: 650; line-height: 20px");
		Rule(
			sb,
			$".{p}-n",
			"background: var(--_text); color: var(--bg); font-size: 11.5px; font-weight: 700; border-radius: 10px; padding: 0 8px; line-height: 20px; flex: none"
		);
		Rule(
			sb,
			$".{p}-chip",
			"font-size: 12px; font-weight: 600; color: color-mix(in srgb, var(--r) 78%, var(--fg)); background: color-mix(in srgb, var(--r) 12%, var(--bg)); " +
				"border: 1px solid color-mix(in srgb, var(--r) 40%, var(--bg)); border-radius: 10px; padding: 0 8px; line-height: 18px"
		);
		Rule(sb, $".{p}-prog", "flex: 1 0 100%; height: 2px; background: var(--_line-soft); margin-top: 4px");
		Rule(sb, $".{p}-prog i", "display: block; height: 2px; background: var(--accent); transform-origin: 0 0");
		Rule(sb, $".{p}-map", "font-size: 12.5px; margin-top: 24px");
		Rule(sb, $".{p}-ctl", "display: flex; align-items: center; gap: 8px; height: 28px; margin-bottom: 12px");
		Rule(sb, $".{p}-cap .{p}-ctl", "margin: 0; height: 20px");
		Rule(sb, $".{p}-cap .{p}-ctl .b", "width: 22px; height: 22px; font-size: 9px");
		Rule(
			sb,
			$".{p}-ctl .b",
			"display: none; width: 28px; height: 28px; align-items: center; justify-content: center; border: 1.25px solid var(--_line-soft); " +
				"border-radius: 50%; box-sizing: border-box; font-size: 10px; color: var(--_text-sec); cursor: pointer; flex: none"
		);
		Rule(
			sb,
			$".{p}-head",
			"flex: 1; display: flex; align-items: center; gap: 8px; font-size: 11px; font-weight: 700; letter-spacing: .08em; text-transform: uppercase; color: var(--_text-muted)"
		);
		Rule(sb, $".{p}-head::after", "content: ''; flex: 1; height: 1.25px; background: var(--_line-soft)");
		Rule(sb, $".{p}-more", "display: none; font-size: 12px; color: var(--_text-sec); cursor: pointer");
		Rule(sb, $".{p}-more::after", "content: ' ▾'");
		Rule(sb, $".{p}:has(#{p}-toc:checked) .{p}-more::after", "content: ' ▴'");
		Rule(
			sb,
			// the edges fade only where the list slides: a list that fits shows every row whole
			$".{p}-win.slides",
			"-webkit-mask-image: linear-gradient(transparent, #000 12px, #000 calc(100% - 12px), transparent); " +
				"mask-image: linear-gradient(transparent, #000 12px, #000 calc(100% - 12px), transparent)"
		);
		// a flex column, so the endings' margins never collapse into each other and every row keeps the rhythm
		Rule(sb, $".{p}-list", "display: flex; flex-direction: column");
		Rule(sb, $".{p}-win", "overflow: hidden");
		Rule(sb, $".{p}-row, .{p}-ending", "display: flex; align-items: center; gap: 8px; cursor: pointer; color: var(--_text-sec); box-sizing: border-box");
		Rule(sb, $".{p}-row", $"height: {Px(RowHeight)}");
		Rule(sb, $".{p}-row.branch", "padding-left: 30px");
		Rule(sb, $".{p}-dot", $"position: relative; width: 28px; height: {Px(RowHeight)}; flex: none");
		var middle = RowHeight / 2;
		Rule(
			sb,
			$".{p}-dot .d",
			$"position: absolute; left: 10px; top: {Px(middle - 4)}; width: 8px; height: 8px; border-radius: 50%; box-sizing: border-box; background: var(--bg); border: 1.5px solid var(--_line)"
		);
		Rule(sb, $".{p}-dot .d.lit", "background: var(--accent); border-color: var(--accent)");
		Rule(sb, $".{p}-dot .t", $"position: absolute; left: 13.25px; top: {Px(middle)}; width: 1.5px; height: {Px(RowHeight)}; background: var(--_line)");
		Rule(sb, $".{p}-dot .t.lit", "left: 12.75px; width: 2.5px; background: var(--accent)");
		Rule(
			sb,
			$".{p}-dot .ring",
			$"position: absolute; left: 5px; top: {Px(middle - 9)}; width: 18px; height: 18px; border-radius: 50%; box-sizing: border-box; " +
				"border: 2.25px solid var(--accent); background: color-mix(in srgb, var(--accent) 14%, transparent); box-shadow: inset 0 0 0 3px var(--bg)"
		);
		Rule(sb, $".{p}-i", "width: 16px; flex: none; text-align: right; color: var(--_text-muted); font-variant-numeric: tabular-nums");
		Rule(sb, $".{p}-c", "white-space: nowrap; overflow: hidden; text-overflow: ellipsis");
		// an ending: a 28px box in its 32px row; border 1.5 + padding 6 puts its icon's centre on the branch (x 44) and,
		// with a 9px gap, its name on the text column (x 60)
		Rule(
			sb,
			$".{p}-ending",
			$"position: relative; height: 28px; margin: {Px((EndingHeight - 28) / 2)} 0 {Px((EndingHeight - 28) / 2)} 30px; padding: 0 8px 0 6px; gap: 9px; border-radius: 8px; " +
				"background: color-mix(in srgb, var(--r) 9%, var(--bg)); border: 1.5px solid color-mix(in srgb, var(--r) 40%, var(--bg))"
		);
		Rule(
			sb,
			$".{p}-ending.open",
			"background: color-mix(in srgb, var(--r) 16%, var(--bg)); border-color: color-mix(in srgb, var(--r) 70%, var(--bg)); color: var(--_text); font-weight: 650"
		);
		Rule(sb, $".{p}-list", "position: relative");
		Rule(sb, $".{p}-forks", "position: absolute; left: 0; top: 0; overflow: visible; pointer-events: none");
		Rule(
			sb,
			$".{p}-junction",
			$"height: {Px(RowHeight)}; padding-left: 30px; display: flex; align-items: center; font-size: 11px; font-weight: 700; " +
				"letter-spacing: .06em; text-transform: uppercase; color: var(--_text-muted)"
		);
		Rule(sb, $".{p}-ending svg", "flex: none");
		Rule(sb, $".{p}-name", "flex: 1; white-space: nowrap; overflow: hidden; text-overflow: ellipsis");
		Rule(
			sb,
			$".{p}-count",
			"font-size: 10.5px; font-weight: 600; color: var(--_text-sec); background: var(--_key-badge); border-radius: 8px; padding: 0 8px; line-height: 16px"
		);
		// wide: the contents beside the figure, which takes the diagram's own width; narrow: folded under it
		_ = sb
			.Append("@container (min-width: 640px) { .")
			.Append(p)
			.Append(":has(> nav) { display: grid; grid-template-columns: minmax(0, ")
			.Append(Px(width))
			.Append(") 260px; justify-content: center; gap: 24px; align-items: start; } .")
			.Append(p)
			.Append("-map { margin-top: 0; } }\n");
		_ = sb
			.Append("@container (max-width: 639.98px) { .")
			.Append(p)
			.Append("-more { display: inline; } .")
			.Append(p)
			.Append(":not(:has(#")
			.Append(p)
			.Append("-toc:checked)) .")
			.Append(p)
			.Append("-win { display: none; } }\n");
	}

	/// <summary>
	/// The fork in the road: the trunk runs on from the last shared step to a junction (the flowchart's decision diamond)
	/// with how many ways the story can go, and every ending's branch leaves that one point, curving to its box. The
	/// chosen route lights up in the accent when the story reaches it, on the same clock as the steps.
	/// </summary>
	private static void Forks(
		StringBuilder sb,
		string p,
		List<(string Kind, int Index, double Y)> rows,
		int chosen,
		double junction,
		double trunk,
		double height,
		string? litClass
	)
	{
		const double X = 14;
		const double Box = 30;
		// each branch heads straight for its ending from the junction, so they spread like roads from a crossing, and
		// settles level into the box's edge
		string Branch(double to) =>
			$"M{Num(X)},{Num(junction)} C{Num(X + ((Box - X) * 0.35))},{Num(junction + ((to - junction) * 0.35))} {Num(X + ((Box - X) * 0.7))},{Num(to)} {Num(Box)},{Num(to)}";

		var ends = rows.Where(r => r.Kind == "ending").Select(r => (r.Index, Middle: r.Y + (EndingHeight / 2))).ToList();
		var route = $"M{Num(X)},{Num(trunk)} V{Num(junction)} " + Branch(ends.Single(e => e.Index == chosen).Middle)[1..].Insert(0, "L");
		_ = sb
			.Append("<svg class=\"")
			.Append(p)
			.Append("-forks\" width=\"")
			.Append(Num(Box))
			.Append("\" height=\"")
			.Append(Num(height))
			.Append("\" viewBox=\"0 0 ")
			.Append(Num(Box))
			.Append(' ')
			.Append(Num(height))
			.Append("\" aria-hidden=\"true\">");
		_ = sb
			.Append("<path d=\"M")
			.Append(Num(X))
			.Append(',')
			.Append(Num(trunk))
			.Append(" V")
			.Append(Num(junction))
			.Append("\" stroke=\"var(--_line)\" stroke-width=\"1.5\" fill=\"none\"/>");
		foreach (var (_, middle) in ends)
			_ = sb.Append("<path d=\"").Append(Branch(middle)).Append("\" stroke=\"var(--_line)\" stroke-width=\"1.5\" fill=\"none\"/>");
		_ = sb
			.Append("<path class=\"")
			.Append(litClass ?? "")
			.Append("\" d=\"")
			.Append(route)
			.Append("\" stroke=\"var(--accent)\" stroke-width=\"2.5\" fill=\"none\" stroke-linecap=\"round\"/>");
		_ = sb
			.Append("<rect x=\"")
			.Append(Num(X - 5))
			.Append("\" y=\"")
			.Append(Num(junction - 5))
			.Append("\" width=\"10\" height=\"10\" rx=\"2\" transform=\"rotate(45 ")
			.Append(Num(X))
			.Append(' ')
			.Append(Num(junction))
			.Append(")\" fill=\"var(--bg)\" stroke=\"var(--_line)\" stroke-width=\"1.5\"/>");
		_ = sb.Append("</svg>");
	}

	private static string Num(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

	/// <summary>The diagram's colour inputs and the tokens derived from them, as the SVG's style block derives them.</summary>
	private static string Tokens(AnimatedDiagram animation)
	{
		var inputs = Between(animation.Svg[..Math.Max(0, animation.Svg.IndexOf('>'))], "style=\"", "\"") is { Length: > 0 } style
			? style.TrimEnd(';') + ";"
			: "--bg:#FFFFFF;--fg:#27272A;";
		var colors = animation.Colors;
		return inputs +
			$"--ok:{colors?.Ok ?? "#59a14f"};--fail:{colors?.Fail ?? "#e15759"};--warn:{colors?.Warn ?? "#edc948"};--info:{colors?.Info ?? "#4e79a7"};" +
			"--_text:var(--fg);--_text-sec:var(--muted,color-mix(in srgb,var(--fg) 72%,var(--bg)));" +
			"--_text-muted:var(--muted,color-mix(in srgb,var(--fg) 64%,var(--bg)));--_line:var(--line,color-mix(in srgb,var(--fg) 50%,var(--bg)));" +
			"--_line-soft:color-mix(in srgb,var(--fg) 14%,var(--bg));--_key-badge:color-mix(in srgb,var(--fg) 9%,var(--bg))";
	}

	private static string Font(AnimatedDiagram animation) =>
		Between(animation.Svg, "text { font-family: ", ";") ?? "'Inter', system-ui, -apple-system, 'Segoe UI', sans-serif";

	/// <summary>The text between the first <paramref name="open"/> and the <paramref name="close"/> after it.</summary>
	private static string? Between(string text, string open, string close)
	{
		var start = text.IndexOf(open, StringComparison.Ordinal);
		if (start < 0)
			return null;
		start += open.Length;
		var end = text.IndexOf(close, start, StringComparison.Ordinal);
		return end < 0 ? null : text[start..end];
	}

	private static string RoleColor(Ending ending) => ending.Role switch
	{
		Role.Ok => "var(--ok)",
		Role.Fail => "var(--fail)",
		Role.Warn => "var(--warn)",
		Role.Info => "var(--info)",
		_ => "var(--_line)",
	};

	/// <summary>The mark icon of the ending's role, as <c>mark</c> draws it: a check, a cross or an exclamation in a disc.</summary>
	private static string Icon(Role? role)
	{
		var glyph = role switch
		{
			Role.Ok => "<path d=\"M-3,0 l2,2 l4,-4\" fill=\"none\" stroke=\"#fff\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>",
			Role.Fail => "<path d=\"M-2.5,-2.5 l5,5 M2.5,-2.5 l-5,5\" stroke=\"#fff\" stroke-width=\"1.8\" stroke-linecap=\"round\"/>",
			Role.Warn or Role.Info => "<path d=\"M0,-3 v3.5 M0,2.6 v.2\" stroke=\"#fff\" stroke-width=\"1.9\" stroke-linecap=\"round\"/>",
			_ => "",
		};
		return $"<svg width=\"14\" height=\"14\" viewBox=\"-7 -7 14 14\" aria-hidden=\"true\"><circle r=\"7\" fill=\"var(--r)\"/>{glyph}</svg>";
	}

	private static void Rule(StringBuilder sb, string selector, string declarations) => sb.Append(selector).Append(" { ").Append(declarations).Append("; }\n");

	private static string Ms(double ms) => Math.Round(ms).ToString(CultureInfo.InvariantCulture) + "ms";

	private static string Px(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture) + "px";

	private static string Pct(double value) => Math.Round(Math.Max(0, Math.Min(100, value)), 3).ToString("0.###", CultureInfo.InvariantCulture) + "%";
}
