using System.Net;
using System.Text;

namespace Mermaider.Animate;

/// <summary>
/// Wraps an animated SVG in an HTML fragment with controls that need no script: Play, Pause and one chip per step. The
/// controls are native radio buttons and a checkbox, wired with <c>:has()</c>. Choosing a step pauses the animation at
/// that step's last frame through a negative <c>animation-delay</c>; choosing Play lets it run again. Arrow keys move
/// between the chips, as in any radio group. The styles use <c>currentColor</c>, so the controls take the page's text
/// colour; a host restyles them through <c>--mp-ink</c>, <c>--mp-soft</c> and <c>--mp-rule</c>.
/// </summary>
internal static class Player
{
	internal static string Render(AnimatedDiagram animation, string prefix)
	{
		var p = prefix + "-p";
		var animated = $"svg [class*=\"{prefix}-a\"]";
		var inputs = new List<(string Id, string Type, string Label)> { ($"{p}-play", "radio", "▶ Play"), ($"{p}-pause", "radio", "❚❚ Pause") };
		for (var i = 0; i < animation.Steps.Count; i++)
			inputs.Add(($"{p}-s{i}", "radio", $"<b>{i + 1}</b>{WebUtility.HtmlEncode(animation.Steps[i].Caption ?? "")}"));

		var sb = new StringBuilder();
		_ = sb.Append("<figure class=\"").Append(p).Append("\">\n<style>\n");
		Rule(
			sb,
			$".{p}",
			"margin: 0; display: grid; gap: 10px; justify-items: center; --mp-ink: currentColor; " +
				"--mp-soft: color-mix(in srgb, currentColor 58%, transparent); --mp-rule: color-mix(in srgb, currentColor 20%, transparent)"
		);
		Rule(sb, $".{p} > input", "position: absolute; opacity: 0; width: 1px; height: 1px; pointer-events: none");
		Rule(sb, $".{p} > svg", "max-width: 100%; height: auto");
		Rule(
			sb,
			$".{p}-bar",
			"display: flex; flex-wrap: wrap; gap: 6px; justify-content: center; font: 500 12px/1.2 system-ui, -apple-system, 'Segoe UI', sans-serif"
		);
		Rule(
			sb,
			$".{p}-bar label",
			"cursor: pointer; padding: 5px 10px; border: 1px solid var(--mp-rule); border-radius: 999px; color: var(--mp-soft); user-select: none"
		);
		Rule(sb, $".{p}-bar label:hover", "color: var(--mp-ink)");
		Rule(sb, $".{p}-bar b", "font-weight: 650; margin-right: 5px");
		Rule(sb, $".{p}:has(#{p}-pause:checked) {animated}", "animation-play-state: paused");

		// a chosen step is a still: every animated element takes its value at the end of that step, and Play restarts
		for (var i = 0; i < animation.Steps.Count; i++)
		{
			var at = Math.Max(0, animation.Steps[i].End.TotalMilliseconds - 40);
			Rule(sb, $".{p}:has(#{p}-s{i}:checked) {animated}", "animation: none");
			foreach (var (cls, declarations) in animation.StylesAt(at))
				Rule(sb, $".{p}:has(#{p}-s{i}:checked) svg .{cls}", declarations);
		}
		foreach (var (id, _, _) in inputs)
		{
			Rule(sb, $".{p}:has(#{id}:checked) label[for=\"{id}\"]", "color: var(--mp-ink); border-color: var(--mp-ink)");
			Rule(sb, $".{p}:has(#{id}:focus-visible) label[for=\"{id}\"]", "outline: 2px solid var(--mp-ink); outline-offset: 2px");
		}
		_ = sb.Append("</style>\n");

		foreach (var (id, type, _) in inputs)
			_ = sb
				.Append("<input type=\"")
				.Append(type)
				.Append("\" id=\"")
				.Append(id)
				.Append('"')
				.Append(type == "radio" ? $" name=\"{p}\"" : "")
				.Append(id.EndsWith("-play", StringComparison.Ordinal) ? " checked" : "")
				.Append(">\n");

		_ = sb.Append(animation.Svg).Append('\n');

		_ = sb.Append("<figcaption class=\"").Append(p).Append("-bar\">\n");
		foreach (var (id, _, label) in inputs)
			_ = sb.Append("<label for=\"").Append(id).Append("\">").Append(label).Append("</label>\n");
		_ = sb.Append("</figcaption>\n</figure>");
		return sb.ToString();
	}

	private static void Rule(StringBuilder sb, string selector, string declarations) => sb.Append(selector).Append(" { ").Append(declarations).Append("; }\n");
}
