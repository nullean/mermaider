using Mermaider.Models;
using Mermaider.Rendering;
using Mermaider.Text;
using Sugiyama;

namespace Mermaider.Layout;

/// <summary>
/// Lightweight ER diagram layout using the Sugiyama engine.
/// Replaces the MSAGL-based <c>ErLayoutEngine</c>.
/// </summary>
internal static class LightweightErLayoutEngine
{
	private const double Padding = 20;
	// Shared entity geometry (class / ER / requirement): 36px header, 28px rows, the EntityGrid column grid.
	private const double HeaderHeight = 36;
	private const double RowHeight = DesignSystem.RowHeight;
	private const double MinWidth = 120;
	private const double NodeSpacing = 20;
	private const double LayerSpacing = 72;

	internal static PositionedErDiagram Layout(ErDiagram diagram)
	{
		if (diagram.Entities.Count == 0)
			return new PositionedErDiagram { Width = 0, Height = 0, Entities = [], Relationships = [] };

		var entitySizes = new Dictionary<string, (double Width, double Height)>();
		foreach (var entity in diagram.Entities)
		{
			var (typeW, nameW, badgeW) = ErSvgRenderer.MeasureColumns(entity.Attributes);
			var gridW = entity.Attributes.Count > 0 ? EntityGrid.BoxWidth(typeW, nameW, signs: false, badgeW) : 0;
			// Edge anchors are spread evenly along a node side; keep them at least ~18px apart so crow's-foot markers don't overlap.
			var degree = diagram.Relationships.Count(r => r.Entity1 != r.Entity2 && (r.Entity1 == entity.Id || r.Entity2 == entity.Id));
			var anchorWidth = degree > 4 ? (degree * 18) + 24 : 0;
			var width = Math.Max(Math.Max(MinWidth, anchorWidth), Math.Max(EntityGrid.HeadingWidth(entity.Label), gridW));
			var height = entity.Attributes.Count == 0
				? HeaderHeight + 8
				: HeaderHeight + (entity.Attributes.Count * RowHeight);
			entitySizes[entity.Id] = (width, height);
		}

		var layoutNodes = new List<LayoutNode>(diagram.Entities.Count);
		foreach (var entity in diagram.Entities)
		{
			var (w, h) = entitySizes[entity.Id];
			layoutNodes.Add(new LayoutNode(entity.Id, w, h));
		}

		var layoutEdges = new List<LayoutEdge>(diagram.Relationships.Count);
		var layoutEdgeRelIndices = new List<int>(diagram.Relationships.Count);
		for (var i = 0; i < diagram.Relationships.Count; i++)
		{
			var rel = diagram.Relationships[i];
			if (rel.Entity1 == rel.Entity2)
				continue; // self-loops carry no layout info; skip to avoid Sugiyama height inflation
			double labelW = 0, labelH = 0;
			if (rel.Label.Length > 0)
			{
				var metrics = TextMetrics.MeasureMultiline(rel.Label.AsSpan(), RenderConstants.FontSizes.EdgeLabel, RenderConstants.FontWeights.EdgeLabel);
				labelW = ErSvgRenderer.LabelBoxWidth(metrics.Width) + 6;
				labelH = metrics.Height + 8;
			}
			layoutEdges.Add(new LayoutEdge(rel.Entity1, rel.Entity2, labelW, labelH));
			layoutEdgeRelIndices.Add(i);
		}

		var layoutDir = diagram.Direction switch
		{
			Direction.LR => LayoutDirection.LR,
			Direction.RL => LayoutDirection.RL,
			Direction.BT => LayoutDirection.BT,
			_ => LayoutDirection.TD, // default TD matches mermaid.js behaviour
		};

		// Labels sit on their own column in the gap (never stacked), so the gap only has to fit one label plus the stubs and
		// crow's-foot markers: the Sugiyama layout sizes each gap from its labels (label extent + 56) and uses LayerSpacing
		// for gaps that carry none.
		var horizontal = layoutDir is LayoutDirection.LR or LayoutDirection.RL;
		_ = horizontal;
		const double effectiveLayerSpacing = LayerSpacing;

		var layoutGraph = new LayoutGraph(layoutDir, layoutNodes, layoutEdges, []);
		var componentCount = CountConnectedComponents(layoutNodes, layoutEdges);
		// 1-3 components: one row; 4+: grid with ceil(sqrt(n)) per row
		var maxPerRow = componentCount <= 3 ? componentCount : (int)Math.Ceiling(Math.Sqrt(componentCount));
		var result = SugiyamaLayout.Compute(layoutGraph, new LayoutOptions
		{
			Padding = Padding,
			NodeSpacing = NodeSpacing,
			LayerSpacing = effectiveLayerSpacing,
			CrossingIterations = 8,
			StrictTopDownFanout = true,
			ForceBottomExitFanOut = true,
			MaxComponentsPerRow = maxPerRow,
			TightSourceLayering = true,
			SeparateComponents = false,
			UseRealFirstTiebreaker = true,
			PortAwareLayout = true,
			CrossingRestarts = 160,
		});

		return ExtractPositioned(result, diagram, layoutEdgeRelIndices);
	}

	/// <summary>Connected components (clusters) numbered in order of their first entity; every isolated entity is its own cluster.</summary>
	private static Dictionary<string, int> AssignClusters(ErDiagram diagram)
	{
		var clusters = new Rendering.ClusterAssigner(diagram.Entities.Select(e => e.Id));
		foreach (var r in diagram.Relationships)
			clusters.Union(r.Entity1, r.Entity2);
		return clusters.Number(diagram.Entities.Select(e => e.Id));
	}

	private static int CountConnectedComponents(List<LayoutNode> nodes, List<LayoutEdge> edges)
	{
		var adj = new Dictionary<string, HashSet<string>>(nodes.Count);
		foreach (var n in nodes)
			adj[n.Id] = [];
		foreach (var e in edges)
		{
			if (adj.TryGetValue(e.Source, out var s))
				_ = s.Add(e.Target);
			if (adj.TryGetValue(e.Target, out var t))
				_ = t.Add(e.Source);
		}
		var visited = new HashSet<string>(nodes.Count);
		var count = 0;
		var stack = new Stack<string>();
		foreach (var n in nodes)
		{
			if (!visited.Add(n.Id))
				continue;
			count++;
			stack.Push(n.Id);
			while (stack.Count > 0)
			{
				var cur = stack.Pop();
				foreach (var nb in adj[cur])
				{
					if (visited.Add(nb))
						stack.Push(nb);
				}
			}
		}
		return count;
	}

	private static PositionedErDiagram ExtractPositioned(LayoutResult result, ErDiagram diagram, IReadOnlyList<int> layoutEdgeRelIndices)
	{
		var nodeLookup = result.Nodes.ToDictionary(n => n.Id);
		var positionedEntities = new List<PositionedErEntity>(diagram.Entities.Count);
		var clusters = AssignClusters(diagram);

		foreach (var entity in diagram.Entities)
		{
			if (!nodeLookup.TryGetValue(entity.Id, out var n))
				continue;
			positionedEntities.Add(new PositionedErEntity
			{
				Id = entity.Id,
				Label = entity.Label,
				Attributes = entity.Attributes,
				X = n.X,
				Y = n.Y,
				Width = n.Width,
				Height = n.Height,
				HeaderHeight = HeaderHeight,
				RowHeight = RowHeight,
				Cluster = clusters[entity.Id],
			});
		}

		// layoutEdgeRelIndices[edgeIdx] → index in diagram.Relationships; self-loops were filtered out
		var positionedRels = new List<PositionedErRelationship>(layoutEdgeRelIndices.Count);
		for (var edgeIdx = 0; edgeIdx < layoutEdgeRelIndices.Count; edgeIdx++)
		{
			var rel = diagram.Relationships[layoutEdgeRelIndices[edgeIdx]];
			var edge = result.Edges.FirstOrDefault(e => e.OriginalIndex == edgeIdx);
			if (edge is null)
				continue;

			var points = edge.Points.Select(p => new Point(p.X, p.Y)).ToList();
			Point? labelPos = edge.LabelPosition is { } lp ? new Point(lp.X, lp.Y) : null;
			positionedRels.Add(new PositionedErRelationship
			{
				Entity1 = rel.Entity1,
				Entity2 = rel.Entity2,
				Cardinality1 = rel.Cardinality1,
				Cardinality2 = rel.Cardinality2,
				Label = rel.Label,
				Identifying = rel.Identifying,
				Points = points,
				LabelPosition = labelPos,
			});
		}

		// Collapse multi-hop paths (through virtual nodes) to 2-point before spread so that
		// SpreadConvergentPorts sees the source exit X as the approach, not V.cx (which BK
		// BALANCED aligns with the target, making all multi-hop approaches identical).
		// After spread, an entity-clearance check reverts any collapse that would route through
		// an intermediate entity box — preserving the original virtual-node routing.
		// PortAwareLayout routes (port → column → port) are final; no post-processing.

		// Self-loops carry no layout info (filtered from Sugiyama). Draw each as a rectangular loop out of the side with more free room,
		// like any other edge: straight stubs for the markers, with the label sitting on the outer vertical so it stays connected.
		var loopsPerSide = new Dictionary<(string Id, bool Left), int>();
		foreach (var rel in diagram.Relationships)
		{
			if (rel.Entity1 != rel.Entity2)
				continue;
			if (!nodeLookup.TryGetValue(rel.Entity1, out var n))
				continue;

			var freeLeft = double.MaxValue;
			var freeRight = double.MaxValue;
			foreach (var other in result.Nodes)
			{
				if (other.Id == n.Id || other.Y + other.Height <= n.Y || other.Y >= n.Y + n.Height)
					continue;
				if (other.X + other.Width <= n.X)
					freeLeft = Math.Min(freeLeft, n.X - (other.X + other.Width));
				else if (other.X >= n.X + n.Width)
					freeRight = Math.Min(freeRight, other.X - (n.X + n.Width));
			}

			var left = freeLeft >= freeRight;
			var dir = left ? -1 : 1;
			var sideX = left ? n.X : n.X + n.Width;
			// The loop must be deep enough that the label pill (centred on the outer vertical) clears the markers beside the entity.
			var labelBox = rel.Label.Length > 0
				? ErSvgRenderer.LabelBoxWidth(TextMetrics.MeasureMultiline(rel.Label.AsSpan(), RenderConstants.FontSizes.EdgeLabel, RenderConstants.FontWeights.EdgeLabel).Width)
				: 0;
			var loopOut = 36 + (labelBox / 2);
			var nth = loopsPerSide.GetValueOrDefault((n.Id, left));
			loopsPerSide[(n.Id, left)] = nth + 1;
			var centreY = n.Y + (n.Height / 2);
			var halfSpan = Math.Clamp(n.Height * 0.3, 24, 40);
			var exitY = centreY - halfSpan;
			var entryY = centreY + halfSpan;
			var outerX = sideX + (dir * (loopOut + (nth * 24)));
			var labelPos = rel.Label.Length > 0 ? new Point(outerX, centreY) : (Point?)null;
			positionedRels.Add(new PositionedErRelationship
			{
				Entity1 = rel.Entity1,
				Entity2 = rel.Entity2,
				Cardinality1 = rel.Cardinality1,
				Cardinality2 = rel.Cardinality2,
				Label = rel.Label,
				Identifying = rel.Identifying,
				Points = [new Point(sideX, exitY), new Point(outerX, exitY), new Point(outerX, entryY), new Point(sideX, entryY)],
				LabelPosition = labelPos,
			});
		}

		return FitLabelsInCanvas(new PositionedErDiagram
		{
			Width = result.Width,
			Height = result.Height,
			Entities = positionedEntities,
			Relationships = positionedRels,
		});
	}

	/// <summary>Label boxes can extend past the leftmost/rightmost entity; shift and grow the canvas so none is clipped.</summary>
	private static PositionedErDiagram FitLabelsInCanvas(PositionedErDiagram d)
	{
		var minX = d.Entities.Count > 0 ? d.Entities.Min(e => e.X) : double.MaxValue;
		var maxX = d.Entities.Count > 0 ? d.Entities.Max(e => e.X + e.Width) : 0;
		foreach (var r in d.Relationships)
		{
			if (r.Label.Length == 0 || r.LabelPosition is not { } lp)
				continue;
			var m = TextMetrics.MeasureMultiline(r.Label.AsSpan(), RenderConstants.FontSizes.EdgeLabel, RenderConstants.FontWeights.EdgeLabel);
			var half = ErSvgRenderer.LabelBoxWidth(m.Width) / 2;
			minX = Math.Min(minX, lp.X - half);
			maxX = Math.Max(maxX, lp.X + half);
		}

		var shift = minX < Padding ? Padding - minX : 0;
		var width = Math.Max(d.Width + shift, maxX + shift + Padding);
		if (shift == 0 && Math.Abs(width - d.Width) < 0.01)
			return d;

		return d with
		{
			Width = width,
			Entities = d.Entities.Select(e => e with { X = e.X + shift }).ToList(),
			Relationships = d.Relationships.Select(r => r with
			{
				Points = r.Points.Select(p => new Point(p.X + shift, p.Y)).ToList(),
				LabelPosition = r.LabelPosition is { } lp ? new Point(lp.X + shift, lp.Y) : null,
			}).ToList(),
		};
	}

	/// <summary>
	/// When multiple edges converge on the same entity port (same entry/exit x and y),
	/// spread their attachment points across the entity's top or bottom edge so each
	/// edge has a distinct anchor.  Edges that already use different x positions are
	/// left unchanged.
	/// </summary>
	private static void SpreadConvergentPorts(
		List<PositionedErRelationship> rels,
		List<PositionedErEntity> entities)
	{
		const double portMarginFraction = 0.15;
		const double snapY = 4.0;

		var entityByName = entities.ToDictionary(e => e.Id);

		// ---------- top-entry (edges entering Entity2 from above) ----------
		// Group by entity2; find edges whose last point Y ≈ entity.Y
		var topGroups = new Dictionary<string, List<int>>();
		for (var i = 0; i < rels.Count; i++)
		{
			var rel = rels[i];
			if (rel.Points.Count < 2)
				continue;
			if (!entityByName.TryGetValue(rel.Entity2, out var ent))
				continue;
			var lastPt = rel.Points[^1];
			if (Math.Abs(lastPt.Y - ent.Y) > snapY)
				continue;
			if (!topGroups.TryGetValue(rel.Entity2, out var list))
				topGroups[rel.Entity2] = list = [];
			list.Add(i);
		}

		foreach (var (entityId, indices) in topGroups)
		{
			if (indices.Count < 2)
				continue;
			if (!entityByName.TryGetValue(entityId, out var ent))
				continue;

			// Sort by source exit X (first point). This reflects where each edge is coming from
			// geometrically: left sources get left entries, right sources get right entries.
			// Using pts[0].X is essential for correctness after multi-hop collapse: collapsed
			// paths have pts[^2].X = pts[0].X (source exit), while direct 4-point paths have
			// pts[^2].X = entity.cx, making mixed comparisons degenerate without this.
			indices.Sort((a, b) =>
			{
				var approachA = rels[a].Points[0].X;
				var approachB = rels[b].Points[0].X;
				var cmp = approachA.CompareTo(approachB);
				return cmp != 0 ? cmp : a.CompareTo(b);
			});

			var margin = ent.Width * portMarginFraction;
			var usable = ent.Width - (margin * 2);

			for (var i = 0; i < indices.Count; i++)
			{
				var t = indices.Count > 1 ? (double)i / (indices.Count - 1) : 0.5;
				var newX = ent.X + (ent.Width * (i + 1) / (indices.Count + 1));

				var rel = rels[indices[i]];
				var pts = new List<Point>(rel.Points);
				var last = pts[^1];
				var secondLast = pts[^2];

				if (Math.Abs(last.X - newX) < 1)
					continue;

				pts[^1] = new Point(newX, last.Y);
				// Pull the source-exit waypoint along only when there is an intermediate
				// waypoint (pts.Count > 2). For 2-point paths the bottom-exit spread owns
				// the source end; letting the two spreads act independently allows the
				// natural D-R-D / D-L-D fan to form between nodes of different widths.
				if (pts.Count > 2 && Math.Abs(secondLast.X - last.X) < 1)
					pts[^2] = new Point(newX, secondLast.Y);

				rels[indices[i]] = rel with { Points = pts };
			}
		}

		// ---------- bottom-exit (edges leaving Entity1 from below) ----------
		// Group by entity1; find edges whose first point Y ≈ entity.Y + entity.Height
		var bottomGroups = new Dictionary<string, List<int>>();
		for (var i = 0; i < rels.Count; i++)
		{
			var rel = rels[i];
			if (rel.Points.Count < 2)
				continue;
			if (!entityByName.TryGetValue(rel.Entity1, out var ent))
				continue;
			var firstPt = rel.Points[0];
			if (Math.Abs(firstPt.Y - (ent.Y + ent.Height)) > snapY)
				continue;
			if (!bottomGroups.TryGetValue(rel.Entity1, out var list))
				bottomGroups[rel.Entity1] = list = [];
			list.Add(i);
		}

		foreach (var (entityId, indices) in bottomGroups)
		{
			if (indices.Count < 2)
				continue;
			if (!entityByName.TryGetValue(entityId, out var ent))
				continue;

			// Sort by target entity center-x (last point = destination X, consistent for
			// both straight D routes and bent D-L-D / D-R-D routes). Tiebreak by edge index.
			indices.Sort((a, b) =>
			{
				var ptsA = rels[a].Points;
				var ptsB = rels[b].Points;
				var approachA = ptsA.Count >= 1 ? ptsA[^1].X : 0;
				var approachB = ptsB.Count >= 1 ? ptsB[^1].X : 0;
				var cmp = approachA.CompareTo(approachB);
				return cmp != 0 ? cmp : a.CompareTo(b);
			});

			var margin = ent.Width * portMarginFraction;
			var usable = ent.Width - (margin * 2);

			for (var i = 0; i < indices.Count; i++)
			{
				var t = indices.Count > 1 ? (double)i / (indices.Count - 1) : 0.5;
				var newX = ent.X + (ent.Width * (i + 1) / (indices.Count + 1));

				var rel = rels[indices[i]];
				var pts = new List<Point>(rel.Points);
				var first = pts[0];
				var second = pts[1];

				if (Math.Abs(first.X - newX) < 1)
					continue;

				pts[0] = new Point(newX, first.Y);
				// Pull the destination-entry waypoint along only when there is an intermediate
				// waypoint (pts.Count > 2). For 2-point paths the top-entry spread owns the
				// destination end; letting both spreads act independently produces the natural
				// D-R-D / D-L-D fan between nodes of different widths.
				if (pts.Count > 2 && Math.Abs(second.X - first.X) < 1)
					pts[1] = new Point(newX, second.Y);

				rels[indices[i]] = rel with { Points = pts };
			}
		}
	}

	/// <summary>
	/// Converts 2-point slanted edges (exit X ≠ entry X) into 4-point orthogonal paths.
	/// A straight diagonal line is classified as a single "D" segment, but ELK routes these
	/// as D-R-D or D-L-D with an explicit horizontal bend. Inserting waypoints at the mid-Y
	/// of the inter-layer gap reproduces that classification so route-shape metrics are correct.
	/// </summary>
	/// <summary>
	/// Collapse multi-hop paths (≥5 points, passing through virtual nodes) to 2-point so
	/// SpreadConvergentPorts sees the source exit X as the approach key, not V.cx (which BK
	/// BALANCED aligns with the target, making all multi-hop approaches degenerate).
	/// Returns the saved original paths keyed by rel index for possible restoration.
	/// </summary>
	private static Dictionary<int, List<Point>> CollapseMultiHopPaths(List<PositionedErRelationship> rels)
	{
		var saved = new Dictionary<int, List<Point>>();
		for (var i = 0; i < rels.Count; i++)
		{
			var rel = rels[i];
			if (rel.Points.Count < 5)
				continue;
			saved[i] = rel.Points.ToList();
			rels[i] = rel with { Points = [rel.Points[0], rel.Points[^1]] };
		}
		return saved;
	}

	/// <summary>
	/// For each collapsed multi-hop path, check whether the 4-point orthogonal path that
	/// InsertOrthogonalBends would produce passes through any intermediate entity box.
	/// If it does, restore the original multi-hop routing to avoid entity passthrough.
	/// </summary>
	private static void RestoreUnsafeCollapses(
		List<PositionedErRelationship> rels,
		Dictionary<int, List<Point>> savedMultiHop,
		List<PositionedErEntity> entities)
	{
		foreach (var (i, original) in savedMultiHop)
		{
			var rel = rels[i];
			if (rel.Points.Count != 2)
			{
				// SpreadConvergentPorts may have already reverted it somehow — keep as-is
				continue;
			}
			if (!IsOrthogonalPathEntitySafe(rel.Points, entities))
				rels[i] = rel with { Points = original };
		}
	}

	/// <summary>
	/// Returns true if the 4-point orthogonal path that InsertOrthogonalBends would produce
	/// from <paramref name="twoPoints"/> does not pass through any entity box.
	/// The four segments are: vertical down from exit, horizontal bus at midY, vertical down to entry.
	/// </summary>
	private static bool IsOrthogonalPathEntitySafe(
		IReadOnlyList<Point> twoPoints,
		IReadOnlyList<PositionedErEntity> entities)
	{
		var exit = twoPoints[0];
		var entry = twoPoints[1];
		var midY = (exit.Y + entry.Y) / 2.0;
		var xMin = Math.Min(exit.X, entry.X);
		var xMax = Math.Max(exit.X, entry.X);
		const double eps = 1.0;

		foreach (var e in entities)
		{
			var eLeft = e.X + eps;
			var eRight = e.X + e.Width - eps;
			var eTop = e.Y + eps;
			var eBottom = e.Y + e.Height - eps;

			// Only consider entities in intermediate Y range (between exit and entry)
			if (eBottom <= exit.Y || eTop >= entry.Y)
				continue;

			// Vertical segment at exit.X from exit.Y to midY
			if (exit.X > eLeft && exit.X < eRight && midY > eTop && exit.Y < eBottom)
				return false;

			// Horizontal bus at midY spanning xMin..xMax
			if (midY > eTop && midY < eBottom && xMax > eLeft && xMin < eRight)
				return false;

			// Vertical segment at entry.X from midY to entry.Y
			if (entry.X > eLeft && entry.X < eRight && entry.Y > eTop && midY < eBottom)
				return false;
		}
		return true;
	}

	private static void InsertOrthogonalBends(List<PositionedErRelationship> rels, double minHorizDelta = 2.0)
	{
		for (var i = 0; i < rels.Count; i++)
		{
			var rel = rels[i];
			if (rel.Points.Count != 2)
				continue;
			var a = rel.Points[0];
			var b = rel.Points[^1];
			if (Math.Abs(b.X - a.X) < minHorizDelta)
				continue;
			var midY = (a.Y + b.Y) / 2.0;
			rels[i] = rel with
			{
				Points = [a, new Point(a.X, midY), new Point(b.X, midY), b]
			};
		}
	}

	private static void OffsetParallelEdges(List<PositionedErRelationship> rels)
	{
		const double parallelStep = 20.0;

		var groups = new Dictionary<(string, string), List<int>>();
		for (var i = 0; i < rels.Count; i++)
		{
			var rel = rels[i];
			var key = string.Compare(rel.Entity1, rel.Entity2, StringComparison.Ordinal) <= 0
				? (rel.Entity1, rel.Entity2)
				: (rel.Entity2, rel.Entity1);
			if (!groups.TryGetValue(key, out var list))
				groups[key] = list = [];
			list.Add(i);
		}

		foreach (var (_, indices) in groups)
		{
			if (indices.Count <= 1)
				continue;

			var sample = rels[indices[0]];
			if (sample.Points.Count < 2)
				continue;
			var dx = sample.Points[^1].X - sample.Points[0].X;
			var dy = sample.Points[^1].Y - sample.Points[0].Y;
			var len = Math.Sqrt((dx * dx) + (dy * dy));
			if (len < 0.001)
				continue;
			// Perpendicular direction: RIGHT relative to edge travel direction, so that
			// lower-indexed (earlier model-order) edges are offset LEFT and higher-indexed
			// edges offset RIGHT — matching ELK's model-order port assignment.
			var perpX = dy / len;
			var perpY = -dx / len;

			for (var i = 0; i < indices.Count; i++)
			{
				var offset = (i - ((indices.Count - 1) / 2.0)) * parallelStep;
				if (Math.Abs(offset) < 0.001)
					continue;
				var rel = rels[indices[i]];
				var newPoints = rel.Points.Select(p => new Point(p.X + (perpX * offset), p.Y + (perpY * offset))).ToList();
				rels[indices[i]] = rel with { Points = newPoints };
			}
		}
	}
}
