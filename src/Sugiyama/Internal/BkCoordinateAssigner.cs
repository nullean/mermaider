namespace Sugiyama.Internal;

/// <summary>
/// Brandes–Köpf BALANCED horizontal coordinate assignment.
///
/// Reference: U. Brandes and B. Köpf, "Fast and Simple Horizontal Coordinate Assignment",
/// Graph Drawing (GD 2001), LNCS 2265, pp. 31–44.
///
/// Runs four independent alignment+compaction passes (TL, TR, BL, BR) and sets each
/// node's X to the median of the four results. Produces more balanced, compact
/// layouts than barycenter/median-pull, and in particular keeps inner segments
/// (virtual-node chains for skip-layer edges) straight — matching ELK's
/// elk.layered behaviour with bk.fixedAlignment: BALANCED.
///
/// Variable widths: all separations use left-edge arithmetic — sep(u, v) = width[u] + spacing,
/// meaning x[v] >= x[u] + width[u] + spacing for adjacent same-layer nodes u, v.
/// </summary>
internal static class BkCoordinateAssigner
{
	internal static void Run(GraphBuffer graph, double nodeSpacing)
	{
		var n = graph.NodeCount;
		var xLayouts = new double[4][];

		// 4 passes: (k=0) TL, (k=1) TR, (k=2) BL, (k=3) BR
		for (var k = 0; k < 4; k++)
		{
			var downward = k < 2;
			var leftward = (k % 2) == 0;
			var (root, align) = VertAlign(graph, downward, leftward);
			xLayouts[k] = Compact(graph, root, align, nodeSpacing, leftward);
		}

		// Normalize each layout: shift so its minimum x = 0
		for (var k = 0; k < 4; k++)
		{
			var minX = double.MaxValue;
			for (var i = 0; i < n; i++)
			{
				if (xLayouts[k][i] < minX)
					minX = xLayouts[k][i];
			}
			if (minX != 0)
			{
				for (var i = 0; i < n; i++)
					xLayouts[k][i] -= minX;
			}
		}

		// Balance: median of 4 for each node
		var tmp = new double[4];
		for (var i = 0; i < n; i++)
		{
			for (var k = 0; k < 4; k++)
				tmp[k] = xLayouts[k][i];
			Array.Sort(tmp);
			graph.X[i] = (tmp[1] + tmp[2]) / 2.0;
		}

		// Post-BALANCED correction: BALANCED averaging of left-edge and right-edge layouts
		// can produce positions that violate the minimum separation for nodes in long blocks.
		// Do a single left-to-right sweep per layer to enforce the minimum gap.
		var layerByX = new List<int>();
		for (var li = 0; li < graph.LayerCount; li++)
		{
			layerByX.Clear();
			layerByX.AddRange(graph.LayerNodes[li]);
			layerByX.Sort((a, b) => graph.X[a].CompareTo(graph.X[b]));

			for (var idx = 1; idx < layerByX.Count; idx++)
			{
				var prev = layerByX[idx - 1];
				var curr = layerByX[idx];
				var prevWidth = prev < graph.RealNodeCount ? graph.NodeWidths[prev] : 0;
				var minX = graph.X[prev] + prevWidth + nodeSpacing;
				if (graph.X[curr] < minX)
					graph.X[curr] = minX;
			}
		}
	}

	/// <summary>
	/// Phase 1: vertical alignment.
	///
	/// For each node v, find a median upper (or lower) neighbor u and align u → v (link
	/// them into the same block). The root[] and align[] arrays together encode a set of
	/// blocks, each a circular linked list traversable via align[]. root[v] is the "root"
	/// (canonical representative) of v's block.
	///
	/// Type-1 conflicts — a non-inner segment that crosses an inner segment — are avoided
	/// by marking them during a pre-scan and skipping marked pairs during alignment.
	/// </summary>
	private static (int[] root, int[] align) VertAlign(GraphBuffer graph, bool downward, bool leftward)
	{
		var n = graph.NodeCount;
		var root = new int[n];
		var align = new int[n];
		for (var i = 0; i < n; i++)
		{
			root[i] = i;
			align[i] = i;
		}

		// Type-1 conflicts are computed in the downward direction (upper→lower layer pairs).
		// For upward passes we check the reversed pair because u/v roles are swapped.
		var blocked = MarkType1Conflicts(graph);

		// Iterate layers from second to last (in direction order).
		// "previous layer" = the layer processed before this one (upper for downward, lower for upward).
		var li0 = downward ? 1 : graph.LayerCount - 2;
		var li1 = downward ? graph.LayerCount : -1;
		var liStep = downward ? 1 : -1;

		for (var li = li0; li != li1; li += liStep)
		{
			var layer = graph.LayerNodes[li];
			var prevLayerIdx = li - liStep; // "upper" layer in this pass direction

			// r: the position (in prevLayer, in the flipped coord for rightward passes) of the last
			// aligned neighbor. posU is already flipped for rightward so "must increase" applies
			// to both directions. Initialize to -1 (below all valid positions) in both cases.
			var r = -1;

			// Process nodes in layer li in hDir order
			var ki0 = leftward ? 0 : layer.Length - 1;
			var ki1 = leftward ? layer.Length : -1;
			var kiStep = leftward ? 1 : -1;

			for (var ki = ki0; ki != ki1; ki += kiStep)
			{
				var v = layer[ki];

				// Collect neighbors in prevLayer
				var neighbors = GetNeighbors(graph, v, prevLayerIdx, downward);
				if (neighbors.Count == 0)
					continue;

				// Sort by position in prevLayer; flip for right-to-left so median is correct
				neighbors.Sort((a, b) => graph.NodePositionInLayer[a].CompareTo(graph.NodePositionInLayer[b]));
				if (!leftward)
					neighbors.Reverse();

				var d = neighbors.Count;
				var m1 = (d - 1) / 2; // lower median index (0-based)
				var m2 = d / 2;       // upper median index (0-based)

				for (var m = m1; m <= m2; m++)
				{
					if (align[v] != v)
						break; // already aligned in this pass

					var u = neighbors[m];
					var posU = leftward
						? graph.NodePositionInLayer[u]
						: (graph.LayerNodes[prevLayerIdx].Length - 1 - graph.NodePositionInLayer[u]);

					// Skip type-1 conflicts (conflicts stored as upper,lower; reverse for upward passes)
					if (downward ? blocked.Contains((u, v)) : blocked.Contains((v, u)))
						continue;

					// r constraint: posU must increase (rightward passes use flipped positions)
					if (posU > r)
					{
						align[u] = v;
						root[v] = root[u];
						align[v] = root[v];
						r = posU;
					}
				}
			}
		}

		return (root, align);
	}

	/// <summary>
	/// Phase 2: horizontal compaction.
	///
	/// Places each block at the leftmost (or rightmost for rightward passes) valid x
	/// position. Blocks are placed in topological order determined by the layer ordering.
	/// A "sink" indirection handles blocks that share the same column class.
	///
	/// Returns left-edge x coordinates (not centers).
	/// </summary>
	private static double[] Compact(
		GraphBuffer graph, int[] root, int[] align, double nodeSpacing, bool leftward)
	{
		var n = graph.NodeCount;
		var x = new double[n];
		var placed = new bool[n];
		var sink = new int[n];
		var shift = new double[n];

		for (var i = 0; i < n; i++)
		{
			sink[i] = i;
			shift[i] = leftward ? double.PositiveInfinity : double.NegativeInfinity;
		}

		// Place each block root (in layer order so neighbors are already placed).
		var li0 = leftward ? 0 : graph.LayerCount - 1;
		var li1 = leftward ? graph.LayerCount : -1;
		var liStep = leftward ? 1 : -1;

		for (var li = li0; li != li1; li += liStep)
		{
			foreach (var v in graph.LayerNodes[li])
			{
				if (root[v] == v && !placed[v])
					PlaceBlock(graph, root, align, sink, shift, x, placed, nodeSpacing, leftward, v);
			}
		}

		// Assign x from roots + shifts
		var result = new double[n];
		for (var v = 0; v < n; v++)
		{
			result[v] = x[root[v]];
			var s = shift[sink[root[v]]];
			if (leftward ? s < double.PositiveInfinity : s > double.NegativeInfinity)
				result[v] += s;
		}

		// For rightward passes: rightward compaction uses right-edge placement convention,
		// where x[root] + width[v] = right_edge is constant per block.
		// Convert: left_edge = -(x[root] + width[v]), negate to get a sortable value.
		if (!leftward)
		{
			for (var v = 0; v < n; v++)
			{
				var w = v < graph.RealNodeCount ? graph.NodeWidths[v] : 0;
				result[v] = -(result[v] + w);
			}
		}

		return result;
	}

	private static void PlaceBlock(
		GraphBuffer graph,
		int[] root, int[] align,
		int[] sink, double[] shift,
		double[] x, bool[] placed,
		double nodeSpacing, bool leftward,
		int v)
	{
		placed[v] = true;

		// Walk the block (v → align[v] → ... → v, circular)
		var w = v;
		do
		{
			var layer = graph.LayerNodes[graph.Layers[w]];
			var posW = graph.NodePositionInLayer[w];

			if (leftward)
			{
				// For left-to-right: ensure x[v] >= x[left_neighbor_root] + sep
				if (posW > 0)
				{
					var uNode = layer[posW - 1];
					var uRoot = root[uNode];
					if (!placed[uRoot])
						PlaceBlock(graph, root, align, sink, shift, x, placed, nodeSpacing, leftward, uRoot);

					var sep = Separation(graph, uNode, nodeSpacing);
					if (sink[v] == v)
						sink[v] = sink[uRoot];

					if (sink[v] != sink[uRoot])
					{
						var candidate = x[v] - x[uRoot] - sep;
						if (candidate < shift[sink[uRoot]])
							shift[sink[uRoot]] = candidate;
					}
					else
					{
						var minX = x[uRoot] + sep;
						if (x[v] < minX)
							x[v] = minX;
					}
				}
			}
			else
			{
				// Right-to-left: use right neighbors.
				// Right-edge convention: x[root] = right_edge. Constraint:
				// right_edge[v] + spacing <= left_edge[uNode] = right_edge[uNode] - width[uNode]
				// → x[root_v] >= x[root_uNode] + width[uNode] + spacing = Separation(uNode).
				if (posW < layer.Length - 1)
				{
					var uNode = layer[posW + 1];
					var uRoot = root[uNode];
					if (!placed[uRoot])
						PlaceBlock(graph, root, align, sink, shift, x, placed, nodeSpacing, leftward, uRoot);

					var sep = Separation(graph, uNode, nodeSpacing);
					if (sink[v] == v)
						sink[v] = sink[uRoot];

					if (sink[v] != sink[uRoot])
					{
						var candidate = x[v] - x[uRoot] - sep;
						if (candidate < shift[sink[uRoot]])
							shift[sink[uRoot]] = candidate;
					}
					else
					{
						var minX = x[uRoot] + sep;
						if (x[v] < minX)
							x[v] = minX;
					}
				}
			}

			w = align[w];
		} while (w != v);
	}

	/// <summary>
	/// Minimum separation between left-edge of u and left-edge of v when u is
	/// immediately to the left of v in the same layer (left-edge convention).
	/// </summary>
	private static double Separation(GraphBuffer graph, int u, double nodeSpacing)
	{
		var wU = u < graph.RealNodeCount ? graph.NodeWidths[u] : 0;
		return wU + nodeSpacing;
	}

	/// <summary>
	/// Collect neighbors of v in a specific layer index, using in-edges (downward pass)
	/// or out-edges (upward pass).
	/// </summary>
	private static List<int> GetNeighbors(GraphBuffer graph, int v, int layerIdx, bool downward)
	{
		var result = new List<int>();
		if (downward)
		{
			for (var ei = graph.InAdjStart[v]; ei < graph.InAdjStart[v + 1]; ei++)
			{
				var u = graph.InAdjNeighbor[ei];
				if (graph.Layers[u] == layerIdx)
					result.Add(u);
			}
		}
		else
		{
			for (var ei = graph.OutAdjStart[v]; ei < graph.OutAdjStart[v + 1]; ei++)
			{
				var u = graph.OutAdjNeighbor[ei];
				if (graph.Layers[u] == layerIdx)
					result.Add(u);
			}
		}

		return result;
	}

	/// <summary>
	/// Mark type-1 conflicts in the downward direction: pairs (u, v) where u is in the
	/// upper layer (lower index) and v is in the lower layer (higher index), and the
	/// edge (u, v) would cross an inner segment if aligned.
	///
	/// An inner segment is an edge between two virtual nodes (dummies in a long-edge
	/// chain). A type-1 conflict is a non-inner edge that crosses an inner segment.
	/// Results are stored as (upper, lower) pairs regardless of pass direction;
	/// VertAlign reverses the pair for upward passes.
	/// </summary>
	private static HashSet<(int U, int V)> MarkType1Conflicts(GraphBuffer graph)
	{
		var blocked = new HashSet<(int, int)>();

		// Always scan downward: upper=layer i, lower=layer i+1
		for (var upperLi = 0; upperLi < graph.LayerCount - 1; upperLi++)
		{
			var lowerLi = upperLi + 1;
			var upper = graph.LayerNodes[upperLi];
			var lower = graph.LayerNodes[lowerLi];

			// For each pair of crossing segments, mark the non-inner one as conflicted.
			// We scan edges from upper layer to lower layer, tracking the rightmost
			// inner-segment target position seen so far.
			var k0 = 0; // leftmost unchecked inner segment position in upper

			for (var l1 = 0; l1 < lower.Length; l1++)
			{
				var v = lower[l1];
				var vIsVirtual = v >= graph.RealNodeCount;

				// Find if v is reached by an inner segment (virtual→virtual edge from upper)
				var innerUpperPos = -1;
				for (var ei = graph.InAdjStart[v]; ei < graph.InAdjStart[v + 1]; ei++)
				{
					var u = graph.InAdjNeighbor[ei];
					if (graph.Layers[u] != upperLi)
						continue;
					if (vIsVirtual && u >= graph.RealNodeCount)
					{
						innerUpperPos = graph.NodePositionInLayer[u];
						break;
					}
				}

				// At the last node or at an inner-segment target: close the scan window
				if (l1 == lower.Length - 1 || innerUpperPos >= 0)
				{
					var k1 = innerUpperPos >= 0 ? innerUpperPos : upper.Length - 1;

					// Mark all non-inner edges from upper[k0..k1] to lower that conflict
					for (var l = k0; l <= l1; l++)
					{
						var w = lower[l];
						for (var ei = graph.InAdjStart[w]; ei < graph.InAdjStart[w + 1]; ei++)
						{
							var u = graph.InAdjNeighbor[ei];
							if (graph.Layers[u] != upperLi)
								continue;
							var posU = graph.NodePositionInLayer[u];
							// Conflict if u is outside the [k0, k1] window
							if (posU < k0 || posU > k1)
								_ = blocked.Add((u, w));
						}
					}

					k0 = innerUpperPos >= 0 ? innerUpperPos : k0;
				}
			}
		}

		return blocked;
	}
}
