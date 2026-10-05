namespace Sugiyama.Internal;

/// <summary>
/// Phase 1: Make the graph acyclic by reversing back-edges detected via DFS.
/// After layout completes, reversed edges have their route points flipped.
/// <para>
/// This mirrors dagre's actual default cycle-breaking pass (<c>lib/acyclic.ts</c>'s
/// <c>dfsFAS</c> — the branch taken whenever <c>acyclicer</c> is left unset, which is what
/// mermaid.js's dagre-wrapper does for flowchart/state layout; the alternative
/// <c>greedy-fas</c> heuristic is opt-in only and mermaid.js never enables it). Dagre's
/// <c>dfsFAS</c> runs its DFS in two passes: first from every node with in-degree 0 (in
/// declaration order), then from every remaining node (in declaration order, skipping
/// already-visited ones). Starting from sources first — rather than a single pass over all
/// nodes in index order — changes which edges land on the DFS stack first and therefore
/// which edges get flagged as "back" in graphs with multiple entry points feeding into a
/// shared cycle, so it is not a cosmetic reordering: it changes the resulting feedback-edge
/// set to match dagre's.
/// </para>
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
		var inDegree = nodeCount <= 64
			? stackalloc int[nodeCount]
			: new int[nodeCount];
		for (var i = 0; i < graph.Edges.Count; i++)
		{
			var e = graph.Edges[i];
			outEdgesPerNode[e.From].Add((e.To, i));
			if (e.From != e.To)
				inDegree[e.To]++;
		}

		var reversals = new List<int>();

		// Pass 1: DFS from every source (in-degree 0) in declaration order — matches dagre's
		// `graph.sources().forEach(dfsFn)`.
		for (var n = 0; n < nodeCount; n++)
		{
			if (inDegree[n] == 0 && state[n] == NodeState.Unvisited)
				Dfs(outEdgesPerNode, n, state, reversals);
		}

		// Pass 2: DFS from every remaining (non-source, or source unreachable in pass 1 due to
		// being its own target via a self-loop) node — matches dagre's
		// `graph.nodes().forEach(dfsFn)`.
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
