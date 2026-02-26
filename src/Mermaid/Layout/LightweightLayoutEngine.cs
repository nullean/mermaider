using Mermaid.Models;
using Mermaid.Rendering;
using Sugiyama;

namespace Mermaid.Layout;

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
			if (!graph.Nodes.TryGetValue(id, out var node)) continue;
			var (w, h) = NodeSizing.Estimate(node.Label, node.Shape);
			layoutNodes.Add(new LayoutNode(id, w, h));
		}

		var layoutEdges = new List<LayoutEdge>(graph.Edges.Count);
		foreach (var edge in graph.Edges)
		{
			double labelW = 0, labelH = 0;
			if (edge.Label is { Length: > 0 })
			{
				var metrics = Text.TextMetrics.MeasureMultiline(
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

		var subgraphIds = new HashSet<string>();
		CollectSubgraphIds(graph.Subgraphs, subgraphIds);

		return MapResult(result, graph, subgraphIds, strict);
	}

	private static void CollectSubgraphIds(IReadOnlyList<MermaidSubgraph> sgs, HashSet<string> ids)
	{
		foreach (var sg in sgs)
		{
			ids.Add(sg.Id);
			CollectSubgraphIds(sg.Children, ids);
		}
	}

	private static LayoutSubgraph MapSubgraph(MermaidSubgraph sg) =>
		new(sg.Id, sg.Label, sg.NodeIds, sg.Children.Select(MapSubgraph).ToList());

	private static PositionedGraph MapResult(LayoutResult result, MermaidGraph graph, HashSet<string> subgraphIds, StrictModeOptions? strict)
	{
		var nodeLookup = graph.Nodes;
		var positionedNodes = new List<PositionedNode>(result.Nodes.Count);
		foreach (var n in result.Nodes)
		{
			if (subgraphIds.Contains(n.Id))
				continue;

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
			if (e.OriginalIndex >= graph.Edges.Count) continue;
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
			result ??= new Dictionary<string, string>();
			foreach (var kvp in nodeStyle)
				result[kvp.Key] = kvp.Value;
		}

		return result;
	}
}
