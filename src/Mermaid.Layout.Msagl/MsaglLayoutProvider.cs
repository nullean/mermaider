using Mermaid.Models;

namespace Mermaid.Layout.Msagl;

/// <summary>
/// Layout provider backed by Microsoft MSAGL (Automatic Graph Layout).
/// Install this package for higher-fidelity edge routing at the cost of higher allocations.
/// <para>
/// Register globally: <c>MermaidRenderer.SetLayoutProvider(new MsaglLayoutProvider());</c>
/// </para>
/// <para>
/// Or per-call: <c>new RenderOptions { LayoutProvider = new MsaglLayoutProvider() }</c>
/// </para>
/// </summary>
public sealed class MsaglLayoutProvider : IGraphLayoutProvider
{
	public PositionedGraph LayoutFlowchart(MermaidGraph graph, RenderOptions? options = null, StrictModeOptions? strict = null) =>
		MsaglFlowchartLayout.Layout(graph, options, strict);

	public PositionedClassDiagram LayoutClass(ClassDiagram diagram) =>
		MsaglClassLayout.Layout(diagram);

	public PositionedErDiagram LayoutEr(ErDiagram diagram) =>
		MsaglErLayout.Layout(diagram);
}
