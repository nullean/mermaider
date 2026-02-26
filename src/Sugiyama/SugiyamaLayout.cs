using Sugiyama.Internal;

namespace Sugiyama;

/// <summary>
/// Lightweight Sugiyama layered layout engine for directed graphs.
/// Zero external dependencies, allocation-aware, designed for Mermaid-sized graphs (&lt;50 nodes).
/// Disconnected components are detected and laid out independently, then tiled.
/// </summary>
public static class SugiyamaLayout
{
	/// <summary>
	/// Compute a layered layout for the given graph.
	/// </summary>
	public static LayoutResult Compute(LayoutGraph input, LayoutOptions? options = null)
	{
		options ??= LayoutOptions.Default;

		if (!options.SeparateComponents)
			return ComputeSingle(input, options);

		var components = FindConnectedComponents(input);
		if (components.Count <= 1)
			return ComputeSingle(input, options);

		return ComputeMultiComponent(input, components, options);
	}

	private static LayoutResult ComputeSingle(LayoutGraph input, LayoutOptions options)
	{
		using var buf = BuildBuffer(input);

		// For LR/RL, swap W/H before layout so the canonical TD pipeline uses the
		// correct dimension per axis. The direction transform swaps them back, so
		// the final visual dimensions match the original node sizes.
		if (input.Direction is LayoutDirection.LR or LayoutDirection.RL)
		{
			for (var i = 0; i < buf.RealNodeCount; i++)
				(buf.NodeWidths[i], buf.NodeHeights[i]) = (buf.NodeHeights[i], buf.NodeWidths[i]);
		}

		CycleRemover.Run(buf);
		LayerAssigner.Run(buf);

		if (input.Subgraphs.Count > 0)
			PromoteDisconnectedSubgraphNodes(buf, input);

		CrossingMinimizer.Run(buf, options.CrossingIterations);
		CoordinateAssigner.Run(buf, options.NodeSpacing, options.LayerSpacing);

		if (input.Subgraphs.Count > 0)
		{
			CompactDisconnectedSubgraphNodes(buf, input, options.NodeSpacing);
			FixSubgraphSpacing(buf, input);
		}

		var useSideRouting = input.Direction is LayoutDirection.LR or LayoutDirection.RL;
		var routes = EdgeRouter.Run(buf, useSideRouting);

		var direction = input.Direction switch
		{
			LayoutDirection.LR => DirectionTransform.Direction.LR,
			LayoutDirection.RL => DirectionTransform.Direction.RL,
			LayoutDirection.BT => DirectionTransform.Direction.BT,
			_ => DirectionTransform.Direction.TD,
		};

		DirectionTransform.Run(buf, routes, direction);
		DirectionTransform.Normalize(buf, routes, options.Padding);

		return ExtractResult(buf, input, routes, options.Padding);
	}

	// ========================================================================
	// Connected component detection + multi-component layout
	// ========================================================================

	private static List<HashSet<string>> FindConnectedComponents(LayoutGraph input)
	{
		var adj = new Dictionary<string, HashSet<string>>(input.Nodes.Count);
		foreach (var node in input.Nodes)
			adj[node.Id] = [];

		foreach (var edge in input.Edges)
		{
			if (adj.ContainsKey(edge.Source) && adj.ContainsKey(edge.Target))
			{
				adj[edge.Source].Add(edge.Target);
				adj[edge.Target].Add(edge.Source);
			}
		}

		// Include subgraph membership as connectivity
		foreach (var sg in input.Subgraphs)
			ConnectSubgraphNodes(adj, sg);

		var visited = new HashSet<string>(input.Nodes.Count);
		var components = new List<HashSet<string>>();

		foreach (var node in input.Nodes)
		{
			if (visited.Contains(node.Id)) continue;

			var component = new HashSet<string>();
			var queue = new Queue<string>();
			queue.Enqueue(node.Id);
			visited.Add(node.Id);

			while (queue.Count > 0)
			{
				var current = queue.Dequeue();
				component.Add(current);

				if (!adj.TryGetValue(current, out var neighbors)) continue;
				foreach (var neighbor in neighbors)
				{
					if (visited.Add(neighbor))
						queue.Enqueue(neighbor);
				}
			}

			components.Add(component);
		}

		return components;
	}

	private static void ConnectSubgraphNodes(Dictionary<string, HashSet<string>> adj, LayoutSubgraph sg)
	{
		var nodeIds = sg.NodeIds;
		for (var i = 1; i < nodeIds.Count; i++)
		{
			if (adj.ContainsKey(nodeIds[i - 1]) && adj.ContainsKey(nodeIds[i]))
			{
				adj[nodeIds[i - 1]].Add(nodeIds[i]);
				adj[nodeIds[i]].Add(nodeIds[i - 1]);
			}
		}

		foreach (var child in sg.Children)
			ConnectSubgraphNodes(adj, child);
	}

	private static LayoutResult ComputeMultiComponent(
		LayoutGraph input, List<HashSet<string>> components, LayoutOptions options)
	{
		var componentResults = new List<(LayoutResult Result, Dictionary<int, int> EdgeMap)>();

		foreach (var component in components)
		{
			var subNodes = new List<LayoutNode>();
			foreach (var node in input.Nodes)
			{
				if (component.Contains(node.Id))
					subNodes.Add(node);
			}

			var edgeMap = new Dictionary<int, int>();
			var subEdges = new List<LayoutEdge>();
			for (var i = 0; i < input.Edges.Count; i++)
			{
				var e = input.Edges[i];
				if (component.Contains(e.Source) && component.Contains(e.Target))
				{
					edgeMap[subEdges.Count] = i;
					subEdges.Add(e);
				}
			}

			var subSubgraphs = FilterSubgraphs(input.Subgraphs, component);
			var subGraph = new LayoutGraph(input.Direction, subNodes, subEdges, subSubgraphs);
			var result = ComputeSingle(subGraph, options);
			componentResults.Add((result, edgeMap));
		}

		return ArrangeComponents(componentResults, input.Direction, options);
	}

	private static List<LayoutSubgraph> FilterSubgraphs(
		IReadOnlyList<LayoutSubgraph> subgraphs, HashSet<string> component)
	{
		var result = new List<LayoutSubgraph>();
		foreach (var sg in subgraphs)
		{
			if (sg.NodeIds.Any(id => component.Contains(id)))
			{
				var filteredChildren = FilterSubgraphs(sg.Children, component);
				var filteredNodeIds = sg.NodeIds.Where(id => component.Contains(id)).ToList();
				result.Add(new LayoutSubgraph(sg.Id, sg.Label, filteredNodeIds, filteredChildren));
			}
		}
		return result;
	}

	private static LayoutResult ArrangeComponents(
		List<(LayoutResult Result, Dictionary<int, int> EdgeMap)> components,
		LayoutDirection direction,
		LayoutOptions options)
	{
		// For TD/BT tile horizontally; for LR/RL tile vertically
		var tileHorizontally = direction is LayoutDirection.TD or LayoutDirection.BT;
		var padding = options.Padding;

		var allNodes = new List<LayoutNodeResult>();
		var allEdges = new List<LayoutEdgeResult>();
		var allGroups = new List<LayoutGroupResult>();

		// Each component's result includes full padding on all sides.
		// When tiling, collapse adjacent paddings into a single componentSpacing gap.
		var offset = 0.0;
		var maxExtent = 0.0;

		for (var c = 0; c < components.Count; c++)
		{
			var (result, edgeMap) = components[c];

			var shiftX = tileHorizontally ? offset : 0.0;
			var shiftY = tileHorizontally ? 0.0 : offset;

			foreach (var node in result.Nodes)
				allNodes.Add(node with { X = node.X + shiftX, Y = node.Y + shiftY });

			foreach (var edge in result.Edges)
			{
				var originalIndex = edgeMap.TryGetValue(edge.OriginalIndex, out var mapped) ? mapped : edge.OriginalIndex;
				var shiftedPoints = new List<LayoutPoint>(edge.Points.Count);
				foreach (var p in edge.Points)
					shiftedPoints.Add(new LayoutPoint(p.X + shiftX, p.Y + shiftY));

				var shiftedLabel = edge.LabelPosition is { } lp
					? new LayoutPoint(lp.X + shiftX, lp.Y + shiftY)
					: edge.LabelPosition;

				allEdges.Add(new LayoutEdgeResult(originalIndex, shiftedPoints, shiftedLabel));
			}

			foreach (var group in result.Groups)
				allGroups.Add(ShiftGroup(group, shiftX, shiftY));

			// Advance by content + componentSpacing, collapsing double-padding between adjacent components
			var size = tileHorizontally ? result.Width : result.Height;
			offset += size - 2 * padding + options.ComponentSpacing;

			var perpendicular = tileHorizontally ? result.Height : result.Width;
			if (perpendicular > maxExtent) maxExtent = perpendicular;
		}

		// Undo the last componentSpacing and restore the outer padding
		var totalTile = offset - options.ComponentSpacing + 2 * padding;

		double totalWidth, totalHeight;
		if (tileHorizontally)
		{
			totalWidth = Math.Max(0, totalTile);
			totalHeight = maxExtent;
		}
		else
		{
			totalWidth = maxExtent;
			totalHeight = Math.Max(0, totalTile);
		}

		return new LayoutResult(totalWidth, totalHeight, allNodes, allEdges, allGroups);
	}

	private static LayoutGroupResult ShiftGroup(LayoutGroupResult g, double dx, double dy)
	{
		var children = new List<LayoutGroupResult>(g.Children.Count);
		foreach (var child in g.Children)
			children.Add(ShiftGroup(child, dx, dy));
		return new LayoutGroupResult(g.Id, g.Label, g.X + dx, g.Y + dy, g.Width, g.Height, children);
	}

	// ========================================================================
	// Disconnected subgraph nodes — move edgeless nodes to their siblings' layer
	// ========================================================================

	private static void PromoteDisconnectedSubgraphNodes(GraphBuffer buf, LayoutGraph input)
	{
		var hasEdge = new bool[buf.RealNodeCount];
		foreach (var e in buf.Edges)
		{
			if (e.From < buf.RealNodeCount) hasEdge[e.From] = true;
			if (e.To < buf.RealNodeCount) hasEdge[e.To] = true;
		}

		var nodeIndex = new Dictionary<string, int>(buf.RealNodeCount);
		for (var i = 0; i < buf.RealNodeCount; i++)
			nodeIndex[buf.NodeIds[i]] = i;

		var changed = false;
		foreach (var sg in input.Subgraphs)
			changed |= PromoteInSubgraph(buf, sg, nodeIndex, hasEdge);

		if (changed)
			LayerAssigner.BuildLayerArrays(buf);
	}

	private static bool PromoteInSubgraph(
		GraphBuffer buf, LayoutSubgraph sg,
		Dictionary<string, int> nodeIndex, bool[] hasEdge)
	{
		var changed = false;
		foreach (var child in sg.Children)
			changed |= PromoteInSubgraph(buf, child, nodeIndex, hasEdge);

		var maxSiblingLayer = -1;
		foreach (var nodeId in sg.NodeIds)
		{
			if (!nodeIndex.TryGetValue(nodeId, out var idx)) continue;
			if (hasEdge[idx] && buf.Layers[idx] > maxSiblingLayer)
				maxSiblingLayer = buf.Layers[idx];
		}

		if (maxSiblingLayer < 0) return changed;

		foreach (var nodeId in sg.NodeIds)
		{
			if (!nodeIndex.TryGetValue(nodeId, out var idx)) continue;
			if (!hasEdge[idx] && buf.Layers[idx] != maxSiblingLayer)
			{
				buf.Layers[idx] = maxSiblingLayer;
				changed = true;
			}
		}

		return changed;
	}

	// ========================================================================
	// Compact disconnected subgraph nodes next to their connected siblings
	// ========================================================================

	private static void CompactDisconnectedSubgraphNodes(
		GraphBuffer buf, LayoutGraph input, double nodeSpacing)
	{
		var nodeIndex = new Dictionary<string, int>(buf.RealNodeCount);
		for (var i = 0; i < buf.RealNodeCount; i++)
			nodeIndex[buf.NodeIds[i]] = i;

		var hasEdge = new bool[buf.RealNodeCount];
		foreach (var e in buf.Edges)
		{
			if (e.From < buf.RealNodeCount) hasEdge[e.From] = true;
			if (e.To < buf.RealNodeCount) hasEdge[e.To] = true;
		}

		CompactSiblings(buf, input.Subgraphs, nodeIndex, hasEdge, nodeSpacing);
	}

	private static void CompactSiblings(
		GraphBuffer buf, IReadOnlyList<LayoutSubgraph> siblings,
		Dictionary<string, int> nodeIndex, bool[] hasEdge, double spacing)
	{
		foreach (var sg in siblings)
			CompactSiblings(buf, sg.Children, nodeIndex, hasEdge, spacing);

		if (siblings.Count == 0) return;

		// Build per-subgraph info sorted by connected-node center X
		var infos = new List<(LayoutSubgraph Sg, double ConnMinX, double ConnMaxX, List<int> Disconnected)>();
		foreach (var sg in siblings)
		{
			var connMinX = double.MaxValue;
			var connMaxX = double.MinValue;
			var disconnected = new List<int>();
			foreach (var nodeId in sg.NodeIds)
			{
				if (!nodeIndex.TryGetValue(nodeId, out var idx)) continue;
				if (hasEdge[idx])
				{
					connMinX = Math.Min(connMinX, buf.X[idx]);
					connMaxX = Math.Max(connMaxX, buf.X[idx] + buf.NodeWidths[idx]);
				}
				else
				{
					disconnected.Add(idx);
				}
			}
			if (disconnected.Count > 0 && connMaxX > double.MinValue)
				infos.Add((sg, connMinX, connMaxX, disconnected));
		}

		if (infos.Count == 0) return;
		infos.Sort((a, b) => a.ConnMinX.CompareTo(b.ConnMinX));

		for (var i = 0; i < infos.Count; i++)
		{
			var (_, connMinX, connMaxX, disconnected) = infos[i];

			// Right boundary: leftmost connected X of the next sibling
			var rightBound = double.MaxValue;
			if (i + 1 < infos.Count)
				rightBound = infos[i + 1].ConnMinX;

			// Measure total width needed for disconnected nodes
			var totalW = 0.0;
			foreach (var idx in disconnected)
				totalW += buf.NodeWidths[idx] + spacing;

			var nextX = connMaxX + spacing;
			if (nextX + totalW > rightBound)
				nextX = connMinX - totalW;

			foreach (var idx in disconnected)
			{
				buf.X[idx] = nextX;
				nextX += buf.NodeWidths[idx] + spacing;
			}
		}
	}

	// ========================================================================
	// Subgraph spacing fix — push layers apart where group boxes would overlap
	// ========================================================================

	private static void FixSubgraphSpacing(GraphBuffer buf, LayoutGraph input)
	{
		const double groupPadding = 16.0;
		const double headerHeight = 28.0;
		const double clearance = 8.0;

		var nodeIndex = new Dictionary<string, int>(buf.RealNodeCount);
		for (var i = 0; i < buf.RealNodeCount; i++)
			nodeIndex[buf.NodeIds[i]] = i;

		var groupBounds = new List<(double Top, double Bottom)>();
		foreach (var sg in input.Subgraphs)
		{
			var minY = double.MaxValue;
			var maxY = double.MinValue;
			CollectSubgraphYBounds(sg, nodeIndex, buf, ref minY, ref maxY);
			if (minY == double.MaxValue) continue;
			groupBounds.Add((minY - groupPadding - headerHeight, maxY + groupPadding));
		}

		groupBounds.Sort((a, b) => a.Top.CompareTo(b.Top));

		for (var i = 0; i < groupBounds.Count - 1; i++)
		{
			var overlap = groupBounds[i].Bottom + clearance - groupBounds[i + 1].Top;
			if (overlap <= 0) continue;

			var threshold = groupBounds[i + 1].Top + groupPadding + headerHeight;

			for (var n = 0; n < buf.NodeCount; n++)
			{
				if (buf.Y[n] >= threshold - 1)
					buf.Y[n] += overlap;
			}

			for (var j = i + 1; j < groupBounds.Count; j++)
				groupBounds[j] = (groupBounds[j].Top + overlap, groupBounds[j].Bottom + overlap);
		}
	}

	private static void CollectSubgraphYBounds(
		LayoutSubgraph sg, Dictionary<string, int> nodeIndex, GraphBuffer buf,
		ref double minY, ref double maxY)
	{
		foreach (var nodeId in sg.NodeIds)
		{
			if (!nodeIndex.TryGetValue(nodeId, out var idx)) continue;
			minY = Math.Min(minY, buf.Y[idx]);
			maxY = Math.Max(maxY, buf.Y[idx] + buf.NodeHeights[idx]);
		}
		foreach (var child in sg.Children)
			CollectSubgraphYBounds(child, nodeIndex, buf, ref minY, ref maxY);
	}

	// ========================================================================
	// Buffer construction
	// ========================================================================

	private static GraphBuffer BuildBuffer(LayoutGraph input)
	{
		var buf = new GraphBuffer(input.Nodes.Count, input.Edges.Count);

		var nodeIndex = new Dictionary<string, int>(input.Nodes.Count);
		for (var i = 0; i < input.Nodes.Count; i++)
		{
			var node = input.Nodes[i];
			nodeIndex[node.Id] = i;
			buf.NodeIds[i] = node.Id;
			buf.NodeWidths[i] = node.Width;
			buf.NodeHeights[i] = node.Height;
		}

		for (var i = 0; i < input.Edges.Count; i++)
		{
			var edge = input.Edges[i];
			if (nodeIndex.TryGetValue(edge.Source, out var from) &&
				nodeIndex.TryGetValue(edge.Target, out var to))
			{
				buf.Edges.Add(new GraphEdge(from, to, i));
			}
		}

		return buf;
	}

	// ========================================================================
	// Result extraction
	// ========================================================================

	private static LayoutResult ExtractResult(
		GraphBuffer buf, LayoutGraph input, List<EdgeRouter.RoutedEdge> routes, double padding)
	{
		var maxX = 0.0;
		var maxY = 0.0;

		var nodes = new List<LayoutNodeResult>(buf.RealNodeCount);
		for (var i = 0; i < buf.RealNodeCount; i++)
		{
			var right = buf.X[i] + buf.NodeWidths[i];
			var bottom = buf.Y[i] + buf.NodeHeights[i];
			if (right > maxX) maxX = right;
			if (bottom > maxY) maxY = bottom;

			nodes.Add(new LayoutNodeResult(
				buf.NodeIds[i],
				buf.X[i],
				buf.Y[i],
				buf.NodeWidths[i],
				buf.NodeHeights[i]));
		}

		var edges = new List<LayoutEdgeResult>(routes.Count);
		foreach (var route in routes)
		{
			if (route.OriginalIndex >= input.Edges.Count) continue;

			var points = new List<LayoutPoint>(route.Points.Count);
			foreach (var p in route.Points)
			{
				points.Add(p);
				if (p.X + padding > maxX) maxX = p.X + padding;
				if (p.Y + padding > maxY) maxY = p.Y + padding;
			}

			edges.Add(new LayoutEdgeResult(route.OriginalIndex, points, route.LabelPosition));
		}

		var groups = ComputeGroups(buf, input, padding);

		// Ensure subgroup headers don't extend beyond the canvas
		var minGroupX = double.MaxValue;
		var minGroupY = double.MaxValue;
		FindMinGroupBounds(groups, ref minGroupX, ref minGroupY);

		if (minGroupX < padding || minGroupY < padding)
		{
			var shiftX = minGroupX < padding ? padding - minGroupX : 0;
			var shiftY = minGroupY < padding ? padding - minGroupY : 0;

			ShiftAll(nodes, edges, groups, shiftX, shiftY);

			for (var i = 0; i < buf.RealNodeCount; i++)
			{
				buf.X[i] += shiftX;
				buf.Y[i] += shiftY;
			}

			maxX += shiftX;
			maxY += shiftY;
		}

		ExpandBoundsForGroups(groups, ref maxX, ref maxY);

		return new LayoutResult(maxX + padding, maxY + padding, nodes, edges, groups);
	}

	private static void FindMinGroupBounds(List<LayoutGroupResult> groups, ref double minX, ref double minY)
	{
		foreach (var g in groups)
		{
			if (g.X < minX) minX = g.X;
			if (g.Y < minY) minY = g.Y;
			FindMinGroupBounds(g.Children.ToList(), ref minX, ref minY);
		}
	}

	private static void ExpandBoundsForGroups(IReadOnlyList<LayoutGroupResult> groups, ref double maxX, ref double maxY)
	{
		foreach (var g in groups)
		{
			var right = g.X + g.Width;
			var bottom = g.Y + g.Height;
			if (right > maxX) maxX = right;
			if (bottom > maxY) maxY = bottom;
			ExpandBoundsForGroups(g.Children, ref maxX, ref maxY);
		}
	}

	private static void ShiftAll(
		List<LayoutNodeResult> nodes,
		List<LayoutEdgeResult> edges,
		List<LayoutGroupResult> groups,
		double dx, double dy)
	{
		for (var i = 0; i < nodes.Count; i++)
		{
			var n = nodes[i];
			nodes[i] = n with { X = n.X + dx, Y = n.Y + dy };
		}

		for (var i = 0; i < edges.Count; i++)
		{
			var e = edges[i];
			var shifted = new List<LayoutPoint>(e.Points.Count);
			foreach (var p in e.Points)
				shifted.Add(new LayoutPoint(p.X + dx, p.Y + dy));

			var labelPos = e.LabelPosition is { } lp
				? new LayoutPoint(lp.X + dx, lp.Y + dy)
				: e.LabelPosition;

			edges[i] = new LayoutEdgeResult(e.OriginalIndex, shifted, labelPos);
		}

		ShiftGroups(groups, dx, dy);
	}

	private static void ShiftGroups(List<LayoutGroupResult> groups, double dx, double dy)
	{
		for (var i = 0; i < groups.Count; i++)
		{
			var g = groups[i];
			var children = g.Children.ToList();
			ShiftGroups(children, dx, dy);
			groups[i] = new LayoutGroupResult(g.Id, g.Label, g.X + dx, g.Y + dy, g.Width, g.Height, children);
		}
	}

	private static List<LayoutGroupResult> ComputeGroups(
		GraphBuffer buf, LayoutGraph input, double padding)
	{
		if (input.Subgraphs.Count == 0) return [];

		var nodeIndex = new Dictionary<string, int>(buf.RealNodeCount);
		for (var i = 0; i < buf.RealNodeCount; i++)
			nodeIndex[buf.NodeIds[i]] = i;

		var groups = new List<LayoutGroupResult>(input.Subgraphs.Count);
		foreach (var sg in input.Subgraphs)
			groups.Add(ComputeGroup(buf, sg, nodeIndex));
		return groups;
	}

	private static LayoutGroupResult ComputeGroup(
		GraphBuffer buf, LayoutSubgraph sg, Dictionary<string, int> nodeIndex)
	{
		const double groupPadding = 16.0;
		const double headerHeight = 28.0;

		var minX = double.MaxValue;
		var minY = double.MaxValue;
		var maxX = double.MinValue;
		var maxY = double.MinValue;

		foreach (var nodeId in sg.NodeIds)
		{
			if (!nodeIndex.TryGetValue(nodeId, out var idx)) continue;
			var x = buf.X[idx];
			var y = buf.Y[idx];
			minX = Math.Min(minX, x);
			minY = Math.Min(minY, y);
			maxX = Math.Max(maxX, x + buf.NodeWidths[idx]);
			maxY = Math.Max(maxY, y + buf.NodeHeights[idx]);
		}

		var children = new List<LayoutGroupResult>();
		foreach (var child in sg.Children)
		{
			var childGroup = ComputeGroup(buf, child, nodeIndex);
			children.Add(childGroup);
			minX = Math.Min(minX, childGroup.X);
			minY = Math.Min(minY, childGroup.Y);
			maxX = Math.Max(maxX, childGroup.X + childGroup.Width);
			maxY = Math.Max(maxY, childGroup.Y + childGroup.Height);
		}

		if (minX == double.MaxValue)
		{
			minX = 0; minY = 0; maxX = 100; maxY = 60;
		}

		return new LayoutGroupResult(
			sg.Id, sg.Label,
			minX - groupPadding,
			minY - groupPadding - headerHeight,
			maxX - minX + groupPadding * 2,
			maxY - minY + groupPadding * 2 + headerHeight,
			children);
	}
}
