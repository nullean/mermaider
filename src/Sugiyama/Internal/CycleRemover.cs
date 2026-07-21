namespace Sugiyama.Internal;

/// <summary>
/// Phase 1: Make the graph acyclic by reversing back-edges detected via DFS.
/// After layout completes, reversed edges have their route points flipped.
/// Complexity: O(V + E)
/// </summary>
internal static class CycleRemover
{
	private enum NodeState : byte { Unvisited, InStack, Done }

	internal static void Run(GraphBuffer graph)
	{
		var nodeCount = graph.NodeCount;
		var state = nodeCount <= 64
			? stackalloc NodeState[nodeCount]
			: new NodeState[nodeCount];

		// Build per-node out-edge lists (target + edge index) in O(E)
		// We need edge indices for the reversal step, so we can't use the CSR directly.
		var outEdgesPerNode = new List<(int To, int EdgeIdx)>[nodeCount];
		for (var i = 0; i < nodeCount; i++)
			outEdgesPerNode[i] = [];
		for (var i = 0; i < graph.Edges.Count; i++)
		{
			var e = graph.Edges[i];
			outEdgesPerNode[e.From].Add((e.To, i));
		}

		var reversals = new List<int>();

		for (var n = 0; n < nodeCount; n++)
		{
			if (state[n] == NodeState.Unvisited)
				Dfs(outEdgesPerNode, n, state, reversals);
		}

		for (var i = 0; i < reversals.Count; i++)
		{
			var idx = reversals[i];
			var e = graph.Edges[idx];
			graph.Edges[idx] = e with { From = e.To, To = e.From, Reversed = !e.Reversed };
		}
	}

	private static void Dfs(List<(int To, int EdgeIdx)>[] outEdgesPerNode, int start,
		Span<NodeState> state, List<int> reversals)
	{
		var stack = new Stack<(int Node, int AdjPos)>();
		state[start] = NodeState.InStack;
		stack.Push((start, 0));

		while (stack.Count > 0)
		{
			var (node, adjPos) = stack.Pop();
			var outEdges = outEdgesPerNode[node];
			var advanced = false;

			for (var i = adjPos; i < outEdges.Count; i++)
			{
				var (target, edgeIdx) = outEdges[i];
				if (state[target] == NodeState.InStack)
				{
					reversals.Add(edgeIdx);
				}
				else if (state[target] == NodeState.Unvisited)
				{
					stack.Push((node, i + 1));
					state[target] = NodeState.InStack;
					stack.Push((target, 0));
					advanced = true;
					break;
				}
			}

			if (!advanced)
				state[node] = NodeState.Done;
		}
	}
}
