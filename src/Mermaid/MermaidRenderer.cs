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

		var lines = PreprocessLines(text);
		if (lines.Length == 0)
			throw new MermaidParseException("Empty mermaid diagram");

		var diagramType = DiagramDetector.Detect(text.AsSpan());

		switch (diagramType)
		{
			case DiagramType.Sequence:
			{
				var seqDiagram = SequenceParser.Parse(lines);
				var positioned = SequenceLayout.Layout(seqDiagram);
				return SequenceSvgRenderer.Render(positioned, colors, font, transparent);
			}
			case DiagramType.Class:
			{
				var classDiagram = ClassParser.Parse(lines);
				var positioned = ClassLayoutEngine.Layout(classDiagram);
				return ClassSvgRenderer.Render(positioned, colors, font, transparent);
			}
			case DiagramType.Er:
			{
				var erDiagram = ErParser.Parse(lines);
				var positioned = ErLayoutEngine.Layout(erDiagram);
				return ErSvgRenderer.Render(positioned, colors, font, transparent);
			}
			default:
			{
				var graph = ParseInternal(lines, diagramType);
				var graphPositioned = MsaglLayoutEngine.Layout(graph, options);
				return SvgRenderer.Render(graphPositioned, colors, font, transparent);
			}
		}
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
