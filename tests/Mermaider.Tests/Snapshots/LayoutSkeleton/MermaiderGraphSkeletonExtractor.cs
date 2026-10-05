using Mermaider.Models;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

/// <summary>
/// Builds a <see cref="LayoutSkeleton"/> directly from Mermaider's own <see cref="MermaidGraph"/>
/// (pre-layout, carries subgraph membership) and <see cref="PositionedGraph"/> (post-layout,
/// carries coordinates) — the shared model flowchart and state diagrams both compile down to
/// via <see cref="Mermaider.Layout.LightweightLayoutEngine"/>. Mirrors
/// <see cref="MermaiderSkeletonExtractor"/>'s "read the internal model, don't re-parse our own
/// SVG" approach for ER.
///
/// Subgraph containment comes from <see cref="MermaidGraph.Subgraphs"/> (a subgraph's direct
/// member node ids and nested child subgraphs are a static property of the diagram source —
/// identical for Mermaider and mermaid.js since both parse the same input); subgraph geometry
/// comes from the matching <see cref="PositionedGroup"/> by id.
/// </summary>
internal static class MermaiderGraphSkeletonExtractor
{
	public static LayoutSkeleton Extract(MermaidGraph original, PositionedGraph positioned)
	{
		var boxes = positioned.Nodes
			.Select(n => (n.Id, n.X, n.Y, n.Width, n.Height))
			.ToList();

		var edges = positioned.Edges
			.Where(e => e.Points.Count >= 2)
			.Select(e => (e.Source, e.Target, e.Label ?? "", (IReadOnlyList<Point>)e.Points))
			.ToList();

		var groups = new List<(string Id, string? ParentId, IReadOnlyList<string> MemberNodeIds, double X, double Y, double W, double H)>();
		CollectGroups(original.Subgraphs, positioned.Groups, parentId: null, groups);

		return LayoutSkeletonBuilder.Build(boxes, edges, groups, original.Direction);
	}

	private static void CollectGroups(
		IReadOnlyList<MermaidSubgraph> subgraphs,
		IReadOnlyList<PositionedGroup> positionedGroups,
		string? parentId,
		List<(string Id, string? ParentId, IReadOnlyList<string> MemberNodeIds, double X, double Y, double W, double H)> result)
	{
		foreach (var sg in subgraphs)
		{
			var positioned = positionedGroups.FirstOrDefault(g => string.Equals(g.Id, sg.Id, StringComparison.Ordinal));
			if (positioned is null)
				continue; // a subgraph the layout engine dropped (e.g. empty) — nothing to compare geometrically

			result.Add((sg.Id, parentId, sg.NodeIds, positioned.X, positioned.Y, positioned.Width, positioned.Height));
			CollectGroups(sg.Children, positioned.Children, sg.Id, result);
		}
	}
}
