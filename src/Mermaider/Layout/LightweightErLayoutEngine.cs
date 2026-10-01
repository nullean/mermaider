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
	private const double BoxPadX = 12;
	private const double HeaderHeight = 38;
	private const double RowHeight = 26;
	private const double MinWidth = 120;
	private static readonly double AttrFontSize = RenderConstants.FontSizes.Member;
	private const double NodeSpacing = 28;
	private const double LayerSpacing = 80;

	internal static PositionedErDiagram Layout(ErDiagram diagram)
	{
		if (diagram.Entities.Count == 0)
			return new PositionedErDiagram { Width = 0, Height = 0, Entities = [], Relationships = [] };

		var entitySizes = new Dictionary<string, (double Width, double Height)>();
		foreach (var entity in diagram.Entities)
		{
			var headerTextW = TextMetrics.MeasureTextWidth(
				entity.Label, RenderConstants.FontSizes.NodeLabel, RenderConstants.FontWeights.NodeLabel);
			var maxAttrW = 0.0;
			foreach (var attr in entity.Attributes)
			{
				// All three columns: type  name  PK/FK — key is now an inline column
				var keyText = attr.Keys.Count > 0 ? "  " + string.Join(",", attr.Keys) : "";
				var attrText = $"{attr.Type}  {attr.Name}{keyText}";
				var w = TextMetrics.EstimateMonoTextWidth(attrText, AttrFontSize);
				if (w > maxAttrW)
					maxAttrW = w;
			}
			var width = Math.Max(MinWidth, Math.Max(headerTextW + (BoxPadX * 2), maxAttrW + (BoxPadX * 2)));
			var height = entity.Attributes.Count == 0
				? HeaderHeight * 2
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
				labelW = metrics.Width + 8;
				labelH = metrics.Height + 6;
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

		// The layer gap runs along the flow axis, so the label has to be measured along that axis too:
		// on LR/RL a label sized by its height overhangs the entities either side and they paint over it.
		var horizontal = layoutDir is LayoutDirection.LR or LayoutDirection.RL;
		var maxLabelExtent = layoutEdges
			.Select(e => horizontal ? e.LabelWidth : e.LabelHeight)
			.Where(v => v > 0)
			.DefaultIfEmpty(0)
			.Max();

		// Estimate the worst-case number of labeled edges sharing one inter-layer gap:
		// that's the max out-degree (fan-out) from any single entity.
		// Each label needs ~(labelH + 4)px of space in the gap direction.
		var labelStep = horizontal ? 30.0 : 26.0; // px per stacked label
		var maxFanOut = layoutEdges
			.Where(e => e.LabelHeight > 0)
			.GroupBy(e => e.Source)
			.Select(g => g.Count())
			.DefaultIfEmpty(0)
			.Max();
		double minSpacingForFanOut = 0;
		if (maxFanOut > 1)
			minSpacingForFanOut = (maxFanOut * labelStep) + 20;

		var effectiveLayerSpacing = Math.Max(LayerSpacing,
			Math.Max((maxLabelExtent > 0) ? (maxLabelExtent + 40) : 0, minSpacingForFanOut));

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
			MaxComponentsPerRow = maxPerRow,
			TightSourceLayering = true,
			SeparateComponents = false,
		});

		return ExtractPositioned(result, diagram, layoutEdgeRelIndices);
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

		OffsetParallelEdges(positionedRels);
		SpreadConvergentPorts(positionedRels, positionedEntities);

		// Synthesize arc paths for self-loop relationships (same entity on both ends).
		// These are filtered from Sugiyama layout; we place them as a right-side loop.
		foreach (var rel in diagram.Relationships)
		{
			if (rel.Entity1 != rel.Entity2)
				continue;
			if (!nodeLookup.TryGetValue(rel.Entity1, out var n))
				continue;
			var loopR = Math.Max(30.0, n.Height * 0.3);
			var exitY = n.Y + (n.Height * 0.35);
			var entryY = n.Y + (n.Height * 0.65);
			var sideX = n.X + n.Width;
			var loopX = sideX + loopR;
			var loopPoints = new List<Point>
			{
				new(sideX, exitY),
				new(loopX, exitY),
				new(loopX, entryY),
				new(sideX, entryY),
			};
			positionedRels.Add(new PositionedErRelationship
			{
				Entity1 = rel.Entity1,
				Entity2 = rel.Entity2,
				Cardinality1 = rel.Cardinality1,
				Cardinality2 = rel.Cardinality2,
				Label = rel.Label,
				Identifying = rel.Identifying,
				Points = loopPoints,
			});
		}

		return new PositionedErDiagram
		{
			Width = result.Width,
			Height = result.Height,
			Entities = positionedEntities,
			Relationships = positionedRels,
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

			// Sort by source entity center-x so leftmost source gets leftmost port
			indices.Sort((a, b) =>
			{
				var approachA = rels[a].Points.Count >= 2 ? rels[a].Points[^2].X : rels[a].Points[^1].X;
				var approachB = rels[b].Points.Count >= 2 ? rels[b].Points[^2].X : rels[b].Points[^1].X;
				return approachA.CompareTo(approachB);
			});

			var margin = ent.Width * portMarginFraction;
			var usable = ent.Width - (margin * 2);

			for (var i = 0; i < indices.Count; i++)
			{
				var t = indices.Count > 1 ? (double)i / (indices.Count - 1) : 0.5;
				var newX = ent.X + margin + (t * usable);

				var rel = rels[indices[i]];
				var pts = new List<Point>(rel.Points);
				var last = pts[^1];
				var secondLast = pts[^2];

				if (Math.Abs(last.X - newX) < 1)
					continue;

				pts[^1] = new Point(newX, last.Y);
				// If the second-to-last point is on the same vertical as the old last,
				// move it too so the final descent remains straight — but only when the
				// new x still falls within the source entity's horizontal bounds.
				// For a 2-point edge, pts[^2] is the source exit; pulling it outside
				// the source entity box makes the line look disconnected.
				if (Math.Abs(secondLast.X - last.X) < 1)
				{
					var srcOk = pts.Count > 2 // waypoint, not entity exit — always safe
						|| !entityByName.TryGetValue(rel.Entity1, out var srcEnt)
						|| (newX >= srcEnt.X && newX <= srcEnt.X + srcEnt.Width);
					if (srcOk)
						pts[^2] = new Point(newX, secondLast.Y);
				}

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

			// Sort by target entity center-x
			indices.Sort((a, b) =>
			{
				var approachA = rels[a].Points.Count >= 2 ? rels[a].Points[1].X : rels[a].Points[0].X;
				var approachB = rels[b].Points.Count >= 2 ? rels[b].Points[1].X : rels[b].Points[0].X;
				return approachA.CompareTo(approachB);
			});

			var margin = ent.Width * portMarginFraction;
			var usable = ent.Width - (margin * 2);

			for (var i = 0; i < indices.Count; i++)
			{
				var t = indices.Count > 1 ? (double)i / (indices.Count - 1) : 0.5;
				var newX = ent.X + margin + (t * usable);

				var rel = rels[indices[i]];
				var pts = new List<Point>(rel.Points);
				var first = pts[0];
				var second = pts[1];

				if (Math.Abs(first.X - newX) < 1)
					continue;

				pts[0] = new Point(newX, first.Y);
				// Only pull the next waypoint to the new x if it stays within the
				// target entity's horizontal bounds (for 2-point edges, pts[1] is the entry).
				if (Math.Abs(second.X - first.X) < 1)
				{
					var tgtOk = pts.Count > 2
						|| !entityByName.TryGetValue(rel.Entity2, out var tgtEnt)
						|| (newX >= tgtEnt.X && newX <= tgtEnt.X + tgtEnt.Width);
					if (tgtOk)
						pts[1] = new Point(newX, second.Y);
				}

				rels[indices[i]] = rel with { Points = pts };
			}
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
			var perpX = -dy / len;
			var perpY = dx / len;

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
