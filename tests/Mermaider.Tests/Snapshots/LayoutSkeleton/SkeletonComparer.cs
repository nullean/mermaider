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

	/// <summary>
	/// Like <see cref="WithinLayerOrderAgreement"/> but tolerates a full left-to-right mirror of
	/// <paramref name="actual"/>'s within-layer ordering. Returns the better of the two scores.
	///
	/// <para>This matters because a mirrored layout is visually just as valid as the original:
	/// when two siblings share no edge fixing their relative order (e.g. er-complex's COMMENT/TAG,
	/// both children of POST with no edge between them), dagre and Sugiyama can legitimately
	/// pick opposite tie-breaks. <see cref="WithinLayerOrderAgreement"/> calls that 0% agreement;
	/// this method recognises it as 100%. Reporting both lets the caller see which diagrams have
	/// a real ordering defect (raw is low, mirror-tolerant is also low) versus a harmless
	/// tie-break difference (raw is low, mirror-tolerant is high).</para>
	/// </summary>
	public static double MirrorToleratedOrderAgreement(LayoutSkeleton reference, LayoutSkeleton actual)
	{
		var raw = WithinLayerOrderAgreement(reference, actual);
		if (raw >= 1.0)
			return raw; // already perfect, no need to check the mirror

		// Build a mirrored copy of actual's nodes: within each reference layer, flip OrderInLayer.
		// We invert on per-reference-layer maxima so the flip is local to each layer (the reference
		// determines which nodes share a layer; actual may disagree, so we use actual's real
		// OrderInLayer values and invert each independently per reference-defined layer group).
		var refById = reference.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
		var actualById = actual.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
		var commonIds = refById.Keys.Where(actualById.ContainsKey).ToList();

		// Group common ids by their reference layer, then for each group compute the max actual
		// OrderInLayer so we can invert as maxOrder - order.
		var byRefLayer = commonIds
			.GroupBy(id => refById[id].Layer)
			.ToDictionary(g => g.Key, g => g.ToList());

		// Build a mirrored actual dictionary: id → mirrored OrderInLayer within its ref-layer group.
		var mirroredOrder = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var (_, ids) in byRefLayer)
		{
			var maxActualOrder = ids.Max(id => actualById.TryGetValue(id, out var n) ? n.OrderInLayer : 0);
			foreach (var id in ids)
			{
				if (actualById.TryGetValue(id, out var n))
					mirroredOrder[id] = maxActualOrder - n.OrderInLayer;
			}
		}

		// Score using the mirrored orders.
		var total = 0;
		var agree = 0;
		for (var i = 0; i < commonIds.Count; i++)
		{
			for (var j = i + 1; j < commonIds.Count; j++)
			{
				var refA = refById[commonIds[i]];
				var refB = refById[commonIds[j]];
				if (refA.Layer != refB.Layer)
					continue;

				total++;
				var refSign = Math.Sign(refA.OrderInLayer - refB.OrderInLayer);
				var mirroredSign = Math.Sign(
					(mirroredOrder.TryGetValue(commonIds[i], out var mo_i) ? mo_i : 0) -
					(mirroredOrder.TryGetValue(commonIds[j], out var mo_j) ? mo_j : 0));
				if (refSign == mirroredSign)
					agree++;
			}
		}

		var mirror = total == 0 ? 1.0 : (double)agree / total;
		return Math.Max(raw, mirror);
	}

	/// <summary>
	/// Fraction (0..1) of edges — matched by (From, To, Label) between the two skeletons —
	/// whose (FromSide, ToSide) attachment pair agrees. Only edges present in both skeletons
	/// (matched by identity) are counted; extras on either side are ignored. Returns 1.0 when
	/// no edges are shared (trivially nothing to disagree on).
	///
	/// <para>The comparison is mirror-aware: if the actual skeleton's OrderInLayer is a
	/// left-to-right mirror of the reference's (detected by checking whether the mirror-tolerant
	/// score substantially exceeds the raw score), Left and Right sides are swapped in actual
	/// before comparing. This ensures a mirrored-but-otherwise-correct layout gets full credit
	/// for side agreement too, not a 0% side score that misrepresents a purely cosmetic
	/// tie-break difference.</para>
	/// </summary>
	public static double SideAgreement(LayoutSkeleton reference, LayoutSkeleton actual)
	{
		// Determine whether actual is mirrored relative to reference.
		var raw = WithinLayerOrderAgreement(reference, actual);
		var tolerant = MirrorToleratedOrderAgreement(reference, actual);
		var isMirrored = tolerant - raw > 0.1 && tolerant > 0.5;

		// Index actual edges by (From, To, Label) for O(1) lookup.
		var actualEdges = actual.Edges.ToDictionary(
			e => (e.From, e.To, e.Label),
			e => e,
			TupleStringComparer.Instance);

		var total = 0;
		var agree = 0;
		foreach (var refEdge in reference.Edges)
		{
			if (!actualEdges.TryGetValue((refEdge.From, refEdge.To, refEdge.Label), out var actualEdge))
				continue; // edge not in both — skip

			total++;
			var actualFrom = isMirrored ? actualEdge.FromSide.MirrorLR() : actualEdge.FromSide;
			var actualTo   = isMirrored ? actualEdge.ToSide.MirrorLR()   : actualEdge.ToSide;
			if (actualFrom == refEdge.FromSide && actualTo == refEdge.ToSide)
				agree++;
		}

		return total == 0 ? 1.0 : (double)agree / total;
	}

	// Equality comparer for (string, string, string) tuples using ordinal string comparison.
	private sealed class TupleStringComparer : IEqualityComparer<(string, string, string)>
	{
		public static readonly TupleStringComparer Instance = new();
		public bool Equals((string, string, string) x, (string, string, string) y) =>
			StringComparer.Ordinal.Equals(x.Item1, y.Item1) &&
			StringComparer.Ordinal.Equals(x.Item2, y.Item2) &&
			StringComparer.Ordinal.Equals(x.Item3, y.Item3);
		public int GetHashCode((string, string, string) obj) =>
			HashCode.Combine(
				StringComparer.Ordinal.GetHashCode(obj.Item1),
				StringComparer.Ordinal.GetHashCode(obj.Item2),
				StringComparer.Ordinal.GetHashCode(obj.Item3));
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
