namespace Mermaider.Models;

/// <summary>
/// Flag set controlling which Mermaid diagram types are accepted by <see cref="MermaidRenderer"/>.
/// Combine flags to build an allowlist; use the named sets as a starting point.
/// </summary>
/// <example>
/// // Accept only the stable set, plus Architecture:
/// var opts = new RenderOptions
/// {
///     AllowedDiagrams = DiagramTypes.Stable | DiagramTypes.Architecture,
/// };
///
/// // Accept everything except TreeView and Block:
/// var opts = new RenderOptions
/// {
///     AllowedDiagrams = DiagramTypes.All &amp; ~(DiagramTypes.TreeView | DiagramTypes.Block),
/// };
/// </example>
[Flags]
public enum DiagramTypes : long
{
	Flowchart = 1L << 0,
	State = 1L << 1,
	Sequence = 1L << 2,
	Class = 1L << 3,
	Er = 1L << 4,
	Pie = 1L << 5,
	Quadrant = 1L << 6,
	Timeline = 1L << 7,
	GitGraph = 1L << 8,
	Mindmap = 1L << 9,
	Gantt = 1L << 10,
	Journey = 1L << 11,
	C4 = 1L << 12,
	Requirement = 1L << 13,
	Kanban = 1L << 14,
	Radar = 1L << 15,
	Treemap = 1L << 16,
	Venn = 1L << 17,
	Sankey = 1L << 18,
	XyChart = 1L << 19,
	Packet = 1L << 20,
	Architecture = 1L << 21,
	Block = 1L << 22,
	TreeView = 1L << 23,

	/// <summary>
	/// Diagram types with stable, non-beta Mermaid syntax.
	/// Flowchart, Sequence, State, Class, ER, Pie, Quadrant, Timeline, GitGraph,
	/// Mindmap, Gantt, Journey, C4, Requirement, Kanban.
	/// </summary>
	Stable = Flowchart | Sequence | State | Class | Er | Pie | Quadrant
		   | Timeline | GitGraph | Mindmap | Gantt | Journey | C4 | Requirement | Kanban,

	/// <summary>
	/// Diagram types that use a <c>-beta</c> keyword in Mermaid source.
	/// Radar, Treemap, Venn, Sankey, XyChart, Packet, Architecture, Block, TreeView.
	/// </summary>
	Beta = Radar | Treemap | Venn | Sankey | XyChart | Packet | Architecture | Block | TreeView,

	/// <summary>All diagram types — both <see cref="Stable"/> and <see cref="Beta"/>. This is the default.</summary>
	All = Stable | Beta,
}

internal static class DiagramTypeExtensions
{
	internal static DiagramTypes ToFlag(this DiagramType type) => type switch
	{
		DiagramType.Flowchart => DiagramTypes.Flowchart,
		DiagramType.State => DiagramTypes.State,
		DiagramType.Sequence => DiagramTypes.Sequence,
		DiagramType.Class => DiagramTypes.Class,
		DiagramType.Er => DiagramTypes.Er,
		DiagramType.Pie => DiagramTypes.Pie,
		DiagramType.Quadrant => DiagramTypes.Quadrant,
		DiagramType.Timeline => DiagramTypes.Timeline,
		DiagramType.GitGraph => DiagramTypes.GitGraph,
		DiagramType.Mindmap => DiagramTypes.Mindmap,
		DiagramType.Gantt => DiagramTypes.Gantt,
		DiagramType.Journey => DiagramTypes.Journey,
		DiagramType.C4 => DiagramTypes.C4,
		DiagramType.Requirement => DiagramTypes.Requirement,
		DiagramType.Kanban => DiagramTypes.Kanban,
		DiagramType.Radar => DiagramTypes.Radar,
		DiagramType.Treemap => DiagramTypes.Treemap,
		DiagramType.Venn => DiagramTypes.Venn,
		DiagramType.Sankey => DiagramTypes.Sankey,
		DiagramType.XyChart => DiagramTypes.XyChart,
		DiagramType.Packet => DiagramTypes.Packet,
		DiagramType.Architecture => DiagramTypes.Architecture,
		DiagramType.Block => DiagramTypes.Block,
		DiagramType.TreeView => DiagramTypes.TreeView,
		_ => 0,
	};
}
