using System.Runtime.CompilerServices;
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
	internal static PositionedGraph Layout(MermaidGraph graph, RenderOptions? options = null, StrictStylingOptions? strict = null,
		int maxNodesAfterLayout = int.MaxValue, CancellationToken ct = default)
	{
		var padding = options?.Padding ?? 16;
		var nodeSpacing = options?.NodeSpacing ?? LayoutDefaults.NodeSpacing;
		var layerSpacing = options?.LayerSpacing ?? 40;

		var nodeOrder = graph.NodeOrder.Count > 0
			? graph.NodeOrder
			: graph.Nodes.Keys.ToList();

		var layoutNodes = new List<LayoutNode>(graph.Nodes.Count);
		foreach (var id in nodeOrder)
		{
			if (!graph.Nodes.TryGetValue(id, out var node))
				continue;
			var (w, h) = NodeSizing.Estimate(node.Label, node.Shape);
			layoutNodes.Add(new LayoutNode(id, w, h)
			{
				Outline = node.Shape switch
				{
					Models.NodeShape.Diamond => PortOutline.Diamond,
					Models.NodeShape.Circle or Models.NodeShape.DoubleCircle => PortOutline.Ellipse,
					Models.NodeShape.Hexagon => PortOutline.Centre,
					_ => PortOutline.Rectangle,
				},
			});
		}

		var layoutEdges = new List<LayoutEdge>(graph.Edges.Count);
		var layoutEdgeToOriginal = new List<int>(graph.Edges.Count);
		var sameRankConstraints = new List<(string A, string B)>();
		// dagre inserts a virtual label node for labeled FLOWCHART edges, consuming one extra rank.
		// Only apply in flat (no-subgraph) non-state diagrams:
		//   - subgraphs: NestingGraphRanker border-node constraints conflict with the extra minLength
		//   - state diagrams: state transitions are already spaced correctly without the bonus
		var isStateDiagramEarly = graph.Nodes.Values.Any(n => n.Shape is Models.NodeShape.StateStart or Models.NodeShape.StateEnd);
		var applyLabelMinLength = false;
		for (var ei = 0; ei < graph.Edges.Count; ei++)
		{
			var edge = graph.Edges[ei];

			// Invisible edges (~~~) affect layout but are not drawn; treat them as zero-label layout edges
			// so the target is placed below the source (matching mermaid.js behavior).
			double labelW = 0, labelH = 0;
			if (edge.Style == Models.EdgeStyle.Invisible)
			{
				layoutEdgeToOriginal.Add(ei);
				layoutEdges.Add(new LayoutEdge(edge.Source, edge.Target, 0, 0, edge.MinLength));
				continue;
			}
			var minLength = edge.MinLength;
			if (edge.Label is { Length: > 0 })
			{
				var metrics = TextMetrics.MeasureMultiline(
					edge.Label.AsSpan(),
					RenderConstants.FontSizes.EdgeLabel,
					RenderConstants.FontWeights.EdgeLabel);
				labelW = ErSvgRenderer.LabelBoxWidth(metrics.Width) + 4;
				labelH = metrics.Height + ErSvgRenderer.LabelPadY + 2;
				if (applyLabelMinLength)
					minLength++;
			}
			layoutEdgeToOriginal.Add(ei);
			_ = graph.SubgraphEdgeRedirections.TryGetValue(ei, out var redirection);
			layoutEdges.Add(new LayoutEdge(edge.Source, edge.Target, labelW, labelH, minLength)
			{
				SourceGroup = redirection.SourceSubgraph,
				TargetGroup = redirection.TargetSubgraph,
			});
		}

		var layoutSubgraphs = graph.Subgraphs.Select(MapSubgraph).ToList();

		var direction = graph.Direction switch
		{
			Direction.LR => LayoutDirection.LR,
			Direction.RL => LayoutDirection.RL,
			Direction.BT => LayoutDirection.BT,
			_ => LayoutDirection.TD,
		};

		var layoutGraph = new LayoutGraph(direction, layoutNodes, layoutEdges, layoutSubgraphs)
		{
			SameRankConstraints = sameRankConstraints,
		};

		// The layer gap runs along the flow axis, so the label has to be measured along that axis too:
		// on LR/RL a label sized by its height overhangs the nodes either side and they paint over it.
		var horizontal = direction is LayoutDirection.LR or LayoutDirection.RL;

		// When a diagram has subgraphs, exclude edges that are entirely within the same subgraph
		// from the global spacing calculation. Inner edge labels (e.g. state transition labels)
		// would otherwise inflate layer spacing for every node including outer ones.
		var innerSubgraphNodeMap = graph.Subgraphs.Count > 0
			? BuildInnerSubgraphNodeMap(graph.Subgraphs)
			: null;

		// State diagrams use tighter layer spacing to match mermaid.js proportions (~53px center-to-center vs flowchart ~100px)
		var baseLayerSpacing = isStateDiagramEarly ? Math.Min(30, layerSpacing) : layerSpacing;

		var maxLabelExtent = layoutEdges
			.Select(
				e =>
				{
					if (innerSubgraphNodeMap != null
						&& innerSubgraphNodeMap.TryGetValue(e.Source, out var srcSg)
						&& innerSubgraphNodeMap.TryGetValue(e.Target, out var tgtSg)
						&& srcSg == tgtSg)
						return 0.0;
					return horizontal ? e.LabelWidth : e.LabelHeight;
				})
			.Where(v => v > 0)
			.DefaultIfEmpty(0)
			.Max();
		// For state diagrams, edge labels sit on the path and need less clearance than flowchart labels
		var labelClearance = isStateDiagramEarly ? 8 : 16;
		var effectiveLayerSpacing = maxLabelExtent > 0
			? Math.Max(baseLayerSpacing, maxLabelExtent + labelClearance)
			: baseLayerSpacing;

		var layoutOptions = new LayoutOptions
		{
			Padding = padding,
			NodeSpacing = nodeSpacing,
			LayerSpacing = baseLayerSpacing,
			CancellationToken = ct,
			MaxNodeCount = maxNodesAfterLayout,
			ForceBottomExitFanOut = isStateDiagramEarly,
			NaturalBackEdgeRouting = isStateDiagramEarly,
			UseModelOrderForVirtualNodes = true,
		};

		LayoutResult result;
		try
		{
			result = HierarchicalLayout.Compute(layoutGraph, layoutOptions);
		}
		catch (InvalidOperationException ex) when (ex.Message.Contains("MaxNodesAfterLayout") || ex.Message.Contains("node count"))
		{
			throw new MermaidResourceLimitException(
				nameof(ResourceLimits.MaxNodesAfterLayout), 0, maxNodesAfterLayout, ex);
		}
		catch (InsufficientExecutionStackException ex)
		{
			throw new MermaidResourceLimitException(
				nameof(ResourceLimits.MaxRecursionDepth), 0, maxNodesAfterLayout, ex);
		}
		var positioned = MapResult(result, graph, strict, layoutEdgeToOriginal);

		if (graph.SubgraphEdgeRedirections.Count > 0)
			ClipSubgraphEdges(positioned, graph);

		if (graph.Notes.Count > 0)
			positioned = AttachNotes(positioned, graph);

		return positioned;
	}

	private static LayoutSubgraph MapSubgraph(MermaidSubgraph sg) =>
		new(sg.Id, sg.Label, sg.NodeIds, sg.Children.Select(MapSubgraph).ToList());

	// Returns nodeId → subgraphId for every node that belongs to a leaf-level subgraph.
	// Used to exclude intra-subgraph edge labels from the global layer-spacing calculation.
	private static Dictionary<string, string> BuildInnerSubgraphNodeMap(
		IEnumerable<MermaidSubgraph> subgraphs)
	{
		var map = new Dictionary<string, string>();
		foreach (var sg in subgraphs)
			CollectSubgraphNodes(sg, map);
		return map;
	}

	private static void CollectSubgraphNodes(MermaidSubgraph sg, Dictionary<string, string> map)
	{
		foreach (var id in sg.NodeIds)
			map[id] = sg.Id;
		foreach (var child in sg.Children)
			CollectSubgraphNodes(child, map);
	}

	private static PositionedGraph MapResult(LayoutResult result, MermaidGraph graph, StrictStylingOptions? strict, List<int>? layoutEdgeToOriginal = null)
	{
		var nodeLookup = graph.Nodes;
		var positionedNodes = new List<PositionedNode>(result.Nodes.Count);
		foreach (var n in result.Nodes)
		{
			var inlineStyle = strict is null ? ResolveNodeStyle(n.Id, graph) : null;
			var cssClass = strict is not null && graph.ClassAssignments.TryGetValue(n.Id, out var cls) ? cls : null;

			var hasNode = nodeLookup.TryGetValue(n.Id, out var mn);
			var label = hasNode ? NodeSizing.WrapLabel(mn.Label) : n.Id;
			positionedNodes.Add(new PositionedNode
			{
				Id = n.Id,
				Label = label,
				Shape = hasNode ? mn.Shape : NodeShape.Rectangle,
				X = n.X,
				Y = n.Y,
				Width = n.Width,
				Height = n.Height,
				InlineStyle = inlineStyle,
				CssClassName = cssClass,
				IsMarkdown = hasNode && mn.IsMarkdown,
				SemanticRole = graph.ClassAssignments.TryGetValue(n.Id, out var roleClass) && (strict is null || strict.AllowedClasses.Any(c => c.Name == roleClass)) ? Rendering.ClusterPalette.RoleName(roleClass) : null,
			});
		}

		var positionedEdges = new List<PositionedEdge>(result.Edges.Count);
		foreach (var e in result.Edges)
		{
			var origIdx = layoutEdgeToOriginal is not null && e.OriginalIndex < layoutEdgeToOriginal.Count
				? layoutEdgeToOriginal[e.OriginalIndex]
				: e.OriginalIndex;
			if (origIdx >= graph.Edges.Count)
				continue;
			var mermaidEdge = graph.Edges[origIdx];

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
				InlineStyle = strict is null ? ResolveEdgeStyle(origIdx, graph) : null,
			});
		}

		var groups = result.Groups.Select(g => MapGroup(g, graph, strict)).ToList();

		return new PositionedGraph
		{
			Width = result.Width,
			Height = result.Height,
			Nodes = positionedNodes,
			Edges = positionedEdges,
			Groups = groups,
		};
	}

	private static PositionedGroup MapGroup(LayoutGroupResult g, MermaidGraph graph, StrictStylingOptions? strict) =>
		new()
		{
			Id = g.Id,
			Label = g.Label,
			X = g.X,
			Y = g.Y,
			Width = g.Width,
			Height = g.Height,
			Children = g.Children.Select(c => MapGroup(c, graph, strict)).ToList(),
			InlineStyle = strict is null ? ResolveNodeStyle(g.Id, graph) : null,
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

	private static IReadOnlyDictionary<string, string>? ResolveEdgeStyle(int edgeIndex, MermaidGraph graph)
	{
		Dictionary<string, string>? result = null;

		if (graph.DefaultEdgeStyle is { } defaults)
		{
			result = new Dictionary<string, string>(defaults);
		}

		if (graph.EdgeStyles.TryGetValue(edgeIndex, out var specific))
		{
			result ??= [];
			foreach (var kvp in specific)
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

			// Fallback: source is inside the box — clip at the exit point
			for (var i = 0; i < points.Count - 1; i++)
			{
				var p0 = points[i];
				var p1 = points[i + 1];
				if (IsInsideBox(p0, bx, by, bw, bh) && !IsInsideBox(p1, bx, by, bw, bh))
				{
					var hit = IntersectSegmentRect(p0, p1, bx, by, bw, bh);
					if (hit != null)
					{
						points.RemoveRange(0, i + 1);
						points.Insert(0, hit.Value);
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

	private const double NoteWidth = 120;
	private const double NoteHPad = 10;
	private const double NoteVPad = 8;
	private const double NoteGap = 30;

	private static PositionedGraph AttachNotes(PositionedGraph positioned, MermaidGraph graph)
	{
		var nodeLookup = new Dictionary<string, PositionedNode>(positioned.Nodes.Count);
		foreach (var n in positioned.Nodes)
			nodeLookup[n.Id] = n;

		var notes = new List<PositionedGraphNote>(graph.Notes.Count);
		var minX = 0.0;
		var maxX = positioned.Width;
		var maxY = positioned.Height;

		foreach (var note in graph.Notes)
		{
			if (!nodeLookup.TryGetValue(note.TargetNodeId, out var target))
				continue;

			var textW = TextMetrics.MeasureTextWidth(
				note.Text, RenderConstants.FontSizes.EdgeLabel, RenderConstants.FontWeights.EdgeLabel) + (NoteHPad * 2);
			var noteW = Math.Max(NoteWidth, textW);
			var noteH = RenderConstants.FontSizes.EdgeLabel + (NoteVPad * 2);

			var leftNote = note.Position == GraphNotePosition.Left;
			var noteX = leftNote
				? target.X - noteW - NoteGap
				: target.X + target.Width + NoteGap;

			var noteY = target.Y + ((target.Height - noteH) / 2);
			var midNoteY = noteY + (noteH / 2);

			var lineFrom = leftNote
				? new Point(noteX + noteW, midNoteY)
				: new Point(noteX, midNoteY);
			var lineTo = leftNote
				? new Point(target.X, target.Y + (target.Height / 2))
				: new Point(target.X + target.Width, target.Y + (target.Height / 2));

			notes.Add(new PositionedGraphNote
			{
				Text = note.Text,
				X = noteX,
				Y = noteY,
				Width = noteW,
				Height = noteH,
				LineFrom = lineFrom,
				LineTo = lineTo,
			});

			minX = Math.Min(minX, noteX - NoteGap);
			maxX = Math.Max(maxX, noteX + noteW + NoteGap);
			maxY = Math.Max(maxY, noteY + noteH + NoteGap);
		}

		return positioned with
		{
			MinX = minX,
			Width = maxX,
			Height = maxY,
			Notes = notes,
		};
	}
}
