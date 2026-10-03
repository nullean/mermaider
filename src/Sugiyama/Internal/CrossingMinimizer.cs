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
	internal static void Run(GraphBuffer graph, int iterations = 4, bool useModelOrderForVirtuals = false,
		bool useModelOrderForRealNodes = false, bool useRealFirstTiebreaker = false, CancellationToken ct = default)
	{
		if (graph.LayerCount <= 1)
			return;

		var barycenters = new double[graph.NodeCount];

		// Track the best ordering seen across sweeps by total crossing count, not just
		// whichever sweep happened to run last — a single barycenter sweep direction can
		// locally improve each layer pair in isolation while making the graph-wide total
		// worse, and an unlucky final sweep can otherwise discard a better ordering an
		// earlier sweep already found. Mirrors dagre's order/index.ts, which runs up to
		// several non-improving sweeps before giving up and keeps the best-by-crossing-count
		// layering it ever saw, rather than trusting monotonic improvement from the heuristic.
		var bestCrossings = int.MaxValue;
		int[]? bestOrder = null;

		for (var iter = 0; iter < iterations; iter++)
		{
			ct.ThrowIfCancellationRequested();
			for (var layer = 1; layer < graph.LayerCount; layer++)
				SweepLayer(graph, layer, barycenters, useInEdges: true, useModelOrderForVirtuals, useModelOrderForRealNodes, useRealFirstTiebreaker);

			ct.ThrowIfCancellationRequested();
			for (var layer = graph.LayerCount - 2; layer >= 0; layer--)
				SweepLayer(graph, layer, barycenters, useInEdges: false, useModelOrderForVirtuals, useModelOrderForRealNodes, useRealFirstTiebreaker);

			var total = TotalCrossings(graph);
			if (total >= bestCrossings)
				continue;
			bestCrossings = total;
			bestOrder ??= new int[graph.NodeCount];
			Array.Copy(graph.NodePositionInLayer, bestOrder, graph.NodeCount);
		}

		if (bestOrder is not null)
			RestoreOrder(graph, bestOrder);

		ct.ThrowIfCancellationRequested();
		EnforceSameRankOrder(graph);

		// Post-barycenter greedy refinement: escape local minima by trying adjacent swaps.
		// Accepts a swap only when it strictly reduces the combined permutation crossing count
		// across the two affected layer pairs (L-1:L and L:L+1). Iterates until stable.
		LocalSwapRefinement(graph, ct);
	}

	private static int TotalCrossings(GraphBuffer graph)
	{
		var total = 0;
		for (var layer = 0; layer < graph.LayerCount - 1; layer++)
			total += CountCrossingsBetween(graph, layer, layer + 1);
		return total;
	}

	/// <summary>Restores a snapshot taken from <see cref="GraphBuffer.NodePositionInLayer"/>.</summary>
	private static void RestoreOrder(GraphBuffer graph, int[] positionSnapshot)
	{
		Array.Copy(positionSnapshot, graph.NodePositionInLayer, graph.NodeCount);
		for (var layer = 0; layer < graph.LayerCount; layer++)
		{
			var nodes = graph.LayerNodes[layer];
			Array.Sort(nodes, (a, b) => positionSnapshot[a].CompareTo(positionSnapshot[b]));
		}
	}

	/// <summary>
	/// Layers larger than this fall back to adjacent-only swaps in <see cref="LocalSwapRefinement"/>:
	/// the full pairwise search below is O(layer_size² × E) per pass, fine for the handful-to-dozens
	/// of siblings real diagrams have per layer, but a guardrail against quadratic blowup on
	/// adversarial input with one very wide layer (<c>ResourceLimits.MaxElements</c> bounds total
	/// node count, not per-layer count).
	/// </summary>
	private const int FullPairwiseSwapLayerSizeLimit = 60;

	/// <summary>
	/// Greedy swap pass. For every pair of real nodes in a layer — not just adjacent ones —
	/// swaps them if doing so doesn't worsen either adjoining layer pair's crossing count and
	/// strictly reduces their combined total. Checking every pair (not only neighbors) matters
	/// because a two-node exchange that helps is sometimes only reachable by swapping nodes
	/// separated by others that must stay put (an adjacent-only pass can't reach that swap at
	/// all, regardless of how many times it repeats). Operates only on real nodes; virtual
	/// nodes are not swapped (they track their chain).
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

				var aboveBefore = layer > 0 ? CountCrossingsBetween(graph, layer - 1, layer) : 0;
				var belowBefore = layer < graph.LayerCount - 1 ? CountCrossingsBetween(graph, layer, layer + 1) : 0;
				if (aboveBefore == 0 && belowBefore == 0)
					continue;

				var maxJump = nodes.Length <= FullPairwiseSwapLayerSizeLimit ? nodes.Length : 2;

				for (var i = 0; i < nodes.Length - 1; i++)
				{
					var a = nodes[i];
					if (a >= graph.RealNodeCount)
						continue;

					var jLimit = Math.Min(nodes.Length, i + maxJump);
					for (var j = i + 1; j < jLimit; j++)
					{
						var b = nodes[j];
						if (b >= graph.RealNodeCount)
							continue;

						nodes[i] = b;
						nodes[j] = a;
						graph.NodePositionInLayer[a] = j;
						graph.NodePositionInLayer[b] = i;

						var aboveAfter = layer > 0 ? CountCrossingsBetween(graph, layer - 1, layer) : 0;
						var belowAfter = layer < graph.LayerCount - 1 ? CountCrossingsBetween(graph, layer, layer + 1) : 0;

						// Accept if: neither touching pair gets worse AND the total strictly
						// decreases. This handles both single-crossing elimination (1→0) and
						// multi-crossing reduction (e.g. 2→1) without risking a regression in
						// either pair.
						var isImprovement =
							aboveAfter <= aboveBefore &&
							belowAfter <= belowBefore &&
							aboveAfter + belowAfter < aboveBefore + belowBefore;

						if (isImprovement)
						{
							changed = true;
							aboveBefore = aboveAfter;
							belowBefore = belowAfter;
							// nodes[i] now holds b, not a — refresh so subsequent j iterations for
							// this i swap against the node actually sitting there.
							a = b;
						}
						else
						{
							// Revert
							nodes[i] = a;
							nodes[j] = b;
							graph.NodePositionInLayer[a] = i;
							graph.NodePositionInLayer[b] = j;
						}

						if (aboveBefore == 0 && belowBefore == 0)
							break;
					}

					if (aboveBefore == 0 && belowBefore == 0)
						break;
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

	private static void SweepLayer(GraphBuffer graph, int layer, double[] barycenters, bool useInEdges,
		bool useModelOrderForVirtuals, bool useModelOrderForRealNodes, bool useRealFirstTiebreaker = false)
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
			var aVirt = a >= graph.RealNodeCount;
			var bVirt = b >= graph.RealNodeCount;
			if (useModelOrderForVirtuals && (aVirt || bVirt))
			{
				// Apply ELK's considerModelOrder.NODES_AND_EDGES for virtual-vs-real ties:
				// virtual chain nodes carry OriginalEdgeIndex as model order, real nodes carry
				// NodeIndex. When model orders equal, real nodes sort before virtual nodes.
				// When both are real, fall through to stable sort by current position.
				var aOrder = aVirt && graph.VirtualNodeModelOrder != null
					? graph.VirtualNodeModelOrder[a - graph.RealNodeCount]
					: a;
				var bOrder = bVirt && graph.VirtualNodeModelOrder != null
					? graph.VirtualNodeModelOrder[b - graph.RealNodeCount]
					: b;
				var orderCmp = aOrder.CompareTo(bOrder);
				if (orderCmp != 0)
					return orderCmp;
				// Same model order: real before virtual.
				if (aVirt != bVirt)
					return aVirt ? 1 : -1;
			}
			else if (aVirt != bVirt)
			{
				if (useRealFirstTiebreaker)
				{
					// Real-first: real nodes sort before virtual (long-edge dummy) nodes on
					// barycenter ties. Matches ELK's NODES_AND_EDGES behaviour where real nodes
					// are seeded before long-edge dummies in the initial layer ordering.
					return aVirt ? 1 : -1;
				}
				// Virtual-first tiebreaker (default): virtual nodes sort before real nodes on ties,
				// anchoring long-edge chains to the left and reducing crossings.
				return aVirt ? -1 : 1;
			}
			// Real vs real: optionally use node model index (ELK NODES_AND_EDGES behavior),
			// otherwise stable-sort by current layer position.
			if (useModelOrderForRealNodes && !aVirt && !bVirt)
				return a.CompareTo(b);
			return graph.NodePositionInLayer[a].CompareTo(graph.NodePositionInLayer[b]);
		});

		for (var pos = 0; pos < nodes.Length; pos++)
			graph.NodePositionInLayer[nodes[pos]] = pos;
	}
}
