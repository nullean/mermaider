namespace Sugiyama.Internal;

/// <summary>
/// Phase 3: Minimize edge crossings using the barycenter heuristic.
/// Sweeps top-down then bottom-up for a configurable number of iterations.
/// All sorting is in-place on flat arrays — no LINQ, no allocations per sweep.
/// Complexity: O(iterations × E) per sweep
/// </summary>
internal static class CrossingMinimizer
{
	internal static void Run(GraphBuffer graph, int iterations = 4)
	{
		if (graph.LayerCount <= 1)
			return;

		var barycenters = new double[graph.NodeCount];

		for (var iter = 0; iter < iterations; iter++)
		{
			for (var layer = 1; layer < graph.LayerCount; layer++)
				SweepLayer(graph, layer, barycenters, useInEdges: true);

			for (var layer = graph.LayerCount - 2; layer >= 0; layer--)
				SweepLayer(graph, layer, barycenters, useInEdges: false);
		}

		EnforceSameRankOrder(graph);
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
				foreach (var e in graph.Edges)
				{
					if (e.To != node)
						continue;
					if (graph.Layers[e.From] != layer - 1)
						continue;
					sum += graph.NodePositionInLayer[e.From];
					count++;
				}
			}
			else
			{
				foreach (var e in graph.Edges)
				{
					if (e.From != node)
						continue;
					if (graph.Layers[e.To] != layer + 1)
						continue;
					sum += graph.NodePositionInLayer[e.To];
					count++;
				}
			}

			barycenters[node] = count > 0 ? sum / count : graph.NodePositionInLayer[node];
		}

		Array.Sort(nodes, (a, b) =>
		{
			var cmp = barycenters[a].CompareTo(barycenters[b]);
			return cmp != 0 ? cmp : graph.NodePositionInLayer[a].CompareTo(graph.NodePositionInLayer[b]);
		});

		for (var pos = 0; pos < nodes.Length; pos++)
			graph.NodePositionInLayer[nodes[pos]] = pos;
	}
}
