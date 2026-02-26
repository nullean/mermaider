using System.Text;
using Mermaid.Models;
using Mermaid.Text;
using Mermaid.Theming;
using Microsoft.Extensions.ObjectPool;

namespace Mermaid.Rendering;

internal static class ErSvgRenderer
{
	private static readonly ObjectPool<StringBuilder> s_sbPool =
		new DefaultObjectPoolProvider().CreateStringBuilderPool(initialCapacity: 4096, maximumRetainedCapacity: 64 * 1024);

	private const int AttrFontSize = 11;
	private const int AttrFontWeight = 400;
	private const int KeyFontSize = 9;
	private const int KeyFontWeight = 600;

	internal static string Render(PositionedErDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null)
	{
		var sb = s_sbPool.Get();
		try
		{
			StyleBlock.AppendSvgOpenTag(sb, diagram.Width, diagram.Height, colors, transparent);
			StyleBlock.AppendStyleBlock(sb, font, strict: strict);
			sb.Append("\n<defs>\n</defs>\n");

			foreach (var rel in diagram.Relationships)
				AppendRelationshipLine(sb, rel);

			foreach (var entity in diagram.Entities)
				AppendEntityBox(sb, entity);

			foreach (var rel in diagram.Relationships)
				AppendCardinality(sb, rel);

			foreach (var rel in diagram.Relationships)
				AppendRelationshipLabel(sb, rel);

			sb.Append("\n</svg>");
			return sb.ToString();
		}
		finally
		{
			sb.Clear();
			s_sbPool.Return(sb);
		}
	}

	private static void AppendEntityBox(StringBuilder sb, PositionedErEntity entity)
	{
		var (x, y, width, height) = (entity.X, entity.Y, entity.Width, entity.Height);
		var headerHeight = entity.HeaderHeight;
		var rowHeight = entity.RowHeight;

		sb.Append("\n<g class=\"entity\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, entity.Id.AsSpan());
		sb.Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, entity.Label.AsSpan());
		sb.Append("\">\n");

		var r = RenderConstants.Radii.Rectangle;
		sb.Append("  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
			.Append("\" width=\"").Append(width).Append("\" height=\"").Append(height)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"var(--_node-fill)\" stroke=\"var(--_node-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n");

		sb.Append("  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
			.Append("\" width=\"").Append(width).Append("\" height=\"").Append(headerHeight)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"var(--_group-hdr)\" stroke=\"var(--_node-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n");

		sb.Append("  ");
		MultilineUtils.AppendMultilineText(
			sb, entity.Label, x + width / 2, y + headerHeight / 2,
			RenderConstants.FontSizes.NodeLabel,
			$"text-anchor=\"middle\" font-size=\"{RenderConstants.FontSizes.NodeLabel}\" font-weight=\"700\" fill=\"var(--_text)\"");
		sb.Append('\n');

		var attrTop = y + headerHeight;
		sb.Append("  <line x1=\"").Append(x).Append("\" y1=\"").Append(attrTop)
			.Append("\" x2=\"").Append(x + width).Append("\" y2=\"").Append(attrTop)
			.Append("\" stroke=\"var(--_node-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.InnerBox).Append("\" />\n");

		if (entity.Attributes.Count == 0)
		{
			sb.Append("  <text x=\"").Append(x + width / 2).Append("\" y=\"").Append(attrTop + rowHeight / 2)
				.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
				.Append("\" font-size=\"").Append(AttrFontSize)
				.Append("\" fill=\"var(--_text-faint)\" font-style=\"italic\">(no attributes)</text>\n");
		}
		else
		{
			for (var i = 0; i < entity.Attributes.Count; i++)
			{
				var rowY = attrTop + i * rowHeight + rowHeight / 2;
				sb.Append("  ");
				AppendAttribute(sb, entity.Attributes[i], x, rowY, width);
				sb.Append('\n');
			}
		}

		sb.Append("</g>");
	}

	private static void AppendAttribute(StringBuilder sb, ErAttribute attr, double boxX, double y, double boxWidth)
	{
		var hasComment = attr.Comment is { Length: > 0 };
		if (hasComment)
		{
			sb.Append("<g><title>");
			MultilineUtils.AppendEscapedXml(sb, attr.Comment!.AsSpan());
			sb.Append("</title>");
		}

		var keyWidth = 0.0;
		if (attr.Keys.Count > 0)
		{
			var keyText = string.Join(",", attr.Keys);
			keyWidth = TextMetrics.MeasureTextWidth(keyText, KeyFontSize, KeyFontWeight) + 8;
			sb.Append("<rect x=\"").Append(boxX + 6).Append("\" y=\"").Append(y - 7)
				.Append("\" width=\"").Append(keyWidth).Append("\" height=\"14\" rx=\"7\" ry=\"7\" fill=\"var(--_key-badge)\" />");
			sb.Append("<text x=\"").Append(boxX + 6 + keyWidth / 2).Append("\" y=\"").Append(y)
				.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
				.Append("\" font-size=\"").Append(KeyFontSize)
				.Append("\" font-weight=\"").Append(KeyFontWeight)
				.Append("\" fill=\"var(--_text-sec)\">").Append(string.Join(",", attr.Keys)).Append("</text>");
		}

		var typeX = boxX + 8 + (keyWidth > 0 ? keyWidth + 6 : 0);
		sb.Append("<text x=\"").Append(typeX).Append("\" y=\"").Append(y)
			.Append("\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(AttrFontSize)
			.Append("\" font-weight=\"").Append(AttrFontWeight)
			.Append("\"><tspan fill=\"var(--_text-muted)\">");
		MultilineUtils.AppendEscapedXml(sb, attr.Type.AsSpan());
		sb.Append("</tspan></text>");

		var nameX = boxX + boxWidth - 8;
		sb.Append("<text x=\"").Append(nameX).Append("\" y=\"").Append(y)
			.Append("\" text-anchor=\"end\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(AttrFontSize)
			.Append("\" font-weight=\"").Append(AttrFontWeight)
			.Append("\"><tspan fill=\"var(--_text-sec)\">");
		MultilineUtils.AppendEscapedXml(sb, attr.Name.AsSpan());
		sb.Append("</tspan></text>");

		if (hasComment)
			sb.Append("</g>");
	}

	private static void AppendRelationshipLine(StringBuilder sb, PositionedErRelationship rel)
	{
		if (rel.Points.Count < 2) return;

		var dashArray = !rel.Identifying ? " stroke-dasharray=\"6 4\"" : "";

		sb.Append("\n<polyline class=\"er-relationship\" data-entity1=\"");
		MultilineUtils.AppendEscapedAttr(sb, rel.Entity1.AsSpan());
		sb.Append("\" data-entity2=\"");
		MultilineUtils.AppendEscapedAttr(sb, rel.Entity2.AsSpan());
		sb.Append("\" data-cardinality1=\"").Append(rel.Cardinality1.ToString().ToLowerInvariant());
		sb.Append("\" data-cardinality2=\"").Append(rel.Cardinality2.ToString().ToLowerInvariant());
		sb.Append("\" data-identifying=\"").Append(rel.Identifying ? "true" : "false");
		sb.Append('"');
		if (rel.Label.Length > 0)
		{
			sb.Append(" data-label=\"");
			MultilineUtils.AppendEscapedAttr(sb, rel.Label.AsSpan());
			sb.Append('"');
		}
		sb.Append(" points=\"");
		for (var i = 0; i < rel.Points.Count; i++)
		{
			if (i > 0) sb.Append(' ');
			sb.Append(rel.Points[i].X).Append(',').Append(rel.Points[i].Y);
		}
		sb.Append("\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.Connector).Append('"').Append(dashArray).Append(" />");
	}

	private static void AppendRelationshipLabel(StringBuilder sb, PositionedErRelationship rel)
	{
		if (rel.Label.Length == 0 || rel.Points.Count < 2) return;

		var mid = ArcMidpoint(rel.Points);
		var metrics = TextMetrics.MeasureMultiline(
			rel.Label.AsSpan(),
			RenderConstants.FontSizes.EdgeLabel,
			RenderConstants.FontWeights.EdgeLabel);

		var bgW = metrics.Width + 8;
		var bgH = metrics.Height + 6;

		var lr = RenderConstants.Radii.EdgeLabel;
		sb.Append("\n<rect x=\"").Append(mid.X - bgW / 2).Append("\" y=\"").Append(mid.Y - bgH / 2)
			.Append("\" width=\"").Append(bgW).Append("\" height=\"").Append(bgH)
			.Append("\" rx=\"").Append(lr).Append("\" ry=\"").Append(lr)
			.Append("\" fill=\"var(--bg)\" stroke=\"var(--_inner-stroke)\" stroke-width=\"0.5\" />\n");
		MultilineUtils.AppendMultilineText(
			sb, rel.Label, mid.X, mid.Y,
			RenderConstants.FontSizes.EdgeLabel,
			$"text-anchor=\"middle\" font-size=\"{RenderConstants.FontSizes.EdgeLabel}\" font-weight=\"{RenderConstants.FontWeights.EdgeLabel}\" fill=\"var(--_text-muted)\"");
	}

	private static void AppendCardinality(StringBuilder sb, PositionedErRelationship rel)
	{
		if (rel.Points.Count < 2) return;

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
		var len = Math.Sqrt(dx * dx + dy * dy);
		if (len == 0) return;
		var ux = dx / len;
		var uy = dy / len;
		var px = -uy;
		var py = ux;

		var tipX = point.X - ux * 4;
		var tipY = point.Y - uy * 4;
		var backX = point.X - ux * 16;
		var backY = point.Y - uy * 16;

		var hasOneLine = cardinality is ErCardinality.One or ErCardinality.ZeroOne;
		var hasCrowsFoot = cardinality is ErCardinality.Many or ErCardinality.ZeroMany;
		var hasCircle = cardinality is ErCardinality.ZeroOne or ErCardinality.ZeroMany;

		if (hasOneLine)
		{
			const double halfW = 6;
			sb.Append("\n<line x1=\"").Append(tipX + px * halfW).Append("\" y1=\"").Append(tipY + py * halfW)
				.Append("\" x2=\"").Append(tipX - px * halfW).Append("\" y2=\"").Append(tipY - py * halfW)
				.Append("\" stroke=\"var(--_line)\" stroke-width=\"").Append(sw).Append("\" />");
			var line2X = tipX - ux * 4;
			var line2Y = tipY - uy * 4;
			sb.Append("\n<line x1=\"").Append(line2X + px * halfW).Append("\" y1=\"").Append(line2Y + py * halfW)
				.Append("\" x2=\"").Append(line2X - px * halfW).Append("\" y2=\"").Append(line2Y - py * halfW)
				.Append("\" stroke=\"var(--_line)\" stroke-width=\"").Append(sw).Append("\" />");
		}

		if (hasCrowsFoot)
		{
			const double fanW = 7;
			sb.Append("\n<line x1=\"").Append(tipX + px * fanW).Append("\" y1=\"").Append(tipY + py * fanW)
				.Append("\" x2=\"").Append(backX).Append("\" y2=\"").Append(backY)
				.Append("\" stroke=\"var(--_line)\" stroke-width=\"").Append(sw).Append("\" />");
			sb.Append("\n<line x1=\"").Append(tipX).Append("\" y1=\"").Append(tipY)
				.Append("\" x2=\"").Append(backX).Append("\" y2=\"").Append(backY)
				.Append("\" stroke=\"var(--_line)\" stroke-width=\"").Append(sw).Append("\" />");
			sb.Append("\n<line x1=\"").Append(tipX - px * fanW).Append("\" y1=\"").Append(tipY - py * fanW)
				.Append("\" x2=\"").Append(backX).Append("\" y2=\"").Append(backY)
				.Append("\" stroke=\"var(--_line)\" stroke-width=\"").Append(sw).Append("\" />");
		}

		if (hasCircle)
		{
			var circleOffset = hasCrowsFoot ? 20 : 12;
			var circleX = point.X - ux * circleOffset;
			var circleY = point.Y - uy * circleOffset;
			sb.Append("\n<circle cx=\"").Append(circleX).Append("\" cy=\"").Append(circleY)
				.Append("\" r=\"4\" fill=\"var(--bg)\" stroke=\"var(--_line)\" stroke-width=\"").Append(sw).Append("\" />");
		}
	}

	private static Point ArcMidpoint(IReadOnlyList<Point> points)
	{
		if (points.Count == 0) return new Point(0, 0);
		if (points.Count == 1) return points[0];

		var totalLen = 0.0;
		for (var i = 1; i < points.Count; i++)
		{
			var dx = points[i].X - points[i - 1].X;
			var dy = points[i].Y - points[i - 1].Y;
			totalLen += Math.Sqrt(dx * dx + dy * dy);
		}
		if (totalLen == 0) return points[0];

		var halfLen = totalLen / 2;
		var walked = 0.0;
		for (var i = 1; i < points.Count; i++)
		{
			var dx = points[i].X - points[i - 1].X;
			var dy = points[i].Y - points[i - 1].Y;
			var segLen = Math.Sqrt(dx * dx + dy * dy);
			if (walked + segLen >= halfLen)
			{
				var t = segLen > 0 ? (halfLen - walked) / segLen : 0;
				return new Point(points[i - 1].X + dx * t, points[i - 1].Y + dy * t);
			}
			walked += segLen;
		}

		return points[^1];
	}
}
