namespace Sugiyama.Internal;

/// <summary>
/// Phase 2: Assign each node to a discrete integer layer via <see cref="NetworkSimplexRanker"/>
/// — dagre's actual default rank-assignment algorithm — then insert virtual nodes for edges
/// that span more than one layer.
/// <para>
/// This used to be plain longest-path ("ASAP") layering plus several ad-hoc post-passes
/// (push sources down toward their nearest child, squeeze intermediate nodes toward their
/// successors) that existed specifically to approximate what network simplex produces as a
/// natural consequence of minimizing total weighted edge length. Those heuristics are gone now
/// that the real algorithm is in place; tight-source-layering-equivalent
/// behavior for X-coordinate assignment and component separation is unaffected — those are a
/// separate concern in <see cref="CoordinateAssigner"/> and <c>SugiyamaLayout</c>.
/// </para>
/// </summary>
internal static class LayerAssigner
{
	internal static void Run(
		GraphBuffer graph, bool naturalBackEdgeRouting = false,
		IReadOnlyList<int>? topLevelLooseNodes = null,
		IReadOnlyList<NestingGraphRanker.Group>? topLevelGroups = null,
		bool reverseSourceOrder = false)
	{
		graph.RebuildAdjacency();
		AssignLayersViaNetworkSimplex(graph, topLevelLooseNodes, topLevelGroups);
		EnforceSameRankConstraints(graph);
		EnforceMinLengths(graph);
		InsertVirtualNodes(graph, naturalBackEdgeRouting);
		graph.RebuildAdjacency();
		BuildLayerArrays(graph, reverseSourceOrder);
	}

	private static void AssignLayersViaNetworkSimplex(
		GraphBuffer graph,
		IReadOnlyList<int>? topLevelLooseNodes,
		IReadOnlyList<NestingGraphRanker.Group>? topLevelGroups)
	{
		var n = graph.NodeCount;
		var simplexEdges = new List<NetworkSimplexRanker.SimplexEdge>(graph.Edges.Count);
		foreach (var e in graph.Edges)
			simplexEdges.Add(new NetworkSimplexRanker.SimplexEdge(e.From, e.To, Weight: 1, e.MinLength));

		var ranks = topLevelGroups is { Count: > 0 }
			? NestingGraphRanker.Rank(n, simplexEdges, topLevelLooseNodes ?? [], topLevelGroups)
			: NetworkSimplexRanker.Rank(n, simplexEdges);

		var maxLayer = 0;
		for (var i = 0; i < n; i++)
		{
			graph.Layers[i] = ranks[i];
			if (ranks[i] > maxLayer)
				maxLayer = ranks[i];
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
		// AssignLayers already produced a valid topological order — except for a self-loop
		// (From == To), whose "required" layer is trivially unsatisfiable (it is always
		// strictly ahead of the node's own current layer by definition), which would otherwise
		// push that node's layer forever. A self-loop carries no real rank-span constraint
		// (both endpoints are the same node), so it is excluded outright. The iteration cap is
		// defense-in-depth against any other unforeseen non-convergent case.
		var changed = true;
		var iterations = 0;
		var maxIterations = (graph.NodeCount * 2) + 10;
		while (changed && iterations++ < maxIterations)
		{
			changed = false;
			foreach (var e in graph.Edges)
			{
				if (e.MinLength <= 1 || e.From == e.To)
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
				// Record the edge's OriginalIndex as the model order for this virtual node.
				// CrossingMinimizer uses this to sort virtual nodes relative to real nodes.
				var vIdx = vNode - graph.RealNodeCount;
				if (graph.VirtualNodeModelOrder == null || vIdx >= graph.VirtualNodeModelOrder.Length)
				{
					var existingLen = graph.VirtualNodeModelOrder?.Length ?? 0;
					var newArr = new int[Math.Max(vIdx + 1, (existingLen * 2) + 4)];
					graph.VirtualNodeModelOrder?.CopyTo(newArr, 0);
					graph.VirtualNodeModelOrder = newArr;
				}
				graph.VirtualNodeModelOrder[vIdx] = e.OriginalIndex;
				prev = vNode;
			}
			newEdges.Add(new GraphEdge(prev, e.To, e.OriginalIndex, IsVirtual: prev != e.From, Reversed: e.Reversed));
		}

		graph.Edges.AddRange(newEdges);
	}

	internal static void BuildLayerArrays(GraphBuffer graph, bool reverseSourceOrder = false)
	{
		// dagre's initOrder uses DFS from sources (nodes sorted by id) to establish the
		// initial within-layer ordering seen by the crossing minimizer. Replicating this
		// lets the barycenter sweeps start from a position closer to dagre's, increasing
		// the probability of converging to the same local minimum.
		//
		// Implementation: collect all nodes with no in-edges from strictly-lower-ranked
		// nodes (true topological sources of the DAG, ignoring back-edges), sort them by
		// NodeIndex (matches dagre's "sort by id" since our ids are creation-order integers),
		// then DFS outward layer by layer. A node is placed in the order its DFS subtree is
		// first entered. Unvisited nodes in each layer (unreachable from any source in DFS,
		// e.g. isolated components) are appended at the end in NodeIndex order.
		var layerArrays = new List<int>[graph.LayerCount];
		for (var i = 0; i < graph.LayerCount; i++)
			layerArrays[i] = [];

		var placed = new bool[graph.NodeCount];

		// Find topological sources: nodes whose in-neighbors are all at equal or higher
		// layers (i.e., no genuine predecessor from a lower layer).
		var sources = new List<int>();
		for (var v = 0; v < graph.NodeCount; v++)
		{
			var hasLowerPredecessor = false;
			for (var j = graph.InAdjStart[v]; j < graph.InAdjStart[v + 1]; j++)
			{
				if (graph.Layers[graph.InAdjNeighbor[j]] < graph.Layers[v])
				{
					hasLowerPredecessor = true;
					break;
				}
			}
			if (!hasLowerPredecessor)
				sources.Add(v);
		}
		// Sort sources by NodeIndex (matches dagre's "sort by id"), or descending when
		// reverseSourceOrder is enabled to start the crossing minimizer from the opposite
		// symmetric local minimum (matches ELK's converged ordering on mirror-symmetric ER graphs).
		if (reverseSourceOrder)
			sources.Sort((a, b) => b.CompareTo(a));
		else
			sources.Sort();

		// Iterative DFS: place each DFS-discovered node into its layer immediately.
		// Stack holds nodes to visit; we process each layer in strictly forward order
		// so virtual-node chains get placed consecutively.
		var stack = new Stack<int>(sources.Count);
		for (var i = sources.Count - 1; i >= 0; i--)
			stack.Push(sources[i]);

		var neighbors = new List<int>(); // reused scratch buffer for sorted child lists

		while (stack.Count > 0)
		{
			var v = stack.Pop();
			if (placed[v])
				continue;
			placed[v] = true;
			layerArrays[graph.Layers[v]].Add(v);

			// Push out-neighbors that are at a strictly higher layer (DAG edges only).
			// Dagre iterates g.successors(v) in sorted nodeId order, so we sort by
			// NodeIndex and push in reverse so the smallest-index neighbour pops first.
			var lo = graph.OutAdjStart[v];
			var hi = graph.OutAdjStart[v + 1];
			// Collect eligible out-neighbours, sort by NodeIndex, push reversed.
			var childStart = neighbors.Count;
			for (var j = lo; j < hi; j++)
			{
				var w = graph.OutAdjNeighbor[j];
				if (!placed[w] && graph.Layers[w] > graph.Layers[v])
					neighbors.Add(w);
			}
			// Insertion sort (few children per node in practice).
			for (var k = childStart + 1; k < neighbors.Count; k++)
			{
				var key = neighbors[k];
				var m = k - 1;
				while (m >= childStart && neighbors[m] > key)
				{
					neighbors[m + 1] = neighbors[m];
					m--;
				}
				neighbors[m + 1] = key;
			}
			for (var k = neighbors.Count - 1; k >= childStart; k--)
				stack.Push(neighbors[k]);
			neighbors.RemoveRange(childStart, neighbors.Count - childStart);
		}

		// Append any nodes not reached by DFS (disconnected components, back-edge targets
		// with no lower-layer predecessor) in stable NodeIndex order.
		for (var i = 0; i < graph.NodeCount; i++)
		{
			if (!placed[i])
				layerArrays[graph.Layers[i]].Add(i);
		}

		graph.LayerNodes = new int[graph.LayerCount][];
		for (var i = 0; i < graph.LayerCount; i++)
			graph.LayerNodes[i] = layerArrays[i].ToArray();

		graph.NodePositionInLayer = graph.RentInt(graph.NodeCount);
		for (var layer = 0; layer < graph.LayerCount; layer++)
		{
			var nodes = graph.LayerNodes[layer];
			for (var pos = 0; pos < nodes.Length; pos++)
				graph.NodePositionInLayer[nodes[pos]] = pos;
		}
	}
}
