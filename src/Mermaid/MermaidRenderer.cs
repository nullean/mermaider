using Mermaid.Layout;
using Mermaid.Models;
using Mermaid.Parsing;
using Mermaid.Rendering;
using Mermaid.Theming;

namespace Mermaid;

/// <summary>
/// Renders Mermaid diagram text to SVG strings.
/// Framework-agnostic, no DOM required. Pure .NET.
/// </summary>
public static class MermaidRenderer
{
	private static volatile IGraphLayoutProvider _layoutProvider = DefaultLayoutProvider.Instance;

	/// <summary>
	/// Replace the built-in layout engine with a custom provider (e.g. MSAGL).
	/// Install the <c>Mermaid.Layout.Msagl</c> NuGet package for the MSAGL provider.
	/// </summary>
	public static void SetLayoutProvider(IGraphLayoutProvider provider) =>
		_layoutProvider = provider ?? throw new ArgumentNullException(nameof(provider));

	/// <summary>Returns the currently active layout provider.</summary>
	public static IGraphLayoutProvider LayoutProvider => _layoutProvider;

	/// <summary>
	/// Render Mermaid diagram text to a self-contained SVG string.
	/// </summary>
	/// <param name="text">Mermaid source text (e.g. "graph TD\n  A --&gt; B")</param>
	/// <param name="options">Optional rendering configuration (colors, font, spacing).</param>
	/// <returns>A self-contained SVG string.</returns>
	/// <exception cref="MermaidParseException">Thrown when the input cannot be parsed.</exception>
	public static string RenderSvg(string text, RenderOptions? options = null)
	{
		var colors = BuildColors(options);
		var font = options?.Font ?? LayoutDefaults.Font;
		var transparent = options?.Transparent ?? false;
		var strict = options?.Strict;
		var provider = options?.LayoutProvider ?? _layoutProvider;

		var lines = PreprocessLines(text);
		if (lines.Length == 0)
			throw new MermaidParseException("Empty mermaid diagram");

		if (strict is not null)
			StrictModeValidator.Validate(lines, strict);

		var diagramType = DiagramDetector.Detect(text.AsSpan());

		var svg = diagramType switch
		{
			DiagramType.Sequence => SequenceSvgRenderer.Render(
				SequenceLayout.Layout(SequenceParser.Parse(lines)),
				colors, font, transparent, strict),

			DiagramType.Class => ClassSvgRenderer.Render(
				provider.LayoutClass(ClassParser.Parse(lines)),
				colors, font, transparent, strict),

			DiagramType.Er => ErSvgRenderer.Render(
				provider.LayoutEr(ErParser.Parse(lines)),
				colors, font, transparent, strict),

			_ => SvgRenderer.Render(
				provider.LayoutFlowchart(ParseInternal(lines, diagramType), options, strict),
				colors, font, transparent, strict),
		};

		if (strict?.Sanitize is { } sanitizeMode)
			svg = StrictModeSanitizer.Sanitize(svg, sanitizeMode);

		return svg;
	}

	/// <summary>
	/// Parse Mermaid diagram text into a logical graph structure without rendering.
	/// </summary>
	/// <param name="text">Mermaid source text.</param>
	/// <returns>The parsed graph model.</returns>
	/// <exception cref="MermaidParseException">Thrown when the input cannot be parsed.</exception>
	public static MermaidGraph Parse(string text)
	{
		var lines = PreprocessLines(text);
		if (lines.Length == 0)
			throw new MermaidParseException("Empty mermaid diagram");

		var diagramType = DiagramDetector.Detect(text.AsSpan());

		return ParseInternal(lines, diagramType);
	}

	private static MermaidGraph ParseInternal(string[] lines, DiagramType diagramType) =>
		diagramType switch
		{
			DiagramType.Flowchart => FlowchartParser.Parse(lines),
			DiagramType.State => StateParser.Parse(lines),
			_ => throw new MermaidParseException($"Diagram type '{diagramType}' is not yet supported.")
		};

	private static DiagramColors BuildColors(RenderOptions? options) =>
		new()
		{
			Bg = options?.Bg ?? "#FFFFFF",
			Fg = options?.Fg ?? "#27272A",
			Line = options?.Line,
			Accent = options?.Accent,
			Muted = options?.Muted,
			Surface = options?.Surface,
			Border = options?.Border,
		};

	private static string[] PreprocessLines(string text) =>
		text.Split('\n')
			.Select(l => l.Trim())
			.Where(l => l.Length > 0 && !l.StartsWith("%%"))
			.ToArray();
}
