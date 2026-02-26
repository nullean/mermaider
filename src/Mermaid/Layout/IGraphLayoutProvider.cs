using Mermaid.Models;

namespace Mermaid.Layout;

/// <summary>
/// Pluggable layout engine for graph-based diagrams (flowchart, state, class, ER).
/// Register a custom implementation via <see cref="MermaidRenderer.SetLayoutProvider"/>.
/// </summary>
public interface IGraphLayoutProvider
{
	PositionedGraph LayoutFlowchart(MermaidGraph graph, RenderOptions? options = null, StrictModeOptions? strict = null);

	PositionedClassDiagram LayoutClass(ClassDiagram diagram);

	PositionedErDiagram LayoutEr(ErDiagram diagram);
}
