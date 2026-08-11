namespace Sugiyama.Internal;

/// <summary>
/// Phase 4: Assign X/Y coordinates to nodes.
/// 1. Assign Y (primary axis) based on layer heights + spacing
/// 2. Assign initial X (secondary axis) by stacking within each layer (virtual nodes keep this)
/// 3. PlaceBySubtreeWidth: compute each node's subtree width bottom-up, then spread
///    children symmetrically top-down into allocated slots so every parent lands exactly
///    centered over its children — no spacing-enforcement drift.
/// 4. Iteratively pull nodes toward the median X of their connected neighbors (fine-tune
///    non-tree edges, virtual nodes, multi-parent nodes)
/// 5. Global X normalise (shift so min X = 0)
/// </summary>
internal static class CoordinateAssigner
{
	internal static void Run(GraphBuffer graph, double nodeSpacing, double layerSpacing)
	{
		AssignPrimaryAxis(graph, layerSpacing);
		AssignSecondaryAxis(graph, nodeSpacing);
		PlaceBySubtreeWidth(graph, nodeSpacing);
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
	/// Place real nodes using subtree-width allocation (Reingold–Tilford style).
	///
	/// Bottom-up: each node's subtree width = max(own width, packed width of its
	/// real children in the next layer). This guarantees every parent gets exactly
	/// enough horizontal room for its entire subtree.
	///
	/// Top-down: layer-0 roots are spread sequentially, each allocated its subtree
	/// width. Then children are spread symmetrically inside the parent's slot.
	/// Because the slot is pre-sized, parents land exactly over the center of their
	/// children — no spacing-enforcement drift that would break symmetry.
	///
	/// Virtual nodes keep their AssignSecondaryAxis positions; AlignToConnections
	/// fine-tunes them afterward.
	/// </summary>
	private static void PlaceBySubtreeWidth(GraphBuffer graph, double nodeSpacing)
	{
		var outEdges = BuildOutEdges(graph);

		// --- 1. Compute subtree widths bottom-up ---
		var subtreeWidths = new double[graph.NodeCount];
		for (var i = 0; i < graph.NodeCount; i++)
			subtreeWidths[i] = i < graph.RealNodeCount ? graph.NodeWidths[i] : 0;

		for (var layer = graph.LayerCount - 2; layer >= 0; layer--)
		{
			foreach (var node in graph.LayerNodes[layer])
			{
				if (node >= graph.RealNodeCount)
					continue;
				if (!outEdges.TryGetValue(node, out var children))
					continue;

				double childrenTotalWidth = 0;
				var childCount = 0;
				foreach (var child in children)
				{
					if (graph.Layers[child] != layer + 1 || child >= graph.RealNodeCount)
						continue;
					childrenTotalWidth += subtreeWidths[child];
					childCount++;
				}
				if (childCount == 0)
					continue;
				childrenTotalWidth += nodeSpacing * (childCount - 1);
				subtreeWidths[node] = Math.Max(graph.NodeWidths[node], childrenTotalWidth);
			}
		}

		// --- 2. Place layer 0: each root allocated its full subtree width ---
		var placed = new bool[graph.NodeCount];
		var currentX = 0.0;
		foreach (var node in graph.LayerNodes[0])
		{
			if (node < graph.RealNodeCount)
			{
				graph.X[node] = currentX + (subtreeWidths[node] / 2.0) - (graph.NodeWidths[node] / 2.0);
				currentX += subtreeWidths[node] + nodeSpacing;
			}
			// virtual nodes in layer 0 keep AssignSecondaryAxis position
			placed[node] = true;
		}

		// --- 3. Top-down: spread each parent's children symmetrically in its slot ---
		for (var layer = 0; layer < graph.LayerCount - 1; layer++)
		{
			foreach (var parent in graph.LayerNodes[layer])
			{
				if (parent >= graph.RealNodeCount || !placed[parent])
					continue;
				if (!outEdges.TryGetValue(parent, out var children))
					continue;

				// Collect unplaced real children in the next layer (sorted by crossing-minimizer order)
				var realChildren = new List<int>();
				foreach (var child in children)
				{
					if (graph.Layers[child] == layer + 1 && child < graph.RealNodeCount && !placed[child])
						if (!realChildren.Contains(child))
							realChildren.Add(child);
				}
				if (realChildren.Count == 0)
					continue;
				realChildren.Sort((a, b) => graph.NodePositionInLayer[a].CompareTo(graph.NodePositionInLayer[b]));

				var parentCX = graph.X[parent] + (graph.NodeWidths[parent] / 2.0);

				double totalSlotWidth = 0;
				foreach (var child in realChildren)
					totalSlotWidth += subtreeWidths[child];
				totalSlotWidth += nodeSpacing * (realChildren.Count - 1);

				var startX = parentCX - (totalSlotWidth / 2.0);
				foreach (var child in realChildren)
				{
					var childW = graph.NodeWidths[child];
					graph.X[child] = startX + (subtreeWidths[child] / 2.0) - (childW / 2.0);
					startX += subtreeWidths[child] + nodeSpacing;
					placed[child] = true;
				}
			}
		}

		// --- 4. Enforce minimum spacing within each layer ---
		// Fixes any remaining overlaps from multi-parent conflicts or orphaned nodes.
		for (var layer = 0; layer < graph.LayerCount; layer++)
		{
			var nodes = graph.LayerNodes[layer];
			for (var pos = 1; pos < nodes.Length; pos++)
			{
				var prev = nodes[pos - 1];
				var curr = nodes[pos];
				var prevW = prev < graph.RealNodeCount ? graph.NodeWidths[prev] : 0;
				var minX = graph.X[prev] + prevW + nodeSpacing;
				if (graph.X[curr] < minX)
					graph.X[curr] = minX;
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

		// Anchored leaves: tree leaves (no real children, one real parent) that have
		// at least one sibling. PlaceBySubtreeWidth already placed these optimally —
		// their position defines the parent's subtree corridor. Pulling them toward
		// their parent in the downward sweep would displace them and cause edge crossings.
		//
		// Sole-child leaves ("floating") are excluded from this set: without siblings
		// they carry no corridor constraint and should follow their parent naturally.
		var anchoredLeaves = new HashSet<int>();
		for (var i = 0; i < graph.RealNodeCount; i++)
		{
			if (outEdges.TryGetValue(i, out var ch))
			{
				var hasRealChild = false;
				foreach (var c in ch)
					if (c < graph.RealNodeCount)
					{
						hasRealChild = true;
						break;
					}

				if (hasRealChild)
					continue;
			}

			var realParents = 0;
			var singleParent = -1;
			if (inEdges.TryGetValue(i, out var parents))
				foreach (var p in parents)
					if (p < graph.RealNodeCount)
					{
						realParents++;
						singleParent = p;
					}

			if (realParents != 1 || singleParent < 0)
				continue;

			// Only anchor leaves that share their parent with at least one sibling.
			if (!outEdges.TryGetValue(singleParent, out var siblingCandidates))
				continue;

			var hasSibling = false;
			foreach (var sibling in siblingCandidates)
			{
				if (sibling != i && sibling < graph.RealNodeCount)
				{
					hasSibling = true;
					break;
				}
			}

			if (hasSibling)
				_ = anchoredLeaves.Add(i);
		}

		for (var pass = 0; pass < 4; pass++)
		{
			for (var layer = 1; layer < graph.LayerCount; layer++)
				MedianPull(graph, layer, inEdges, nodeSpacing, sweepDown: true, anchoredLeaves);

			for (var layer = graph.LayerCount - 2; layer >= 0; layer--)
				MedianPull(graph, layer, outEdges, nodeSpacing, sweepDown: false, null);
		}
	}

	private static void MedianPull(
		GraphBuffer graph, int layer,
		Dictionary<int, List<int>> adjacency,
		double nodeSpacing,
		bool sweepDown,
		HashSet<int>? skipNodes)
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

			if (skipNodes != null && skipNodes.Contains(node))
				continue;

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
