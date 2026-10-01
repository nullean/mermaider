namespace Sugiyama.Internal;

/// <summary>
/// Faithful port of dagre's nesting-graph technique (dagrejs/dagre's <c>lib/nesting-graph.ts</c>),
/// which <see cref="NetworkSimplexRanker"/> alone does not provide: compound-graph (subgraph /
/// cluster) rank containment.
/// <para>
/// Plain network simplex ranks every node independently by total weighted edge length — it has
/// no notion that some nodes are grouped into a subgraph that must render as a single contiguous
/// box. Two sibling subgraphs with independent internal dependency chains can therefore end up
/// with members sharing the same rank, or with one subgraph's member landing inside a sibling
/// subgraph's rank range — a real, visible rendering bug (a node box drawn inside the wrong
/// subgraph's rectangle), not just a cosmetic placement difference.
/// </para>
/// <para>
/// dagre solves this by: (1) adding a dummy root node; (2) for every subgraph, adding a
/// border-top/border-bottom dummy node pair wired to its children's own borders (or the children
/// directly, if they are leaves) via weight/minlen formulas that force every member strictly
/// between its subgraph's two border ranks and pull the subgraph vertically compact; (3) scaling
/// every pre-existing edge's minlen by <c>nodeSep = 2*height+1</c> to reserve enough integer rank
/// space for border ranks to be inserted between real ranks without collision; (4) connecting
/// every leaf (at any nesting depth) and every top-level subgraph to the dummy root so the whole
/// augmented graph is one connected component (network simplex requires this). The augmented
/// graph is ranked with the SAME network-simplex algorithm used for subgraph-free graphs, then
/// the root and border nodes are discarded and the occupied ranks compacted back to a dense
/// 0..N-1 sequence (dagre's <c>removeEmptyRanks</c> + <c>nestingGraph.cleanup</c> +
/// <c>normalizeRanks</c>, collapsed into one post-pass here since this port does not carry
/// border nodes forward into ordering/positioning — Mermaider already computes subgraph bounding
/// boxes from member node positions post-layout, so it only needs nesting-graph for the RANK
/// values, not for dagre's border-segment-based box geometry).
/// </para>
/// <para>
/// Deliberately scoped to activate only when the caller actually has subgraphs: dagre's real
/// nesting-graph ALSO fully connects otherwise-disconnected top-level nodes/clusters through the
/// dummy root (every leaf gets a root edge, cluster or not). Unifying ranking across disconnected
/// components for subgraph-free diagrams is a separate, already-documented, deliberately deferred
/// change (see flowchart-edges/flowchart-long-edges baselines in
/// FlowchartSkeletonComparisonTests) with its own regression surface across every Sugiyama-based
/// diagram type; this port does not fold that in. When there are no subgraphs, <see cref="Rank"/>
/// falls straight through to plain <see cref="NetworkSimplexRanker.Rank"/>, preserving today's
/// per-weakly-connected-component ranking behavior exactly.
/// </para>
/// </summary>
internal static class NestingGraphRanker
{
	/// <summary>
	/// One subgraph/cluster: <see cref="OwnNodes"/> are its direct leaf members (real node
	/// indices not inside any nested child subgraph); <see cref="Children"/> are nested
	/// subgraphs. Mirrors dagre's compound-graph "children of v" (a mix of plain nodes and
	/// cluster nodes), just split into two lists since Mermaider doesn't give subgraphs their
	/// own real-node identity the way dagre's compound graph does.
	/// </summary>
	internal sealed class Group
	{
		internal List<int> OwnNodes { get; } = [];
		internal List<Group> Children { get; } = [];
	}

	/// <summary>
	/// Ranks <paramref name="realNodeCount"/> real nodes. When <paramref name="topLevelGroups"/>
	/// is empty, this is exactly <see cref="NetworkSimplexRanker.Rank"/> (no augmentation
	/// overhead, no behavior change for the vast majority of diagrams that have no subgraphs).
	/// Otherwise builds dagre's nesting-graph augmentation, ranks the augmented graph, and
	/// returns compacted 0-based ranks for the real nodes only.
	/// </summary>
	internal static int[] Rank(
		int realNodeCount,
		IReadOnlyList<NetworkSimplexRanker.SimplexEdge> edges,
		IReadOnlyList<int> topLevelLooseNodes,
		IReadOnlyList<Group> topLevelGroups)
	{
		if (topLevelGroups.Count == 0)
			return NetworkSimplexRanker.Rank(realNodeCount, edges);

		// ---- 1. Tree depths (dagre's treeDepths): every node in the hierarchy — leaf or
		// cluster, at any nesting level — gets a depth; top-level children of the implicit
		// graph root start at depth 1. height = max(depth) - 1.
		var depthOfGroup = new Dictionary<Group, int>();
		var maxDepth = topLevelLooseNodes.Count > 0 ? 1 : 0;

		void DfsDepth(Group g, int depth)
		{
			depthOfGroup[g] = depth;
			if (depth > maxDepth)
				maxDepth = depth;
			if (g.OwnNodes.Count > 0 && depth + 1 > maxDepth)
				maxDepth = depth + 1; // leaves sit one level deeper than their enclosing group
			foreach (var child in g.Children)
				DfsDepth(child, depth + 1);
		}

		foreach (var g in topLevelGroups)
			DfsDepth(g, 1);

		var height = maxDepth - 1;
		var nodeSep = (2 * height) + 1;

		// ---- 2. Scale every pre-existing edge's minlen by nodeSep so border ranks can be
		// inserted between real ranks without collision.
		var augmented = new List<NetworkSimplexRanker.SimplexEdge>(edges.Count + (topLevelGroups.Count * 8) + 16);
		foreach (var e in edges)
			augmented.Add(e with { MinLength = e.MinLength * nodeSep });

		// ---- 3. Weight sufficient to keep every subgraph vertically compact — must exceed
		// anything else in the graph, same as dagre's sumWeights(graph) + 1.
		var sumWeights = 0L;
		foreach (var e in edges)
			sumWeights += e.Weight;
		var weight = (int)Math.Min(sumWeights + 1, int.MaxValue);

		var nextIndex = realNodeCount;
		var rootIndex = nextIndex++;

		// ---- 4. Recursive border-node construction (dagre's dfs), uniform leaf/cluster
		// handling matching dagre's actual recursion: dfs() is called for EVERY child
		// (leaf or cluster) at every depth, so leaves anywhere in the tree get a direct
		// root edge, not just top-level ones.
		(int Top, int Bottom) DfsBuild(Group g)
		{
			var top = nextIndex++;
			var bottom = nextIndex++;
			var depth = depthOfGroup[g];

			foreach (var leaf in g.OwnNodes)
			{
				// Matches dfs(leaf) hitting dagre's "no children" branch unconditionally.
				augmented.Add(new NetworkSimplexRanker.SimplexEdge(rootIndex, leaf, Weight: 0, MinLength: nodeSep));

				// childNode.borderTop is undefined for a plain leaf -> weight*2, minlen via
				// the enclosing group's own depth (childTop === childBottom branch).
				var minlen = height - depth + 1;
				augmented.Add(new NetworkSimplexRanker.SimplexEdge(top, leaf, 2 * weight, minlen));
				augmented.Add(new NetworkSimplexRanker.SimplexEdge(leaf, bottom, 2 * weight, minlen));
			}

			foreach (var child in g.Children)
			{
				var (childTop, childBottom) = DfsBuild(child);
				// childNode.borderTop defined -> weight, minlen 1 (childTop !== childBottom).
				augmented.Add(new NetworkSimplexRanker.SimplexEdge(top, childTop, weight, MinLength: 1));
				augmented.Add(new NetworkSimplexRanker.SimplexEdge(childBottom, bottom, weight, MinLength: 1));
			}

			return (top, bottom);
		}

		foreach (var g in topLevelGroups)
		{
			var (top, _) = DfsBuild(g);
			// Top-level cluster (no parent) -> edge(root, top, weight 0, minlen height+depth).
			augmented.Add(new NetworkSimplexRanker.SimplexEdge(rootIndex, top, Weight: 0, MinLength: height + depthOfGroup[g]));
		}

		foreach (var v in topLevelLooseNodes)
		{
			// Top-level leaf (v !== root) -> edge(root, v, weight 0, minlen nodeSep).
			augmented.Add(new NetworkSimplexRanker.SimplexEdge(rootIndex, v, Weight: 0, MinLength: nodeSep));
		}

		var totalNodeCount = nextIndex;
		var rawRanks = NetworkSimplexRanker.Rank(totalNodeCount, augmented);

		// ---- 5. Cleanup: compact the ranks actually occupied by REAL nodes back to a dense
		// 0..N-1 sequence. Deliberately excludes border/root node ranks from the compaction set
		// — dagre keeps border nodes in its rank sequence because it renders them (they define
		// the subgraph's visual box edges); Mermaider discards them entirely here because it
		// already reserves subgraph-header/padding space in its own post-layout box-fitting
		// pass (FixSubgraphSpacing et al.), so a rank occupied ONLY by a border node must not
		// survive as a visibly empty row in the real node sequence. The border nodes already
		// did their job — pulling real nodes into the right relative order via the weighted
		// constraints above — before this point.
		var occupied = new SortedSet<int>();
		for (var i = 0; i < realNodeCount; i++)
			_ = occupied.Add(rawRanks[i]);

		var remap = new Dictionary<int, int>(occupied.Count);
		var next = 0;
		foreach (var r in occupied)
			remap[r] = next++;

		var result = new int[realNodeCount];
		for (var i = 0; i < realNodeCount; i++)
			result[i] = remap[rawRanks[i]];
		return result;
	}
}
