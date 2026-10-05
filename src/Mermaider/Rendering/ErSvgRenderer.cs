using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class ErSvgRenderer
{
	internal static string Render(PositionedErDiagram diagram, SvgRenderContext context)
	{
		var sb = RenderToBuilder(diagram, context);
		try
		{
			return sb.ToString();
		}
		finally
		{
			_ = sb.Clear();
			SharedStringBuilderPool.Instance.Return(sb);
		}
	}

	internal static StringBuilder RenderToBuilder(PositionedErDiagram diagram, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		StyleBlock.AppendSvgOpenTag(sb, diagram.Width, diagram.Height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles);
		var ds = DesignSystem.For(context);

		foreach (var rel in diagram.Relationships)
			AppendRelationshipLine(sb, ds, rel, context.EdgeRadius);

		foreach (var entity in diagram.Entities)
			AppendEntityBox(sb, ds, entity);

		foreach (var rel in diagram.Relationships)
			AppendCardinality(sb, ds, rel);

		var labelPositions = ResolveErLabelPositions(diagram.Relationships, diagram.Entities, diagram.Width);
		for (var i = 0; i < diagram.Relationships.Count; i++)
			AppendRelationshipLabel(sb, ds, diagram.Relationships[i], labelPositions[i]);

		ds.Close(sb);
		return sb;
	}

	// Computes the visual midpoint of the rendered bezier curve, matching BuildErPath's detection
	// logic. This distributes labels naturally across the height/width of each edge rather than
	// clustering them near the target node (as Sugiyama's LabelPosition does).
	private static Point ComputeRenderedMidpoint(IReadOnlyList<Point> points)
	{
		var p0 = points[0];
		var pN = points[^1];

		// Long waypoint lists mean BuildErPath renders the full route (see
		// MaxWaypointsForCurveSimplification) rather than a simplified 2-point curve — the
		// label needs to follow suit and sit on that actual route, not at the arithmetic
		// center of just the first/last point (which can land back inside whatever
		// intervening entity the route was bent around to avoid).
		if (points.Count > MaxWaypointsForCurveSimplification)
			return PathLengthMidpoint(points);

		// Self-loop signature (see BuildSelfLoopPath): exit/entry share an X (the entity's
		// edge) while the two middle waypoints bulge out to a shared, different X (the loop
		// radius). The generic checks below key off p0/pN X/Y deltas and would otherwise
		// treat this as a same-column S-curve, placing the label back on the entity's edge
		// instead of out at the visible loop. Put it at the bulge's horizontal center instead.
		if (points.Count == 4)
		{
			var c1 = points[1];
			var c2 = points[2];
			if (Math.Abs(p0.X - pN.X) < 0.5 && Math.Abs(c1.X - c2.X) < 0.5 && Math.Abs(c1.X - p0.X) > 4.0)
				return new Point(c1.X, (p0.Y + pN.Y) / 2);
		}

		if (points.Count >= 3)
		{
			var p1 = points[1];
			var pN1 = points[^2];
			var dx0 = Math.Abs(p1.X - p0.X);
			var dy0 = Math.Abs(p1.Y - p0.Y);
			var dxN = Math.Abs(pN.X - pN1.X);
			var dyN = Math.Abs(pN.Y - pN1.Y);

			// S-curve TD/BT: midpoint at arithmetic center
			if (dx0 < 4.0 && dxN < 4.0 && Math.Abs(p0.X - pN.X) > 4.0)
				return new Point((p0.X + pN.X) / 2, (p0.Y + pN.Y) / 2);

			// J-curve (horizontal exit, vertical entry): label on vertical segment
			if (dy0 < 4.0 && dxN < 4.0 && dx0 > 4.0)
				return new Point(pN.X, (p0.Y + pN.Y) / 2);

			// J-curve rotated (vertical exit, horizontal entry): label on horizontal segment
			if (dx0 < 4.0 && dyN < 4.0 && dy0 > 4.0)
				return new Point((p0.X + pN.X) / 2, pN.Y);

			// S-curve LR/RL: midpoint at arithmetic center
			if (dy0 < 4.0 && dyN < 4.0 && Math.Abs(p0.Y - pN.Y) > 4.0)
				return new Point((p0.X + pN.X) / 2, (p0.Y + pN.Y) / 2);
		}

		return new Point((p0.X + pN.X) / 2, (p0.Y + pN.Y) / 2);
	}

	// Picks the midpoint of the single LONGEST straight segment in a multi-bend avoidance
	// route. A cumulative-path-length midpoint (the technique flowchart edges use — see
	// SvgRenderer.EdgeMidpoint) sounds appealing but can land right next to a corner, in the
	// narrow corridor a dogleg route uses to squeeze past an obstacle; there's no room for a
	// label there, and PushOutOfEntityBoxes then has nowhere to push it but further out —
	// observed shoving a label to a negative X, off the left edge of the canvas entirely.
	// The longest segment is, by construction, the most open straight run in the route, so a
	// label centered on it has the best chance of landing somewhere with real clearance.
	private static Point PathLengthMidpoint(IReadOnlyList<Point> points)
	{
		var bestLenSq = -1.0;
		var best = points[^1];
		for (var i = 1; i < points.Count; i++)
		{
			var dx = points[i].X - points[i - 1].X;
			var dy = points[i].Y - points[i - 1].Y;
			var lenSq = (dx * dx) + (dy * dy);
			if (lenSq <= bestLenSq)
				continue;

			bestLenSq = lenSq;
			best = new Point((points[i - 1].X + points[i].X) / 2, (points[i - 1].Y + points[i].Y) / 2);
		}

		return best;
	}

	// Computes final label positions from bezier midpoints and resolves any remaining overlaps —
	// both against other labels and against entity boxes a long/skip-layer edge happens to pass near.
	private static Point?[] ResolveErLabelPositions(
		IReadOnlyList<PositionedErRelationship> rels, IReadOnlyList<PositionedErEntity> entities, double canvasWidth)
	{
		var positions = new Point?[rels.Count];
		var sizes = new (double w, double h)[rels.Count];
		var entityLookup = new Dictionary<string, PositionedErEntity>(entities.Count, StringComparer.Ordinal);
		foreach (var e in entities)
			entityLookup[e.Id] = e;

		for (var i = 0; i < rels.Count; i++)
		{
			var rel = rels[i];
			if (rel.Label.Length == 0 || rel.Points.Count < 2)
				continue;
			var pos = rel.LabelPosition is { } routed ? routed : ComputeRenderedMidpoint(rel.Points);
			var metrics = TextMetrics.MeasureMultiline(
				rel.Label.AsSpan(),
				RenderConstants.FontSizes.EdgeLabel,
				RenderConstants.FontWeights.EdgeLabel);
			positions[i] = pos;
			sizes[i] = (LabelBoxWidth(metrics.Width), metrics.Height + LabelPadY);
		}

		// A chain of N labels overlapping the same corridor (common when several edges
		// converge on the same entity from different sources) needs roughly N-1 full
		// passes for the pairwise push to propagate end-to-end: fixing the outermost pair
		// can reintroduce a smaller overlap on the pair just inside it, which then needs
		// its own pass to resolve. 8 was enough for 2-3 way conflicts but left dense
		// diagrams (docs-builder-style, 4+ edges converging on one entity) with visibly
		// overlapping labels after the loop was cut off mid-convergence.
		const int maxIterations = 40;
		const double labelPad = 4.0;
		const double edgeMargin = 4.0;
		for (var iter = 0; iter < maxIterations; iter++)
		{
			var moved = false;
			for (var i = 0; i < rels.Count; i++)
			{
				if (positions[i] is null)
					continue;
				var (w, h) = sizes[i];
				// Self-loop labels intentionally sit right at their own entity's edge —
				// only push a label away from entities it does *not* belong to.
				var rel = rels[i];
				var pushed = PushOutOfEntityBoxes(positions[i]!.Value, w, h, entities, rel.Entity1, rel.Entity2);

				// Keep long-avoidance-route labels inside the canvas, as part of the same
				// fixed-point loop as entity-avoidance and pairwise separation below (not a
				// standalone pass afterward — clamping only at the very end can undo the
				// pairwise push's separation). Scoped to PathLengthMidpoint's longest-segment
				// pick (rel.Points.Count > MaxWaypointsForCurveSimplification): that heuristic
				// can land in a narrow corridor near the canvas edge with nowhere to push but
				// off-canvas. Short/simple edges and self-loops keep their original
				// unclamped placement — their natural midpoint can legitimately sit a few
				// pixels past the nominal canvas width (e.g. a self-loop bulge control point),
				// which was already working fine and isn't the bug this clamp targets.
				if (rel.Points.Count > MaxWaypointsForCurveSimplification)
				{
					var halfW = w / 2;
					var minX = halfW + edgeMargin;
					var maxX = canvasWidth - halfW - edgeMargin;
					if (maxX >= minX)
						pushed = pushed with { X = Math.Clamp(pushed.X, minX, maxX) };
				}

				if (pushed != positions[i]!.Value)
				{
					positions[i] = pushed;
					moved = true;
				}
			}

			for (var a = 0; a < rels.Count - 1; a++)
			{
				if (positions[a] is null)
					continue;
				var pa = positions[a]!.Value;
				var (wa, ha) = sizes[a];
				for (var b = a + 1; b < rels.Count; b++)
				{
					if (positions[b] is null)
						continue;
					var pb = positions[b]!.Value;
					var (wb, hb) = sizes[b];

					var ax0 = pa.X - (wa / 2) - labelPad;
					var ax1 = pa.X + (wa / 2) + labelPad;
					var ay0 = pa.Y - (ha / 2) - labelPad;
					var ay1 = pa.Y + (ha / 2) + labelPad;

					var bx0 = pb.X - (wb / 2) - labelPad;
					var bx1 = pb.X + (wb / 2) + labelPad;
					var by0 = pb.Y - (hb / 2) - labelPad;
					var by1 = pb.Y + (hb / 2) + labelPad;

					if (ax1 <= bx0 || bx1 <= ax0 || ay1 <= by0 || by1 <= ay0)
						continue; // no overlap

					// First try sliding the two labels apart along their own vertical runs: they stay on their edges.
					if (TrySeparateVertically(rels[a], rels[b], ref pa, ref pb, ha, hb))
					{
						positions[a] = pa;
						positions[b] = pb;
						moved = true;
						continue;
					}

					// Push apart horizontally to resolve overlap.
					// Always push in the direction that increases separation: if a is to the
					// left of b, push a further left and b further right; if a is to the right
					// of b (index order differs from position order), reverse the push direction.
					// Without this, the fixed-point loop can invert label order, sending labels
					// far from their edges in the wrong direction.
					var overlapX = Math.Min(ax1 - bx0, bx1 - ax0);
					var shiftX = (overlapX / 2.0) + 1.0;
					if (pa.X <= pb.X)
					{
						positions[a] = new Point(pa.X - shiftX, pa.Y);
						positions[b] = new Point(pb.X + shiftX, pb.Y);
					}
					else
					{
						positions[a] = new Point(pa.X + shiftX, pa.Y);
						positions[b] = new Point(pb.X - shiftX, pb.Y);
					}
					pa = positions[a]!.Value;
					pb = positions[b]!.Value;
					moved = true;
				}
			}

			if (!moved)
				break;
		}

		return positions;
	}

	// Vertical extent (label-centre range) of the longest vertical segment of the route at x, or null if the route has none there.
	private static (double Min, double Max)? VerticalRun(IReadOnlyList<Point> pts, double x, double labelHeight)
	{
		(double Min, double Max)? best = null;
		var bestLen = 0.0;
		for (var i = 0; i < pts.Count - 1; i++)
		{
			if (Math.Abs(pts[i].X - pts[i + 1].X) > 0.5 || Math.Abs(pts[i].X - x) > 2)
				continue;
			var lo = Math.Min(pts[i].Y, pts[i + 1].Y) + (labelHeight / 2) + 6;
			var hi = Math.Max(pts[i].Y, pts[i + 1].Y) - (labelHeight / 2) - 6;
			if (hi - lo > bestLen)
			{
				bestLen = hi - lo;
				best = (lo, hi);
			}
		}
		return best;
	}

	private static bool TrySeparateVertically(
		PositionedErRelationship ra, PositionedErRelationship rb, ref Point pa, ref Point pb, double ha, double hb)
	{
		if (VerticalRun(ra.Points, pa.X, ha) is not { } runA || VerticalRun(rb.Points, pb.X, hb) is not { } runB)
			return false;

		var need = ((ha + hb) / 2) + 2 - Math.Abs(pa.Y - pb.Y);
		if (need <= 0)
			return false;

		var aUp = pa.Y <= pb.Y;
		var roomA = aUp ? pa.Y - runA.Min : runA.Max - pa.Y;
		var roomB = aUp ? runB.Max - pb.Y : pb.Y - runB.Min;
		if (roomA < 0 || roomB < 0)
			return false;

		var ta = Math.Min(need / 2, roomA);
		var tb = Math.Min(need - ta, roomB);
		ta = Math.Min(need - tb, roomA);
		if (ta + tb < need - 0.5)
			return false;

		pa = pa with { Y = pa.Y + (aUp ? -ta : ta) };
		pb = pb with { Y = pb.Y + (aUp ? tb : -tb) };
		return true;
	}

	// Pushes a label out of any entity box it overlaps, moving it the shortest distance
	// (along whichever axis clears the box first) so it lands just outside the nearest edge.
	// Guards against edges that pass near/through an unrelated entity — e.g. a long "skip
	// layer" edge whose bezier midpoint happens to fall inside an intervening entity's box.
	// Self-loop labels intentionally sit at their own entity's edge, so that entity is skipped.
	private static Point PushOutOfEntityBoxes(
		Point pos, double w, double h, IReadOnlyList<PositionedErEntity> entities, string entity1, string entity2)
	{
		const double margin = 6.0;
		var isSelfLoop = string.Equals(entity1, entity2, StringComparison.Ordinal);

		// Resolve against the UNION of every entity currently overlapping the label, not
		// one entity at a time. A per-entity minimal push can bounce a label back and
		// forth forever when it sits above a tightly-packed row of several entities: the
		// cheapest escape from entity A lands inside neighbouring entity B, whose cheapest
		// escape lands right back inside A (this happened for a skip-layer edge's label
		// landing on a row with three adjacent entities — the gaps between them were
		// narrower than the label, so no single-entity horizontal push could ever clear
		// the whole row). Computing one push against the combined bounding box of every
		// overlapping entity always escapes the entire obstacle in a single move.
		for (var guard = 0; guard < 8; guard++)
		{
			var lx = pos.X - (w / 2);
			var rx = pos.X + (w / 2);
			var ty = pos.Y - (h / 2);
			var by = pos.Y + (h / 2);

			var left = double.MaxValue;
			var right = double.MinValue;
			var top = double.MaxValue;
			var bottom = double.MinValue;
			var anyOverlap = false;

			foreach (var e in entities)
			{
				if (isSelfLoop && string.Equals(e.Id, entity1, StringComparison.Ordinal))
					continue;

				var eLeft = e.X - margin;
				var eRight = e.X + e.Width + margin;
				var eTop = e.Y - margin;
				var eBottom = e.Y + e.Height + margin;

				if (rx <= eLeft || lx >= eRight || by <= eTop || ty >= eBottom)
					continue; // no overlap with this entity

				anyOverlap = true;
				if (eLeft < left)
					left = eLeft;
				if (eRight > right)
					right = eRight;
				if (eTop < top)
					top = eTop;
				if (eBottom > bottom)
					bottom = eBottom;
			}

			if (!anyOverlap)
				break;

			var pushLeft = rx - left;
			var pushRight = right - lx;
			var pushUp = by - top;
			var pushDown = bottom - ty;
			var minPush = Math.Min(Math.Min(pushLeft, pushRight), Math.Min(pushUp, pushDown));

			pos = minPush == pushUp ? pos with { Y = pos.Y - pushUp }
				: minPush == pushDown ? pos with { Y = pos.Y + pushDown }
				: minPush == pushLeft ? pos with { X = pos.X - pushLeft }
				: pos with { X = pos.X + pushRight };
		}
		return pos;
	}

	/// <summary>Widest type, name and key-badge group of an entity's attributes, on the shared <see cref="EntityGrid"/>.</summary>
	internal static (double TypeWidth, double NameWidth, double BadgeWidth) MeasureColumns(IReadOnlyList<ErAttributeInfo> attributes)
	{
		var (typeW, nameW, badgeW) = (0.0, 0.0, 0.0);
		foreach (var a in attributes)
		{
			typeW = Math.Max(typeW, EntityGrid.TypeWidth(a.Type));
			nameW = Math.Max(nameW, EntityGrid.NameWidth(a.Name));
			badgeW = Math.Max(badgeW, KeyBadgesWidth(a.Keys));
		}

		return (typeW, nameW, badgeW);
	}

	private const double BadgeSpacing = 4;

	private static double KeyBadgesWidth(IReadOnlyList<ErKeyType> keys)
	{
		var w = 0.0;
		for (var i = 0; i < keys.Count; i++)
			w += EntityGrid.BadgeWidth(keys[i].ToString()) + (i > 0 ? BadgeSpacing : 0);
		return w;
	}

	private static void AppendEntityBox(StringBuilder sb, DesignSystem ds, PositionedErEntity entity)
	{
		// One family per connected cluster: the shared entity frame (band / plain / card per preset), rows on the column grid.
		var family = ds.Cluster(entity.Cluster);
		var (x, y, width, height) = (entity.X, entity.Y, entity.Width, entity.Height);
		var rowHeight = entity.RowHeight;
		var headerHeight = entity.HeaderHeight;

		_ = sb.Append("\n<g class=\"entity\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, entity.Id.AsSpan());
		_ = sb.Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, entity.Label.AsSpan());
		_ = sb.Append("\">\n  ");

		if (entity.Attributes.Count == 0)
		{
			ds.AppendHeaderOnlyEntity(sb, x, y, width, height, entity.Label, family);
			_ = sb.Append("\n</g>");
			return;
		}

		ds.AppendEntityFrame(sb, x, y, width, height, headerHeight, family);
		var attrTop = y + headerHeight;
		for (var i = 0; i < entity.Attributes.Count; i++)
			ds.AppendZebraRow(sb, x, attrTop + (i * rowHeight), width, rowHeight, i, i == entity.Attributes.Count - 1);
		for (var i = 1; i < entity.Attributes.Count; i++)
			ds.AppendEntityRowDivider(sb, x, width, attrTop + (i * rowHeight));

		_ = sb.Append("\n  ");
		ds.AppendEntityName(sb, x, y, width, headerHeight, entity.Label, family);

		var (typeW, _, _) = MeasureColumns(entity.Attributes);
		var nameX = x + EntityGrid.NameOffset(typeW);
		for (var i = 0; i < entity.Attributes.Count; i++)
		{
			var rowY = attrTop + (i * rowHeight) + (rowHeight / 2);
			_ = sb.Append("\n  ");
			AppendAttribute(sb, ds, entity.Attributes[i], x, nameX, rowY, width);
		}

		_ = sb.Append("\n</g>");
	}

	// type (meta, muted mono) | name (body mono) … key badges right-aligned: PK in the accent, UK / FK neutral
	private static void AppendAttribute(StringBuilder sb, DesignSystem ds, ErAttributeInfo attr, double boxX, double nameX, double y, double boxWidth)
	{
		var hasComment = attr.Comment is { Length: > 0 };
		if (hasComment)
		{
			_ = sb.Append("<g><title>");
			MultilineUtils.AppendEscapedXml(sb, attr.Comment.AsSpan());
			_ = sb.Append("</title>");
		}

		if (attr.Type.Length > 0)
			ds.AppendMonoText(sb, attr.Type, boxX + EntityGrid.Pad, y, TypeRole.Meta);
		ds.AppendMonoText(sb, attr.Name, nameX, y, TypeRole.Body);

		var right = boxX + boxWidth - EntityGrid.Pad;
		for (var k = attr.Keys.Count - 1; k >= 0; k--)
		{
			var key = attr.Keys[k];
			var w = ds.AppendBadge(sb, right, y, key.ToString(), key == ErKeyType.PK ? ds.Accent : ds.Neutral, anchor: "end");
			right -= w + BadgeSpacing;
		}

		if (hasComment)
			_ = sb.Append("</g>");
	}

	// 2px clear padding on each side of the label text, plus the border stroke (it straddles the rect edge).
	// Horizontal gets extra because the text-width estimate runs a little short of the real glyph widths.
	internal const double LabelPadX = 16 + 2.25;

	/// <summary>The text-width estimate runs ~6% short of real glyph widths; labels and headers are widened by this factor.</summary>
	internal const double TextWidthCorrection = 1.06;

	/// <summary>Width of the label pill for a measured text width: corrected text plus horizontal padding.</summary>
	internal static double LabelBoxWidth(double measuredTextWidth) => (measuredTextWidth * TextWidthCorrection) + LabelPadX;
	internal const double LabelPadY = 4 + 2.25;

	private const int MaxWaypointsForCurveSimplification = 5;

	private static void AppendRelationshipLine(StringBuilder sb, DesignSystem ds, PositionedErRelationship rel, double cornerRadius)
	{
		if (rel.Points.Count < 2)
			return;

		// solid = identifying (structure), dashed = non-identifying (dependency)
		var dashArray = !rel.Identifying ? $" stroke-dasharray=\"{DesignSystem.DashArray}\"" : "";

		_ = sb.Append("\n<path class=\"er-relationship\" data-entity1=\"");
		MultilineUtils.AppendEscapedAttr(sb, rel.Entity1.AsSpan());
		_ = sb.Append("\" data-entity2=\"");
		MultilineUtils.AppendEscapedAttr(sb, rel.Entity2.AsSpan());
		_ = sb.Append("\" data-cardinality1=\"").Append(rel.Cardinality1.ToLower());
		_ = sb.Append("\" data-cardinality2=\"").Append(rel.Cardinality2.ToLower());
		_ = sb.Append("\" data-identifying=\"").Append(rel.Identifying ? "true" : "false");
		_ = sb.Append('"');
		if (rel.Label.Length > 0)
		{
			_ = sb.Append(" data-label=\"");
			MultilineUtils.AppendEscapedAttr(sb, rel.Label.AsSpan());
			_ = sb.Append('"');
		}
		_ = sb.Append(" d=\"");
		BuildErPath(sb, rel.Points, cornerRadius);
		_ = sb.Append("\" fill=\"none\" stroke=\"").Append(DesignSystem.EdgeColor).Append("\" stroke-width=\"")
			.Append(ds.EdgeWidth).Append('"').Append(dashArray).Append(" />");
	}

	// ER edges are pure orthogonal polylines (port → column → port, as routed by the layout) with
	// uniformly rounded corners — no Bezier S/J-curves, so every edge has the same visual style.
	private static void BuildErPath(StringBuilder sb, IReadOnlyList<Point> points, double cornerRadius)
	{
		if (points.Count < 2)
			return;

		var p = points[0];
		_ = sb.Append('M').Append(p.X).Append(',').Append(p.Y);
		for (var i = 1; i < points.Count - 1; i++)
		{
			var prev = points[i - 1];
			var cur = points[i];
			var next = points[i + 1];
			var inLen = Math.Abs(cur.X - prev.X) + Math.Abs(cur.Y - prev.Y);
			var outLen = Math.Abs(next.X - cur.X) + Math.Abs(next.Y - cur.Y);
			var r = Math.Min(cornerRadius, Math.Min(inLen, outLen) / 2);
			var inDx = Math.Sign(cur.X - prev.X);
			var inDy = Math.Sign(cur.Y - prev.Y);
			var outDx = Math.Sign(next.X - cur.X);
			var outDy = Math.Sign(next.Y - cur.Y);
			if (r < 0.5 || (inDx == outDx && inDy == outDy))
			{
				_ = sb.Append(" L").Append(cur.X).Append(',').Append(cur.Y);
				continue;
			}
			_ = sb.Append(" L").Append(cur.X - (inDx * r)).Append(',').Append(cur.Y - (inDy * r))
				.Append(" Q").Append(cur.X).Append(',').Append(cur.Y)
				.Append(' ').Append(cur.X + (outDx * r)).Append(',').Append(cur.Y + (outDy * r));
		}
		var last = points[^1];
		_ = sb.Append(" L").Append(last.X).Append(',').Append(last.Y);
	}

	private static void AppendRelationshipLabel(StringBuilder sb, DesignSystem ds, PositionedErRelationship rel, Point? resolvedPosition)
	{
		if (rel.Label.Length == 0 || rel.Points.Count < 2)
			return;

		var mid = resolvedPosition ?? ComputeRenderedMidpoint(rel.Points);
		_ = sb.Append("\n<g class=\"edge-label\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, rel.Label.AsSpan());
		_ = sb.Append("\">\n  ");
		ds.AppendEdgeLabel(sb, mid.X, mid.Y, rel.Label);
		_ = sb.Append("\n</g>");
	}

	private static void AppendCardinality(StringBuilder sb, DesignSystem ds, PositionedErRelationship rel)
	{
		if (rel.Points.Count < 2)
			return;

		AppendCrowsFoot(sb, ds, rel.Points[0], rel.Points[1], rel.Cardinality1);
		AppendCrowsFoot(sb, ds, rel.Points[^1], rel.Points[^2], rel.Cardinality2);
	}

	// Cardinality markers sit ON the line, measured from the entity edge, in the line colour and weight so they read as part of it
	// (the accent in presets with thin accent heads); hollow parts knock out to the page:
	//   One      ||   two bars
	//   ZeroOne  o|   bar + circle
	//   Many     |<   crow's foot (apex on the line, prongs fanning to the entity edge) + bar
	//   ZeroMany o<   crow's foot + circle
	// The router keeps the first/last segment straight for at least ErEdgeRouter.Stub, so a marker never straddles a bend.
	private static void AppendCrowsFoot(StringBuilder sb, DesignSystem ds, Point point, Point toward, ErCardinality cardinality)
	{
		var sw = ds.EdgeWidth;
		var color = ds.OwnMarkerColor;
		var join = ds.Spec.Marker == MarkerKind.Chunky ? " stroke-linejoin=\"round\" stroke-linecap=\"round\"" : " stroke-linejoin=\"round\"";
		var dx = toward.X - point.X;
		var dy = toward.Y - point.Y;
		var len = Math.Sqrt((dx * dx) + (dy * dy));
		if (len == 0)
			return;
		var ax = dx / len; // unit vector from the entity edge along the line
		var ay = dy / len;
		var nx = -ay;
		var ny = ax;

		const double halfBar = 6.5;
		const double fan = 7.5;
		const double apex = 13;
		const double circleR = 4.5;

		Point At(double along, double across) => new(point.X + (ax * along) + (nx * across), point.Y + (ay * along) + (ny * across));

		void Bar(double along)
		{
			var (p, q) = (At(along, -halfBar), At(along, halfBar));
			_ = sb.Append("\n<path d=\"M").Append(p.X).Append(',').Append(p.Y).Append(" L").Append(q.X).Append(',').Append(q.Y)
				.Append("\" fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"").Append(sw).Append('"').Append(join).Append(" />");
		}

		void Circle(double along)
		{
			var c = At(along, 0);
			_ = sb.Append("\n<circle cx=\"").Append(c.X).Append("\" cy=\"").Append(c.Y).Append("\" r=\"").Append(circleR)
				.Append("\" fill=\"var(--bg)\" stroke=\"").Append(color).Append("\" stroke-width=\"").Append(sw).Append("\" />");
		}

		void Foot()
		{
			var (tip, left, right) = (At(apex, 0), At(0, -fan), At(0, fan));
			_ = sb.Append("\n<path d=\"M").Append(left.X).Append(',').Append(left.Y).Append(" L").Append(tip.X).Append(',').Append(tip.Y)
				.Append(" L").Append(right.X).Append(',').Append(right.Y)
				.Append("\" fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"").Append(sw).Append('"').Append(join).Append(" />");
		}

		switch (cardinality)
		{
			case ErCardinality.One:
				Bar(9);
				Bar(14);
				break;
			case ErCardinality.ZeroOne:
				Bar(9);
				Circle(19);
				break;
			case ErCardinality.Many:
				Foot();
				Bar(18);
				break;
			case ErCardinality.ZeroMany:
				Foot();
				Circle(22);
				break;
		}
	}

	private static Point ArcMidpoint(IReadOnlyList<Point> points)
	{
		if (points.Count == 0)
			return new Point(0, 0);
		if (points.Count == 1)
			return points[0];

		var totalLen = 0.0;
		for (var i = 1; i < points.Count; i++)
		{
			var dx = points[i].X - points[i - 1].X;
			var dy = points[i].Y - points[i - 1].Y;
			totalLen += Math.Sqrt((dx * dx) + (dy * dy));
		}
		if (totalLen == 0)
			return points[0];

		var halfLen = totalLen / 2;
		var walked = 0.0;
		for (var i = 1; i < points.Count; i++)
		{
			var dx = points[i].X - points[i - 1].X;
			var dy = points[i].Y - points[i - 1].Y;
			var segLen = Math.Sqrt((dx * dx) + (dy * dy));
			if (walked + segLen >= halfLen)
			{
				var t = segLen > 0 ? (halfLen - walked) / segLen : 0;
				return new Point(points[i - 1].X + (dx * t), points[i - 1].Y + (dy * t));
			}
			walked += segLen;
		}

		return points[^1];
	}
}
