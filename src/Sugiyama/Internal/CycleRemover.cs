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

		var reversals = new List<int>();

		for (var n = 0; n < nodeCount; n++)
		{
			if (state[n] == NodeState.Unvisited)
				Dfs(graph, n, state, reversals);
		}

		for (var i = 0; i < reversals.Count; i++)
		{
			var idx = reversals[i];
			var e = graph.Edges[idx];
			graph.Edges[idx] = e with { From = e.To, To = e.From, Reversed = !e.Reversed };
		}
	}

	private static void Dfs(GraphBuffer graph, int start, Span<NodeState> state, List<int> reversals)
	{
		var stack = new Stack<(int Node, int EdgeIdx)>();
		state[start] = NodeState.InStack;
		stack.Push((start, 0));

		while (stack.Count > 0)
		{
			var (node, edgeIdx) = stack.Pop();
			var advanced = false;

			for (var i = edgeIdx; i < graph.Edges.Count; i++)
			{
				var e = graph.Edges[i];
				if (e.From != node)
					continue;

				var target = e.To;
				if (state[target] == NodeState.InStack)
				{
					reversals.Add(i);
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
