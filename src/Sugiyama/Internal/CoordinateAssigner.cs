namespace Sugiyama.Internal;

/// <summary>
/// Phase 4: Assign X/Y coordinates to nodes.
/// Uses a priority-based placement inspired by the ELK layered algorithm:
/// 1. Assign Y (primary axis) based on layer heights + spacing
/// 2. Assign initial X (secondary axis) by stacking within each layer
/// 3. Seed parent positions bottom-up from children midpoints (subtree seeding)
/// 4. Iteratively pull nodes toward the median X of their connected neighbors
/// 5. Global X normalise (shift so min X = 0)
/// </summary>
internal static class CoordinateAssigner
{
	internal static void Run(GraphBuffer graph, double nodeSpacing, double layerSpacing)
	{
		AssignPrimaryAxis(graph, layerSpacing);
		AssignSecondaryAxis(graph, nodeSpacing);
		SeedFromSubtrees(graph, nodeSpacing);
		AlignToConnections(graph, nodeSpacing);
		NormalizeX(graph);
	}

	private static void AssignPrimaryAxis(GraphBuffer graph, double layerSpacing)
	{
		var currentY = 0.0;

		for (var layer = 0; layer < graph.LayerCount; layer++)
		{
			var nodes = graph.LayerNodes[layer];
			var maxHeight = 0.0;

			foreach (var node in nodes)
			{
				var h = node < graph.RealNodeCount ? graph.NodeHeights[node] : 0;
				if (h > maxHeight)
					maxHeight = h;
			}

			foreach (var node in nodes)
				graph.Y[node] = currentY;

			currentY += maxHeight + layerSpacing;
		}
	}

	private static void AssignSecondaryAxis(GraphBuffer graph, double nodeSpacing)
	{
		for (var layer = 0; layer < graph.LayerCount; layer++)
		{
			var nodes = graph.LayerNodes[layer];
			var currentX = 0.0;

			for (var i = 0; i < nodes.Length; i++)
			{
				var node = nodes[i];
				graph.X[node] = currentX;
				var w = node < graph.RealNodeCount ? graph.NodeWidths[node] : 0;
				currentX += w + nodeSpacing;
			}
		}
	}

	/// <summary>
	/// Seed initial X positions bottom-up from children.
	/// For each node that has real children in the next layer, place it at the
	/// median X of those children's centers. Compact siblings after each layer
	/// to restore minimum spacing without breaking the crossing-minimizer order.
	/// This gives AlignToConnections a much better starting point than global
	/// centering over the widest layer, keeping parents close to their subtrees.
	/// </summary>
	private static void SeedFromSubtrees(GraphBuffer graph, double nodeSpacing)
	{
		var outEdges = BuildOutEdges(graph);

		for (var layer = graph.LayerCount - 2; layer >= 0; layer--)
		{
			var nodes = graph.LayerNodes[layer];
			var anyMoved = false;

			for (var pos = 0; pos < nodes.Length; pos++)
			{
				var node = nodes[pos];
				if (!outEdges.TryGetValue(node, out var children))
					continue;

				var childCenters = new List<double>(children.Count);
				foreach (var child in children)
				{
					if (graph.Layers[child] != layer + 1 || child >= graph.RealNodeCount)
						continue;
					childCenters.Add(graph.X[child] + (graph.NodeWidths[child] / 2.0));
				}

				if (childCenters.Count == 0)
					continue;

				childCenters.Sort();
				var median = childCenters.Count % 2 == 1
					? childCenters[childCenters.Count / 2]
					: (childCenters[(childCenters.Count / 2) - 1] + childCenters[childCenters.Count / 2]) / 2.0;

				var nodeW = node < graph.RealNodeCount ? graph.NodeWidths[node] : 0;
				graph.X[node] = median - (nodeW / 2.0);
				anyMoved = true;
			}

			if (!anyMoved)
				continue;

			// Enforce minimum spacing left-to-right (push right if crowded)
			for (var pos = 1; pos < nodes.Length; pos++)
			{
				var prev = nodes[pos - 1];
				var curr = nodes[pos];
				var prevW = prev < graph.RealNodeCount ? graph.NodeWidths[prev] : 0;
				var minX = graph.X[prev] + prevW + nodeSpacing;
				if (graph.X[curr] < minX)
					graph.X[curr] = minX;
			}

			// Enforce minimum spacing right-to-left (pull back if piled right)
			for (var pos = nodes.Length - 2; pos >= 0; pos--)
			{
				var curr = nodes[pos];
				var next = nodes[pos + 1];
				var currW = curr < graph.RealNodeCount ? graph.NodeWidths[curr] : 0;
				var maxX = graph.X[next] - currW - nodeSpacing;
				if (graph.X[curr] > maxX)
					graph.X[curr] = maxX;
			}
		}
	}

	/// <summary>
	/// Global X normalize: shift all nodes so the minimum X is 0.
	/// Unlike CenterLayers, this preserves the relative alignment set by median-pull.
	/// </summary>
	private static void NormalizeX(GraphBuffer graph)
	{
		var minX = double.MaxValue;
		for (var i = 0; i < graph.NodeCount; i++)
		{
			if (graph.X[i] < minX)
				minX = graph.X[i];
		}

		if (Math.Abs(minX) < 0.5)
			return;
		for (var i = 0; i < graph.NodeCount; i++)
			graph.X[i] -= minX;
	}

	private static void AlignToConnections(GraphBuffer graph, double nodeSpacing)
	{
		var outEdges = BuildOutEdges(graph);
		var inEdges = BuildInEdges(graph);

		for (var pass = 0; pass < 4; pass++)
		{
			for (var layer = 1; layer < graph.LayerCount; layer++)
				MedianPull(graph, layer, inEdges, nodeSpacing, sweepDown: true);

			for (var layer = graph.LayerCount - 2; layer >= 0; layer--)
				MedianPull(graph, layer, outEdges, nodeSpacing, sweepDown: false);
		}
	}

	private static void MedianPull(
		GraphBuffer graph, int layer,
		Dictionary<int, List<int>> adjacency,
		double nodeSpacing,
		bool sweepDown)
	{
		var nodes = graph.LayerNodes[layer];
		var targetLayer = sweepDown ? layer - 1 : layer + 1;

		int start, end, step;
		if (sweepDown)
		{
			start = 0;
			end = nodes.Length;
			step = 1;
		}
		else
		{
			start = nodes.Length - 1;
			end = -1;
			step = -1;
		}

		for (var idx = start; idx != end; idx += step)
		{
			var node = nodes[idx];

			if (!adjacency.TryGetValue(node, out var neighbors))
				continue;

			var realPositions = new List<double>();
			var allPositions = new List<double>();
			foreach (var neighbor in neighbors)
			{
				if (graph.Layers[neighbor] != targetLayer)
					continue;
				var neighborW = neighbor < graph.RealNodeCount ? graph.NodeWidths[neighbor] : 0;
				var pos = graph.X[neighbor] + (neighborW / 2.0);
				allPositions.Add(pos);
				if (neighbor < graph.RealNodeCount)
					realPositions.Add(pos);
			}

			var positions = realPositions.Count > 0 ? realPositions : allPositions;
			if (positions.Count == 0)
				continue;

			positions.Sort();
			var median = positions.Count % 2 == 1
				? positions[positions.Count / 2]
				: (positions[(positions.Count / 2) - 1] + positions[positions.Count / 2]) / 2.0;

			var nodeW = node < graph.RealNodeCount ? graph.NodeWidths[node] : 0;
			var target = median - (nodeW / 2.0);

			var posInLayer = graph.NodePositionInLayer[node];

			if (posInLayer > 0)
			{
				var prev = nodes[posInLayer - 1];
				var prevW = prev < graph.RealNodeCount ? graph.NodeWidths[prev] : 0;
				var minX = graph.X[prev] + prevW + nodeSpacing;
				if (target < minX)
					target = minX;
			}

			if (posInLayer < nodes.Length - 1)
			{
				var next = nodes[posInLayer + 1];
				var maxX = graph.X[next] - nodeSpacing - nodeW;
				if (target > maxX)
					target = maxX;
			}

			graph.X[node] = target;
		}
	}

	private static Dictionary<int, List<int>> BuildOutEdges(GraphBuffer graph)
	{
		var result = new Dictionary<int, List<int>>();
		for (var i = 0; i < graph.Edges.Count; i++)
		{
			var e = graph.Edges[i];
			if (!result.TryGetValue(e.From, out var list))
			{
				list = [];
				result[e.From] = list;
			}

			list.Add(e.To);
		}

		return result;
	}

	private static Dictionary<int, List<int>> BuildInEdges(GraphBuffer graph)
	{
		var result = new Dictionary<int, List<int>>();
		for (var i = 0; i < graph.Edges.Count; i++)
		{
			var e = graph.Edges[i];
			if (!result.TryGetValue(e.To, out var list))
			{
				list = [];
				result[e.To] = list;
			}

			list.Add(e.From);
		}

		return result;
	}
}
