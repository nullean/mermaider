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

			// Mutually-referencing items (a cycle through subgraph boxes) are ordered by the order that turns the fewest edges backwards;
			// only the layout copy of the edges is re-oriented.
			itemEdges = OrientCycles(itemNodes.Select(n => n.Id).ToList(), OrientByMajority(itemEdges));

			var perRow = ComponentsPerRow(itemNodes, itemEdges);
			var horizontalFlow = input.Direction is LayoutDirection.LR or LayoutDirection.RL;
			var labelExtent = itemEdges.Select(e => horizontalFlow ? e.LabelWidth : e.LabelHeight).DefaultIfEmpty(0).Max();
			// labelled edges between the same two items run side by side in one gap: their labels stack along the flow axis
			var busiestPair = itemEdges
				.Where(e => e.LabelWidth > 0)
				.GroupBy(e => string.CompareOrdinal(e.Source, e.Target) < 0 ? (e.Source, e.Target) : (e.Target, e.Source))
				.Select(g => g.Count())
				.DefaultIfEmpty(0)
				.Max();
			if (busiestPair > 1 && !horizontalFlow)
				labelExtent *= busiestPair;
			var levelOptions = options with
			{
				LayerSpacing = labelExtent > 0 ? Math.Max(options.LayerSpacing, labelExtent + (2 * 22) + 8) : options.LayerSpacing,
				Padding = 0,
				SeparateComponents = true,
				MaxComponentsPerRow = perRow,
				PortAwareLayout = false,
				CrossingRestarts = Math.Max(options.CrossingRestarts, 16),
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

		ReduceCrossingsBySwaps(root.Nodes, input, PathOf);

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

	/// <summary>
	/// Each level is laid out on its own, so edges crossing a subgraph border are invisible to its interior ordering.
	/// Here, on the composed boxes, adjacent same-layer siblings are swapped (span preserved) whenever that lowers the
	/// number of crossings between straight centre-to-centre edges.
	/// </summary>
	private static void ReduceCrossingsBySwaps(
		Dictionary<string, (double X, double Y, double W, double H)> nodes, LayoutGraph input, Func<string, List<string>> pathOf)
	{
		var vertical = input.Direction is LayoutDirection.TD or LayoutDirection.BT;
		var edges = input.Edges.Where(e => e.Source != e.Target && nodes.ContainsKey(e.Source) && nodes.ContainsKey(e.Target)).ToList();
		if (edges.Count is < 3 or > 80)
			return;

		(double X, double Y) C(string id) => (nodes[id].X + (nodes[id].W / 2), nodes[id].Y + (nodes[id].H / 2));

		bool Cross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
		{
			static double Orient((double X, double Y) p, (double X, double Y) q, (double X, double Y) r) => ((q.X - p.X) * (r.Y - p.Y)) - ((q.Y - p.Y) * (r.X - p.X));
			var s1 = Orient(a, b, c) * Orient(a, b, d);
			var s2 = Orient(c, d, a) * Orient(c, d, b);
			return s1 < -1e-6 && s2 < -1e-6;
		}

		int Count()
		{
			var n = 0;
			for (var i = 0; i < edges.Count; i++)
			{
				var a = C(edges[i].Source);
				var b = C(edges[i].Target);
				for (var j = i + 1; j < edges.Count; j++)
				{
					var e = edges[j];
					if (e.Source == edges[i].Source || e.Target == edges[i].Target || e.Source == edges[i].Target || e.Target == edges[i].Source)
						continue;
					if (Cross(a, b, C(e.Source), C(e.Target)))
						n++;
				}
			}
			return n;
		}

		var layers = nodes.Keys
			.GroupBy(id => (string.Join('/', pathOf(id)), Math.Round(vertical ? C(id).Y : C(id).X)))
			.Where(g => g.Count() > 1)
			.Select(g => g.ToList())
			.ToList();

		var best = Count();
		for (var pass = 0; pass < 4 && best > 0; pass++)
		{
			var improved = false;
			foreach (var layer in layers)
			{
				layer.Sort((a, b) => (vertical ? nodes[a].X : nodes[a].Y).CompareTo(vertical ? nodes[b].X : nodes[b].Y));
				for (var i = 0; i + 1 < layer.Count; i++)
				{
					var a = layer[i];
					var b = layer[i + 1];
					var na = nodes[a];
					var nb = nodes[b];
					var gap = vertical ? nb.X - (na.X + na.W) : nb.Y - (na.Y + na.H);
					(double X, double Y, double W, double H) sa, sb;
					if (vertical)
					{
						sb = (na.X, nb.Y, nb.W, nb.H);
						sa = (na.X + nb.W + gap, na.Y, na.W, na.H);
					}
					else
					{
						sb = (nb.X, na.Y, nb.W, nb.H);
						sa = (na.X, na.Y + nb.H + gap, na.W, na.H);
					}

					nodes[a] = sa;
					nodes[b] = sb;
					var c = Count();
					if (c < best)
					{
						best = c;
						improved = true;
						layer[i] = b;
						layer[i + 1] = a;
					}
					else
					{
						nodes[a] = na;
						nodes[b] = nb;
					}
				}
			}

			if (!improved)
				break;
		}
	}

	/// <summary>
	/// Items that reference each other through a cycle are laid out in the order that turns the fewest edges backwards:
	/// a weighted greedy feedback-arc-set order, used only when it beats declaration order. Only edges
	/// inside a strongly connected component are re-oriented, and only in the layout copy.
	/// </summary>
	private static List<LayoutEdge> OrientCycles(List<string> ids, List<LayoutEdge> edges)
	{
		var scc = StronglyConnected(ids, edges);
		if (!edges.Any(e => scc[e.Source] == scc[e.Target]))
			return edges;

		var inner = edges.Where(e => scc[e.Source] == scc[e.Target]).ToList();
		var declared = ids.Select((id, i) => (id, i)).ToDictionary(t => t.id, t => t.i, StringComparer.Ordinal);
		var greedy = GreedyOrder(ids, inner);
		int Back(Dictionary<string, int> pos) => inner.Count(e => pos[e.Source] > pos[e.Target]);
		// Cycles whose edges all weigh the same have no better answer than the default cycle breaking; weighted ones
		// (several edges one way, few the other) follow whichever order turns the fewest edges backwards.
		var weighted = inner.GroupBy(e => (e.Source, e.Target)).Any(g => g.Count() > 1);
		if (!weighted && Back(greedy) >= Back(declared))
			return edges;
		var pos = Back(greedy) < Back(declared) ? greedy : declared;
		return edges.Select(e => scc[e.Source] == scc[e.Target] && pos[e.Source] > pos[e.Target] ? e with { Source = e.Target, Target = e.Source } : e).ToList();
	}

	/// <summary>Items that reference each other (edges in both directions) are laid out in the direction that carries more edges.</summary>
	private static List<LayoutEdge> OrientByMajority(List<LayoutEdge> edges)
	{
		var counts = new Dictionary<(string, string), int>();
		foreach (var e in edges)
			counts[(e.Source, e.Target)] = counts.GetValueOrDefault((e.Source, e.Target)) + 1;
		return edges.Select(e =>
		{
			var forward = counts[(e.Source, e.Target)];
			var back = counts.GetValueOrDefault((e.Target, e.Source));
			return back > forward ? e with { Source = e.Target, Target = e.Source } : e;
		}).ToList();
	}

	private static Dictionary<string, int> GreedyOrder(List<string> ids, List<LayoutEdge> edges)
	{
		var outW = ids.ToDictionary(i => i, _ => 0, StringComparer.Ordinal);
		var inW = ids.ToDictionary(i => i, _ => 0, StringComparer.Ordinal);
		foreach (var e in edges)
		{
			outW[e.Source]++;
			inW[e.Target]++;
		}

		var remaining = ids.ToHashSet(StringComparer.Ordinal);
		var left = new List<string>();
		var right = new List<string>();
		void Remove(string id)
		{
			_ = remaining.Remove(id);
			foreach (var e in edges)
			{
				if (e.Source == id && remaining.Contains(e.Target))
					inW[e.Target]--;
				else if (e.Target == id && remaining.Contains(e.Source))
					outW[e.Source]--;
			}
		}

		while (remaining.Count > 0)
		{
			var sink = ids.FirstOrDefault(i => remaining.Contains(i) && outW[i] <= 0);
			if (sink is not null)
			{
				right.Add(sink);
				Remove(sink);
				continue;
			}

			var source = ids.FirstOrDefault(i => remaining.Contains(i) && inW[i] <= 0);
			if (source is not null)
			{
				left.Add(source);
				Remove(source);
				continue;
			}

			var pick = ids.Where(remaining.Contains).OrderByDescending(i => outW[i] - inW[i]).First();
			left.Add(pick);
			Remove(pick);
		}

		right.Reverse();
		var pos = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var id in left.Concat(right))
			pos[id] = pos.Count;
		return pos;
	}

	private static Dictionary<string, int> StronglyConnected(List<string> ids, List<LayoutEdge> edges)
	{
		var adj = ids.ToDictionary(i => i, _ => new List<string>(), StringComparer.Ordinal);
		foreach (var e in edges)
			adj[e.Source].Add(e.Target);

		var index = new Dictionary<string, int>(StringComparer.Ordinal);
		var low = new Dictionary<string, int>(StringComparer.Ordinal);
		var onStack = new HashSet<string>(StringComparer.Ordinal);
		var stack = new Stack<string>();
		var comp = new Dictionary<string, int>(StringComparer.Ordinal);
		var counter = 0;
		var compId = 0;

		void Visit(string v)
		{
			index[v] = low[v] = counter++;
			stack.Push(v);
			_ = onStack.Add(v);
			foreach (var w in adj[v])
			{
				if (!index.TryGetValue(w, out var iw))
				{
					Visit(w);
					low[v] = Math.Min(low[v], low[w]);
				}
				else if (onStack.Contains(w))
				{
					low[v] = Math.Min(low[v], iw);
				}
			}

			if (low[v] != index[v])
				return;
			string x;
			do
			{
				x = stack.Pop();
				_ = onStack.Remove(x);
				comp[x] = compId;
			}
			while (x != v);
			compId++;
		}

		foreach (var id in ids)
		{
			if (!index.ContainsKey(id))
				Visit(id);
		}
		return comp;
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
