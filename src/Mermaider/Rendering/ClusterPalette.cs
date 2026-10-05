using Mermaider.Models;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Cluster colours for flow-style diagrams (flowchart, state): each connected cluster of nodes (edges + shared subgraph) gets one
/// palette colour; subgraphs alternate through the palette in document order and never take the colour of a step they hold.
/// </summary>
internal sealed class ClusterPalette(DiagramColors colors, Dictionary<string, int> nodeCluster, Dictionary<string, int> groupCluster)
{
	internal string NodeFill(string id) => VisualLanguage.Tint(Color(nodeCluster[id]), VisualLanguage.NodeTint);

	internal string NodeStroke(string id) => VisualLanguage.Border(Color(nodeCluster[id]));

	internal bool Has(string id) => nodeCluster.ContainsKey(id);

	internal string GroupFill(string id, int depth) => VisualLanguage.GroupFill(Color(GroupCluster(id)), depth);

	internal string GroupStroke(string id) => VisualLanguage.GroupBorder(Color(GroupCluster(id)));

	private int GroupCluster(string id) => groupCluster.GetValueOrDefault(id);

	private string Color(int cluster) => colors.PaletteAt(cluster);

	internal static ClusterPalette Build(PositionedGraph graph, DiagramColors colors)
	{
		var clusters = new ClusterAssigner(graph.Nodes.Select(n => n.Id));
		foreach (var e in graph.Edges)
			clusters.Union(e.Source, e.Target);

		static bool Inside(PositionedNode n, PositionedGroup g) =>
			n.X + (n.Width / 2) >= g.X && n.X + (n.Width / 2) <= g.X + g.Width && n.Y + (n.Height / 2) >= g.Y && n.Y + (n.Height / 2) <= g.Y + g.Height;

		void UnionGroup(PositionedGroup g, string? anchor)
		{
			foreach (var n in graph.Nodes)
			{
				if (!Inside(n, g))
					continue;
				if (anchor is null)
					anchor = n.Id;
				else
					clusters.Union(anchor, n.Id);
			}

			foreach (var c in g.Children)
				UnionGroup(c, anchor);
		}

		foreach (var g in graph.Groups)
			UnionGroup(g, null);

		var nodeCluster = clusters.Number(graph.Nodes.Select(n => n.Id));

		var groupCluster = new Dictionary<string, int>(StringComparer.Ordinal);
		var ordinal = 0;
		var last = -1;
		void AssignGroups(PositionedGroup g)
		{
			var held = graph.Nodes.Where(n => Inside(n, g)).Select(n => nodeCluster[n.Id]).ToHashSet();
			var colour = ordinal++;
			for (var guard = 0; (held.Contains(colour) || colour == last) && guard < 32; guard++)
				colour++;
			groupCluster[g.Id] = colour;
			last = colour;
			foreach (var c in g.Children)
				AssignGroups(c);
		}

		foreach (var g in graph.Groups)
			AssignGroups(g);
		return new ClusterPalette(colors, nodeCluster, groupCluster);
	}
}
