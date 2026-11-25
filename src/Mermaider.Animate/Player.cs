using System.Globalization;
using System.Net;
using System.Text;

namespace Mermaider.Animate;

/// <summary>
/// Wraps an animated SVG in an HTML fragment with step controls that need no script, at the end of the caption band.
/// One radio group holds the player's state; <c>:has()</c> applies it:
/// <list type="bullet">
/// <item><c>play</c> runs the animation as it would run alone; <c>pause</c> stops it where it is.</item>
/// <item><c>still-k</c> shows the frame at the end of step k, as plain styles from the same timeline the frame sampler
/// reads. Its buttons step back, play from here, and step forward.</item>
/// <item><c>from-k</c> plays from the start of step k: leaving a still starts the animation afresh, and a negative
/// <c>animation-delay</c> per animation puts it at step k. <c>hold-k</c> pauses it there with the same delays, so play
/// resumes rather than restarts.</item>
/// </list>
/// Only the buttons that act in the current state show; each is a label for the radio it moves to, so the keyboard and
/// screen readers get native radio behaviour. The player is as wide as the diagram, at most its container (a size
/// container has no width of its own), so the controls end where the caption band ends. Where the longest caption would
/// run into them at the diagram's scale, they drop under it.
/// </summary>
internal static class Player
{
	internal static string Render(AnimatedDiagram animation, string prefix)
	{
		var timeline = animation.Timeline!;
		var p = prefix + "-p";
		var animated = $"svg [class*=\"{prefix}-a\"]";
		var steps = animation.Steps.Where(s => s.Caption is not null).ToList();
		var band = timeline.CaptionBand > 0 && timeline.ViewBox.Height > 0 ? timeline.CaptionBand / timeline.ViewBox.Height * 100 : 0;

		var sb = new StringBuilder();
		_ = sb.Append("<figure class=\"").Append(p).Append("\">\n<style>\n");
		Rule(
			sb,
			$".{p}",
			$"position: relative; margin: 0 auto; width: min(100%, {Px(timeline.ViewBox.Width)}); container-type: inline-size; --mp-ink: currentColor; " +
				"--mp-soft: color-mix(in srgb, currentColor 55%, transparent); --mp-rule: color-mix(in srgb, currentColor 22%, transparent)"
		);
		Rule(sb, $".{p} > input", "position: absolute; opacity: 0; width: 1px; height: 1px; pointer-events: none");
		Rule(sb, $".{p} > svg", "display: block; max-width: 100%; height: auto; margin: 0 auto");
		Rule(
			sb,
			$".{p}-bar",
			$"position: absolute; top: 0; right: 0; height: {Pct(band)}; display: flex; align-items: center; gap: 6px; padding-right: 8px; " +
				"font: 600 12px/1 system-ui, -apple-system, 'Segoe UI', sans-serif"
		);
		_ = sb
			.Append("@container (max-width: ")
			.Append(Px(Breakpoint(steps, timeline.ViewBox.Width)))
			.Append(") { .")
			.Append(p)
			.Append("-bar { position: static; height: auto; justify-content: center; padding: 8px 0 0; } }\n");
		Rule(sb, $".{p}-bar label", "cursor: pointer; user-select: none; color: var(--mp-soft)");
		Rule(sb, $".{p}-bar label:hover", "color: var(--mp-ink)");
		Rule(
			sb,
			$".{p}-bar .b",
			"display: none; width: 28px; height: 28px; align-items: center; justify-content: center; border: 1px solid var(--mp-rule); border-radius: 50%; font-size: 11px"
		);
		Rule(sb, $".{p}-bar .dots", "display: flex; gap: 5px; margin-left: 4px");
		Rule(sb, $".{p}-bar .dot", "display: block; width: 8px; height: 8px; border-radius: 50%; background: var(--mp-rule)");

		// the states, and what each one does to the animation
		Rule(sb, $".{p}:has(#{p}-pause:checked) {animated}", "animation-play-state: paused");
		var classes = animation.AnimatedClasses();
		for (var k = 0; k < steps.Count; k++)
		{
			var still = $"{p}-still{k}";
			var from = $"{p}-from{k}";
			var hold = $"{p}-hold{k}";
			Rule(sb, $".{p}:has(#{still}:checked) {animated}", "animation: none");
			foreach (var (cls, declarations) in animation.StylesAt(Math.Max(0, steps[k].End.TotalMilliseconds - 40)))
				Rule(sb, $".{p}:has(#{still}:checked) svg .{cls}", declarations);

			var at = steps[k].Start.TotalMilliseconds;
			// each animation is put at step k on its own clock: the intro's starts at 0, the story's where the intro ends
			string Selectors(IEnumerable<(string Class, string Starts)> group) =>
				string.Join(", ", group.SelectMany(c => new[] { $".{p}:has(#{from}:checked) svg .{c.Class}", $".{p}:has(#{hold}:checked) svg .{c.Class}" }));

			string Delays(string starts) =>
				"animation-delay: " + string.Join(", ", starts.Split(',').Select(s => Ms(double.Parse(s, CultureInfo.InvariantCulture) - at)));

			foreach (var group in classes.GroupBy(c => c.Starts))
				Rule(sb, Selectors(group), Delays(group.Key));
			Rule(sb, $".{p}:has(#{hold}:checked) {animated}", "animation-play-state: paused");
			Rule(sb, $".{p}:has(#{still}:checked) .dot[for=\"{still}\"]", "background: var(--mp-ink)");
		}

		// the buttons: each shows only in the states it acts in
		var buttons = new List<(string State, string Target, string Glyph, string Name)>
		{
			($"{p}-play", $"{p}-pause", "❚❚", "Pause"),
			($"{p}-pause", $"{p}-play", "▶", "Play"),
		};
		for (var k = 0; k < steps.Count; k++)
		{
			var still = $"{p}-still{k}";
			if (k > 0)
				buttons.Add((still, $"{p}-still{k - 1}", "◀", "Previous step"));
			buttons.Add((still, $"{p}-from{k}", "▶", "Play from this step"));
			if (k < steps.Count - 1)
				buttons.Add((still, $"{p}-still{k + 1}", "▶▏", "Next step"));
			buttons.Add(($"{p}-from{k}", $"{p}-hold{k}", "❚❚", "Pause"));
			buttons.Add(($"{p}-hold{k}", $"{p}-from{k}", "▶", "Play"));
		}
		foreach (var state in buttons.Select(b => b.State).Distinct())
			Rule(sb, $".{p}:has(#{state}:checked) .{p}-bar .when-{state}", "display: inline-flex");
		foreach (var target in buttons.Select(b => b.Target).Distinct())
			Rule(sb, $".{p}:has(#{target}:focus-visible) .{p}-bar label[for=\"{target}\"]", "outline: 2px solid var(--mp-ink); outline-offset: 2px");
		_ = sb.Append("</style>\n");

		// one radio group: play first and checked, then the stills in order so arrow keys walk the steps
		var radios = new List<string> { $"{p}-play", $"{p}-pause" };
		radios.AddRange(Enumerable.Range(0, steps.Count).Select(k => $"{p}-still{k}"));
		radios.AddRange(Enumerable.Range(0, steps.Count).SelectMany(k => new[] { $"{p}-from{k}", $"{p}-hold{k}" }));
		foreach (var id in radios)
			_ = sb
				.Append("<input type=\"radio\" name=\"")
				.Append(p)
				.Append("\" id=\"")
				.Append(id)
				.Append('"')
				.Append(id == $"{p}-play" ? " checked" : "")
				.Append(">\n");

		_ = sb.Append(animation.Svg).Append('\n');

		_ = sb.Append("<figcaption class=\"").Append(p).Append("-bar\">\n");
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
				.Append("</label>\n");
		_ = sb.Append("<span class=\"dots\">");
		for (var k = 0; k < steps.Count; k++)
			_ = sb
				.Append("<label class=\"dot\" for=\"")
				.Append(p)
				.Append("-still")
				.Append(k)
				.Append("\" title=\"")
				.Append(k + 1)
				.Append(": ")
				.Append(WebUtility.HtmlEncode(steps[k].Caption!))
				.Append("\" aria-label=\"Step ")
				.Append(k + 1)
				.Append(": ")
				.Append(WebUtility.HtmlEncode(steps[k].Caption!))
				.Append("\"></label>");
		_ = sb.Append("</span>\n</figcaption>\n</figure>");
		return sb.ToString();
	}

	private static void Rule(StringBuilder sb, string selector, string declarations) => sb.Append(selector).Append(" { ").Append(declarations).Append("; }\n");

	private static string Ms(double ms) => Math.Round(ms).ToString(CultureInfo.InvariantCulture) + "ms";

	/// <summary>
	/// The player width below which the controls would overlap the longest caption. Captions scale with the diagram and
	/// the controls do not, so the overlap starts at width W where captionWidth * W / viewBoxWidth + controls = W.
	/// </summary>
	internal static double Breakpoint(IReadOnlyList<AnimationStep> steps, double viewBoxWidth)
	{
		const double CharWidth = 8.4; // 15px semibold caption text, per character, in user units
		const double ButtonsWidth = (3 * 34) + 24;
		var counter = $"{steps.Count}/{steps.Count}".Length;
		var caption = 14 + (counter * 7) + 16 + 9 + (steps.Max(s => s.Caption!.Length) * CharWidth);
		var controls = ButtonsWidth + (steps.Count * 13);
		var free = viewBoxWidth - caption;
		return free <= 0 ? 100000 : Math.Ceiling(controls * viewBoxWidth / free);
	}

	private static string Px(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture) + "px";

	private static string Pct(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture) + "%";
}
