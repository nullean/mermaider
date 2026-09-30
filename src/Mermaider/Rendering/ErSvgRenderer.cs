using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class ErSvgRenderer
{
	private static readonly string EntityHeaderAttrs =
		RenderConstants.TextAttrs.NodeLabelBoldCenterFill + "var(--_text)\"";

	private static readonly string RelLabelAttrs =
		RenderConstants.TextAttrs.EdgeLabelCenterFill + "var(--_text-sec)\"";

	private static readonly string AttrFontSize = RenderConstants.FsVar.S;
	private static readonly int AttrFontWeight = RenderConstants.FontWeights.Member;
	private static readonly string KeyFontSize = RenderConstants.FsVar.Xs;
	private static readonly int KeyFontWeight = RenderConstants.FontWeights.KeyBadge;

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
		StyleBlock.AppendStyleBlock(sb, context.Styles.Font, context.Styles.Strict, context.Styles.FontScale, context.Styles.MonoFont);
		_ = sb.Append("\n<defs>\n</defs>\n");

		foreach (var rel in diagram.Relationships)
			AppendRelationshipLine(sb, rel);

		foreach (var entity in diagram.Entities)
			AppendEntityBox(sb, entity);

		foreach (var rel in diagram.Relationships)
			AppendCardinality(sb, rel);

		var labelPositions = ResolveErLabelPositions(diagram.Relationships);
		for (var i = 0; i < diagram.Relationships.Count; i++)
			AppendRelationshipLabel(sb, diagram.Relationships[i], labelPositions[i]);

		_ = sb.Append("\n</svg>");
		return sb;
	}

	// Computes final label positions by starting from Sugiyama positions (or arc midpoints) and
	// pushing overlapping labels apart horizontally so they don't obscure each other.
	private static Point?[] ResolveErLabelPositions(IReadOnlyList<PositionedErRelationship> rels)
	{
		var positions = new Point?[rels.Count];
		var sizes = new (double w, double h)[rels.Count];

		for (var i = 0; i < rels.Count; i++)
		{
			var rel = rels[i];
			if (rel.Label.Length == 0 || rel.Points.Count < 2)
				continue;
			var pos = rel.LabelPosition ?? ArcMidpoint(rel.Points);
			var metrics = TextMetrics.MeasureMultiline(
				rel.Label.AsSpan(),
				RenderConstants.FontSizes.EdgeLabel,
				RenderConstants.FontWeights.EdgeLabel);
			positions[i] = pos;
			sizes[i] = (metrics.Width + 8, metrics.Height + 6);
		}

		const int maxIterations = 8;
		const double labelPad = 4.0;
		for (var iter = 0; iter < maxIterations; iter++)
		{
			var moved = false;
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

					// Push apart horizontally to resolve overlap while keeping labels
					// within their layer corridor (avoids pushing into entity boxes).
					var overlapX = Math.Min(ax1 - bx0, bx1 - ax0);
					var shiftX = (overlapX / 2.0) + 1.0;
					positions[a] = new Point(pa.X - shiftX, pa.Y);
					positions[b] = new Point(pb.X + shiftX, pb.Y);
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

	private static void AppendEntityBox(StringBuilder sb, PositionedErEntity entity)
	{
		var (x, y, width, height) = (entity.X, entity.Y, entity.Width, entity.Height);
		var headerHeight = entity.HeaderHeight;
		var rowHeight = entity.RowHeight;

		// Max type text width across all attributes — used to align the name column
		var typeColWidth = 0.0;
		var keyColWidth = 0.0;
		foreach (var a in entity.Attributes)
		{
			var w = TextMetrics.EstimateMonoTextWidth(a.Type, RenderConstants.FontSizes.Member);
			if (w > typeColWidth)
				typeColWidth = w;
			if (a.Keys.Count > 0)
			{
				var keyText = string.Join(",", a.Keys);
				var kw = TextMetrics.MeasureTextWidth(keyText, RenderConstants.FontSizes.KeyBadge, RenderConstants.FontWeights.KeyBadge);
				if (kw > keyColWidth)
					keyColWidth = kw;
			}
		}

		_ = sb.Append("\n<g class=\"entity\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, entity.Id.AsSpan());
		_ = sb.Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, entity.Label.AsSpan());
		_ = sb.Append("\">\n");

		var r = RenderConstants.Radii.Rectangle;

		if (entity.Attributes.Count == 0)
		{
			// No attributes: plain box — entire box is the header
			_ = sb.Append("  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
				.Append("\" width=\"").Append(width).Append("\" height=\"").Append(height)
				.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
				.Append("\" fill=\"var(--_accent-stroke)\" stroke=\"var(--_node-stroke)\" stroke-width=\"")
				.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n");
			_ = sb.Append("  ");
			MultilineUtils.AppendMultilineText(
				sb, entity.Label, x + (width / 2), y + (height / 2),
				RenderConstants.FontSizes.NodeLabel,
				EntityHeaderAttrs);
			_ = sb.Append('\n');
		}
		else
		{
			// Render order:
			// 1. Background fill (no stroke — border painted last so fills don't obscure it)
			// 2. Header fill
			// 3. Even-row shading fills
			// 4. Separator lines on top of fills
			// 5. Text
			// 6. Outer border stroke-only (covers fill overflow at rounded corners)

			// 1. Background
			_ = sb.Append("  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
				.Append("\" width=\"").Append(width).Append("\" height=\"").Append(height)
				.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
				.Append("\" fill=\"var(--_node-fill)\" />\n");
			// 2. Header fill
			_ = sb.Append("  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
				.Append("\" width=\"").Append(width).Append("\" height=\"").Append(headerHeight)
				.Append("\" fill=\"var(--_accent-stroke)\" />\n");
			// 3. Even-row shading
			var attrTop = y + headerHeight;
			for (var i = 0; i < entity.Attributes.Count; i++)
			{
				if (i % 2 == 0)
				{
					var rowTop = attrTop + (i * rowHeight);
					_ = sb.Append("  <rect x=\"").Append(x).Append("\" y=\"").Append(rowTop)
						.Append("\" width=\"").Append(width).Append("\" height=\"").Append(rowHeight)
						.Append("\" fill=\"var(--_group-hdr)\" />\n");
				}
			}
			// 4. Separators (header + between rows) — all on top of fills
			_ = sb.Append("  <line x1=\"").Append(x).Append("\" y1=\"").Append(attrTop)
				.Append("\" x2=\"").Append(x + width).Append("\" y2=\"").Append(attrTop)
				.Append("\" stroke=\"var(--_node-stroke)\" stroke-width=\"")
				.Append(RenderConstants.StrokeWidths.InnerBox).Append("\" />\n");
			for (var i = 0; i < entity.Attributes.Count - 1; i++)
			{
				var sepY = attrTop + ((i + 1) * rowHeight);
				_ = sb.Append("  <line x1=\"").Append(x).Append("\" y1=\"").Append(sepY)
					.Append("\" x2=\"").Append(x + width).Append("\" y2=\"").Append(sepY)
					.Append("\" stroke=\"var(--_node-stroke)\" stroke-width=\"1\" opacity=\"0.5\" />\n");
			}
			// Vertical column dividers (type | name | key)
			var attrBottom = y + height;
			var typeDivX = x + 8 + typeColWidth + 5;
			_ = sb.Append("  <line x1=\"").Append(typeDivX).Append("\" y1=\"").Append(attrTop)
				.Append("\" x2=\"").Append(typeDivX).Append("\" y2=\"").Append(attrBottom)
				.Append("\" stroke=\"var(--_node-stroke)\" stroke-width=\"1\" />\n");
			if (keyColWidth > 0)
			{
				var keyDivX = x + width - 8 - keyColWidth - 5;
				_ = sb.Append("  <line x1=\"").Append(keyDivX).Append("\" y1=\"").Append(attrTop)
					.Append("\" x2=\"").Append(keyDivX).Append("\" y2=\"").Append(attrBottom)
					.Append("\" stroke=\"var(--_node-stroke)\" stroke-width=\"1\" />\n");
			}
			// 5. Entity name + attribute text
			_ = sb.Append("  ");
			MultilineUtils.AppendMultilineText(
				sb, entity.Label, x + (width / 2), y + (headerHeight / 2),
				RenderConstants.FontSizes.NodeLabel,
				EntityHeaderAttrs);
			_ = sb.Append('\n');
			for (var i = 0; i < entity.Attributes.Count; i++)
			{
				var rowY = attrTop + (i * rowHeight) + (rowHeight / 2);
				_ = sb.Append("  ");
				AppendAttribute(sb, entity.Attributes[i], x, rowY, width, typeColWidth);
				_ = sb.Append('\n');
			}
			// 6. Outer border on top — uniform rounded border over all fills
			_ = sb.Append("  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
				.Append("\" width=\"").Append(width).Append("\" height=\"").Append(height)
				.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
				.Append("\" fill=\"none\" stroke=\"var(--_node-stroke)\" stroke-width=\"")
				.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n");
		}

		_ = sb.Append("</g>");
	}

	private static void AppendAttribute(StringBuilder sb, ErAttributeInfo attr, double boxX, double y, double boxWidth, double typeColWidth)
	{
		var hasComment = attr.Comment is { Length: > 0 };
		if (hasComment)
		{
			_ = sb.Append("<g><title>");
			MultilineUtils.AppendEscapedXml(sb, attr.Comment.AsSpan());
			_ = sb.Append("</title>");
		}

		// Type: left-aligned
		var typeX = boxX + 8;
		_ = sb.Append("<text class=\"mono\" x=\"").Append(typeX).Append("\" y=\"").Append(y)
			.Append("\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(AttrFontSize)
			.Append("\" font-weight=\"").Append(AttrFontWeight)
			.Append("\"><tspan fill=\"var(--_text-sec)\">");
		MultilineUtils.AppendEscapedXml(sb, attr.Type.AsSpan());
		_ = sb.Append("</tspan></text>");

		// Name: left-aligned, second column aligned to max type width across the entity
		var nameX = boxX + 8 + typeColWidth + 10;
		_ = sb.Append("<text class=\"mono\" x=\"").Append(nameX).Append("\" y=\"").Append(y)
			.Append("\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(AttrFontSize)
			.Append("\" font-weight=\"").Append(AttrFontWeight)
			.Append("\"><tspan fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, attr.Name.AsSpan());
		_ = sb.Append("</tspan></text>");

		// Key: third column, right-aligned inside the box
		if (attr.Keys.Count > 0)
		{
			var keyText = string.Join(",", attr.Keys);
			var keyX = boxX + boxWidth - 8;
			_ = sb.Append("<text x=\"").Append(keyX).Append("\" y=\"").Append(y)
				.Append("\" text-anchor=\"end\" dy=\"").Append(RenderConstants.TextBaselineShift)
				.Append("\" font-size=\"").Append(KeyFontSize)
				.Append("\" font-weight=\"").Append(KeyFontWeight)
				.Append("\" fill=\"var(--_accent-text)\">").Append(keyText).Append("</text>");
		}

		if (hasComment)
			_ = sb.Append("</g>");
	}

	private const double CornerRadius = 6;

	private static void AppendRelationshipLine(StringBuilder sb, PositionedErRelationship rel)
	{
		if (rel.Points.Count < 2)
			return;

		var dashArray = !rel.Identifying ? " stroke-dasharray=\"6 4\"" : "";

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
		BuildErPath(sb, rel.Points);
		_ = sb.Append("\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.Connector).Append('"').Append(dashArray).Append(" />");
	}

	// ER-specific path builder: converts cross-column paths to smooth S-curves (or J-curves for
	// side-exit connections) regardless of intermediate waypoint complexity. This ensures all
	// ER edges from the same entity use a consistent curvy style rather than a mix of S-curves
	// and rectilinear staircases.
	private static void BuildErPath(StringBuilder sb, IReadOnlyList<Point> points)
	{
		if (points.Count < 2)
			return;

		if (points.Count >= 3)
		{
			var p0 = points[0];
			var p1 = points[1];
			var pN = points[^1];
			var pN1 = points[^2];

			var dx0 = Math.Abs(p1.X - p0.X);
			var dy0 = Math.Abs(p1.Y - p0.Y);
			var dxN = Math.Abs(pN.X - pN1.X);
			var dyN = Math.Abs(pN.Y - pN1.Y);

			// Cross-column vertical exit and entry (TD/BT): S-curve M p0 C p0.x,midY pN.x,midY pN
			if (dx0 < 4.0 && dxN < 4.0 && Math.Abs(p0.X - pN.X) > 4.0)
			{
				var ym = (p0.Y + pN.Y) / 2.0;
				_ = sb.Append('M').Append(p0.X).Append(',').Append(p0.Y)
					.Append(" C").Append(p0.X).Append(',').Append(ym)
					.Append(' ').Append(pN.X).Append(',').Append(ym)
					.Append(' ').Append(pN.X).Append(',').Append(pN.Y);
				return;
			}

			// Horizontal exit then vertical entry (TD side-exit, e.g. StrictTopDownFanout): J-curve
			if (dy0 < 4.0 && dxN < 4.0 && dx0 > 4.0)
			{
				var ym = (p0.Y + pN.Y) / 2.0;
				_ = sb.Append('M').Append(p0.X).Append(',').Append(p0.Y)
					.Append(" C").Append(pN.X).Append(',').Append(p0.Y)
					.Append(' ').Append(pN.X).Append(',').Append(ym)
					.Append(' ').Append(pN.X).Append(',').Append(pN.Y);
				return;
			}

			// Vertical exit then horizontal entry (LR side-exit): J-curve rotated
			if (dx0 < 4.0 && dyN < 4.0 && dy0 > 4.0)
			{
				var xm = (p0.X + pN.X) / 2.0;
				_ = sb.Append('M').Append(p0.X).Append(',').Append(p0.Y)
					.Append(" C").Append(p0.X).Append(',').Append(pN.Y)
					.Append(' ').Append(xm).Append(',').Append(pN.Y)
					.Append(' ').Append(pN.X).Append(',').Append(pN.Y);
				return;
			}

			// Cross-row horizontal exit and entry (LR/RL): S-curve
			if (dy0 < 4.0 && dyN < 4.0 && Math.Abs(p0.Y - pN.Y) > 4.0)
			{
				var xm = (p0.X + pN.X) / 2.0;
				_ = sb.Append('M').Append(p0.X).Append(',').Append(p0.Y)
					.Append(" C").Append(xm).Append(',').Append(p0.Y)
					.Append(' ').Append(xm).Append(',').Append(pN.Y)
					.Append(' ').Append(pN.X).Append(',').Append(pN.Y);
				return;
			}
		}

		SvgRenderer.BuildRoundedPath(sb, points, CornerRadius);
	}

	private static void AppendRelationshipLabel(StringBuilder sb, PositionedErRelationship rel, Point? resolvedPosition)
	{
		if (rel.Label.Length == 0 || rel.Points.Count < 2)
			return;

		var mid = resolvedPosition ?? rel.LabelPosition ?? ArcMidpoint(rel.Points);
		var metrics = TextMetrics.MeasureMultiline(
			rel.Label.AsSpan(),
			RenderConstants.FontSizes.EdgeLabel,
			RenderConstants.FontWeights.EdgeLabel);

		var bgW = metrics.Width + 8;
		var bgH = metrics.Height + 6;

		var lr = RenderConstants.Radii.EdgeLabel;
		_ = sb.Append("\n<rect x=\"").Append(mid.X - (bgW / 2)).Append("\" y=\"").Append(mid.Y - (bgH / 2))
			.Append("\" width=\"").Append(bgW).Append("\" height=\"").Append(bgH)
			.Append("\" rx=\"").Append(lr).Append("\" ry=\"").Append(lr)
			.Append("\" fill=\"var(--bg)\" stroke=\"var(--_inner-stroke)\" stroke-width=\"0.5\" />\n");
		MultilineUtils.AppendMultilineText(
			sb, rel.Label, mid.X, mid.Y,
			RenderConstants.FontSizes.EdgeLabel,
			RelLabelAttrs);
	}

	private static void AppendCardinality(StringBuilder sb, PositionedErRelationship rel)
	{
		if (rel.Points.Count < 2)
			return;

		var p1 = rel.Points[0];
		var p2 = rel.Points[1];
		AppendCrowsFoot(sb, p1, p2, rel.Cardinality1);

		var pN = rel.Points[^1];
		var pN1 = rel.Points[^2];
		AppendCrowsFoot(sb, pN, pN1, rel.Cardinality2);
	}

	private static void AppendCrowsFoot(StringBuilder sb, Point point, Point toward, ErCardinality cardinality)
	{
		var sw = RenderConstants.StrokeWidths.Connector + 0.25;

		var dx = point.X - toward.X;
		var dy = point.Y - toward.Y;
		var len = Math.Sqrt((dx * dx) + (dy * dy));
		if (len == 0)
			return;
		var ux = dx / len;
		var uy = dy / len;
		var px = -uy;
		var py = ux;

		var tipX = point.X - (ux * 4);
		var tipY = point.Y - (uy * 4);
		var backX = point.X - (ux * 16);
		var backY = point.Y - (uy * 16);

		var hasOneLine = cardinality is ErCardinality.One or ErCardinality.ZeroOne;
		var hasCrowsFoot = cardinality is ErCardinality.Many or ErCardinality.ZeroMany;
		var hasCircle = cardinality is ErCardinality.ZeroOne or ErCardinality.ZeroMany;

		if (hasOneLine)
		{
			const double halfW = 6;
			_ = sb.Append("\n<line x1=\"").Append(tipX + (px * halfW)).Append("\" y1=\"").Append(tipY + (py * halfW))
				.Append("\" x2=\"").Append(tipX - (px * halfW)).Append("\" y2=\"").Append(tipY - (py * halfW))
				.Append("\" stroke=\"var(--_line)\" stroke-width=\"").Append(sw).Append("\" />");
			var line2X = tipX - (ux * 4);
			var line2Y = tipY - (uy * 4);
			_ = sb.Append("\n<line x1=\"").Append(line2X + (px * halfW)).Append("\" y1=\"").Append(line2Y + (py * halfW))
				.Append("\" x2=\"").Append(line2X - (px * halfW)).Append("\" y2=\"").Append(line2Y - (py * halfW))
				.Append("\" stroke=\"var(--_line)\" stroke-width=\"").Append(sw).Append("\" />");
		}

		if (hasCrowsFoot)
		{
			const double fanW = 7;
			_ = sb.Append("\n<line x1=\"").Append(tipX + (px * fanW)).Append("\" y1=\"").Append(tipY + (py * fanW))
				.Append("\" x2=\"").Append(backX).Append("\" y2=\"").Append(backY)
				.Append("\" stroke=\"var(--_line)\" stroke-width=\"").Append(sw).Append("\" />");
			_ = sb.Append("\n<line x1=\"").Append(tipX).Append("\" y1=\"").Append(tipY)
				.Append("\" x2=\"").Append(backX).Append("\" y2=\"").Append(backY)
				.Append("\" stroke=\"var(--_line)\" stroke-width=\"").Append(sw).Append("\" />");
			_ = sb.Append("\n<line x1=\"").Append(tipX - (px * fanW)).Append("\" y1=\"").Append(tipY - (py * fanW))
				.Append("\" x2=\"").Append(backX).Append("\" y2=\"").Append(backY)
				.Append("\" stroke=\"var(--_line)\" stroke-width=\"").Append(sw).Append("\" />");
		}

		if (hasCircle)
		{
			var circleOffset = hasCrowsFoot ? 20 : 12;
			var circleX = point.X - (ux * circleOffset);
			var circleY = point.Y - (uy * circleOffset);
			_ = sb.Append("\n<circle cx=\"").Append(circleX).Append("\" cy=\"").Append(circleY)
				.Append("\" r=\"4\" fill=\"var(--bg)\" stroke=\"var(--_line)\" stroke-width=\"").Append(sw).Append("\" />");
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
