using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using Mermaider.Models;

namespace Mermaider.Theming;

/// <summary>
/// CSS custom property derivation system for SVG theming.
/// Generates the &lt;style&gt; block and SVG opening tag.
/// </summary>
internal static class StyleBlock
{
	private static class Mix
	{
		internal const int TextSec = 72;
		internal const int TextMuted = 64;
		internal const int TextFaint = 30;
		internal const int Line = 50;
		internal const int LineSoft = 14;
		internal const int LineStrong = 68;
		internal const int Arrow = 70;
		internal const int NodeFill = 10;
		internal const int NodeStroke = 22;
		internal const int GroupFill = 4;
		internal const int GroupHeader = 15;
		internal const int GroupStroke = 10;
		internal const int InnerStroke = 12;
		internal const int KeyBadge = 9;

		internal const int AccentFill = 8;
		internal const int AccentStroke = 20;
		internal const int AccentText = 65;
	}

	/// <summary>
	/// The derived-token declarations inside <c>svg { … }</c>. One source for <see cref="AppendStyleBlock(StringBuilder, string?, StrictStylingOptions?, Rendering.FontScale?, string?, DesignInputs?, bool)"/>
	/// and the stylesheet allowlist, so they cannot drift.
	/// </summary>
	internal static readonly string[] TokenLines =
	[
		"    --_text:          var(--fg);",
		$"    --_text-sec:      var(--muted, color-mix(in srgb, var(--fg) {Mix.TextSec}%, var(--bg)));",
		$"    --_text-muted:    var(--muted, color-mix(in srgb, var(--fg) {Mix.TextMuted}%, var(--bg)));",
		$"    --_text-faint:    color-mix(in srgb, var(--fg) {Mix.TextFaint}%, var(--bg));",
		$"    --_line:          var(--line, color-mix(in srgb, var(--fg) {Mix.Line}%, var(--bg)));",
		$"    --_line-soft:     color-mix(in srgb, var(--fg) {Mix.LineSoft}%, var(--bg));",
		$"    --_line-strong:   color-mix(in srgb, var(--fg) {Mix.LineStrong}%, var(--bg));",
		$"    --_arrow:         var(--accent, color-mix(in srgb, var(--fg) {Mix.Arrow}%, var(--bg)));",
		$"    --_node-fill:     var(--surface, color-mix(in srgb, var(--fg) {Mix.NodeFill}%, var(--bg)));",
		$"    --_node-stroke:   var(--border, color-mix(in srgb, var(--fg) {Mix.NodeStroke}%, var(--bg)));",
		$"    --_group-fill:    color-mix(in srgb, var(--fg) {Mix.GroupFill}%, var(--bg));",
		$"    --_group-hdr:     color-mix(in srgb, var(--accent, var(--fg)) {Mix.GroupHeader}%, var(--bg));",
		$"    --_group-stroke:  color-mix(in srgb, var(--fg) {Mix.GroupStroke}%, var(--bg));",
		$"    --_inner-stroke:  color-mix(in srgb, var(--fg) {Mix.InnerStroke}%, var(--bg));",
		$"    --_key-badge:     color-mix(in srgb, var(--fg) {Mix.KeyBadge}%, var(--bg));",
		$"    --_accent-fill:   color-mix(in srgb, var(--accent, var(--fg)) {Mix.AccentFill}%, var(--bg));",
		$"    --_accent-stroke: color-mix(in srgb, var(--accent, var(--fg)) {Mix.AccentStroke}%, var(--bg));",
		$"    --_accent-text:   color-mix(in srgb, var(--accent, var(--fg)) {Mix.AccentText}%, var(--bg));",
	];

	/// <summary>Selectors that cast the box shadow (nodes, cards) and the container shadow.</summary>
	internal const string BoxSelectors = ".node, .actor, .entity, .class-node, .architecture-service, .kanban-card";

	internal const string ContainerSelectors = ".subgraph, .kanban-column";

	/// <summary>
	/// The elevation rules for a preset and elevation level. The shadow colour follows the page: soft zinc on light
	/// backgrounds, deeper black on dark ones. Empty when the preset or the level has no shadows.
	/// </summary>
	internal static string[] ElevationLines(StyleSpec spec, int elevation, bool darkBg)
	{
		if (elevation <= 0 || (!spec.NodeShadow && !spec.ContainerShadow))
			return [];

		var shadow = darkBg ? "rgba(0,0,0,.45)" : "rgba(24,24,27,.10)";
		var ambient = darkBg ? "rgba(0,0,0,.30)" : "rgba(24,24,27,.06)";
		var box = spec.Style == DiagramStyle.Tonal
			? $"drop-shadow(0 2px 6px {shadow})"
			: $"drop-shadow(0 1px 2px {shadow})";
		if (elevation >= 2)
			box += $" drop-shadow(0 6px 14px {ambient})";

		var lines = new List<string>(2);
		if (spec.NodeShadow)
			lines.Add($"  {BoxSelectors} {{ filter: {box}; }}");
		if (spec.ContainerShadow)
			lines.Add($"  {ContainerSelectors} {{ filter: drop-shadow(0 1px 2px {ambient}); }}");
		return [.. lines];
	}

	/// <summary>Every elevation line any preset / level / background can produce (the allowlist accepts exactly these).</summary>
	internal static readonly FrozenSet<string> AllElevationLines =
		new[] { StyleSpec.Quiet, StyleSpec.Blueprint, StyleSpec.Tonal }
			.SelectMany(spec => new[] { 1, 2 }.SelectMany(level => new[] { false, true }.SelectMany(dark => ElevationLines(spec, level, dark))))
			.ToFrozenSet(StringComparer.Ordinal);

	internal static void AppendSvgOpenTag(
		StringBuilder sb, double width, double height,
		DiagramColors colors, bool transparent,
		AccessibilityInfo? accessibility = null, DiagramType? diagramType = null,
		double minX = 0)
	{
		var viewWidth = width - minX;
		_ = sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"")
			.Append(minX).Append(" 0 ")
			.Append(viewWidth).Append(' ').Append(height)
			.Append("\" width=\"").Append(viewWidth)
			.Append("\" height=\"").Append(height).Append('"');

		if (accessibility?.HasContent == true)
		{
			_ = sb.Append(" role=\"img\"");
			if (diagramType.HasValue)
				_ = sb.Append(" aria-roledescription=\"").Append(GetRoleDescription(diagramType.Value)).Append('"');
			if (accessibility.Title is { Length: > 0 })
			{
				_ = sb.Append(" aria-label=\"");
				Text.MultilineUtils.AppendEscapedAttr(sb, accessibility.Title.AsSpan());
				_ = sb.Append('"');
			}
		}

		// Colors may originate from source-authored %%{init}%% themeVariables (non-strict mode),
		// so they are user-controlled and must be escaped before landing in the style="..."
		// attribute — otherwise a value like `red"><script>...` breaks out of the attribute.
		_ = sb.Append(" style=\"--bg:").Append(Text.MultilineUtils.EscapeAttr(colors.Bg))
			.Append(";--fg:").Append(Text.MultilineUtils.EscapeAttr(colors.Fg));

		if (colors.Line is not null)
			_ = sb.Append(";--line:").Append(Text.MultilineUtils.EscapeAttr(colors.Line));
		if (colors.Accent is not null)
			_ = sb.Append(";--accent:").Append(Text.MultilineUtils.EscapeAttr(colors.Accent));
		if (colors.Muted is not null)
			_ = sb.Append(";--muted:").Append(Text.MultilineUtils.EscapeAttr(colors.Muted));
		if (colors.Surface is not null)
			_ = sb.Append(";--surface:").Append(Text.MultilineUtils.EscapeAttr(colors.Surface));
		if (colors.Border is not null)
			_ = sb.Append(";--border:").Append(Text.MultilineUtils.EscapeAttr(colors.Border));

		if (!transparent)
			_ = sb.Append(";background:var(--bg)");

		_ = sb.Append("\">");

		AppendAccessibilityElements(sb, accessibility);
	}

	internal static void AppendAccessibilityElements(StringBuilder sb, AccessibilityInfo? accessibility)
	{
		if (accessibility?.HasContent != true)
			return;

		if (accessibility.Title is { Length: > 0 })
		{
			_ = sb.Append("\n<title>");
			Text.MultilineUtils.AppendEscapedXml(sb, accessibility.Title.AsSpan());
			_ = sb.Append("</title>");
		}

		if (accessibility.Description is { Length: > 0 })
		{
			_ = sb.Append("\n<desc>");
			Text.MultilineUtils.AppendEscapedXml(sb, accessibility.Description.AsSpan());
			_ = sb.Append("</desc>");
		}
	}

	private static string GetRoleDescription(DiagramType type) => type switch
	{
		DiagramType.Flowchart => "flowchart",
		DiagramType.State => "state diagram",
		DiagramType.Sequence => "sequence diagram",
		DiagramType.Class => "class diagram",
		DiagramType.Er => "ER diagram",
		DiagramType.Pie => "pie chart",
		DiagramType.Quadrant => "quadrant chart",
		DiagramType.Timeline => "timeline",
		DiagramType.GitGraph => "git graph",
		DiagramType.Radar => "radar chart",
		DiagramType.Treemap => "treemap",
		DiagramType.Venn => "venn diagram",
		DiagramType.Mindmap => "mindmap",
		DiagramType.Gantt => "gantt chart",
		DiagramType.Journey => "user journey",
		DiagramType.C4 => "C4 diagram",
		DiagramType.Sankey => "sankey diagram",
		DiagramType.XyChart => "XY chart",
		DiagramType.Requirement => "requirement diagram",
		DiagramType.Packet => "packet diagram",
		DiagramType.Kanban => "kanban board",
		DiagramType.Architecture => "architecture diagram",
		DiagramType.Block => "block diagram",
		DiagramType.TreeView => "tree view",
		_ => "diagram"
	};

	// CSS generic font family keywords must not be quoted in font-family declarations.
	private static readonly System.Collections.Frozen.FrozenSet<string> GenericFontKeywords =
		FrozenSet.ToFrozenSet(
		[
			"serif", "sans-serif", "monospace", "cursive", "fantasy",
			"system-ui", "ui-serif", "ui-sans-serif", "ui-monospace", "ui-rounded",
			"emoji", "math", "fangsong"
		], StringComparer.OrdinalIgnoreCase);

	private static void AppendFontFamilyCss(StringBuilder sb, string selector, string fontName, string fallbackStack)
	{
		_ = sb.Append("  ").Append(selector).Append(" { font-family: ");
		if (GenericFontKeywords.Contains(fontName))
		{
			_ = sb.Append(fontName);
		}
		else
		{
			_ = sb.Append('\'');
			AppendCssString(sb, fontName);
			_ = sb.Append('\'');
		}
		_ = sb.Append(", ").Append(fallbackStack).Append("; }\n");
	}

	/// <summary>
	/// Emits a CSS string using only literal ASCII identifier characters and positive
	/// hexadecimal escapes. Quotes, braces, newlines, and other syntax characters therefore
	/// remain data inside the surrounding quoted font-family string.
	/// </summary>
	private static void AppendCssString(StringBuilder sb, string value)
	{
		foreach (var c in value)
		{
			if (char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_')
			{
				_ = sb.Append(c);
				continue;
			}

			_ = sb.Append('\\')
				.Append(((int)c).ToString("X", CultureInfo.InvariantCulture))
				.Append(' ');
		}
	}

	internal static void AppendStyleBlock(StringBuilder sb, Rendering.NormalizedRenderStyles styles) =>
		AppendStyleBlock(sb, styles.Font, styles.Strict, styles.FontScale, styles.MonoFont, styles.Design, ColorUtils.IsDark(styles.Colors.Bg));

	internal static void AppendStyleBlock(StringBuilder sb, string? font = null, StrictStylingOptions? strict = null, Rendering.FontScale? fontScale = null, string? monoFont = null, DesignInputs? design = null, bool darkBg = false)
	{
		_ = sb.Append("\n<style>\n");

		if (font is { Length: > 0 })
			AppendFontFamilyCss(sb, "text", font, Rendering.RenderConstants.SansStack);
		else
			_ = sb.Append("  text { font-family: ").Append(Rendering.RenderConstants.SansStack).Append("; }\n");

		if (monoFont is { Length: > 0 })
			AppendFontFamilyCss(sb, ".mono", monoFont, Rendering.RenderConstants.MonoStack);
		else
			_ = sb.Append("  .mono { font-family: ").Append(Rendering.RenderConstants.MonoStack).Append("; }\n");

		_ = sb.Append("  svg {\n");
		foreach (var line in TokenLines)
			_ = sb.Append(line).Append('\n');

		var fs = fontScale ?? Rendering.FontScale.Default;
		_ = sb.Append("    --fs-xs: ").Append(fs.Xs).Append(";\n");
		_ = sb.Append("    --fs-s:  ").Append(fs.S).Append(";\n");
		_ = sb.Append("    --fs-m:  ").Append(fs.M).Append(";\n");
		_ = sb.Append("    --fs-l:  ").Append(fs.L).Append(";\n");
		_ = sb.Append("  }\n");

		var d = design ?? DesignInputs.Default;
		foreach (var line in ElevationLines(d.Spec, d.Elevation, darkBg))
			_ = sb.Append(line).Append('\n');

		if (strict is not null)
			AppendStrictStylingClasses(sb, strict);

		_ = sb.Append("</style>\n");
	}

	private static void AppendStrictStylingClasses(StringBuilder sb, StrictStylingOptions strict)
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
			_ = sb.Append("  ").Append(selector).Append(" rect, ").Append(selector).Append(" polygon, ")
				.Append(selector).Append(" circle, ").Append(selector).Append(" ellipse { fill: ")
				.Append(fill).Append("; stroke: ").Append(stroke).Append("; }\n");
			if (color is not null)
				_ = sb.Append("  ").Append(selector).Append(" text { fill: ").Append(color).Append("; }\n");
		}

		_ = sb.Append("  @media (prefers-color-scheme: dark) {\n");
		foreach (var (selector, fill, stroke, color) in darkRules)
		{
			_ = sb.Append("    ").Append(selector).Append(" rect, ").Append(selector).Append(" polygon, ")
				.Append(selector).Append(" circle, ").Append(selector).Append(" ellipse { fill: ")
				.Append(fill).Append("; stroke: ").Append(stroke).Append("; }\n");
			if (color is not null)
				_ = sb.Append("    ").Append(selector).Append(" text { fill: ").Append(color).Append("; }\n");
		}
		_ = sb.Append("  }\n");
	}
}
