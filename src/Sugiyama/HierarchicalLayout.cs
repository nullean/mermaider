using Sugiyama.Internal;

namespace Sugiyama;

/// <summary>
/// Compound (hierarchical) layout: every subgraph is laid out on its own and then placed as ONE node of its parent, so subgraph
/// boxes can never overlap each other or foreign nodes. Edges are routed afterwards on the final absolute boxes by
/// <see cref="FlowEdgeRouter"/> (orthogonal, obstacle-aware, ports spread along the node sides).
/// </summary>
public static class HierarchicalLayout
{
	private sealed class Placed
	{
		internal Dictionary<string, (double X, double Y, double W, double H)> Nodes = new(StringComparer.Ordinal);
		internal List<GroupRect> Groups = [];
		internal double Width;
		internal double Height;
	}

	private sealed class GroupRect(string id, string label, double x, double y, double w, double h)
	{
		internal string Id = id;
		internal string Label = label;
		internal double X = x;
		internal double Y = y;
		internal double W = w;
		internal double H = h;
		internal List<GroupRect> Children = [];
		internal HashSet<string> NodeIds = new(StringComparer.Ordinal);
	}

	public static LayoutResult Compute(LayoutGraph input, LayoutOptions options)
	{
		var nodeById = input.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

		// node -> ancestor subgraph path (first wins for overlapping membership)
		var paths = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		void Walk(LayoutSubgraph sg, List<string> prefix)
		{
			var path = new List<string>(prefix) { sg.Id };
			foreach (var id in sg.NodeIds)
			{
				if (!paths.TryGetValue(id, out var existing) || existing.Count < path.Count)
					paths[id] = path;
			}
			foreach (var c in sg.Children)
				Walk(c, path);
		}

		foreach (var sg in input.Subgraphs)
			Walk(sg, []);

		var subById = new Dictionary<string, LayoutSubgraph>(StringComparer.Ordinal);
		void Index(LayoutSubgraph sg)
		{
			subById[sg.Id] = sg;
			foreach (var c in sg.Children)
				Index(c);
		}

		foreach (var sg in input.Subgraphs)
			Index(sg);

		List<string> PathOf(string node) => paths.TryGetValue(node, out var p) ? p : [];

		Placed LayoutLevel(IReadOnlyList<string> levelPath, IEnumerable<string> ownNodes, IEnumerable<LayoutSubgraph> childSubs, bool isRoot)
		{
			var depth = levelPath.Count;
			var itemNodes = new List<LayoutNode>();
			var childPlaced = new Dictionary<string, Placed>(StringComparer.Ordinal);
			var groupBoxSize = new Dictionary<string, (double W, double H)>(StringComparer.Ordinal);

			foreach (var id in ownNodes)
			{
				if (nodeById.TryGetValue(id, out var n))
					itemNodes.Add(n);
			}

			foreach (var child in childSubs)
			{
				var cp = LayoutLevel([.. levelPath, child.Id], DirectNodes(child), child.Children, false);
				childPlaced[child.Id] = cp;
				var w = cp.Width + (2 * options.GroupPadding);
				var h = cp.Height + options.GroupHeaderHeight + (2 * options.GroupPadding);
				w = Math.Max(w, 80);
				groupBoxSize[child.Id] = (w, h);
				itemNodes.Add(new LayoutNode("\u0001sg:" + child.Id, w, h));
			}

			string ItemOf(string node)
			{
				var p = PathOf(node);
				return p.Count > depth ? "\u0001sg:" + p[depth] : node;
			}

			bool InLevel(string node)
			{
				var p = PathOf(node);
				if (p.Count < depth)
					return false;
				for (var i = 0; i < depth; i++)
				{
					if (p[i] != levelPath[i])
						return false;
				}
				return true;
			}

			var itemEdges = new List<LayoutEdge>();
			foreach (var e in input.Edges)
			{
				if (!InLevel(e.Source) || !InLevel(e.Target))
					continue;
				var a = ItemOf(e.Source);
				var b = ItemOf(e.Target);
				if (a == b)
					continue;
				itemEdges.Add(e with { Source = a, Target = b });
			}

			var placed = new Placed();
			if (itemNodes.Count == 0)
				return placed;

			var perRow = ComponentsPerRow(itemNodes, itemEdges);
			var horizontalFlow = input.Direction is LayoutDirection.LR or LayoutDirection.RL;
			var labelExtent = itemEdges.Select(e => horizontalFlow ? e.LabelWidth : e.LabelHeight).DefaultIfEmpty(0).Max();
			var levelOptions = options with
			{
				LayerSpacing = labelExtent > 0 ? Math.Max(options.LayerSpacing, labelExtent + (2 * 22) + 8) : options.LayerSpacing,
				Padding = 0,
				SeparateComponents = true,
				MaxComponentsPerRow = perRow,
				PortAwareLayout = false,
			};
			var flat = SugiyamaLayout.Compute(
				new LayoutGraph(input.Direction, itemNodes, itemEdges, []), levelOptions);

			foreach (var n in flat.Nodes)
			{
				if (n.Id.StartsWith("\u0001sg:", StringComparison.Ordinal))
				{
					var gid = n.Id["\u0001sg:".Length..];
					var cp = childPlaced[gid];
					var sg = subById[gid];
					var box = new GroupRect(gid, sg.Label, n.X, n.Y, n.Width, n.Height);
					var ox = n.X + options.GroupPadding + ((n.Width - (2 * options.GroupPadding) - cp.Width) / 2);
					var oy = n.Y + options.GroupHeaderHeight + options.GroupPadding;
					foreach (var (id, r) in cp.Nodes)
					{
						placed.Nodes[id] = (r.X + ox, r.Y + oy, r.W, r.H);
						_ = box.NodeIds.Add(id);
					}
					foreach (var g in cp.Groups)
					{
						var shifted = Shift(g, ox, oy);
						box.Children.Add(shifted);
						foreach (var id in shifted.NodeIds)
							_ = box.NodeIds.Add(id);
					}
					placed.Groups.Add(box);
				}
				else
				{
					placed.Nodes[n.Id] = (n.X, n.Y, n.Width, n.Height);
				}
			}

			placed.Width = flat.Width;
			placed.Height = flat.Height;
			return placed;
		}

		IEnumerable<string> DirectNodes(LayoutSubgraph sg) =>
			sg.NodeIds.Where(id => PathOf(id).Count > 0 && PathOf(id)[^1] == sg.Id);

		var rootNodes = input.Nodes.Select(n => n.Id).Where(id => PathOf(id).Count == 0).ToList();
		var root = LayoutLevel([], rootNodes, input.Subgraphs, true);

		var pad = options.Padding;
		var abs = new List<FlowEdgeRouter.Box>();
		foreach (var (id, r) in root.Nodes)
			abs.Add(new FlowEdgeRouter.Box(id, r.X + pad, r.Y + pad, r.W, r.H, nodeById[id].CentrePorts));
		var groupBoxes = new List<FlowEdgeRouter.GroupBox>();
		void Flatten(GroupRect g)
		{
			groupBoxes.Add(new FlowEdgeRouter.GroupBox(g.Id, g.X + pad, g.Y + pad, g.W, g.H, g.NodeIds));
			foreach (var c in g.Children)
				Flatten(c);
		}

		foreach (var g in root.Groups)
			Flatten(g);

		var routeEdges = new List<FlowEdgeRouter.RouteEdge>();
		for (var i = 0; i < input.Edges.Count; i++)
		{
			var e = input.Edges[i];
			routeEdges.Add(new FlowEdgeRouter.RouteEdge(i, e.Source, e.Target, e.LabelWidth, e.LabelHeight));
		}

		var routes = FlowEdgeRouter.Route(abs, groupBoxes, routeEdges, input.Direction);

		var nodesOut = abs.Select(b => new LayoutNodeResult(b.Id, b.X, b.Y, b.W, b.H)).ToList();
		var width = nodesOut.Count > 0 ? nodesOut.Max(n => n.X + n.Width) : 0;
		var height = nodesOut.Count > 0 ? nodesOut.Max(n => n.Y + n.Height) : 0;
		foreach (var g in groupBoxes)
		{
			width = Math.Max(width, g.X + g.W);
			height = Math.Max(height, g.Y + g.H);
		}

		foreach (var r in routes)
		{
			foreach (var p in r.Points)
			{
				width = Math.Max(width, p.X);
				height = Math.Max(height, p.Y);
			}
		}

		LayoutGroupResult ToResult(GroupRect g) => new(
			g.Id, g.Label, g.X + pad, g.Y + pad, g.W, g.H, g.Children.Select(ToResult).ToList());

		return new LayoutResult(width + pad, height + pad, nodesOut, routes, root.Groups.Select(ToResult).ToList());
	}

	private static GroupRect Shift(GroupRect g, double dx, double dy)
	{
		var s = new GroupRect(g.Id, g.Label, g.X + dx, g.Y + dy, g.W, g.H) { NodeIds = g.NodeIds };
		s.Children.AddRange(g.Children.Select(c => Shift(c, dx, dy)));
		return s;
	}

	private static int ComponentsPerRow(List<LayoutNode> nodes, List<LayoutEdge> edges)
	{
		var parent = nodes.ToDictionary(n => n.Id, n => n.Id, StringComparer.Ordinal);
		string Find(string id)
		{
			while (parent[id] != id)
			{
				parent[id] = parent[parent[id]];
				id = parent[id];
			}
			return id;
		}

		foreach (var e in edges)
		{
			if (parent.ContainsKey(e.Source) && parent.ContainsKey(e.Target))
				parent[Find(e.Source)] = Find(e.Target);
		}

		var count = nodes.Select(n => Find(n.Id)).Distinct().Count();
		return count <= 3 ? count : (int)Math.Ceiling(Math.Sqrt(count));
	}
}
