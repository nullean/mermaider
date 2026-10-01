using System.Text;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

/// <summary>
/// Which side of an entity's bounding box an edge attaches to. Derived purely from geometry
/// (comparing the edge's start/end point against its owning entity's box edges), so it means
/// the same thing whether the box+point pair came from Mermaider's own model or from parsing
/// a rendered mermaid.js SVG.
/// </summary>
internal enum EdgeSide { Top, Bottom, Left, Right }

/// <summary>
/// One entity, reduced to just the facts that matter for placement/ordering comparisons:
/// which layer (rank) it sits in, and its left-to-right position among siblings in that
/// layer. <see cref="CenterX"/>/<see cref="CenterY"/>/<see cref="Width"/>/<see cref="Height"/>
/// are kept only as the raw geometry <see cref="LayoutSkeletonBuilder"/> derived Layer/
/// OrderInLayer from — comparisons should use the ordinal fields, not these.
/// </summary>
internal sealed record SkeletonNode(
	string Id, int Layer, int OrderInLayer, double CenterX, double CenterY, double Width, double Height);

/// <summary>
/// One relationship, reduced to its endpoints' entity ids and which side of each entity's
/// box it attaches to. Does not carry the edge's interior routing/waypoints — that stays out
/// of scope for skeleton comparisons (see AGENTS.md / the ER layout skeleton plan).
/// </summary>
internal sealed record SkeletonEdge(
	string From, string To, string Label, EdgeSide FromSide, EdgeSide ToSide, bool IsSelfLoop);

/// <summary>
/// One subgraph/cluster, reduced to the same ordinal facts as <see cref="SkeletonNode"/> (which
/// layer it sits in among its siblings, its left-to-right order there) plus the containment
/// structure that makes a group a group: its parent group id (null at the top level) and the
/// ids of the leaf nodes it directly contains (not recursively — a nested child group's members
/// are attributed to that child group, not re-listed here). Layer/OrderInLayer are computed
/// over groups exactly like they are over nodes (same box-overlap clustering, run on group
/// boxes instead of node boxes) so group-vs-group placement is comparable the same way
/// node-vs-node placement is. <see cref="CenterX"/>/<see cref="CenterY"/>/<see cref="Width"/>/
/// <see cref="Height"/> are raw geometry kept only for building Layer/OrderInLayer from.
/// </summary>
internal sealed record SkeletonGroup(
	string Id, string? ParentId, IReadOnlyList<string> MemberNodeIds, int Layer, int OrderInLayer,
	double CenterX, double CenterY, double Width, double Height);

/// <summary>
/// Renderer-agnostic snapshot of a diagram's layout structure: which layer each entity/node is
/// in, its order within that layer, which side each edge attaches to, and (for diagram types
/// that have them) subgraph/cluster containment and placement. Both Mermaider's own model
/// (<see cref="MermaiderSkeletonExtractor"/> for ER, <see cref="MermaiderGraphSkeletonExtractor"/>
/// for flowchart/state) and a real mermaid.js SVG (<see cref="MermaidJsSkeletonExtractor"/>,
/// <see cref="MermaidJsFlowchartSkeletonExtractor"/>, <see cref="MermaidJsStateSkeletonExtractor"/>)
/// project onto this same shape via <see cref="LayoutSkeletonBuilder"/>, so
/// <see cref="SkeletonComparer"/> can compare "did we make the same placement/ordering/
/// containment decisions" without caring about exact pixels or SVG syntax. <see cref="Groups"/>
/// is empty for diagram types without subgraphs (ER).
/// </summary>
internal sealed record LayoutSkeleton(
	IReadOnlyList<SkeletonNode> Nodes, IReadOnlyList<SkeletonEdge> Edges, IReadOnlyList<SkeletonGroup> Groups)
{
	/// <summary>
	/// Human-readable layer-by-layer dump: entities per layer in left-to-right order, then
	/// every edge with its label and attachment sides. Meant to be eyeballed directly in a
	/// test failure or a Verify snapshot diff — no SVG viewer required.
	/// </summary>
	public string ToAsciiArt()
	{
		var sb = new StringBuilder();
		foreach (var layerGroup in Nodes.GroupBy(n => n.Layer).OrderBy(g => g.Key))
		{
			var ordered = layerGroup.OrderBy(n => n.OrderInLayer).Select(n => n.Id);
			sb.Append("Layer ").Append(layerGroup.Key).Append(": ").AppendLine(string.Join(", ", ordered));
		}

		if (Groups.Count > 0)
		{
			sb.AppendLine("Groups:");
			var byParent = Groups.ToLookup(g => g.ParentId, StringComparer.Ordinal);
			void WriteGroup(SkeletonGroup g, int depth)
			{
				sb.Append(new string(' ', depth * 2)).Append("- ").Append(g.Id)
					.Append(" (layer=").Append(g.Layer).Append(" order=").Append(g.OrderInLayer)
					.Append(") members=[").Append(string.Join(", ", g.MemberNodeIds)).AppendLine("]");
				foreach (var child in byParent[g.Id].OrderBy(c => c.Layer).ThenBy(c => c.OrderInLayer))
					WriteGroup(child, depth + 1);
			}
			foreach (var top in byParent[null].OrderBy(g => g.Layer).ThenBy(g => g.OrderInLayer))
				WriteGroup(top, 0);
		}

		if (Edges.Count > 0)
		{
			sb.AppendLine("Edges:");
			foreach (var e in Edges.OrderBy(e => e.From, StringComparer.Ordinal).ThenBy(e => e.To, StringComparer.Ordinal))
			{
				var loopMark = e.IsSelfLoop ? " (self)" : "";
				sb.Append("  ").Append(e.From).Append(" --").Append(e.Label).Append("--> ").Append(e.To)
					.Append(" [").Append(e.FromSide).Append("->").Append(e.ToSide).Append(']').Append(loopMark)
					.AppendLine();
			}
		}

		return sb.ToString();
	}
}
