using Mermaider.Models;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Cluster colours for flow-style diagrams (flowchart, state): each connected cluster of nodes (edges + shared subgraph) gets one
/// palette colour; subgraphs alternate through the palette in document order and never take the colour of a step they hold.
/// </summary>
internal sealed class ClusterPalette(DiagramColors colors, Dictionary<string, int> nodeCluster, Dictionary<string, int> groupCluster)
{
	// Auto colouring never uses a role hue (success / failure / warning), see DiagramColors.AutoPalette.
	private readonly string[] _palette = colors.AutoPalette();

	/// <summary>The role named by a class (<c>success</c>, <c>failure</c>, <c>warning</c>, <c>info</c>), else null.</summary>
	internal static string? RoleName(string? className) => className switch
	{
		"success" or "failure" or "warning" or "info" => className,
		_ => null,
	};

	private static ColorRole RoleOf(string name) => name switch
	{
		"success" => ColorRole.Success,
		"failure" => ColorRole.Failure,
		"warning" => ColorRole.Warning,
		_ => ColorRole.Info,
	};

	internal string RoleFill(string role) => VisualLanguage.Tint(colors.RoleColor(RoleOf(role)), VisualLanguage.NodeTint);

	internal string RoleStroke(string role) => VisualLanguage.Border(colors.RoleColor(RoleOf(role)));

	internal string NodeFill(string id) => VisualLanguage.Tint(Color(nodeCluster[id]), VisualLanguage.NodeTint);

	internal string NodeStroke(string id) => VisualLanguage.Border(Color(nodeCluster[id]));

	internal bool Has(string id) => nodeCluster.ContainsKey(id);

	internal string GroupFill(string id, int depth) => VisualLanguage.GroupFill(Color(GroupCluster(id)), depth);

	internal string GroupStroke(string id) => VisualLanguage.GroupBorder(Color(GroupCluster(id)));

	private int GroupCluster(string id) => groupCluster.GetValueOrDefault(id);

	private string Color(int cluster) => _palette[cluster % _palette.Length];

	internal string HeaderFill(string id) => VisualLanguage.Tint(Color(nodeCluster[id]), VisualLanguage.HeaderTint);

	internal static ClusterPalette Build(PositionedGraph graph, DiagramColors colors)
	{
		static ClusterGroup Map(PositionedGroup g) => new(g.Id, g.X, g.Y, g.Width, g.Height, g.Children.Select(Map).ToList());
		return Build(
			graph.Nodes.Select(n => new ClusterBox(n.Id, n.X, n.Y, n.Width, n.Height)).ToList(),
			graph.Edges.Select(e => (e.Source, e.Target)),
			graph.Groups.Select(Map).ToList(),
			colors);
	}

	/// <summary>
	/// Clusters = connected components over the edges, plus everything sharing a subgraph/namespace box. Boxes alternate through
	/// the auto palette in document order and never take the colour of a member.
	/// </summary>
	internal static ClusterPalette Build(
		IReadOnlyList<ClusterBox> nodes, IEnumerable<(string From, string To)> edges, IReadOnlyList<ClusterGroup> groups, DiagramColors colors)
	{
		var clusters = new ClusterAssigner(nodes.Select(n => n.Id));
		foreach (var (from, to) in edges)
			clusters.Union(from, to);

		static bool Inside(ClusterBox n, ClusterGroup g) =>
			n.X + (n.W / 2) >= g.X && n.X + (n.W / 2) <= g.X + g.W && n.Y + (n.H / 2) >= g.Y && n.Y + (n.H / 2) <= g.Y + g.H;

		void UnionGroup(ClusterGroup g, string? anchor)
		{
			foreach (var n in nodes)
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

		foreach (var g in groups)
			UnionGroup(g, null);

		var nodeCluster = clusters.Number(nodes.Select(n => n.Id));

		var groupCluster = new Dictionary<string, int>(StringComparer.Ordinal);
		var ordinal = 0;
		var last = -1;
		void AssignGroups(ClusterGroup g)
		{
			var held = nodes.Where(n => Inside(n, g)).Select(n => nodeCluster[n.Id]).ToHashSet();
			var colour = ordinal++;
			for (var guard = 0; (held.Contains(colour) || colour == last) && guard < 32; guard++)
				colour++;
			groupCluster[g.Id] = colour;
			last = colour;
			foreach (var c in g.Children)
				AssignGroups(c);
		}

		foreach (var g in groups)
			AssignGroups(g);
		return new ClusterPalette(colors, nodeCluster, groupCluster);
	}
}

/// <summary>A node's bounding box, as far as cluster colouring is concerned.</summary>
internal readonly record struct ClusterBox(string Id, double X, double Y, double W, double H);

/// <summary>A subgraph / namespace box with its nested boxes.</summary>
internal sealed record ClusterGroup(string Id, double X, double Y, double W, double H, IReadOnlyList<ClusterGroup> Children);
