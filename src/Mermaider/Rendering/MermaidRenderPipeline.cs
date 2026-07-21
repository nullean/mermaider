using System.Globalization;
using System.Text;
using Mermaider.Layout;
using Mermaider.Models;
using Mermaider.Parsing;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Typed rendering pipeline: prepare source, normalize styles, render raw SVG, then sanitize.
/// </summary>
internal static class MermaidRenderPipeline
{
	internal static string Execute(string text, RenderOptions? options)
	{
		using var culture = InvariantCultureScope.Enter();
		var prepared = DiagramPreparationStage.Prepare(text, options);
		var configuration = RenderConfigurationNormalizer.Normalize(prepared, options);
		var request = new NormalizedRenderRequest(prepared, configuration, options);
		var rawSvg = DiagramSvgStage.Render(request);
		return SvgSanitizationStage.Apply(rawSvg, options?.SanitizeMode ?? SanitizeMode.Strip);
	}

	private readonly struct InvariantCultureScope : IDisposable
	{
		private readonly CultureInfo _previousCulture;

		private InvariantCultureScope(CultureInfo previousCulture) => _previousCulture = previousCulture;

		internal static InvariantCultureScope Enter()
		{
			var previousCulture = CultureInfo.CurrentCulture;
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			return new InvariantCultureScope(previousCulture);
		}

		public void Dispose() => CultureInfo.CurrentCulture = _previousCulture;
	}
}

internal sealed record PreparedDiagram(
	string CleanedSource,
	DiagramMetadata Metadata,
	string[] Lines,
	string[] FilteredLines,
	DiagramType DiagramType,
	AccessibilityInfo Accessibility);

internal sealed record NormalizedRenderStyles(
	DiagramColors Colors,
	string Font,
	string? MonoFont,
	FontScale FontScale,
	bool Transparent,
	StrictStylingOptions? Strict);

internal sealed record NormalizedRenderConfiguration(
	NormalizedRenderStyles Styles,
	IGraphLayoutProvider LayoutProvider,
	double EdgeRadius);

internal sealed record NormalizedRenderRequest(
	PreparedDiagram Diagram,
	NormalizedRenderConfiguration Configuration,
	RenderOptions? Options);

/// <summary>Normalized rendering inputs shared by every SVG renderer.</summary>
internal readonly record struct SvgRenderContext(
	NormalizedRenderStyles Styles,
	AccessibilityInfo Accessibility,
	DiagramType DiagramType,
	double EdgeRadius);

internal static class DiagramPreparationStage
{
	internal static PreparedDiagram Prepare(string text, RenderOptions? options)
	{
		var (cleaned, metadata) = DiagramPreprocessor.Process(text);
		var lines = MermaidRenderer.PreprocessLines(cleaned);
		if (lines.Length == 0)
			throw new MermaidParseException("Empty mermaid diagram");

		var diagramType = DiagramDetector.Detect(cleaned.AsSpan());
		var allowedDiagrams = options?.AllowedDiagrams ?? DiagramTypes.All;
		if ((allowedDiagrams & diagramType.ToFlag()) == 0)
			throw new MermaidParseException(
				$"Diagram type '{diagramType}' is not in the allowed set. " +
				$"Adjust RenderOptions.AllowedDiagrams to include it.");

		var (accessibility, filteredLines) = AccessibilityParser.Extract(lines);
		return new PreparedDiagram(cleaned, metadata, lines, filteredLines, diagramType, accessibility);
	}
}

internal static class RenderConfigurationNormalizer
{
	internal static NormalizedRenderConfiguration Normalize(PreparedDiagram diagram, RenderOptions? options)
	{
		var strict = options?.Strict;
		if (strict is not null)
		{
			if (diagram.Metadata.Theme is { Length: > 0 } || diagram.Metadata.ThemeVariables is not null)
				throw new MermaidParseException(
					"Strict mode: source-authored 'theme' / 'themeVariables' overrides are not allowed. " +
					"Styling is controlled by the host design system; remove the %%{init}%% theme directive " +
					"or frontmatter 'theme:' key.");

			StrictStylingValidator.Validate(diagram.Lines, strict);
		}

		var styles = new NormalizedRenderStyles(
			BuildColors(options, diagram.Metadata),
			options?.Font ?? LayoutDefaults.Font,
			options?.MonoFont,
			FontScale.From(options),
			options?.Transparent ?? true,
			strict);

		return new NormalizedRenderConfiguration(
			styles,
			options?.LayoutProvider ?? MermaidRenderer.LayoutProvider,
			options?.RoundedEdges != false ? 6.0 : 0);
	}

	private static DiagramColors BuildColors(RenderOptions? options, DiagramMetadata metadata)
	{
		DiagramColors? themeColors = null;
		if (metadata.Theme is { Length: > 0 } themeName)
			_ = Themes.BuiltIn.TryGetValue(themeName, out themeColors);

		var baseColors = themeColors ?? Themes.Default;
		var colors = new DiagramColors
		{
			Bg = SelectSafeColor(options?.Bg, baseColors.Bg),
			Fg = SelectSafeColor(options?.Fg, baseColors.Fg),
			Line = SelectSafeOptionalColor(options?.Line, baseColors.Line),
			Accent = SelectSafeOptionalColor(options?.Accent, baseColors.Accent),
			Muted = SelectSafeOptionalColor(options?.Muted, baseColors.Muted),
			Surface = SelectSafeOptionalColor(options?.Surface, baseColors.Surface),
			Border = SelectSafeOptionalColor(options?.Border, baseColors.Border),
			DataPalette = SelectSafePalette(options?.DataPalette, baseColors.DataPalette),
		};

		if (metadata.ThemeVariables is { } vars)
		{
			colors = colors with
			{
				Bg = SelectSafeColor(vars.GetValueOrDefault("background"), colors.Bg),
				Fg = SelectSafeColor(vars.GetValueOrDefault("primaryTextColor"), colors.Fg),
				Line = SelectSafeOptionalColor(vars.GetValueOrDefault("lineColor"), colors.Line),
				Accent = SelectSafeOptionalColor(vars.GetValueOrDefault("primaryColor"), colors.Accent),
			};
		}

		return colors;
	}

	private static string SelectSafeColor(string? candidate, string fallback) =>
		candidate is not null && SvgValueAllowlist.IsAllowedColor(candidate) ? candidate.Trim() : fallback;

	private static string? SelectSafeOptionalColor(string? candidate, string? fallback) =>
		candidate is not null && SvgValueAllowlist.IsAllowedColor(candidate) ? candidate.Trim() : fallback;

	private static string[]? SelectSafePalette(string[]? candidate, string[]? fallback)
	{
		if (candidate is not { Length: > 0 }
			|| candidate.Any(color => color is null || !SvgValueAllowlist.IsAllowedColor(color)))
			return fallback;

		return candidate.Select(color => color.Trim()).ToArray();
	}
}

internal static class DiagramSvgStage
{
	internal static string Render(NormalizedRenderRequest request)
	{
		var diagram = request.Diagram;
		var configuration = request.Configuration;
		var styles = configuration.Styles;
		var lines = diagram.FilteredLines;
		var strict = styles.Strict;
		var accessibility = diagram.Accessibility;
		var diagramType = diagram.DiagramType;
		var provider = configuration.LayoutProvider;
		var context = new SvgRenderContext(styles, accessibility, diagramType, configuration.EdgeRadius);

		var sb = diagramType switch
		{
			DiagramType.Sequence => SequenceSvgRenderer.RenderToBuilder(
				SequenceLayout.Layout(SequenceParser.Parse(lines)), context),

			DiagramType.Class => ClassSvgRenderer.RenderToBuilder(
				provider.LayoutClass(ClassParser.Parse(lines)), context),

			DiagramType.Er => ErSvgRenderer.RenderToBuilder(
				provider.LayoutEr(ErParser.Parse(lines)), context),

			DiagramType.Pie => PieSvgRenderer.RenderToBuilder(
				PieParser.Parse(lines), context),

			DiagramType.Quadrant => QuadrantSvgRenderer.RenderToBuilder(
				QuadrantParser.Parse(lines), context),

			DiagramType.Timeline => TimelineSvgRenderer.RenderToBuilder(
				TimelineParser.Parse(lines), context),

			DiagramType.GitGraph => GitGraphSvgRenderer.RenderToBuilder(
				GitGraphParser.Parse(lines), context),

			DiagramType.Radar => RadarSvgRenderer.RenderToBuilder(
				RadarParser.Parse(lines), context),

			DiagramType.Treemap => TreemapSvgRenderer.RenderToBuilder(
				TreemapParser.Parse(MermaidRenderer.PreprocessLinesPreserveIndent(diagram.CleanedSource)), context),

			DiagramType.Venn => VennSvgRenderer.RenderToBuilder(
				VennParser.Parse(lines), context),

			DiagramType.Mindmap => MindmapSvgRenderer.RenderToBuilder(
				MindmapParser.Parse(MermaidRenderer.PreprocessLinesPreserveIndent(diagram.CleanedSource)), context),

			DiagramType.Gantt => GanttSvgRenderer.RenderToBuilder(
				GanttParser.Parse(lines), context),

			DiagramType.Journey => JourneySvgRenderer.RenderToBuilder(
				JourneyParser.Parse(lines), context),

			DiagramType.C4 => C4SvgRenderer.RenderToBuilder(
				C4Parser.Parse(lines), context),

			DiagramType.Sankey => SankeySvgRenderer.RenderToBuilder(
				SankeyParser.Parse(lines), context),

			DiagramType.XyChart => XyChartSvgRenderer.RenderToBuilder(
				XyChartParser.Parse(lines), context),

			DiagramType.Requirement => RequirementSvgRenderer.RenderToBuilder(
				RequirementParser.Parse(lines), context),

			DiagramType.Packet => PacketSvgRenderer.RenderToBuilder(
				PacketParser.Parse(lines), context),

			DiagramType.Kanban => KanbanSvgRenderer.RenderToBuilder(
				KanbanParser.Parse(MermaidRenderer.PreprocessLinesPreserveIndent(diagram.CleanedSource, accessibility)), context),

			DiagramType.Architecture => ArchitectureSvgRenderer.RenderToBuilder(
				ArchitectureLayout.Layout(ArchitectureParser.Parse(lines)), context),

			DiagramType.Block => BlockSvgRenderer.RenderToBuilder(
				BlockParser.Parse(lines), context),

			DiagramType.TreeView => TreeViewSvgRenderer.RenderToBuilder(
				TreeViewParser.Parse(MermaidRenderer.PreprocessLinesPreserveIndent(diagram.CleanedSource, accessibility)), context),

			_ => SvgRenderer.RenderToBuilder(
				provider.LayoutFlowchart(MermaidRenderer.ParseInternal(lines, diagramType), request.Options, strict),
				context),
		};

		try
		{
			if (diagram.Metadata.Title is { Length: > 0 } title)
				InsertSvgTitle(sb, title);
			return sb.ToString();
		}
		finally
		{
			_ = sb.Clear();
			SharedStringBuilderPool.Instance.Return(sb);
		}
	}

	private static void InsertSvgTitle(StringBuilder sb, string title)
	{
		const string svgClose = "\">";
		var insertPos = sb.ToString().IndexOf(svgClose, StringComparison.Ordinal);
		if (insertPos < 0)
			return;

		var titleBuilder = new StringBuilder("\n<title>");
		Text.MultilineUtils.AppendEscapedXml(titleBuilder, title.AsSpan());
		_ = titleBuilder.Append("</title>");
		_ = sb.Insert(insertPos + svgClose.Length, titleBuilder.ToString());
	}
}

internal static class SvgSanitizationStage
{
	internal static string Apply(string rawSvg, SanitizeMode mode)
	{
		if (mode is not (SanitizeMode.Strip or SanitizeMode.Block))
			throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown SVG sanitization mode.");

		var result = SvgSanitizer.SanitizeRendererOutput(rawSvg);
		if (mode == SanitizeMode.Block && result.HasViolations)
			throw new MermaidSvgException(result.Violations);

		return result.Svg;
	}
}
