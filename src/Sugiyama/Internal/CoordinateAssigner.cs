namespace Sugiyama.Internal;

/// <summary>
/// Phase 4: Assign X/Y coordinates to nodes.
/// Uses a priority-based placement inspired by the ELK layered algorithm:
/// 1. Assign Y (primary axis) based on layer heights + spacing
/// 2. Assign initial X (secondary axis) by stacking within each layer
/// 3. Center all layers relative to the widest
/// 4. Iteratively pull nodes toward the median X of their connected neighbors
/// 5. Final compaction pass to re-center layers
/// </summary>
internal static class CoordinateAssigner
{
	internal static void Run(GraphBuffer graph, double nodeSpacing, double layerSpacing)
	{
		AssignPrimaryAxis(graph, layerSpacing);
		AssignSecondaryAxis(graph, nodeSpacing);
		CenterLayers(graph);
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
	/// Center each layer horizontally within the widest layer's bounding box.
	/// Uses actual bounding box positions, not just widths.
	/// </summary>
	private static void CenterLayers(GraphBuffer graph)
	{
		var maxRight = 0.0;
		for (var layer = 0; layer < graph.LayerCount; layer++)
		{
			var nodes = graph.LayerNodes[layer];
			if (nodes.Length == 0)
				continue;
			var last = nodes[^1];
			var lastW = last < graph.RealNodeCount ? graph.NodeWidths[last] : 0;
			var right = graph.X[last] + lastW;
			if (right > maxRight)
				maxRight = right;
		}

		for (var layer = 0; layer < graph.LayerCount; layer++)
		{
			var nodes = graph.LayerNodes[layer];
			if (nodes.Length == 0)
				continue;

			var layerLeft = graph.X[nodes[0]];
			var last = nodes[^1];
			var lastW = last < graph.RealNodeCount ? graph.NodeWidths[last] : 0;
			var layerRight = graph.X[last] + lastW;
			var layerWidth = layerRight - layerLeft;

			var desiredLeft = (maxRight - layerWidth) / 2.0;
			var shift = desiredLeft - layerLeft;
			if (Math.Abs(shift) < 0.5)
				continue;

			foreach (var node in nodes)
				graph.X[node] += shift;
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
