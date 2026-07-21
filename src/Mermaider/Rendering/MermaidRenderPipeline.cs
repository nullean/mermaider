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
	internal static string Execute(string text, RenderOptions? options, CancellationToken ct = default)
	{
		var limits = options?.Limits ?? ResourceLimits.Default;

		// Input length check before any work (string allocation already done by caller)
		ResourceGuard.CheckInputLength(text, limits);

		// Create a deadline CTS using limits.TimeProvider so tests can inject a FakeTimeProvider
		// and advance time to trigger the deadline deterministically without wall-clock delays.
		// When the caller also supplies a CancellationToken, we link both into a combined source
		// so either the deadline or the caller's cancellation aborts the render.
		CancellationTokenSource? deadlineCts = null;    // fires after RenderDeadline elapses
		CancellationTokenSource? linkedCts = null;      // union of deadline + caller token
		if (limits.RenderDeadline.HasValue)
		{
			deadlineCts = new CancellationTokenSource(limits.RenderDeadline.Value, limits.TimeProvider);
			if (ct != default)
				linkedCts = CancellationTokenSource.CreateLinkedTokenSource(deadlineCts.Token, ct);
		}

		try
		{
			var token = linkedCts?.Token ?? deadlineCts?.Token ?? ct;

			using var culture = InvariantCultureScope.Enter();
			token.ThrowIfCancellationRequested();

			var prepared = DiagramPreparationStage.Prepare(text, options, limits, token);
			token.ThrowIfCancellationRequested();

			var configuration = RenderConfigurationNormalizer.Normalize(prepared, options);
			token.ThrowIfCancellationRequested();

			var request = new NormalizedRenderRequest(prepared, configuration, options, limits, token);
			var rawSvg = DiagramSvgStage.Render(request);
			token.ThrowIfCancellationRequested();

			return SvgSanitizationStage.Apply(rawSvg, options?.SanitizeMode ?? SanitizeMode.Strip);
		}
		finally
		{
			linkedCts?.Dispose();
			deadlineCts?.Dispose();
		}
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
	RenderOptions? Options,
	ResourceLimits Limits,
	CancellationToken CancellationToken);

/// <summary>Normalized rendering inputs shared by every SVG renderer.</summary>
internal readonly record struct SvgRenderContext(
	NormalizedRenderStyles Styles,
	AccessibilityInfo Accessibility,
	DiagramType DiagramType,
	double EdgeRadius,
	ResourceLimits Limits);

internal static class DiagramPreparationStage
{
	internal static PreparedDiagram Prepare(string text, RenderOptions? options,
		ResourceLimits limits, CancellationToken ct = default)
	{
		var (cleaned, metadata) = DiagramPreprocessor.Process(text);
		var lines = MermaidRenderer.PreprocessLines(cleaned);
		if (lines.Length == 0)
			throw new MermaidParseException("Empty mermaid diagram");

		// Line count + line length checks (bounds aggregate regex budget)
		ResourceGuard.CheckLines(lines, limits);
		ResourceGuard.CheckLineLength(lines, limits);
		ct.ThrowIfCancellationRequested();

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
		var limits = request.Limits;
		var ct = request.CancellationToken;
		var context = new SvgRenderContext(styles, accessibility, diagramType, configuration.EdgeRadius, limits);

		StringBuilder sb;

		// Each arm parses, checks element count, then renders.
		// MaxLines already bounds all parser iterations; element count is a secondary
		// guard for the super-linear Sugiyama-backed types and for explicit per-type
		// counts that the caller can tune via ResourceLimits.MaxElements.
		switch (diagramType)
		{
			case DiagramType.Sequence:
				{
					var parsed = SequenceParser.Parse(lines);
					ResourceGuard.CheckElements(parsed.Actors.Count + parsed.Messages.Count + parsed.Notes.Count, limits);
					sb = SequenceSvgRenderer.RenderToBuilder(SequenceLayout.Layout(parsed), context);
					break;
				}
			case DiagramType.Class:
				{
					var parsed = ClassParser.Parse(lines);
					ResourceGuard.CheckElements(parsed.Classes.Count + parsed.Relationships.Count, limits);
					ct.ThrowIfCancellationRequested();
					sb = ClassSvgRenderer.RenderToBuilder(provider.LayoutClass(parsed), context);
					break;
				}
			case DiagramType.Er:
				{
					var parsed = ErParser.Parse(lines);
					ResourceGuard.CheckElements(parsed.Entities.Count + parsed.Relationships.Count, limits);
					ct.ThrowIfCancellationRequested();
					sb = ErSvgRenderer.RenderToBuilder(provider.LayoutEr(parsed), context);
					break;
				}
			case DiagramType.Pie:
				{
					var parsed = PieParser.Parse(lines);
					sb = PieSvgRenderer.RenderToBuilder(parsed, context);
					break;
				}
			case DiagramType.Quadrant:
				{
					sb = QuadrantSvgRenderer.RenderToBuilder(QuadrantParser.Parse(lines), context);
					break;
				}
			case DiagramType.Timeline:
				{
					sb = TimelineSvgRenderer.RenderToBuilder(TimelineParser.Parse(lines), context);
					break;
				}
			case DiagramType.GitGraph:
				{
					sb = GitGraphSvgRenderer.RenderToBuilder(GitGraphParser.Parse(lines), context);
					break;
				}
			case DiagramType.Radar:
				{
					sb = RadarSvgRenderer.RenderToBuilder(RadarParser.Parse(lines), context);
					break;
				}
			case DiagramType.Treemap:
				{
					sb = TreemapSvgRenderer.RenderToBuilder(
						TreemapParser.Parse(MermaidRenderer.PreprocessLinesPreserveIndent(diagram.CleanedSource)),
						context);
					break;
				}
			case DiagramType.Venn:
				{
					var parsed = VennParser.Parse(lines);
					ResourceGuard.CheckElements(parsed.Sets.Count + parsed.Unions.Count, limits);
					sb = VennSvgRenderer.RenderToBuilder(parsed, context);
					break;
				}
			case DiagramType.Mindmap:
				{
					sb = MindmapSvgRenderer.RenderToBuilder(
						MindmapParser.Parse(MermaidRenderer.PreprocessLinesPreserveIndent(diagram.CleanedSource)),
						context);
					break;
				}
			case DiagramType.Gantt:
				{
					sb = GanttSvgRenderer.RenderToBuilder(GanttParser.Parse(lines), context);
					break;
				}
			case DiagramType.Journey:
				{
					sb = JourneySvgRenderer.RenderToBuilder(JourneyParser.Parse(lines), context);
					break;
				}
			case DiagramType.C4:
				{
					sb = C4SvgRenderer.RenderToBuilder(C4Parser.Parse(lines), context);
					break;
				}
			case DiagramType.Sankey:
				{
					sb = SankeySvgRenderer.RenderToBuilder(SankeyParser.Parse(lines), context);
					break;
				}
			case DiagramType.XyChart:
				{
					sb = XyChartSvgRenderer.RenderToBuilder(XyChartParser.Parse(lines), context);
					break;
				}
			case DiagramType.Requirement:
				{
					sb = RequirementSvgRenderer.RenderToBuilder(RequirementParser.Parse(lines), context);
					break;
				}
			case DiagramType.Packet:
				{
					sb = PacketSvgRenderer.RenderToBuilder(PacketParser.Parse(lines), context);
					break;
				}
			case DiagramType.Kanban:
				{
					sb = KanbanSvgRenderer.RenderToBuilder(
						KanbanParser.Parse(MermaidRenderer.PreprocessLinesPreserveIndent(diagram.CleanedSource, accessibility)),
						context);
					break;
				}
			case DiagramType.Architecture:
				{
					var parsed = ArchitectureParser.Parse(lines);
					ResourceGuard.CheckElements(
						parsed.Services.Count + parsed.Groups.Count + parsed.Edges.Count + parsed.Junctions.Count,
						limits);
					ct.ThrowIfCancellationRequested();
					sb = ArchitectureSvgRenderer.RenderToBuilder(ArchitectureLayout.Layout(parsed), context);
					break;
				}
			case DiagramType.Block:
				{
					sb = BlockSvgRenderer.RenderToBuilder(BlockParser.Parse(lines), context);
					break;
				}
			case DiagramType.TreeView:
				{
					sb = TreeViewSvgRenderer.RenderToBuilder(
						TreeViewParser.Parse(MermaidRenderer.PreprocessLinesPreserveIndent(diagram.CleanedSource, accessibility)),
						context);
					break;
				}
			default:
				{
					// Flowchart, State — primary Sugiyama path, most DoS-critical
					var parsed = MermaidRenderer.ParseInternal(lines, diagramType);
					ResourceGuard.CheckElements(parsed.Nodes.Count + parsed.Edges.Count, limits);
					ct.ThrowIfCancellationRequested();

					// For the built-in layout provider, thread the token and node cap directly into
					// Sugiyama so hot loops can be interrupted. Custom providers get the pre-call check only.
					var positioned = provider is DefaultLayoutProvider
						? LightweightLayoutEngine.Layout(parsed, request.Options, strict, limits.MaxNodesAfterLayout, ct)
						: provider.LayoutFlowchart(parsed, request.Options, strict);
					sb = SvgRenderer.RenderToBuilder(positioned, context);
					break;
				}
		}

		try
		{
			ResourceGuard.CheckOutputLength(sb, limits);
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
