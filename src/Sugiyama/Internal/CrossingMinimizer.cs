using System.Collections.Generic;
using System.Threading;

namespace Sugiyama.Internal;

/// <summary>
/// Phase 3: Minimize edge crossings using the barycenter heuristic.
/// Sweeps top-down then bottom-up for a configurable number of iterations.
/// All sorting is in-place on flat arrays — no LINQ, no allocations per sweep.
/// Complexity: O(iterations × E) per layer
/// </summary>
internal static class CrossingMinimizer
{
	internal static void Run(GraphBuffer graph, int iterations = 4, CancellationToken ct = default)
	{
		if (graph.LayerCount <= 1)
			return;

		var barycenters = new double[graph.NodeCount];

		for (var iter = 0; iter < iterations; iter++)
		{
			ct.ThrowIfCancellationRequested();
			for (var layer = 1; layer < graph.LayerCount; layer++)
				SweepLayer(graph, layer, barycenters, useInEdges: true);

			ct.ThrowIfCancellationRequested();
			for (var layer = graph.LayerCount - 2; layer >= 0; layer--)
				SweepLayer(graph, layer, barycenters, useInEdges: false);
		}

		ct.ThrowIfCancellationRequested();
		EnforceSameRankOrder(graph);

		// Post-barycenter greedy refinement: escape local minima by trying adjacent swaps.
		// Accepts a swap only when it strictly reduces the combined permutation crossing count
		// across the two affected layer pairs (L-1:L and L:L+1). Iterates until stable.
		LocalSwapRefinement(graph, ct);
	}

	/// <summary>
	/// Greedy adjacent-swap pass. For every pair of adjacent real nodes in a layer, swaps
	/// them if the combined crossing count for the two adjoining layer pairs decreases.
	/// Operates only on real nodes; virtual nodes are not swapped (they track their chain).
	/// </summary>
	private static void LocalSwapRefinement(GraphBuffer graph, CancellationToken ct)
	{
		if (graph.LayerCount <= 1)
			return;

		bool changed;
		do
		{
			changed = false;
			for (var layer = 0; layer < graph.LayerCount; layer++)
			{
				ct.ThrowIfCancellationRequested();
				var nodes = graph.LayerNodes[layer];
				if (nodes.Length <= 1)
					continue;

				for (var i = 0; i < nodes.Length - 1; i++)
				{
					var a = nodes[i];
					var b = nodes[i + 1];
					// Only swap real nodes; virtual node ordering is determined by their chain.
					if (a >= graph.RealNodeCount || b >= graph.RealNodeCount)
						continue;

					// Conservative acceptance: a swap is accepted only if it eliminates the last
					// crossing in at least one of the two touching layer pairs. This avoids partial
					// improvements (e.g. 3→2) that risk diverging from MJS ordering, while still
					// allowing swaps that eliminate the sole remaining crossing in a pair.
					var aboveBefore = layer > 0 ? CountCrossingsBetween(graph, layer - 1, layer) : 0;
					var belowBefore = layer < graph.LayerCount - 1 ? CountCrossingsBetween(graph, layer, layer + 1) : 0;
					if (aboveBefore == 0 && belowBefore == 0)
						continue;
					// Perform the swap
					nodes[i] = b;
					nodes[i + 1] = a;
					graph.NodePositionInLayer[a] = i + 1;
					graph.NodePositionInLayer[b] = i;
					var aboveAfter = layer > 0 ? CountCrossingsBetween(graph, layer - 1, layer) : 0;
					var belowAfter = layer < graph.LayerCount - 1 ? CountCrossingsBetween(graph, layer, layer + 1) : 0;
					// Accept only if at least one pair was at exactly 1 crossing and is now at 0.
					var eliminatesLastCrossing =
						(aboveBefore == 1 && aboveAfter == 0) ||
						(belowBefore == 1 && belowAfter == 0);
					if (eliminatesLastCrossing)
					{
						changed = true;
					}
					else
					{
						// Revert
						nodes[i] = a;
						nodes[i + 1] = b;
						graph.NodePositionInLayer[a] = i;
						graph.NodePositionInLayer[b] = i + 1;
					}
				}
			}
		} while (changed);
	}

	/// <summary>
	/// Counts permutation crossings touching the given layer: crossings between (layer-1, layer)
	/// and between (layer, layer+1). Edges between same-layer nodes are ignored.
	/// </summary>
	private static int CrossingsForLayer(GraphBuffer graph, int layer)
	{
		var total = 0;
		if (layer > 0)
			total += CountCrossingsBetween(graph, layer - 1, layer);
		if (layer < graph.LayerCount - 1)
			total += CountCrossingsBetween(graph, layer, layer + 1);
		return total;
	}

	/// <summary>
	/// O(E²) permutation crossing count between two adjacent layers using the in/out adjacency.
	/// Only counts edges whose endpoints are strictly in the two specified layers.
	/// </summary>
	private static int CountCrossingsBetween(GraphBuffer graph, int upper, int lower)
	{
		// Collect all edges from upper → lower as (pos_upper, pos_lower) pairs
		var edges = CollectEdgePairs(graph, upper, lower);
		var n = edges.Count;
		var crossings = 0;
		for (var i = 0; i < n; i++)
		{
			for (var j = i + 1; j < n; j++)
			{
				var (a, b) = edges[i];
				var (c, d) = edges[j];
				if ((a < c && b > d) || (a > c && b < d))
					crossings++;
			}
		}
		return crossings;
	}

	private static List<(int, int)> CollectEdgePairs(GraphBuffer graph, int upper, int lower)
	{
		var edges = new List<(int, int)>();
		var nodes = graph.LayerNodes[upper];
		foreach (var node in nodes)
		{
			for (var j = graph.OutAdjStart[node]; j < graph.OutAdjStart[node + 1]; j++)
			{
				var to = graph.OutAdjNeighbor[j];
				if (graph.Layers[to] != lower)
					continue;
				edges.Add((graph.NodePositionInLayer[node], graph.NodePositionInLayer[to]));
			}
		}
		return edges;
	}

	private static void EnforceSameRankOrder(GraphBuffer graph)
	{
		if (graph.SameRankPairs.Count == 0)
			return;

		foreach (var (a, b) in graph.SameRankPairs)
		{
			if (graph.Layers[a] != graph.Layers[b])
				continue;

			var posA = graph.NodePositionInLayer[a];
			var posB = graph.NodePositionInLayer[b];
			if (posA >= posB)
			{
				var layer = graph.Layers[a];
				var nodes = graph.LayerNodes[layer];
				(nodes[posA], nodes[posB]) = (nodes[posB], nodes[posA]);
				graph.NodePositionInLayer[a] = posB;
				graph.NodePositionInLayer[b] = posA;
			}
		}
	}

	private static void SweepLayer(GraphBuffer graph, int layer, double[] barycenters, bool useInEdges)
	{
		var nodes = graph.LayerNodes[layer];
		if (nodes.Length <= 1)
			return;

		foreach (var node in nodes)
		{
			double sum = 0;
			var count = 0;

			if (useInEdges)
			{
				// Iterate only in-neighbors of this node (O(in-degree) not O(E))
				for (var j = graph.InAdjStart[node]; j < graph.InAdjStart[node + 1]; j++)
				{
					var from = graph.InAdjNeighbor[j];
					if (graph.Layers[from] != layer - 1)
						continue;
					sum += graph.NodePositionInLayer[from];
					count++;
				}
			}
			else
			{
				// Iterate only out-neighbors
				for (var j = graph.OutAdjStart[node]; j < graph.OutAdjStart[node + 1]; j++)
				{
					var to = graph.OutAdjNeighbor[j];
					if (graph.Layers[to] != layer + 1)
						continue;
					sum += graph.NodePositionInLayer[to];
					count++;
				}
			}

			barycenters[node] = count > 0 ? sum / count : graph.NodePositionInLayer[node];
		}

		Array.Sort(nodes, (a, b) =>
		{
			var cmp = barycenters[a].CompareTo(barycenters[b]);
			if (cmp != 0)
				return cmp;
			// On a tie prefer virtual nodes first: they represent skip-layer edge chains and
			// should be placed on the same side as their ultimate source, not pushed past real
			// nodes whose coordinates are not yet finalised.
			var aVirt = a >= graph.RealNodeCount;
			var bVirt = b >= graph.RealNodeCount;
			if (aVirt != bVirt)
				return aVirt ? -1 : 1;
			return graph.NodePositionInLayer[a].CompareTo(graph.NodePositionInLayer[b]);
		});

		for (var pos = 0; pos < nodes.Length; pos++)
			graph.NodePositionInLayer[nodes[pos]] = pos;
	}
}
