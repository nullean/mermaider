using Mermaid.Models;

namespace Mermaid.Layout;

/// <summary>
/// Default layout provider using the lightweight Sugiyama engine.
/// Zero external dependencies.
/// </summary>
internal sealed class DefaultLayoutProvider : IGraphLayoutProvider
{
	internal static readonly DefaultLayoutProvider Instance = new();

	public PositionedGraph LayoutFlowchart(MermaidGraph graph, RenderOptions? options = null, StrictModeOptions? strict = null) =>
		LightweightLayoutEngine.Layout(graph, options, strict);

	public PositionedClassDiagram LayoutClass(ClassDiagram diagram) =>
		LightweightClassLayoutEngine.Layout(diagram);

	public PositionedErDiagram LayoutEr(ErDiagram diagram) =>
		LightweightErLayoutEngine.Layout(diagram);
}
