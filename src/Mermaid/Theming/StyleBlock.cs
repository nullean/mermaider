using System.Text;
using Mermaid.Models;

namespace Mermaid.Theming;

/// <summary>
/// CSS custom property derivation system for SVG theming.
/// Generates the &lt;style&gt; block and SVG opening tag.
/// </summary>
internal static class StyleBlock
{
	private static class Mix
	{
		internal const int Text = 100;
		internal const int TextSec = 55;
		internal const int TextMuted = 35;
		internal const int TextFaint = 20;
		internal const int Line = 32;
		internal const int Arrow = 70;
		internal const int NodeFill = 4;
		internal const int NodeStroke = 14;
		internal const int GroupHeader = 4;
		internal const int GroupStroke = 10;
		internal const int InnerStroke = 10;
		internal const int KeyBadge = 8;
	}

	internal static void AppendSvgOpenTag(
		StringBuilder sb, double width, double height,
		DiagramColors colors, bool transparent)
	{
		sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ")
			.Append(width).Append(' ').Append(height)
			.Append("\" width=\"").Append(width)
			.Append("\" height=\"").Append(height)
			.Append("\" style=\"--bg:").Append(colors.Bg)
			.Append(";--fg:").Append(colors.Fg);

		if (colors.Line is not null) sb.Append(";--line:").Append(colors.Line);
		if (colors.Accent is not null) sb.Append(";--accent:").Append(colors.Accent);
		if (colors.Muted is not null) sb.Append(";--muted:").Append(colors.Muted);
		if (colors.Surface is not null) sb.Append(";--surface:").Append(colors.Surface);
		if (colors.Border is not null) sb.Append(";--border:").Append(colors.Border);

		if (!transparent)
			sb.Append(";background:var(--bg)");

		sb.Append("\">");
	}

	internal static void AppendStyleBlock(StringBuilder sb, string? font = null, StrictModeOptions? strict = null)
	{
		sb.Append("\n<style>\n");

		if (font is { Length: > 0 })
			sb.Append("  text { font-family: '").Append(font).Append("', ").Append(Rendering.RenderConstants.SansStack).Append("; }\n");
		else
			sb.Append("  text { font-family: ").Append(Rendering.RenderConstants.SansStack).Append("; }\n");
		sb.Append("  .mono { font-family: ").Append(Rendering.RenderConstants.MonoStack).Append("; }\n");

		sb.Append("  svg {\n");
		sb.Append("    --_text:          var(--fg);\n");
		sb.Append("    --_text-sec:      var(--muted, color-mix(in srgb, var(--fg) ").Append(Mix.TextSec).Append("%, var(--bg)));\n");
		sb.Append("    --_text-muted:    var(--muted, color-mix(in srgb, var(--fg) ").Append(Mix.TextMuted).Append("%, var(--bg)));\n");
		sb.Append("    --_text-faint:    color-mix(in srgb, var(--fg) ").Append(Mix.TextFaint).Append("%, var(--bg));\n");
		sb.Append("    --_line:          var(--line, color-mix(in srgb, var(--fg) ").Append(Mix.Line).Append("%, var(--bg)));\n");
		sb.Append("    --_arrow:         var(--accent, color-mix(in srgb, var(--fg) ").Append(Mix.Arrow).Append("%, var(--bg)));\n");
		sb.Append("    --_node-fill:     var(--surface, color-mix(in srgb, var(--fg) ").Append(Mix.NodeFill).Append("%, var(--bg)));\n");
		sb.Append("    --_node-stroke:   var(--border, color-mix(in srgb, var(--fg) ").Append(Mix.NodeStroke).Append("%, var(--bg)));\n");
		sb.Append("    --_group-fill:    var(--bg);\n");
		sb.Append("    --_group-hdr:     color-mix(in srgb, var(--fg) ").Append(Mix.GroupHeader).Append("%, var(--bg));\n");
		sb.Append("    --_group-stroke:  color-mix(in srgb, var(--fg) ").Append(Mix.GroupStroke).Append("%, var(--bg));\n");
		sb.Append("    --_inner-stroke:  color-mix(in srgb, var(--fg) ").Append(Mix.InnerStroke).Append("%, var(--bg));\n");
		sb.Append("    --_key-badge:     color-mix(in srgb, var(--fg) ").Append(Mix.KeyBadge).Append("%, var(--bg));\n");
		sb.Append("  }\n");
		sb.Append("  .node, .actor, .entity, .class-node { filter: drop-shadow(0 1px 3px rgba(0,0,0,.07)); }\n");
		sb.Append("  .subgraph { filter: drop-shadow(0 1px 2px rgba(0,0,0,.04)); }\n");

		if (strict is not null)
			AppendStrictModeClasses(sb, strict);

		sb.Append("</style>\n");
	}

	private static void AppendStrictModeClasses(StringBuilder sb, StrictModeOptions strict)
	{
		var lightRules = new List<(string Selector, string Fill, string Stroke, string? Color)>();
		var darkRules = new List<(string Selector, string Fill, string Stroke, string? Color)>();

		foreach (var cls in strict.AllowedClasses)
		{
			if (cls.IsExternal)
				continue;

			var selector = $".cls-{cls.Name}";

			lightRules.Add((selector, cls.Fill!, cls.Stroke ?? cls.Fill!, cls.Color));

			var darkFill = cls.DarkFill ?? ColorUtils.InvertLightness(cls.Fill!);
			var darkStroke = cls.DarkStroke ?? (cls.Stroke is not null ? ColorUtils.InvertLightness(cls.Stroke) : darkFill);
			var darkColor = cls.DarkColor ?? (cls.Color is not null ? ColorUtils.InvertLightness(cls.Color) : null);
			darkRules.Add((selector, darkFill, darkStroke, darkColor));
		}

		if (lightRules.Count == 0)
			return;

		foreach (var (selector, fill, stroke, color) in lightRules)
		{
			sb.Append("  ").Append(selector).Append(" rect, ").Append(selector).Append(" polygon, ")
				.Append(selector).Append(" circle, ").Append(selector).Append(" ellipse { fill: ")
				.Append(fill).Append("; stroke: ").Append(stroke).Append("; }\n");
			if (color is not null)
				sb.Append("  ").Append(selector).Append(" text { fill: ").Append(color).Append("; }\n");
		}

		sb.Append("  @media (prefers-color-scheme: dark) {\n");
		foreach (var (selector, fill, stroke, color) in darkRules)
		{
			sb.Append("    ").Append(selector).Append(" rect, ").Append(selector).Append(" polygon, ")
				.Append(selector).Append(" circle, ").Append(selector).Append(" ellipse { fill: ")
				.Append(fill).Append("; stroke: ").Append(stroke).Append("; }\n");
			if (color is not null)
				sb.Append("    ").Append(selector).Append(" text { fill: ").Append(color).Append("; }\n");
		}
		sb.Append("  }\n");
	}
}
