namespace Sugiyama.Internal;

/// <summary>
/// Phase 2: Assign each node to a discrete integer layer using longest-path layering
/// (Kahn's algorithm for topological order). Then insert virtual nodes for edges
/// that span more than one layer.
/// Complexity: O(V + E) via CSR adjacency
/// </summary>
internal static class LayerAssigner
{
	internal static void Run(GraphBuffer graph, bool naturalBackEdgeRouting = false)
	{
		graph.RebuildAdjacency();
		AssignLayers(graph);
		EnforceSameRankConstraints(graph);
		PushDownSources(graph);
		EnforceMinLengths(graph);
		InsertVirtualNodes(graph, naturalBackEdgeRouting);
		graph.RebuildAdjacency();
		BuildLayerArrays(graph);
	}

	private static void AssignLayers(GraphBuffer graph)
	{
		var n = graph.NodeCount;
		var inDegree = graph.RentInt(n);

		// Use CSR in-adjacency for fast in-degree
		for (var i = 0; i < n; i++)
			inDegree[i] = graph.InAdjStart[i + 1] - graph.InAdjStart[i];

		var queue = new Queue<int>(n);
		for (var i = 0; i < n; i++)
		{
			if (inDegree[i] == 0)
			{
				queue.Enqueue(i);
				graph.Layers[i] = 0;
			}
		}

		var maxLayer = 0;
		while (queue.Count > 0)
		{
			var node = queue.Dequeue();
			// Use CSR out-adjacency instead of scanning all edges
			for (var j = graph.OutAdjStart[node]; j < graph.OutAdjStart[node + 1]; j++)
			{
				var target = graph.OutAdjNeighbor[j];
				var newLayer = graph.Layers[node] + 1;
				if (newLayer > graph.Layers[target])
					graph.Layers[target] = newLayer;
				inDegree[target]--;
				if (inDegree[target] == 0)
					queue.Enqueue(target);
				if (newLayer > maxLayer)
					maxLayer = newLayer;
			}
		}

		graph.LayerCount = maxLayer + 1;
	}

	private static void EnforceMinLengths(GraphBuffer graph)
	{
		var hasMinLength = false;
		foreach (var e in graph.Edges)
		{
			if (e.MinLength > 1)
			{
				hasMinLength = true;
				break;
			}
		}

		if (!hasMinLength)
			return;

		// Iteratively push target nodes deeper until all min-length constraints are satisfied.
		// A single forward pass over the topologically-ordered edge list converges because
		// AssignLayers already produced a valid topological order.
		var changed = true;
		while (changed)
		{
			changed = false;
			foreach (var e in graph.Edges)
			{
				if (e.MinLength <= 1)
					continue;
				var required = graph.Layers[e.From] + e.MinLength;
				if (graph.Layers[e.To] < required)
				{
					graph.Layers[e.To] = required;
					changed = true;
				}
			}
		}

		var maxLayer = 0;
		for (var i = 0; i < graph.NodeCount; i++)
			if (graph.Layers[i] > maxLayer)
				maxLayer = graph.Layers[i];
		graph.LayerCount = maxLayer + 1;
	}

	private static void EnforceSameRankConstraints(GraphBuffer graph)
	{
		if (graph.SameRankPairs.Count == 0)
			return;

		foreach (var (a, b) in graph.SameRankPairs)
		{
			var targetLayer = Math.Max(graph.Layers[a], graph.Layers[b]);
			graph.Layers[a] = targetLayer;
			graph.Layers[b] = targetLayer;
		}

		var maxLayer = 0;
		for (var i = 0; i < graph.NodeCount; i++)
		{
			if (graph.Layers[i] > maxLayer)
				maxLayer = graph.Layers[i];
		}
		graph.LayerCount = maxLayer + 1;
	}

	/// <summary>
	/// For source nodes (no incoming edges) that have all their children two or more
	/// layers away, push them down to sit directly above their nearest child.
	/// This mirrors mermaid.js behaviour: isolated sources with deep connections appear
	/// adjacent to where they connect, not stranded at layer 0.
	/// </summary>
	private static void PushDownSources(GraphBuffer graph)
	{
		for (var node = 0; node < graph.RealNodeCount; node++)
		{
			if (graph.InAdjStart[node + 1] - graph.InAdjStart[node] > 0)
				continue; // has incoming edges — not a source

			var minChildLayer = int.MaxValue;
			for (var j = graph.OutAdjStart[node]; j < graph.OutAdjStart[node + 1]; j++)
			{
				var child = graph.OutAdjNeighbor[j];
				if (child < graph.RealNodeCount && graph.Layers[child] < minChildLayer)
					minChildLayer = graph.Layers[child];
			}

			if (minChildLayer is int.MaxValue or <= 1)
				continue; // no real children, or already adjacent

			// Don't push down if there's another source node at layer 0 —
			// sibling sources should stay at the same layer for visual coherence.
			var hasSiblingSource = false;
			for (var other = 0; other < graph.RealNodeCount; other++)
			{
				if (other == node || graph.Layers[other] != 0)
					continue;
				if (graph.InAdjStart[other + 1] - graph.InAdjStart[other] == 0)
				{
					hasSiblingSource = true;
					break;
				}
			}
			if (hasSiblingSource)
				continue;

			graph.Layers[node] = minChildLayer - 1;
		}

		// Recompute LayerCount in case sources moved to higher layers
		var maxLayer = 0;
		for (var i = 0; i < graph.NodeCount; i++)
			if (graph.Layers[i] > maxLayer)
				maxLayer = graph.Layers[i];
		graph.LayerCount = maxLayer + 1;
	}

	private static void InsertVirtualNodes(GraphBuffer graph, bool naturalBackEdgeRouting = false)
	{
		var edgeCount = graph.Edges.Count;
		var newEdges = new List<GraphEdge>();

		for (var i = edgeCount - 1; i >= 0; i--)
		{
			var e = graph.Edges[i];
			var span = graph.Layers[e.To] - graph.Layers[e.From];

			if (span <= 1)
				continue;

			// When natural back-edge routing is active, reversed back-edges route
			// directly from source bottom to target top via RouteNaturalBackEdge,
			// so virtual nodes would only distort the crossing minimizer and
			// coordinate assigner without helping the actual edge routing.
			if (naturalBackEdgeRouting && e.Reversed)
				continue;

			graph.Edges.RemoveAt(i);

			var prev = e.From;
			for (var layer = graph.Layers[e.From] + 1; layer < graph.Layers[e.To]; layer++)
			{
				var vNode = graph.AddVirtualNode();
				graph.Layers[vNode] = layer;
				newEdges.Add(new GraphEdge(prev, vNode, e.OriginalIndex, IsVirtual: true, Reversed: e.Reversed));
				prev = vNode;
			}
			newEdges.Add(new GraphEdge(prev, e.To, e.OriginalIndex, IsVirtual: prev != e.From, Reversed: e.Reversed));
		}

		graph.Edges.AddRange(newEdges);
	}

	internal static void BuildLayerArrays(GraphBuffer graph)
	{
		var layers = new List<int>[graph.LayerCount];
		for (var i = 0; i < graph.LayerCount; i++)
			layers[i] = [];

		for (var i = 0; i < graph.NodeCount; i++)
			layers[graph.Layers[i]].Add(i);

		graph.LayerNodes = new int[graph.LayerCount][];
		for (var i = 0; i < graph.LayerCount; i++)
			graph.LayerNodes[i] = layers[i].ToArray();

		graph.NodePositionInLayer = graph.RentInt(graph.NodeCount);
		for (var layer = 0; layer < graph.LayerCount; layer++)
		{
			var nodes = graph.LayerNodes[layer];
			for (var pos = 0; pos < nodes.Length; pos++)
				graph.NodePositionInLayer[nodes[pos]] = pos;
		}
	}
}
