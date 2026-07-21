namespace Sugiyama.Internal;

/// <summary>
/// Phase 2: Assign each node to a discrete integer layer using longest-path layering
/// (Kahn's algorithm for topological order). Then insert virtual nodes for edges
/// that span more than one layer.
/// Complexity: O(V + E) via CSR adjacency
/// </summary>
internal static class LayerAssigner
{
	internal static void Run(GraphBuffer graph)
	{
		graph.RebuildAdjacency();
		AssignLayers(graph);
		EnforceSameRankConstraints(graph);
		InsertVirtualNodes(graph);
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

	private static void InsertVirtualNodes(GraphBuffer graph)
	{
		var edgeCount = graph.Edges.Count;
		var newEdges = new List<GraphEdge>();

		for (var i = edgeCount - 1; i >= 0; i--)
		{
			var e = graph.Edges[i];
			var span = graph.Layers[e.To] - graph.Layers[e.From];

			if (span <= 1)
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
