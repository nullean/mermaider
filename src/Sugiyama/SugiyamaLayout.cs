using System.Runtime.CompilerServices;
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
		if (input.Subgraphs.Count > 0)
		{
			var (looseNodes, groups) = BuildNestingGroups(input, buf);
			LayerAssigner.Run(buf, options.NaturalBackEdgeRouting, looseNodes, groups, options.ReverseSourceOrder);
		}
		else
		{
			LayerAssigner.Run(buf, options.NaturalBackEdgeRouting, reverseSourceOrder: options.ReverseSourceOrder);
		}

		options.CancellationToken.ThrowIfCancellationRequested();
		if (buf.NodeCount > options.MaxNodeCount)
			throw new InvalidOperationException(
				$"Layout node count {buf.NodeCount} exceeds limit {options.MaxNodeCount} " +
				$"(MaxNodesAfterLayout). Raise ResourceLimits.MaxNodesAfterLayout or simplify the diagram.");

		if (input.Subgraphs.Count > 0)
			PromoteDisconnectedSubgraphNodes(buf, input);

		if (options.PortAwareLayout)
		{
			var horizontalFlow = input.Direction is LayoutDirection.LR or LayoutDirection.RL;
			buf.EdgeLabelExtent = input.Edges.Select(e => horizontalFlow ? e.LabelHeight : e.LabelWidth).ToArray();
		}

		var tied = options.PortAwareLayout && options.CrossingRestarts > 0 ? new List<int[]>() : null;
		CrossingMinimizer.Run(buf, options.CrossingIterations, options.UseModelOrderForVirtualNodes,
			options.UseModelOrderForRealNodes, options.UseRealFirstTiebreaker, options.CrossingRestarts, tied, options.CancellationToken);
		if (tied is { Count: > 1 })
			ChooseOrderByRoutedGeometry(buf, input, options, tied);
		CoordinateAssigner.Run(buf, options.NodeSpacing, options.LayerSpacing, options.TightSourceLayering);
		if (!options.PortAwareLayout)
			SpreadFanOutChildren(buf, options.NodeSpacing);

		// SpreadForkBranches assumes a flowchart/state-diagram "fork" — source → intermediate →
		// convergence plus source → convergence — and shoves the intermediate branch sideways so
		// the two paths read as visually distinct. In ER diagrams the exact same edge shape is just
		// an ordinary multi-parent relationship (e.g. USER→POST→COMMENT with USER→COMMENT too), and
		// applying the heuristic there shoves the entire intermediate subtree ~200px away from its
		// sibling for no reason. TightSourceLayering is exclusively opted into by the ER layout
		// engine, so it doubles as the signal to skip this flowchart-only heuristic.
		if (!options.TightSourceLayering)
			SpreadForkBranches(buf, options.NodeSpacing, BuildNodeSubgraphMap(buf, input.Subgraphs));

		if (input.Subgraphs.Count > 0)
		{
			CompactDisconnectedSubgraphNodes(buf, input, options.NodeSpacing);
			FixSubgraphSpacing(buf, input);
			FixSubgraphXSpacing(buf, input); // fixes secondary-axis (X) overlap for LR/RL layouts
		}

		var useSideRouting = input.Direction is LayoutDirection.LR or LayoutDirection.RL;
		var routes = options.PortAwareLayout
			? ErEdgeRouter.Run(buf, input.Edges, useSideRouting)
			: EdgeRouter.Run(buf, useSideRouting, input.Edges, options.StrictTopDownFanout, options.NaturalBackEdgeRouting, options.ForceBottomExitFanOut);

		if (input.Subgraphs.Count > 0)
			RerouteSubgraphCrossingEdges(buf, routes, input);

		var direction = input.Direction switch
		{
			LayoutDirection.LR => DirectionTransform.Direction.LR,
			LayoutDirection.RL => DirectionTransform.Direction.RL,
			LayoutDirection.BT => DirectionTransform.Direction.BT,
			_ => DirectionTransform.Direction.TD,
		};

		DirectionTransform.Run(buf, routes, direction);
		_ = DirectionTransform.Normalize(buf, routes, options.Padding);

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
			if (!adj.TryGetValue(edge.Source, out var value) || !adj.TryGetValue(edge.Target, out var value1))
				continue;

			_ = value.Add(edge.Target);
			_ = value1.Add(edge.Source);
		}

		foreach (var (a, b) in input.SameRankConstraints)
		{
			if (adj.TryGetValue(a, out var va) && adj.TryGetValue(b, out var vb))
			{
				_ = va.Add(b);
				_ = vb.Add(a);
			}
		}

		foreach (var sg in input.Subgraphs)
			ConnectSubgraphNodes(adj, sg);

		var visited = new HashSet<string>(input.Nodes.Count);
		var components = new List<HashSet<string>>();

		foreach (var node in input.Nodes)
		{
			if (visited.Contains(node.Id))
				continue;

			var component = new HashSet<string>();
			var queue = new Queue<string>();
			queue.Enqueue(node.Id);
			_ = visited.Add(node.Id);

			while (queue.Count > 0)
			{
				var current = queue.Dequeue();
				_ = component.Add(current);

				if (!adj.TryGetValue(current, out var neighbors))
					continue;
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
			if (!adj.TryGetValue(nodeIds[i - 1], out var value) || !adj.TryGetValue(nodeIds[i], out var value1))
				continue;

			_ = value.Add(nodeIds[i]);
			_ = value1.Add(nodeIds[i - 1]);
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
			var subSameRank = input.SameRankConstraints
				.Where(c => component.Contains(c.A) && component.Contains(c.B))
				.ToList();
			var subGraph = new LayoutGraph(input.Direction, subNodes, subEdges, subSubgraphs)
			{
				SameRankConstraints = subSameRank,
			};
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
		var maxPerRow = options.MaxComponentsPerRow > 0 ? options.MaxComponentsPerRow : components.Count;

		var allNodes = new List<LayoutNodeResult>();
		var allEdges = new List<LayoutEdgeResult>();
		var allGroups = new List<LayoutGroupResult>();

		// Each component's result includes full padding on all sides.
		// When tiling, collapse adjacent paddings into a single componentSpacing gap.

		// Track row/column offsets for grid wrapping
		var primaryOffset = 0.0;   // offset along the primary tiling axis
		var secondaryOffset = 0.0; // offset along the perpendicular axis (for new rows)
		var rowMaxExtent = 0.0;    // tallest component in the current row
		var colInRow = 0;          // how many components placed in the current row

		// Track overall extents for computing final canvas size
		var totalPrimary = 0.0;
		var totalSecondary = 0.0;

		for (var c = 0; c < components.Count; c++)
		{
			var (result, edgeMap) = components[c];

			// Wrap to a new row/column when the per-row limit is reached
			if (colInRow > 0 && colInRow >= maxPerRow)
			{
				secondaryOffset += rowMaxExtent - (2 * padding) + options.ComponentSpacing;
				primaryOffset = 0.0;
				rowMaxExtent = 0.0;
				colInRow = 0;
			}

			var shiftX = tileHorizontally ? primaryOffset : secondaryOffset;
			var shiftY = tileHorizontally ? secondaryOffset : primaryOffset;

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

			// Advance primary offset, collapsing double-padding between adjacent components
			var primarySize = tileHorizontally ? result.Width : result.Height;
			primaryOffset += primarySize - (2 * padding) + options.ComponentSpacing;

			var perpendicularSize = tileHorizontally ? result.Height : result.Width;
			if (perpendicularSize > rowMaxExtent)
				rowMaxExtent = perpendicularSize;

			colInRow++;

			// Track max primary extent across all rows
			var rowEndPrimary = primaryOffset - options.ComponentSpacing + (2 * padding);
			if (rowEndPrimary > totalPrimary)
				totalPrimary = rowEndPrimary;
		}

		// Total secondary = all completed rows + final partial row
		totalSecondary = secondaryOffset + rowMaxExtent;

		double totalWidth, totalHeight;
		if (tileHorizontally)
		{
			totalWidth = Math.Max(0, totalPrimary);
			totalHeight = Math.Max(0, totalSecondary);
		}
		else
		{
			totalWidth = Math.Max(0, totalSecondary);
			totalHeight = Math.Max(0, totalPrimary);
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
			if (e.From < buf.RealNodeCount)
				hasEdge[e.From] = true;
			if (e.To < buf.RealNodeCount)
				hasEdge[e.To] = true;
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
			if (!nodeIndex.TryGetValue(nodeId, out var idx))
				continue;
			if (hasEdge[idx] && buf.Layers[idx] > maxSiblingLayer)
				maxSiblingLayer = buf.Layers[idx];
		}

		if (maxSiblingLayer < 0)
			return changed;

		foreach (var nodeId in sg.NodeIds)
		{
			if (!nodeIndex.TryGetValue(nodeId, out var idx))
				continue;
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
			if (e.From < buf.RealNodeCount)
				hasEdge[e.From] = true;
			if (e.To < buf.RealNodeCount)
				hasEdge[e.To] = true;
		}

		CompactSiblings(buf, input.Subgraphs, nodeIndex, hasEdge, nodeSpacing);
	}

	private static void CompactSiblings(
		GraphBuffer buf, IReadOnlyList<LayoutSubgraph> siblings,
		Dictionary<string, int> nodeIndex, bool[] hasEdge, double spacing)
	{
		foreach (var sg in siblings)
			CompactSiblings(buf, sg.Children, nodeIndex, hasEdge, spacing);

		if (siblings.Count == 0)
			return;

		// Build per-subgraph info sorted by connected-node center X
		var infos = new List<(LayoutSubgraph Sg, double ConnMinX, double ConnMaxX, List<int> Disconnected)>();
		foreach (var sg in siblings)
		{
			var connMinX = double.MaxValue;
			var connMaxX = double.MinValue;
			var disconnected = new List<int>();
			foreach (var nodeId in sg.NodeIds)
			{
				if (!nodeIndex.TryGetValue(nodeId, out var idx))
					continue;
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

		if (infos.Count == 0)
			return;
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
	// Subgraph crossing reroute — when edges cross into a subgraph, route
	// them from the source's sides so they avoid the subgraph header text
	// ========================================================================

	private static void RerouteSubgraphCrossingEdges(
		GraphBuffer buf, List<EdgeRouter.RoutedEdge> routes, LayoutGraph input)
	{
		var nodeSubgraph = new Dictionary<string, string>();
		foreach (var sg in input.Subgraphs)
			CollectSubgraphMembership(sg, nodeSubgraph);

		if (nodeSubgraph.Count == 0)
			return;

		var nodeIndex = new Dictionary<string, int>(buf.RealNodeCount);
		for (var i = 0; i < buf.RealNodeCount; i++)
			nodeIndex[buf.NodeIds[i]] = i;

		var subgraphTopY = new Dictionary<string, double>();
		foreach (var sg in input.Subgraphs)
			ComputeSubgraphTopY(sg, nodeIndex, buf, subgraphTopY);

		var crossingEdgesBySource = new Dictionary<int, List<int>>();
		for (var ri = 0; ri < routes.Count; ri++)
		{
			var route = routes[ri];
			if (route.OriginalIndex >= input.Edges.Count)
				continue;
			var edge = input.Edges[route.OriginalIndex];
			if (!nodeIndex.TryGetValue(edge.Source, out var srcIdx) ||
				!nodeIndex.TryGetValue(edge.Target, out var tgtIdx))
				continue;

			_ = nodeSubgraph.TryGetValue(edge.Source, out var srcSg);
			_ = nodeSubgraph.TryGetValue(edge.Target, out var tgtSg);

			if (tgtSg is null || srcSg is not null)
				continue;

			var srcBottom = buf.Y[srcIdx] + buf.NodeHeights[srcIdx];
			if (!subgraphTopY.TryGetValue(tgtSg, out var sgTop))
				continue;
			// Source is below subgraph top (already adjacent/inside) — no reroute needed.
			if (srcBottom > sgTop)
				continue;
			// Target is the topmost node of the subgraph AND source is vertically aligned
			// with it — the edge enters straight from the top and needs no side-routing.
			// (Example: composite-state [*] → inner [*] where both share the same center X.)
			if (Math.Abs(buf.Y[tgtIdx] - sgTop) < 1)
			{
				var srcCXLocal = buf.X[srcIdx] + (buf.NodeWidths[srcIdx] / 2.0);
				var tgtCXLocal = buf.X[tgtIdx] + (buf.NodeWidths[tgtIdx] / 2.0);
				if (Math.Abs(srcCXLocal - tgtCXLocal) <= 8.0)
					continue;
			}

			if (!crossingEdgesBySource.TryGetValue(srcIdx, out var list))
			{
				list = [];
				crossingEdgesBySource[srcIdx] = list;
			}
			list.Add(ri);
		}

		const double sideExtension = 20;

		foreach (var (srcIdx, routeIndices) in crossingEdgesBySource)
		{
			if (routeIndices.Count < 1)
				continue;

			var srcCX = buf.X[srcIdx] + (buf.NodeWidths[srcIdx] / 2.0);
			var srcCY = buf.Y[srcIdx] + (buf.NodeHeights[srcIdx] / 2.0);
			var srcLeft = buf.X[srcIdx];
			var srcRight = srcLeft + buf.NodeWidths[srcIdx];

			var sorted = routeIndices.OrderBy(ri =>
			{
				var e = input.Edges[routes[ri].OriginalIndex];
				return nodeIndex.TryGetValue(e.Target, out var ti)
					? buf.Y[ti]
					: 0.0;
			}).ToList();

			for (var i = 0; i < sorted.Count; i++)
			{
				var ri = sorted[i];
				var route = routes[ri];
				var edge = input.Edges[route.OriginalIndex];
				if (!nodeIndex.TryGetValue(edge.Target, out var tgtIdx))
					continue;

				var tgtCY = buf.Y[tgtIdx] + (buf.NodeHeights[tgtIdx] / 2.0);

				var goRight = sorted.Count == 1
					? tgtCY > srcCY
					: i % 2 == 0;

				var tgtBorderX = goRight
					? buf.X[tgtIdx] + buf.NodeWidths[tgtIdx]
					: buf.X[tgtIdx];
				var laneX = goRight
					? Math.Max(srcRight, tgtBorderX) + sideExtension
					: Math.Min(srcLeft, tgtBorderX) - sideExtension;

				var points = new List<LayoutPoint>
				{
					new(goRight ? srcRight : srcLeft, srcCY),
					new(laneX, srcCY),
					new(laneX, tgtCY),
					new(tgtBorderX, tgtCY),
				};

				route.ReplacePoints(points);
			}
		}
	}

	private static void ComputeSubgraphTopY(
		LayoutSubgraph sg, Dictionary<string, int> nodeIndex, GraphBuffer buf,
		Dictionary<string, double> result)
	{
		var minY = double.MaxValue;
		foreach (var nodeId in sg.NodeIds)
		{
			if (nodeIndex.TryGetValue(nodeId, out var idx))
				minY = Math.Min(minY, buf.Y[idx]);
		}
		foreach (var child in sg.Children)
		{
			ComputeSubgraphTopY(child, nodeIndex, buf, result);
			if (result.TryGetValue(child.Id, out var childTop))
				minY = Math.Min(minY, childTop);
		}
		if (minY < double.MaxValue)
			result[sg.Id] = minY;
	}

	private static void CollectSubgraphMembership(LayoutSubgraph sg, Dictionary<string, string> result)
	{
		foreach (var nodeId in sg.NodeIds)
			result[nodeId] = sg.Id;
		foreach (var child in sg.Children)
			CollectSubgraphMembership(child, result);
	}

	// ========================================================================
	// Fan-out spread — push children of fan-out nodes apart so side-exit
	// edges have visible horizontal legs and connect to child top-centers
	// ========================================================================

	// Orderings with the same permutation-crossing count can still route very differently (a jog crossing another edge's
	// column is invisible to the permutation count). Lay out and route each tied candidate, keep the best geometry.
	private static void ChooseOrderByRoutedGeometry(
		GraphBuffer buf, LayoutGraph input, LayoutOptions options, List<int[]> candidates)
	{
		// Candidate 0 is the deterministic model-order result: only a strictly lower routed-crossing count may replace it
		// (bends/length are ignored so mirror images and near-ties never flip an already-optimal ordering).
		var best = 0;
		var bestCrossings = int.MaxValue;
		for (var i = 0; i < candidates.Count; i++)
		{
			options.CancellationToken.ThrowIfCancellationRequested();
			CrossingMinimizer.ApplyOrder(buf, candidates[i]);
			CoordinateAssigner.Run(buf, options.NodeSpacing, options.LayerSpacing, options.TightSourceLayering);
			var routes = ErEdgeRouter.Run(buf, input.Edges, useSideRouting: false);
			var crossings = ErEdgeRouter.CountCrossings(routes, input.Edges);
			if (crossings >= bestCrossings)
				continue;
			bestCrossings = crossings;
			best = i;
		}

		CrossingMinimizer.ApplyOrder(buf, candidates[best]);
	}

	private static void SpreadFanOutChildren(GraphBuffer buf, double nodeSpacing)
	{
		const double minSpread = 28;

		for (var parent = 0; parent < buf.RealNodeCount; parent++)
		{
			var parentCX = buf.X[parent] + (buf.NodeWidths[parent] / 2.0);
			var leftChild = -1;
			var rightChild = -1;

			foreach (var e in buf.Edges)
			{
				if (e.From != parent || e.IsVirtual)
					continue;
				var child = e.To;
				if (child >= buf.RealNodeCount)
					continue;
				var childCX = buf.X[child] + (buf.NodeWidths[child] / 2.0);
				if (childCX < parentCX - 10)
				{
					if (leftChild < 0 || childCX < buf.X[leftChild] + (buf.NodeWidths[leftChild] / 2.0))
						leftChild = child;
				}
				if (childCX > parentCX + 10)
				{
					if (rightChild < 0 || childCX > buf.X[rightChild] + (buf.NodeWidths[rightChild] / 2.0))
						rightChild = child;
				}
			}

			if (leftChild < 0 || rightChild < 0)
				continue;

			var parentLeft = buf.X[parent];
			var parentRight = parentLeft + buf.NodeWidths[parent];
			var leftCX = buf.X[leftChild] + (buf.NodeWidths[leftChild] / 2.0);
			var rightCX = buf.X[rightChild] + (buf.NodeWidths[rightChild] / 2.0);

			var leftGap = parentLeft - leftCX;
			var rightGap = rightCX - parentRight;

			if (leftGap >= minSpread && rightGap >= minSpread)
				continue;

			var leftShift = leftGap < minSpread ? minSpread - leftGap : 0;
			var rightShift = rightGap < minSpread ? minSpread - rightGap : 0;

			if (leftShift > 0)
				ShiftNodeAndLayerNeighbors(buf, leftChild, -leftShift, nodeSpacing);
			if (rightShift > 0)
				ShiftNodeAndLayerNeighbors(buf, rightChild, rightShift, nodeSpacing);
		}
	}

	private static void ShiftNodeAndLayerNeighbors(GraphBuffer buf, int node, double shift, double nodeSpacing)
	{
		var layer = buf.Layers[node];
		var nodes = buf.LayerNodes[layer];
		var pos = buf.NodePositionInLayer[node];

		buf.X[node] += shift;

		if (shift < 0)
		{
			for (var i = pos - 1; i >= 0; i--)
			{
				var prev = nodes[i];
				var prevRight = buf.X[prev] + (prev < buf.RealNodeCount ? buf.NodeWidths[prev] : 0);
				var gap = buf.X[nodes[i + 1]] - prevRight;
				if (gap >= nodeSpacing)
					break;
				buf.X[prev] -= nodeSpacing - gap;
			}
		}
		else
		{
			for (var i = pos + 1; i < nodes.Length; i++)
			{
				var next = nodes[i];
				var prevNode = nodes[i - 1];
				var prevRight = buf.X[prevNode] + (prevNode < buf.RealNodeCount ? buf.NodeWidths[prevNode] : 0);
				var gap = buf.X[next] - prevRight;
				if (gap >= nodeSpacing)
					break;
				buf.X[next] += nodeSpacing - gap;
			}
		}
	}

	private static Dictionary<int, string>? BuildNodeSubgraphMap(GraphBuffer buf, IReadOnlyList<LayoutSubgraph> subgraphs)
	{
		if (subgraphs.Count == 0)
			return null;

		var idToSg = new Dictionary<string, string>();
		CollectSubgraphMembership(subgraphs, idToSg, parentId: null);

		var result = new Dictionary<int, string>();
		for (var i = 0; i < buf.RealNodeCount; i++)
		{
			if (idToSg.TryGetValue(buf.NodeIds[i], out var sg))
				result[i] = sg;
		}
		return result;
	}

	private static void CollectSubgraphMembership(IReadOnlyList<LayoutSubgraph> subgraphs, Dictionary<string, string> result, string? parentId)
	{
		foreach (var sg in subgraphs)
		{
			var effectiveId = parentId ?? sg.Id;
			foreach (var nodeId in sg.NodeIds)
				_ = result.TryAdd(nodeId, effectiveId);
			CollectSubgraphMembership(sg.Children, result, effectiveId);
		}
	}

	// ========================================================================
	// Fork branch spread — when S→A→B and S→B exist, push A to the side
	// so the two paths are visually distinct (matching Mermaid.js fork layout)
	// ========================================================================

	private static void SpreadForkBranches(GraphBuffer buf, double nodeSpacing, Dictionary<int, string>? nodeSubgraph = null)
	{
		var realOutgoing = BuildRealOutgoing(buf);

		foreach (var (source, targets) in realOutgoing)
		{
			if (targets.Count != 2)
				continue;

			var a = targets[0];
			var b = targets[1];

			int intermediate;
			int convergence;

			if (realOutgoing.TryGetValue(a, out var aTargets) && aTargets.Contains(b))
			{
				intermediate = a;
				convergence = b;
			}
			else if (realOutgoing.TryGetValue(b, out var bTargets) && bTargets.Contains(a))
			{
				intermediate = b;
				convergence = a;
			}
			else
			{
				continue;
			}

			// Don't spread when source and convergence are in different subgraphs — cross-subgraph
			// fork patterns would misalign nodes that belong together within the source subgraph.
			if (nodeSubgraph != null)
			{
				_ = nodeSubgraph.TryGetValue(source, out var srcSg);
				_ = nodeSubgraph.TryGetValue(convergence, out var convSg);
				if (srcSg != convSg)
					continue;
			}

			// Don't spread when the intermediate node has a back-edge returning to the source
			// (bidirectional pairs like Active↔Inactive with disable/reactivate should stay
			// vertically aligned rather than being pushed sideways).
			var hasBidirectionalReturn = false;
			foreach (var e in buf.Edges)
			{
				if (e.Reversed && ((e.From == source && e.To == intermediate) || (e.From == intermediate && e.To == source)))
				{
					hasBidirectionalReturn = true;
					break;
				}
			}
			if (hasBidirectionalReturn)
				continue;

			var srcCX = buf.X[source] + (buf.NodeWidths[source] / 2.0);
			var intCX = buf.X[intermediate] + (buf.NodeWidths[intermediate] / 2.0);

			if (Math.Abs(intCX - srcCX) > nodeSpacing)
				continue;

			var offset = (buf.NodeWidths[source] / 2.0) + (buf.NodeWidths[intermediate] / 2.0) + nodeSpacing;
			ShiftNodeAndLayerNeighbors(buf, intermediate, offset, nodeSpacing);

			ShiftForkDescendants(buf, intermediate, convergence, offset, nodeSpacing, realOutgoing, [intermediate]);

			AlignShortcutVirtualNodes(buf, source, convergence);
		}
	}

	private static void AlignShortcutVirtualNodes(GraphBuffer buf, int source, int convergence)
	{
		var convergenceCX = buf.X[convergence] + (buf.NodeWidths[convergence] / 2.0);

		foreach (var e in buf.Edges)
		{
			if (e.From != source)
				continue;

			var current = e.To;
			while (current >= buf.RealNodeCount)
			{
				var nextFound = false;
				foreach (var ve in buf.Edges)
				{
					if (ve.From == current && ve.OriginalIndex == e.OriginalIndex)
					{
						if (ve.To == convergence || ve.To >= buf.RealNodeCount)
						{
							buf.X[current] = convergenceCX;
						}
						current = ve.To;
						nextFound = true;
						break;
					}
				}
				if (!nextFound)
					break;
			}

			if (current == convergence)
				return;
		}
	}

	private static void ShiftForkDescendants(
		GraphBuffer buf, int node, int convergence, double shift, double nodeSpacing,
		Dictionary<int, List<int>> realOutgoing, HashSet<int> visited)
	{
		// Defense-in-depth: convert an uncatchable StackOverflowException into a
		// catchable InsufficientExecutionStackException for any future pathological
		// graph that somehow slips past the visited-set guard.
		RuntimeHelpers.EnsureSufficientExecutionStack();

		if (!realOutgoing.TryGetValue(node, out var children))
			return;

		foreach (var child in children)
		{
			if (child == convergence)
				continue;
			if (!visited.Add(child))
				continue; // already shifted by a previous path through this node
			ShiftNodeAndLayerNeighbors(buf, child, shift, nodeSpacing);
			ShiftForkDescendants(buf, child, convergence, shift, nodeSpacing, realOutgoing, visited);
		}
	}

	private static Dictionary<int, List<int>> BuildRealOutgoing(GraphBuffer buf)
	{
		var result = new Dictionary<int, List<int>>();
		foreach (var e in buf.Edges)
		{
			if (e.From >= buf.RealNodeCount)
				continue;
			// Reversed back-edges are cycle-breaking artefacts; they must not be
			// treated as real forward connections for fork/fan-out detection.
			if (e.Reversed)
				continue;

			var finalTarget = e.To;
			if (finalTarget >= buf.RealNodeCount)
			{
				var current = finalTarget;
				while (current >= buf.RealNodeCount)
				{
					var found = false;
					foreach (var ve in buf.Edges)
					{
						if (ve.From == current && ve.OriginalIndex == e.OriginalIndex)
						{
							current = ve.To;
							found = true;
							break;
						}
					}
					if (!found)
						break;
				}
				finalTarget = current;
			}

			if (finalTarget >= buf.RealNodeCount)
				continue;

			// Self-loop: the resolved target is the source node itself.
			// Self-loops carry no layering information and must not enter the
			// fork-descendant adjacency — doing so makes ShiftForkDescendants
			// recurse on a node that is its own successor (→ infinite recursion).
			if (finalTarget == e.From)
				continue;

			if (!result.TryGetValue(e.From, out var list))
			{
				list = [];
				result[e.From] = list;
			}
			if (!list.Contains(finalTarget))
				list.Add(finalTarget);
		}
		return result;
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
			if (minY == double.MaxValue)
				continue;
			groupBounds.Add((minY - groupPadding - headerHeight, maxY + groupPadding));
		}

		groupBounds.Sort((a, b) => a.Top.CompareTo(b.Top));

		for (var i = 0; i < groupBounds.Count - 1; i++)
		{
			var overlap = groupBounds[i].Bottom + clearance - groupBounds[i + 1].Top;
			if (overlap <= 0)
				continue;

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
			if (!nodeIndex.TryGetValue(nodeId, out var idx))
				continue;
			minY = Math.Min(minY, buf.Y[idx]);
			maxY = Math.Max(maxY, buf.Y[idx] + buf.NodeHeights[idx]);
		}
		foreach (var child in sg.Children)
			CollectSubgraphYBounds(child, nodeIndex, buf, ref minY, ref maxY);
	}

	/// <summary>
	/// Fixes secondary-axis (X) overlap between subgraphs that share the same Sugiyama layer.
	/// <para>
	/// <c>FixSubgraphSpacing</c> handles the primary axis (Y = depth); this handles the secondary
	/// axis (X = within-layer position) — which is what causes subgraph boxes to overlap
	/// vertically in LR diagrams.  For each layer we scan nodes in X order and, whenever we
	/// cross a subgraph boundary, ensure the gap is wide enough for both subgraph headers +
	/// padding.  Nodes are shifted per-layer so that cross-layer relationships are not disturbed.
	/// </para>
	/// </summary>
	private static void FixSubgraphXSpacing(GraphBuffer buf, LayoutGraph input)
	{
		const double groupPadding = 16.0;
		const double headerHeight = 28.0;
		const double clearance = 8.0;
		var minGap = ((groupPadding + headerHeight) * 2) + clearance;

		var nodeIndex = new Dictionary<string, int>(buf.RealNodeCount);
		for (var i = 0; i < buf.RealNodeCount; i++)
			nodeIndex[buf.NodeIds[i]] = i;

		// Map node index → top-level subgraph ID
		var nodeSubgraph = new Dictionary<int, string>(buf.RealNodeCount);
		foreach (var sg in input.Subgraphs)
			MapNodesToTopSubgraph(sg, sg.Id, nodeIndex, nodeSubgraph);

		for (var layer = 0; layer < buf.LayerCount; layer++)
		{
			// Collect real nodes in this layer that belong to a subgraph, sorted by X
			var layerNodes = new List<int>();
			foreach (var n in buf.LayerNodes[layer])
			{
				if (n < buf.RealNodeCount && nodeSubgraph.ContainsKey(n))
					layerNodes.Add(n);
			}

			if (layerNodes.Count < 2)
				continue;

			layerNodes.Sort((a, b) => buf.X[a].CompareTo(buf.X[b]));

			for (var pos = 0; pos < layerNodes.Count - 1; pos++)
			{
				var curr = layerNodes[pos];
				var next = layerNodes[pos + 1];

				if (!nodeSubgraph.TryGetValue(curr, out var sgA) ||
					!nodeSubgraph.TryGetValue(next, out var sgB) ||
					sgA == sgB)
					continue;

				var actualGap = buf.X[next] - (buf.X[curr] + buf.NodeWidths[curr]);
				if (actualGap >= minGap)
					continue;

				var push = minGap - actualGap;
				for (var j = pos + 1; j < layerNodes.Count; j++)
					buf.X[layerNodes[j]] += push;
			}
		}
	}

	private static void MapNodesToTopSubgraph(
		LayoutSubgraph sg, string topId,
		Dictionary<string, int> nodeIndex,
		Dictionary<int, string> nodeSubgraph)
	{
		foreach (var nodeId in sg.NodeIds)
		{
			if (nodeIndex.TryGetValue(nodeId, out var idx))
				_ = nodeSubgraph.TryAdd(idx, topId);
		}
		foreach (var child in sg.Children)
			MapNodesToTopSubgraph(child, topId, nodeIndex, nodeSubgraph);
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
				buf.Edges.Add(new GraphEdge(from, to, i, MinLength: input.Edges[i].MinLength));
			}
		}

		foreach (var (a, b) in input.SameRankConstraints)
		{
			if (nodeIndex.TryGetValue(a, out var idxA) && nodeIndex.TryGetValue(b, out var idxB))
				buf.SameRankPairs.Add((idxA, idxB));
		}

		return buf;
	}

	// ========================================================================
	// Nesting-graph group tree (int-indexed) for subgraph-aware ranking
	// ========================================================================

	/// <summary>
	/// Converts the string-keyed <see cref="LayoutSubgraph"/> tree into the int-indexed
	/// <see cref="NestingGraphRanker.Group"/> tree <see cref="LayerAssigner"/> needs, plus the
	/// list of real node indices that belong to no subgraph at all (dagre's top-level loose
	/// nodes, which still need a direct root edge for nesting-graph's connectivity guarantee).
	/// </summary>
	private static (List<int> LooseNodes, List<NestingGraphRanker.Group> Groups) BuildNestingGroups(
		LayoutGraph input, GraphBuffer buf)
	{
		var nodeIndex = new Dictionary<string, int>(buf.RealNodeCount);
		for (var i = 0; i < buf.RealNodeCount; i++)
			nodeIndex[buf.NodeIds[i]] = i;

		var claimed = new HashSet<int>(buf.RealNodeCount);
		var groups = new List<NestingGraphRanker.Group>(input.Subgraphs.Count);
		foreach (var sg in input.Subgraphs)
			groups.Add(BuildNestingGroup(sg, nodeIndex, claimed));

		var loose = new List<int>(buf.RealNodeCount);
		for (var i = 0; i < buf.RealNodeCount; i++)
		{
			if (!claimed.Contains(i))
				loose.Add(i);
		}

		return (loose, groups);
	}

	private static NestingGraphRanker.Group BuildNestingGroup(
		LayoutSubgraph sg, Dictionary<string, int> nodeIndex, HashSet<int> claimed)
	{
		var group = new NestingGraphRanker.Group();
		foreach (var id in sg.NodeIds)
		{
			// claimed.Add returns false for a node already claimed by an earlier subgraph
			// (overlapping subgraph membership) — first occurrence wins, matching the
			// TryAdd-based "first wins" convention used elsewhere (CollectSubgraphMembership).
			if (nodeIndex.TryGetValue(id, out var idx) && claimed.Add(idx))
				group.OwnNodes.Add(idx);
		}
		foreach (var child in sg.Children)
			group.Children.Add(BuildNestingGroup(child, nodeIndex, claimed));
		return group;
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
			if (right > maxX)
				maxX = right;
			if (bottom > maxY)
				maxY = bottom;

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
			if (route.OriginalIndex >= input.Edges.Count)
				continue;

			var points = new List<LayoutPoint>(route.Points.Count);
			foreach (var p in route.Points)
			{
				points.Add(p);
				if (p.X + padding > maxX)
					maxX = p.X + padding;
				if (p.Y + padding > maxY)
					maxY = p.Y + padding;
			}

			edges.Add(new LayoutEdgeResult(route.OriginalIndex, points, route.LabelPosition));
		}

		var groups = ComputeGroups(buf, input);

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
			if (g.X < minX)
				minX = g.X;
			if (g.Y < minY)
				minY = g.Y;
			FindMinGroupBounds(g.Children.ToList(), ref minX, ref minY);
		}
	}

	private static void ExpandBoundsForGroups(IReadOnlyList<LayoutGroupResult> groups, ref double maxX, ref double maxY)
	{
		foreach (var g in groups)
		{
			var right = g.X + g.Width;
			var bottom = g.Y + g.Height;
			if (right > maxX)
				maxX = right;
			if (bottom > maxY)
				maxY = bottom;
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

	private static List<LayoutGroupResult> ComputeGroups(GraphBuffer buf, LayoutGraph input)
	{
		if (input.Subgraphs.Count == 0)
			return [];

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
			if (!nodeIndex.TryGetValue(nodeId, out var idx))
				continue;
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
			minX = 0;
			minY = 0;
			maxX = 100;
			maxY = 60;
		}

		return new LayoutGroupResult(
			sg.Id, sg.Label,
			minX - groupPadding,
			minY - groupPadding - headerHeight,
			maxX - minX + (groupPadding * 2),
			maxY - minY + (groupPadding * 2) + headerHeight,
			children);
	}
}
