namespace Sugiyama.Internal;

/// <summary>
/// Dagre's rank-assignment algorithm — network simplex (Gansner, Koutsofios, North &amp; Vo,
/// "A Technique for Drawing Directed Graphs", 1993) — ported faithfully from dagre's
/// <c>lib/rank/network-simplex.ts</c>, <c>lib/rank/feasible-tree.ts</c>, and
/// <c>lib/rank/util.ts</c>.
/// <para>
/// This is dagre's actual default ranker: <c>lib/rank/index.ts</c> falls through to
/// network-simplex whenever the caller leaves <c>ranker</c> unset, which is what mermaid.js's
/// dagre-wrapper does for every diagram type that uses dagre layout. It assigns each node an
/// integer rank that minimizes the sum of weighted edge lengths subject to every edge's
/// minlen constraint — a materially different (globally optimized, more compact) result than
/// simple longest-path/"ASAP" layering, which is why Mermaider previously needed several
/// ad-hoc post-passes (source push-down, intermediate-node squeeze) to approximate what
/// network simplex produces as a natural consequence of its objective.
/// </para>
/// <para>
/// Sketch of the algorithm: (1) seed ranks via longest-path from sources; (2) grow a
/// "feasible tree" — a spanning tree of zero-slack ("tight") edges, repeatedly pulling in the
/// globally minimum-slack edge and shifting the already-grown side's ranks to make it tight
/// when no more tight edges are reachable; (3) compute a "cut value" for every tree edge (the
/// net weight of all non-tree edges crossing the cut that edge induces); (4) while any tree
/// edge has a negative cut value (meaning swapping it for a particular non-tree edge shortens
/// total weighted edge length), exchange it for the minimum-slack valid replacement edge and
/// recompute cut values/ranks; repeat until no negative cut value remains (local optimum).
/// </para>
/// <para>
/// Verified against dagre's own unit-test fixtures for this algorithm (single node, 2-node,
/// diamond, minlen, the Gansner-paper example graph, multi-edge merging) — see
/// <c>NetworkSimplexRankerTests</c>.
/// </para>
/// </summary>
internal static class NetworkSimplexRanker
{
	internal readonly record struct SimplexEdge(int From, int To, int Weight, int MinLength);

	/// <summary>
	/// Computes an integer rank for every node in [0, nodeCount) given a (possibly cyclic-free,
	/// not-necessarily-connected) directed edge list. Disconnected nodes default to rank 0 (no
	/// constraint reaches them). The caller is expected to have already broken cycles.
	/// </summary>
	internal static int[] Rank(int nodeCount, IReadOnlyList<SimplexEdge> inputEdges)
	{
		var rank = new int[nodeCount];
		if (nodeCount <= 1 || inputEdges.Count == 0)
			return rank;

		var edges = Simplify(inputEdges);
		if (edges.Count == 0)
			return rank;

		var incident = BuildIncidence(nodeCount, edges);
		LongestPath(nodeCount, edges, rank);

		// Network simplex requires a connected graph (it grows a single spanning tree). Callers
		// that may pass disconnected input (e.g. before a nesting-graph / dummy-root connectivity
		// pass is wired in) get each weakly-connected component ranked independently and
		// relatively normalized; this keeps today's disconnected-component behavior stable while
		// still giving the (connected) common case dagre's real algorithm.
		var componentOf = AssignComponents(nodeCount, edges);
		var componentCount = 0;
		for (var i = 0; i < nodeCount; i++)
			if (componentOf[i] >= componentCount)
				componentCount = componentOf[i] + 1;

		for (var c = 0; c < componentCount; c++)
			RankComponent(nodeCount, edges, incident, componentOf, c, rank);

		return rank;
	}

	private static void RankComponent(
		int nodeCount, List<SimplexEdge> edges, List<int>[] incident, int[] componentOf, int component, int[] rank)
	{
		var seed = -1;
		for (var i = 0; i < nodeCount; i++)
		{
			if (componentOf[i] != component)
				continue;
			seed = i;
			break;
		}

		if (seed < 0)
			return;

		// A single-node component has no edges to rank against; longest-path already left it at 0.
		var hasEdge = false;
		foreach (var e in edges)
		{
			if (componentOf[e.From] != component)
				continue;
			hasEdge = true;
			break;
		}

		if (!hasEdge)
			return;

		var tree = BuildFeasibleTree(nodeCount, edges, incident, componentOf, component, seed, rank);

		InitLowLimAndParents(tree, edges, seed);
		InitCutValues(tree, edges, incident);

		var guardLimit = Math.Max(1000, (nodeCount + edges.Count) * 50);
		var guard = 0;
		int leave;
		while ((leave = FindLeaveChild(tree, seed)) >= 0 && guard++ < guardLimit)
		{
			var enter = FindEnterEdge(tree, edges, leave, rank);
			if (enter < 0)
				break; // defensive: a valid feasible tree always has a replacement candidate

			ExchangeEdge(tree, edges, leave, enter);
			InitLowLimAndParents(tree, edges, seed);
			InitCutValues(tree, edges, incident);
			UpdateRanks(tree, seed, rank);
		}

		NormalizeComponentRanks(nodeCount, componentOf, component, rank);
	}

	// ------------------------------------------------------------------
	// Graph prep
	// ------------------------------------------------------------------

	/// <summary>
	/// Merges parallel edges between the same ordered pair into one (summed weight, max
	/// minlen) and drops self-loops (they carry no rank-span constraint). Mirrors dagre's
	/// <c>util.simplify</c>. Preserves first-seen order, matching dagre's edge-array iteration
	/// order for the (minor, convergence-neutral) tie-breaks inside the main loop.
	/// </summary>
	private static List<SimplexEdge> Simplify(IReadOnlyList<SimplexEdge> inputEdges)
	{
		var index = new Dictionary<(int From, int To), int>();
		var result = new List<SimplexEdge>();
		foreach (var e in inputEdges)
		{
			if (e.From == e.To)
				continue;

			var key = (e.From, e.To);
			if (index.TryGetValue(key, out var idx))
			{
				var existing = result[idx];
				result[idx] = existing with
				{
					Weight = existing.Weight + e.Weight,
					MinLength = Math.Max(existing.MinLength, e.MinLength),
				};
			}
			else
			{
				index[key] = result.Count;
				result.Add(e);
			}
		}

		return result;
	}

	private static List<int>[] BuildIncidence(int nodeCount, List<SimplexEdge> edges)
	{
		var incident = new List<int>[nodeCount];
		for (var i = 0; i < nodeCount; i++)
			incident[i] = [];
		for (var i = 0; i < edges.Count; i++)
		{
			incident[edges[i].From].Add(i);
			incident[edges[i].To].Add(i);
		}

		return incident;
	}

	/// <summary>Union-find over the (undirected view of the) simplified edge list.</summary>
	private static int[] AssignComponents(int nodeCount, List<SimplexEdge> edges)
	{
		var parent = new int[nodeCount];
		for (var i = 0; i < nodeCount; i++)
			parent[i] = i;

		int Find(int x)
		{
			while (parent[x] != x)
			{
				parent[x] = parent[parent[x]];
				x = parent[x];
			}

			return x;
		}

		foreach (var e in edges)
		{
			var ra = Find(e.From);
			var rb = Find(e.To);
			if (ra != rb)
				parent[ra] = rb;
		}

		var label = new int[nodeCount];
		var next = new Dictionary<int, int>();
		for (var i = 0; i < nodeCount; i++)
		{
			var root = Find(i);
			if (!next.TryGetValue(root, out var id))
			{
				id = next.Count;
				next[root] = id;
			}

			label[i] = id;
		}

		return label;
	}

	private static void NormalizeComponentRanks(int nodeCount, int[] componentOf, int component, int[] rank)
	{
		var min = int.MaxValue;
		for (var i = 0; i < nodeCount; i++)
			if (componentOf[i] == component && rank[i] < min)
				min = rank[i];

		if (min is int.MaxValue or 0)
			return;

		for (var i = 0; i < nodeCount; i++)
			if (componentOf[i] == component)
				rank[i] -= min;
	}

	// ------------------------------------------------------------------
	// Phase 1: longest-path seed ranking (dagre's rank/util.ts longestPath)
	// ------------------------------------------------------------------

	/// <summary>
	/// Seeds ranks via memoized DFS from every source (in-degree 0) node: a sink gets rank 0,
	/// and every other node gets the minimum over its out-edges of (target rank - minlen) —
	/// i.e. each node sits exactly minlen above its tightest (nearest) successor. This is a
	/// poor, often very "wide", layering on its own (dagre's own comment: "results are far
	/// from optimal"); it exists purely as a fast, always-feasible starting point for the
	/// feasible-tree/network-simplex refinement that follows. Written iteratively (explicit
	/// stack) rather than recursively to avoid stack-overflow risk on long chains, matching the
	/// style already used by <see cref="CycleRemover"/>.
	/// </summary>
	private static void LongestPath(int nodeCount, List<SimplexEdge> edges, int[] rank)
	{
		var outEdges = new List<int>[nodeCount];
		for (var i = 0; i < nodeCount; i++)
			outEdges[i] = [];
		for (var i = 0; i < edges.Count; i++)
			outEdges[edges[i].From].Add(i);

		var inDegree = new int[nodeCount];
		foreach (var e in edges)
			inDegree[e.To]++;

		var state = new byte[nodeCount]; // 0 = unvisited, 1 = on stack, 2 = done
		var stack = new Stack<(int Node, int Pos)>();

		for (var start = 0; start < nodeCount; start++)
		{
			if (inDegree[start] != 0 || state[start] != 0)
				continue;

			stack.Push((start, 0));
			state[start] = 1;

			while (stack.Count > 0)
			{
				var (node, pos) = stack.Pop();
				var outs = outEdges[node];
				var advanced = false;
				for (var i = pos; i < outs.Count; i++)
				{
					var target = edges[outs[i]].To;
					if (state[target] != 0)
						continue;
					stack.Push((node, i + 1));
					state[target] = 1;
					stack.Push((target, 0));
					advanced = true;
					break;
				}

				if (advanced)
					continue;

				var min = int.MaxValue;
				foreach (var ei in outs)
				{
					var e = edges[ei];
					var candidate = rank[e.To] - e.MinLength;
					if (candidate < min)
						min = candidate;
				}

				rank[node] = min == int.MaxValue ? 0 : min;
				state[node] = 2;
			}
		}
	}

	// ------------------------------------------------------------------
	// Tree model
	// ------------------------------------------------------------------

	/// <summary>
	/// The undirected spanning "tree" of tight edges network simplex maintains and repeatedly
	/// re-roots/re-numbers, plus the per-node bookkeeping (parent edge orientation, low/lim
	/// subtree-interval numbers, cut value) the algorithm needs.
	/// </summary>
	private sealed class SimplexTree(int nodeCount)
	{
		public readonly int NodeCount = nodeCount;
		public readonly List<int>[] Adj = BuildEmptyAdj(nodeCount);
		public readonly int[] Parent = new int[nodeCount];
		public readonly bool[] ParentFlipped = new bool[nodeCount]; // true: graph edge is parent->this
		public readonly int[] ParentWeight = new int[nodeCount];
		public readonly int[] ParentMinLen = new int[nodeCount];
		public readonly int[] CutValue = new int[nodeCount];
		public readonly int[] Low = new int[nodeCount];
		public readonly int[] Lim = new int[nodeCount];
		public readonly List<int> Postorder = new(nodeCount);

		private static List<int>[] BuildEmptyAdj(int n)
		{
			var adj = new List<int>[n];
			for (var i = 0; i < n; i++)
				adj[i] = [];
			return adj;
		}

		public void AddTreeEdge(int u, int v)
		{
			Adj[u].Add(v);
			Adj[v].Add(u);
		}

		public void RemoveTreeEdge(int u, int v)
		{
			_ = Adj[u].Remove(v);
			_ = Adj[v].Remove(u);
		}
	}

	// ------------------------------------------------------------------
	// Phase 2: feasible tree construction (dagre's rank/feasible-tree.ts)
	// ------------------------------------------------------------------

	private static SimplexTree BuildFeasibleTree(
		int nodeCount, List<SimplexEdge> edges, List<int>[] incident, int[] componentOf, int component, int seed, int[] rank)
	{
		var tree = new SimplexTree(nodeCount);
		var inTree = new bool[nodeCount];
		var treeOrder = new List<int> { seed };
		inTree[seed] = true;

		var componentSize = 0;
		for (var i = 0; i < nodeCount; i++)
			if (componentOf[i] == component)
				componentSize++;

		while (treeOrder.Count < componentSize)
		{
			TightTreeExtend(tree, inTree, treeOrder, edges, incident, rank);
			if (treeOrder.Count >= componentSize)
				break;

			var bestSlack = int.MaxValue;
			var bestEdge = -1;
			for (var i = 0; i < edges.Count; i++)
			{
				var e = edges[i];
				if (componentOf[e.From] != component)
					continue;
				if (inTree[e.From] == inTree[e.To])
					continue;
				var slack = rank[e.To] - rank[e.From] - e.MinLength;
				if (slack < bestSlack)
				{
					bestSlack = slack;
					bestEdge = i;
				}
			}

			if (bestEdge < 0)
				break; // defensive: shouldn't happen — component is connected by construction

			var be = edges[bestEdge];
			var delta = inTree[be.From] ? bestSlack : -bestSlack;
			foreach (var v in treeOrder)
				rank[v] += delta;
			// The next TightTreeExtend call discovers this now-tight edge and absorbs it —
			// mirrors dagre, which never explicitly adds the min-slack edge itself here either.
		}

		return tree;
	}

	/// <summary>
	/// DFS from every current tree member (snapshotted before the pass, matching dagre's
	/// <c>tree.nodes().forEach(dfs)</c> over a fixed array) along zero-slack incident edges,
	/// absorbing newly-reachable nodes into the tree. Re-run after every rank shift because a
	/// shift can make a previously-slack edge newly tight.
	/// </summary>
	private static void TightTreeExtend(
		SimplexTree tree, bool[] inTree, List<int> treeOrder, List<SimplexEdge> edges, List<int>[] incident, int[] rank)
	{
		var snapshot = treeOrder.ToArray();
		foreach (var seed in snapshot)
		{
			var stack = new Stack<(int Node, int Pos)>();
			stack.Push((seed, 0));

			while (stack.Count > 0)
			{
				var (v, pos) = stack.Pop();
				var inc = incident[v];
				for (var i = pos; i < inc.Count; i++)
				{
					var e = edges[inc[i]];
					var w = e.From == v ? e.To : e.From;
					if (inTree[w])
						continue;
					var slack = rank[e.To] - rank[e.From] - e.MinLength;
					if (slack != 0)
						continue;

					inTree[w] = true;
					treeOrder.Add(w);
					tree.AddTreeEdge(v, w);
					stack.Push((v, i + 1));
					stack.Push((w, 0));
					break;
				}
			}
		}
	}

	// ------------------------------------------------------------------
	// Phase 3: low/lim numbering, cut values (dagre's network-simplex.ts)
	// ------------------------------------------------------------------

	/// <summary>
	/// Postorder DFS from the fixed root assigning, per node: <c>Low</c> (the postorder index
	/// in effect when the node was first entered), <c>Lim</c> (the postorder index when the
	/// node's whole subtree is finished — together these let <c>IsDescendant</c> answer
	/// "is X in Y's subtree?" in O(1)), <c>Parent</c>, and the parent-edge's graph orientation
	/// (weight/minlen/flip). Iterative (explicit stack) for the same overflow-safety reason as
	/// <see cref="LongestPath"/>.
	/// </summary>
	private static void InitLowLimAndParents(SimplexTree tree, List<SimplexEdge> edges, int root)
	{
		tree.Postorder.Clear();
		var n = tree.NodeCount;
		var visited = new bool[n];
		var lowAtEntry = new int[n];
		var nextLim = 1;

		var stack = new Stack<(int Node, int Pos, int Parent)>();
		stack.Push((root, 0, -1));
		visited[root] = true;
		lowAtEntry[root] = nextLim;

		while (stack.Count > 0)
		{
			var (v, pos, parent) = stack.Pop();
			var neighbors = tree.Adj[v];
			var advanced = false;
			for (var i = pos; i < neighbors.Count; i++)
			{
				var w = neighbors[i];
				if (visited[w])
					continue;
				visited[w] = true;
				lowAtEntry[w] = nextLim;
				stack.Push((v, i + 1, parent));
				stack.Push((w, 0, v));
				advanced = true;
				break;
			}

			if (advanced)
				continue;

			tree.Low[v] = lowAtEntry[v];
			tree.Lim[v] = nextLim++;
			tree.Parent[v] = parent;
			tree.Postorder.Add(v);
		}

		// Resolve each non-root node's parent-edge orientation/weight/minlen against the graph's
		// simplified edge list now that Parent[] is fully known.
		var edgeIndex = new Dictionary<(int From, int To), int>(edges.Count);
		for (var i = 0; i < edges.Count; i++)
			edgeIndex[(edges[i].From, edges[i].To)] = i;

		for (var v = 0; v < n; v++)
		{
			var p = tree.Parent[v];
			if (p < 0)
				continue;

			if (edgeIndex.TryGetValue((v, p), out var idx))
			{
				var e = edges[idx];
				tree.ParentFlipped[v] = false; // graph edge v -> p
				tree.ParentWeight[v] = e.Weight;
				tree.ParentMinLen[v] = e.MinLength;
			}
			else if (edgeIndex.TryGetValue((p, v), out var idx2))
			{
				var e = edges[idx2];
				tree.ParentFlipped[v] = true; // graph edge p -> v
				tree.ParentWeight[v] = e.Weight;
				tree.ParentMinLen[v] = e.MinLength;
			}
		}
	}

	/// <summary>
	/// Assigns a cut value to every non-root tree node's parent edge, in postorder (a node's
	/// cut value can only be computed once all of its own children's cut values are known).
	/// </summary>
	private static void InitCutValues(SimplexTree tree, List<SimplexEdge> edges, List<int>[] incident)
	{
		// Postorder excludes nothing but the root is naturally last and has no parent edge.
		foreach (var v in tree.Postorder)
		{
			if (tree.Parent[v] < 0)
				continue;
			tree.CutValue[v] = CalcCutValue(tree, edges, incident, v);
		}
	}

	/// <summary>
	/// The cut value of the tree edge between <paramref name="child"/> and its parent: the net
	/// weight (summed, with sign) of every graph edge that crosses the cut this tree edge
	/// induces — positive contributions from edges pointing the same way as the tree edge
	/// (toward the "head" side), negative from edges pointing the other way. A negative total
	/// means the cut would shrink (total weighted edge length would drop) if this tree edge
	/// were replaced by a cheaper edge crossing the same cut, which is exactly the signal the
	/// main loop looks for.
	/// </summary>
	private static int CalcCutValue(SimplexTree tree, List<SimplexEdge> edges, List<int>[] incident, int child)
	{
		var parent = tree.Parent[child];
		var childIsTail = !tree.ParentFlipped[child]; // graph edge child -> parent?
		var cutValue = tree.ParentWeight[child];

		foreach (var ei in incident[child])
		{
			var e = edges[ei];
			var isOutEdge = e.From == child;
			var other = isOutEdge ? e.To : e.From;
			if (other == parent)
				continue;

			var pointsToHead = isOutEdge == childIsTail;
			cutValue += pointsToHead ? e.Weight : -e.Weight;

			// The only way `child` and `other` can also be tree-adjacent here (parent already
			// excluded above) is if `other` is one of child's own tree children.
			if (tree.Parent[other] == child)
				cutValue += pointsToHead ? -tree.CutValue[other] : tree.CutValue[other];
		}

		return cutValue;
	}

	// ------------------------------------------------------------------
	// Phase 4: main loop — leave/enter/exchange (dagre's network-simplex.ts)
	// ------------------------------------------------------------------

	private static int FindLeaveChild(SimplexTree tree, int root)
	{
		for (var v = 0; v < tree.NodeCount; v++)
		{
			if (v == root)
				continue;
			if (tree.Adj[v].Count == 0 && tree.Parent[v] < 0)
				continue; // not part of this component's tree
			if (tree.CutValue[v] < 0)
				return v;
		}

		return -1;
	}

	/// <summary>
	/// Given a leaving tree edge (the one between <paramref name="leaveChild"/> and its
	/// parent), finds the minimum-slack graph edge that crosses the same cut in the valid
	/// (tail-to-head) direction — the replacement that keeps the tree spanning and feasible
	/// while improving (or at worst not worsening) total weighted edge length.
	/// </summary>
	private static int FindEnterEdge(SimplexTree tree, List<SimplexEdge> edges, int leaveChild, int[] rank)
	{
		var parent = tree.Parent[leaveChild];
		int v, w;
		if (!tree.ParentFlipped[leaveChild])
		{
			v = leaveChild;
			w = parent;
		}
		else
		{
			v = parent;
			w = leaveChild;
		}

		int tailLow, tailLim;
		bool flip;
		if (tree.Lim[v] > tree.Lim[w])
		{
			tailLow = tree.Low[w];
			tailLim = tree.Lim[w];
			flip = true;
		}
		else
		{
			tailLow = tree.Low[v];
			tailLim = tree.Lim[v];
			flip = false;
		}

		bool IsDescendant(int node) => tailLow <= tree.Lim[node] && tree.Lim[node] <= tailLim;

		var bestSlack = int.MaxValue;
		var bestEdge = -1;
		for (var i = 0; i < edges.Count; i++)
		{
			var e = edges[i];
			if (flip != IsDescendant(e.From) || flip == IsDescendant(e.To))
				continue;
			var slack = rank[e.To] - rank[e.From] - e.MinLength;
			if (slack < bestSlack)
			{
				bestSlack = slack;
				bestEdge = i;
			}
		}

		return bestEdge;
	}

	private static void ExchangeEdge(SimplexTree tree, List<SimplexEdge> edges, int leaveChild, int enterEdgeIdx)
	{
		var parent = tree.Parent[leaveChild];
		tree.RemoveTreeEdge(leaveChild, parent);

		var ee = edges[enterEdgeIdx];
		tree.AddTreeEdge(ee.From, ee.To);
	}

	/// <summary>
	/// Propagates ranks top-down from the (fixed) root along the current tree shape, after an
	/// edge exchange changed which edges are tight. <c>InitLowLimAndParents</c> must have been
	/// re-run first so <c>Parent</c>/<c>ParentFlipped</c>/<c>ParentMinLen</c> reflect the new
	/// tree shape.
	/// </summary>
	private static void UpdateRanks(SimplexTree tree, int root, int[] rank)
	{
		var visited = new bool[tree.NodeCount];
		var stack = new Stack<int>();
		stack.Push(root);
		visited[root] = true;

		while (stack.Count > 0)
		{
			var v = stack.Pop();
			foreach (var w in tree.Adj[v])
			{
				if (visited[w])
					continue;
				visited[w] = true;
				rank[w] = rank[v] + (tree.ParentFlipped[w] ? tree.ParentMinLen[w] : -tree.ParentMinLen[w]);
				stack.Push(w);
			}
		}
	}
}
