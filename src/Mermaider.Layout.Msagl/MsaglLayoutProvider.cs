using Mermaider.Models;

namespace Mermaider.Layout.Msagl;

/// <summary>
/// Layout provider backed by Microsoft MSAGL (Automatic Graph Layout).
/// Optional and legacy-compatible: the built-in layout engine is faster and supports compound subgraph layout and
/// orthogonal routing, which this provider does not.
/// <para>
/// Register globally: <c>MermaidRenderer.SetLayoutProvider(new MsaglLayoutProvider());</c>
/// </para>
/// <para>
/// Or per-call: <c>new RenderOptions { LayoutProvider = new MsaglLayoutProvider() }</c>
/// </para>
/// </summary>
public sealed class MsaglLayoutProvider : IGraphLayoutProvider
{
	public PositionedGraph LayoutFlowchart(MermaidGraph graph, RenderOptions? options = null, StrictStylingOptions? strict = null) =>
		MsaglFlowchartLayout.Layout(graph, options, strict);

	public PositionedClassDiagram LayoutClass(ClassDiagram diagram) =>
		MsaglClassLayout.Layout(diagram);

	public PositionedErDiagram LayoutEr(ErDiagram diagram) =>
		MsaglErLayout.Layout(diagram);
}
