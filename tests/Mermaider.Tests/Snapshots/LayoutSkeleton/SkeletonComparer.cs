namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

/// <summary>
/// Scores how well two <see cref="LayoutSkeleton"/>s agree on placement/ordering decisions.
/// All comparisons are ordinal (layer index, order-within-layer index) rather than pixel-based,
/// and are restricted to entity ids present in both skeletons — a skeleton built from
/// Mermaider's own model and one parsed from a real mermaid.js SVG should always have the same
/// entity ids (both come from parsing the same diagram source), but restricting defensively
/// keeps these metrics well-defined even if that ever isn't true.
/// </summary>
internal static class SkeletonComparer
{
	/// <summary>
	/// Counts non-self-loop edges whose <c>From</c> entity sits in a strictly later layer than
	/// its <c>To</c> entity — edges that run "backwards" against the layering the engine chose.
	/// A layered graph engine (Sugiyama/dagre) tries to keep edges flowing layer-to-layer in one
	/// direction; a non-zero count normally means the layering is inconsistent with the
	/// diagram's own edges, independent of any mermaid.js comparison — this is why it takes a
	/// single skeleton, not a reference/actual pair.
	///
	/// ER relationship graphs are frequently cyclic (e.g. a foreign key cycle
	/// Product→BundleRegistry→Bundle→...→ProductRelease→Product) — inside such a cycle, *some*
	/// edge must run backward no matter how entities are laid out, so counting it would flag a
	/// property of the diagram's own graph, not a layout defect. Edges are therefore only
	/// counted when their endpoints are not mutually reachable (i.e. not part of the same
	/// strongly-connected component) — genuine DAG-level backward edges still count.
	/// </summary>
	public static int TopologicalInversions(LayoutSkeleton skeleton)
	{
		var ids = skeleton.Nodes.Select(n => n.Id).ToList();
		var indexOf = new Dictionary<string, int>(StringComparer.Ordinal);
		for (var i = 0; i < ids.Count; i++)
			indexOf[ids[i]] = i;

		var edges = skeleton.Edges
			.Where(e => !e.IsSelfLoop && indexOf.ContainsKey(e.From) && indexOf.ContainsKey(e.To))
			.ToList();
		var sameCycle = MutualReachability(ids.Count, indexOf, edges);

		var layerById = skeleton.Nodes.ToDictionary(n => n.Id, n => n.Layer, StringComparer.Ordinal);
		var count = 0;
		foreach (var e in edges)
		{
			if (layerById[e.From] <= layerById[e.To])
				continue;
			if (sameCycle[indexOf[e.From], indexOf[e.To]])
				continue; // backward edge is unavoidable inside a cycle — not a layout defect
			count++;
		}
		return count;
	}

	// n×n matrix where [i,j] is true when i and j can each reach the other via directed edges
	// (i.e. they sit in the same strongly-connected component). Plain Floyd-Warshall transitive
	// closure is more than fast enough for the entity counts these diagrams have.
	private static bool[,] MutualReachability(int n, Dictionary<string, int> indexOf, List<SkeletonEdge> edges)
	{
		var reach = new bool[n, n];
		for (var i = 0; i < n; i++)
			reach[i, i] = true;
		foreach (var e in edges)
			reach[indexOf[e.From], indexOf[e.To]] = true;

		for (var k = 0; k < n; k++)
			for (var i = 0; i < n; i++)
				if (reach[i, k])
					for (var j = 0; j < n; j++)
						if (reach[k, j])
							reach[i, j] = true;

		var mutual = new bool[n, n];
		for (var i = 0; i < n; i++)
			for (var j = 0; j < n; j++)
				mutual[i, j] = reach[i, j] && reach[j, i];
		return mutual;
	}

	/// <summary>
	/// Fraction (0..1) of entity pairs — common to both skeletons — whose relative layer
	/// ordering agrees between <paramref name="reference"/> and <paramref name="actual"/>. 1.0
	/// means every pair that is "A's layer before/after/same-as B's layer" in one skeleton says
	/// the same in the other. Layer index offsets/gaps don't matter, only relative order, so
	/// this is meaningful even though the two engines number layers independently.
	/// </summary>
	public static double LayerAgreement(LayoutSkeleton reference, LayoutSkeleton actual) =>
		PairwiseAgreement(reference, actual, static n => n.Layer);

	/// <summary>
	/// Fraction (0..1) of entity pairs that share a layer in <paramref name="reference"/> whose
	/// left-to-right order (<see cref="SkeletonNode.OrderInLayer"/>) agrees in
	/// <paramref name="actual"/>. Pairs the reference places in different layers are excluded —
	/// order comparisons only make sense between siblings (see <see cref="LayerAgreement"/> for
	/// cross-layer placement).
	/// </summary>
	public static double WithinLayerOrderAgreement(LayoutSkeleton reference, LayoutSkeleton actual)
	{
		var refById = reference.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
		var actualById = actual.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
		var commonIds = refById.Keys.Where(actualById.ContainsKey).ToList();
		ThrowIfSuspiciouslyDisjoint(reference, actual, commonIds.Count);

		var total = 0;
		var agree = 0;
		for (var i = 0; i < commonIds.Count; i++)
		{
			for (var j = i + 1; j < commonIds.Count; j++)
			{
				var refA = refById[commonIds[i]];
				var refB = refById[commonIds[j]];
				if (refA.Layer != refB.Layer)
					continue; // only compare order within the same reference layer

				total++;
				var actualA = actualById[commonIds[i]];
				var actualB = actualById[commonIds[j]];
				var refSign = Math.Sign(refA.OrderInLayer - refB.OrderInLayer);
				var actualSign = Math.Sign(actualA.OrderInLayer - actualB.OrderInLayer);
				if (refSign == actualSign)
					agree++;
			}
		}

		return total == 0 ? 1.0 : (double)agree / total;
	}

	private static double PairwiseAgreement(LayoutSkeleton reference, LayoutSkeleton actual, Func<SkeletonNode, int> rank)
	{
		var refById = reference.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
		var actualById = actual.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
		var commonIds = refById.Keys.Where(actualById.ContainsKey).ToList();
		ThrowIfSuspiciouslyDisjoint(reference, actual, commonIds.Count);

		if (commonIds.Count < 2)
			return 1.0; // legitimately trivial: too few *shared* entities to form any pair

		var total = 0;
		var agree = 0;
		for (var i = 0; i < commonIds.Count; i++)
		{
			for (var j = i + 1; j < commonIds.Count; j++)
			{
				total++;
				var refSign = Math.Sign(rank(refById[commonIds[i]]) - rank(refById[commonIds[j]]));
				var actualSign = Math.Sign(rank(actualById[commonIds[i]]) - rank(actualById[commonIds[j]]));
				if (refSign == actualSign)
					agree++;
			}
		}

		return total == 0 ? 1.0 : (double)agree / total;
	}

	// Zero entity ids in common between two skeletons that both actually have entities is
	// never a legitimate "nothing to compare" result — it means one side's extraction silently
	// failed (e.g. a node-shape variant the SVG parser didn't recognize) and produced an empty
	// or mismatched skeleton. Left unguarded, the "too few pairs" fallbacks below would report
	// a vacuous 100% agreement for a comparison that never actually happened — which is exactly
	// how the original mermaid.js node extraction bug (missing the label-only <rect> entity
	// shape) went unnoticed: it silently returned zero boxes, and every agreement metric
	// "passed" with nothing behind it.
	private static void ThrowIfSuspiciouslyDisjoint(LayoutSkeleton reference, LayoutSkeleton actual, int commonCount)
	{
		if (commonCount > 0 || reference.Nodes.Count == 0 || actual.Nodes.Count == 0)
			return;

		throw new InvalidOperationException(
			$"Skeletons share zero entity ids (reference has {reference.Nodes.Count}, actual has " +
			$"{actual.Nodes.Count}) — this almost certainly means extraction failed on one side, " +
			"not that the comparison is trivially perfect.");
	}
}
