using Mermaider.Models;
using Mermaider.Rendering;
using Mermaider.Text;
using Sugiyama;

namespace Mermaider.Layout;

/// <summary>
/// Adapter that bridges <see cref="MermaidGraph"/> to the standalone
/// <see cref="SugiyamaLayout"/> engine and maps the result back to
/// <see cref="PositionedGraph"/>.
/// </summary>
internal static class LightweightLayoutEngine
{
	internal static PositionedGraph Layout(MermaidGraph graph, RenderOptions? options = null, StrictModeOptions? strict = null)
	{
		var padding = options?.Padding ?? LayoutDefaults.Padding;
		var nodeSpacing = options?.NodeSpacing ?? LayoutDefaults.NodeSpacing;
		var layerSpacing = options?.LayerSpacing ?? LayoutDefaults.LayerSpacing;

		var nodeOrder = graph.NodeOrder.Count > 0
			? graph.NodeOrder
			: graph.Nodes.Keys.ToList();

		var layoutNodes = new List<LayoutNode>(graph.Nodes.Count);
		foreach (var id in nodeOrder)
		{
			if (!graph.Nodes.TryGetValue(id, out var node))
				continue;
			var (w, h) = NodeSizing.Estimate(node.Label, node.Shape);
			layoutNodes.Add(new LayoutNode(id, w, h));
		}

		var layoutEdges = new List<LayoutEdge>(graph.Edges.Count);
		foreach (var edge in graph.Edges)
		{
			double labelW = 0, labelH = 0;
			if (edge.Label is { Length: > 0 })
			{
				var metrics = TextMetrics.MeasureMultiline(
					edge.Label.AsSpan(),
					RenderConstants.FontSizes.EdgeLabel,
					RenderConstants.FontWeights.EdgeLabel);
				labelW = metrics.Width + 8;
				labelH = metrics.Height + 6;
			}
			layoutEdges.Add(new LayoutEdge(edge.Source, edge.Target, labelW, labelH));
		}

		var layoutSubgraphs = graph.Subgraphs.Select(MapSubgraph).ToList();

		var direction = graph.Direction switch
		{
			Direction.LR => LayoutDirection.LR,
			Direction.RL => LayoutDirection.RL,
			Direction.BT => LayoutDirection.BT,
			_ => LayoutDirection.TD,
		};

		var layoutGraph = new LayoutGraph(direction, layoutNodes, layoutEdges, layoutSubgraphs);
		var layoutOptions = new LayoutOptions
		{
			Padding = padding,
			NodeSpacing = nodeSpacing,
			LayerSpacing = layerSpacing,
		};

		var result = SugiyamaLayout.Compute(layoutGraph, layoutOptions);
		var positioned = MapResult(result, graph, strict);

		if (graph.SubgraphEdgeRedirections.Count > 0)
			ClipSubgraphEdges(positioned, graph);

		return positioned;
	}

	private static LayoutSubgraph MapSubgraph(MermaidSubgraph sg) =>
		new(sg.Id, sg.Label, sg.NodeIds, sg.Children.Select(MapSubgraph).ToList());

	private static PositionedGraph MapResult(LayoutResult result, MermaidGraph graph, StrictModeOptions? strict)
	{
		var nodeLookup = graph.Nodes;
		var positionedNodes = new List<PositionedNode>(result.Nodes.Count);
		foreach (var n in result.Nodes)
		{
			var inlineStyle = strict is null ? ResolveNodeStyle(n.Id, graph) : null;
			var cssClass = strict is not null && graph.ClassAssignments.TryGetValue(n.Id, out var cls) ? cls : null;

			positionedNodes.Add(new PositionedNode
			{
				Id = n.Id,
				Label = nodeLookup.TryGetValue(n.Id, out var mn) ? mn.Label : n.Id,
				Shape = nodeLookup.TryGetValue(n.Id, out var mn2) ? mn2.Shape : NodeShape.Rectangle,
				X = n.X,
				Y = n.Y,
				Width = n.Width,
				Height = n.Height,
				InlineStyle = inlineStyle,
				CssClassName = cssClass,
			});
		}

		var positionedEdges = new List<PositionedEdge>(result.Edges.Count);
		foreach (var e in result.Edges)
		{
			if (e.OriginalIndex >= graph.Edges.Count)
				continue;
			var mermaidEdge = graph.Edges[e.OriginalIndex];

			var points = new List<Models.Point>(e.Points.Count);
			foreach (var p in e.Points)
				points.Add(new Models.Point(p.X, p.Y));

			Models.Point? labelPos = e.LabelPosition is { } lp
				? new Models.Point(lp.X, lp.Y)
				: null;

			positionedEdges.Add(new PositionedEdge
			{
				Source = mermaidEdge.Source,
				Target = mermaidEdge.Target,
				Label = mermaidEdge.Label,
				Style = mermaidEdge.Style,
				HasArrowStart = mermaidEdge.HasArrowStart,
				HasArrowEnd = mermaidEdge.HasArrowEnd,
				Points = points,
				LabelPosition = labelPos,
			});
		}

		var groups = result.Groups.Select(MapGroup).ToList();

		return new PositionedGraph
		{
			Width = result.Width,
			Height = result.Height,
			Nodes = positionedNodes,
			Edges = positionedEdges,
			Groups = groups,
		};
	}

	private static PositionedGroup MapGroup(LayoutGroupResult g) =>
		new()
		{
			Id = g.Id,
			Label = g.Label,
			X = g.X,
			Y = g.Y,
			Width = g.Width,
			Height = g.Height,
			Children = g.Children.Select(MapGroup).ToList(),
		};

	private static IReadOnlyDictionary<string, string>? ResolveNodeStyle(string nodeId, MermaidGraph graph)
	{
		Dictionary<string, string>? result = null;

		if (graph.ClassAssignments.TryGetValue(nodeId, out var className) &&
			graph.ClassDefs.TryGetValue(className, out var classDef))
		{
			result = new Dictionary<string, string>(classDef);
		}

		if (graph.NodeStyles.TryGetValue(nodeId, out var nodeStyle))
		{
			result ??= [];
			foreach (var kvp in nodeStyle)
				result[kvp.Key] = kvp.Value;
		}

		return result;
	}

	private static void ClipSubgraphEdges(PositionedGraph positioned, MermaidGraph graph)
	{
		var groupLookup = new Dictionary<string, PositionedGroup>();
		CollectGroups(positioned.Groups, groupLookup);

		var edges = (List<PositionedEdge>)positioned.Edges;
		for (var i = 0; i < edges.Count; i++)
		{
			var e = edges[i];
			var mIdx = FindOriginalEdgeIndex(e, graph);
			if (mIdx < 0 || !graph.SubgraphEdgeRedirections.TryGetValue(mIdx, out var redir))
				continue;

			var pts = new List<Models.Point>(e.Points);
			var changed = false;

			if (redir.SourceSubgraph is { } srcSg && groupLookup.TryGetValue(srcSg, out var srcGroup))
				changed |= ClipEndAtBox(pts, srcGroup, clipStart: true);

			if (redir.TargetSubgraph is { } tgtSg && groupLookup.TryGetValue(tgtSg, out var tgtGroup))
				changed |= ClipEndAtBox(pts, tgtGroup, clipStart: false);

			if (changed)
				edges[i] = e with { Points = pts };
		}
	}

	private static int FindOriginalEdgeIndex(PositionedEdge pe, MermaidGraph graph)
	{
		for (var i = 0; i < graph.Edges.Count; i++)
		{
			var me = graph.Edges[i];
			if (me.Source == pe.Source && me.Target == pe.Target && me.Label == pe.Label)
				return i;
		}
		return -1;
	}

	private static void CollectGroups(IReadOnlyList<PositionedGroup> groups, Dictionary<string, PositionedGroup> lookup)
	{
		foreach (var g in groups)
		{
			lookup[g.Id] = g;
			CollectGroups(g.Children, lookup);
		}
	}

	private static bool ClipEndAtBox(List<Models.Point> points, PositionedGroup box, bool clipStart)
	{
		var bx = box.X;
		var by = box.Y;
		var bw = box.Width;
		var bh = box.Height;

		if (clipStart)
		{
			for (var i = 0; i < points.Count - 1; i++)
			{
				var p0 = points[i];
				var p1 = points[i + 1];
				if (!IsInsideBox(p0, bx, by, bw, bh) && IsInsideBox(p1, bx, by, bw, bh))
				{
					var hit = IntersectSegmentRect(p0, p1, bx, by, bw, bh);
					if (hit != null)
					{
						points.RemoveRange(i + 1, points.Count - i - 1);
						points.Add(hit.Value);
						return true;
					}
				}
			}
		}
		else
		{
			for (var i = points.Count - 1; i > 0; i--)
			{
				var p0 = points[i - 1];
				var p1 = points[i];
				if (IsInsideBox(p0, bx, by, bw, bh) && !IsInsideBox(p1, bx, by, bw, bh))
				{
					var hit = IntersectSegmentRect(p0, p1, bx, by, bw, bh);
					if (hit != null)
					{
						points.RemoveRange(0, i);
						points.Insert(0, hit.Value);
						return true;
					}
				}
			}

			// Target is inside the box — clip at box border from outside
			for (var i = points.Count - 1; i > 0; i--)
			{
				var p0 = points[i - 1];
				var p1 = points[i];
				if (!IsInsideBox(p0, bx, by, bw, bh))
				{
					var hit = IntersectSegmentRect(p0, p1, bx, by, bw, bh);
					if (hit != null)
					{
						points.RemoveRange(i, points.Count - i);
						points.Add(hit.Value);
						return true;
					}
				}
			}
		}

		return false;
	}

	private static bool IsInsideBox(Models.Point p, double bx, double by, double bw, double bh)
		=> p.X >= bx && p.X <= bx + bw && p.Y >= by && p.Y <= by + bh;

	private static Models.Point? IntersectSegmentRect(Models.Point a, Models.Point b, double rx, double ry, double rw, double rh)
	{
		Models.Point? best = null;
		var bestDist = double.MaxValue;

		TryEdge(a, b, rx, ry, rx + rw, ry, ref best, ref bestDist);         // top
		TryEdge(a, b, rx, ry + rh, rx + rw, ry + rh, ref best, ref bestDist); // bottom
		TryEdge(a, b, rx, ry, rx, ry + rh, ref best, ref bestDist);           // left
		TryEdge(a, b, rx + rw, ry, rx + rw, ry + rh, ref best, ref bestDist); // right

		return best;
	}

	private static void TryEdge(Models.Point a, Models.Point b, double x1, double y1, double x2, double y2, ref Models.Point? best, ref double bestDist)
	{
		var dx = b.X - a.X;
		var dy = b.Y - a.Y;
		var ex = x2 - x1;
		var ey = y2 - y1;
		var denom = (dx * ey) - (dy * ex);
		if (Math.Abs(denom) < 1e-10)
			return;

		var t = (((x1 - a.X) * ey) - ((y1 - a.Y) * ex)) / denom;
		var u = (((x1 - a.X) * dy) - ((y1 - a.Y) * dx)) / denom;

		if (t < 0 || t > 1 || u < 0 || u > 1)
			return;

		var px = a.X + (dx * t);
		var py = a.Y + (dy * t);
		var d = t;
		if (d < bestDist)
		{ bestDist = d; best = new Models.Point(px, py); }
	}
}
