using System.Text.RegularExpressions;
using Mermaider.Models;
using Mermaider.Rendering;
using Mermaider.Theming;

namespace Mermaider.Tests.Rendering;

/// <summary>
/// Expected markup of the shared design system, for contract tests across diagram types. Every helper derives its
/// expectation from the same family / spec code the renderers use, so tests pin "uses the shared component", not hues.
/// </summary>
internal static class DesignContract
{
	internal static string Cluster(int i) => Themes.Default.AutoPaletteAt(i);

	internal static ColorFamily Family(string color) => new("x", color);

	/// <summary>A gradient stop at the top stage of <paramref name="color"/>: a node filled with that family (Quiet, gradient on).</summary>
	internal static string NodeGradientStop(string color) => $"stop-color=\"{Family(color).Top}\"";

	/// <summary>Node / entity outline in <paramref name="color"/>.</summary>
	internal static string Outline(string color) => $"stroke=\"{Family(color).Stroke}\"";

	/// <summary>Container / stereotype / title text in the ink of <paramref name="color"/>.</summary>
	internal static string InkText(string color) => $"fill=\"{Family(color).Ink}\"";

	/// <summary>Regex: a text element reading <paramref name="text"/> drawn in the ink of <paramref name="color"/>.</summary>
	internal static string InkTextRegex(string color, string text) =>
		Regex.Escape(InkText(color)) + "[^>]*>" + Regex.Escape(text) + "</text>";

	/// <summary>Header band / badge fill in <paramref name="color"/>.</summary>
	internal static string Band(string color) => Family(color).Band;

	/// <summary>The Quiet edge-label pill.</summary>
	internal const string Pill = "fill=\"var(--bg)\" stroke=\"var(--_line-soft)\" stroke-width=\"1\"";

	/// <summary>The accent mark every Quiet container header carries.</summary>
	internal const string AccentMark = "fill=\"var(--accent, var(--fg))\"";

	internal static string Render(string source, DiagramStyle style = DiagramStyle.Quiet, bool gradient = true) =>
		MermaidRenderer.RenderSvg(source, new RenderOptions { Style = style, Gradient = gradient });

	/// <summary>Literal hex colours in the SVG body (outside the root style attribute and the data palette).</summary>
	internal static IEnumerable<string> HexColours(string svg)
	{
		var body = svg[(svg.IndexOf("</style>", StringComparison.Ordinal) + 8)..];
		return Regex.Matches(body, "#[0-9A-Fa-f]{6}\\b").Select(m => m.Value);
	}
}
