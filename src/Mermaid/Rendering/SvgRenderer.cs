using System.Text;
using Mermaid.Models;
using Mermaid.Text;
using Mermaid.Theming;
using Microsoft.Extensions.ObjectPool;

namespace Mermaid.Rendering;

/// <summary>
/// Converts a <see cref="PositionedGraph"/> to an SVG string via pooled StringBuilder.
/// Pure string concatenation, no DOM.
/// </summary>
internal static class SvgRenderer
{
	private static readonly ObjectPool<StringBuilder> s_sbPool =
		new DefaultObjectPoolProvider().CreateStringBuilderPool(initialCapacity: 4096, maximumRetainedCapacity: 64 * 1024);

	internal static string Render(PositionedGraph graph, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null)
	{
		var sb = s_sbPool.Get();
		try
		{
			StyleBlock.AppendSvgOpenTag(sb, graph.Width, graph.Height, colors, transparent);
			StyleBlock.AppendStyleBlock(sb, font, false, strict);
			AppendArrowDefs(sb);

		foreach (var group in graph.Groups)
			AppendGroupBody(sb, group);

		foreach (var edge in graph.Edges)
			AppendEdge(sb, edge);

		foreach (var group in graph.Groups)
			AppendGroupHeader(sb, group, font);

		foreach (var edge in graph.Edges)
		{
			if (edge.Label is not null)
				AppendEdgeLabel(sb, edge, font);
		}

		foreach (var node in graph.Nodes)
			AppendNode(sb, node, font, strict);

		sb.Append("\n</svg>");
			return sb.ToString();
		}
		finally
		{
			sb.Clear();
			s_sbPool.Return(sb);
		}
	}

	// ========================================================================
	// Arrow marker defs
	// ========================================================================

	private static void AppendArrowDefs(StringBuilder sb)
	{
		var s = RenderConstants.ArrowHead.Size;
		var w = s;
		var h = s;

		sb.Append("\n<defs>\n");
		sb.Append("  <marker id=\"arrowhead\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"").Append(w)
			.Append("\" refY=\"").Append(h / 2.0)
			.Append("\" orient=\"auto\">\n");
		sb.Append("    <polygon points=\"0 0, ").Append(w).Append(' ').Append(h / 2.0)
			.Append(", 0 ").Append(h)
			.Append("\" fill=\"var(--_arrow)\" stroke=\"var(--_arrow)\" stroke-width=\"0.75\" stroke-linejoin=\"round\" />\n");
		sb.Append("  </marker>\n");

		sb.Append("  <marker id=\"arrowhead-start\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"1\" refY=\"").Append(h / 2.0)
			.Append("\" orient=\"auto-start-reverse\">\n");
		sb.Append("    <polygon points=\"").Append(w).Append(" 0, 0 ").Append(h / 2.0)
			.Append(", ").Append(w).Append(' ').Append(h)
			.Append("\" fill=\"var(--_arrow)\" stroke=\"var(--_arrow)\" stroke-width=\"0.75\" stroke-linejoin=\"round\" />\n");
		sb.Append("  </marker>\n");
		sb.Append("</defs>\n");
	}

	// ========================================================================
	// Group rendering
	// ========================================================================

	private static void AppendGroupBody(StringBuilder sb, PositionedGroup group)
	{
		var r = RenderConstants.Radii.Group;

		sb.Append("\n<g class=\"subgraph\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, group.Id.AsSpan());
		sb.Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, group.Label.AsSpan());
		sb.Append("\">\n");

		sb.Append("  <rect x=\"").Append(group.X).Append("\" y=\"").Append(group.Y)
			.Append("\" width=\"").Append(group.Width).Append("\" height=\"").Append(group.Height)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"var(--_group-fill)\" stroke=\"var(--_group-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n");

		sb.Append("</g>\n");

		foreach (var child in group.Children)
			AppendGroupBody(sb, child);
	}

	private static void AppendGroupHeader(StringBuilder sb, PositionedGroup group, string font)
	{
		var headerHeight = RenderConstants.FontSizes.GroupHeader + 16;
		var r = RenderConstants.Radii.Group;

		sb.Append("  <rect x=\"").Append(group.X).Append("\" y=\"").Append(group.Y)
			.Append("\" width=\"").Append(group.Width).Append("\" height=\"").Append(headerHeight)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"var(--_group-hdr)\" stroke=\"var(--_group-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n");

		sb.Append("  ");
		MultilineUtils.AppendMultilineText(
			sb, group.Label,
			group.X + 12, group.Y + headerHeight / 2.0,
			RenderConstants.FontSizes.GroupHeader,
			$"font-size=\"{RenderConstants.FontSizes.GroupHeader}\" font-weight=\"{RenderConstants.FontWeights.GroupHeader}\" fill=\"var(--_text-sec)\"");
		sb.Append('\n');

		foreach (var child in group.Children)
			AppendGroupHeader(sb, child, font);
	}

	// ========================================================================
	// Edge rendering
	// ========================================================================

	private const double CornerRadius = 6;

	private static void AppendEdge(StringBuilder sb, PositionedEdge edge)
	{
		if (edge.Points.Count < 2)
			return;

		var dashArray = edge.Style == EdgeStyle.Dotted ? " stroke-dasharray=\"4 4\"" : "";
		var strokeWidth = edge.Style == EdgeStyle.Thick
			? RenderConstants.StrokeWidths.Connector + 1
			: RenderConstants.StrokeWidths.Connector;

		sb.Append("\n<path class=\"edge\" data-from=\"");
		MultilineUtils.AppendEscapedAttr(sb, edge.Source.AsSpan());
		sb.Append("\" data-to=\"");
		MultilineUtils.AppendEscapedAttr(sb, edge.Target.AsSpan());
		sb.Append("\" data-style=\"").Append(edge.Style.ToString().ToLowerInvariant());
		sb.Append("\" data-arrow-start=\"").Append(edge.HasArrowStart ? "true" : "false");
		sb.Append("\" data-arrow-end=\"").Append(edge.HasArrowEnd ? "true" : "false");
		sb.Append('"');

		if (edge.Label is not null)
		{
			sb.Append(" data-label=\"");
			MultilineUtils.AppendEscapedAttr(sb, edge.Label.AsSpan());
			sb.Append('"');
		}

		sb.Append(" d=\"");
		BuildRoundedPath(sb, edge.Points, CornerRadius);
		sb.Append("\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"")
			.Append(strokeWidth).Append('"').Append(dashArray);

		if (edge.HasArrowEnd)
			sb.Append(" marker-end=\"url(#arrowhead)\"");
		if (edge.HasArrowStart)
			sb.Append(" marker-start=\"url(#arrowhead-start)\"");

		sb.Append(" />");
	}

	internal static void BuildRoundedPath(StringBuilder sb, IReadOnlyList<Point> points, double radius)
	{
		if (points.Count < 2) return;

		sb.Append('M').Append(points[0].X).Append(',').Append(points[0].Y);

		if (points.Count == 2)
		{
			sb.Append(" L").Append(points[1].X).Append(',').Append(points[1].Y);
			return;
		}

		for (var i = 1; i < points.Count - 1; i++)
		{
			var prev = points[i - 1];
			var curr = points[i];
			var next = points[i + 1];

			var dx1 = curr.X - prev.X;
			var dy1 = curr.Y - prev.Y;
			var len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);

			var dx2 = next.X - curr.X;
			var dy2 = next.Y - curr.Y;
			var len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);

			if (len1 < 0.1 || len2 < 0.1)
			{
				sb.Append(" L").Append(curr.X).Append(',').Append(curr.Y);
				continue;
			}

			var r = Math.Min(radius, Math.Min(len1 / 2, len2 / 2));

			var startX = curr.X - dx1 / len1 * r;
			var startY = curr.Y - dy1 / len1 * r;
			var endX = curr.X + dx2 / len2 * r;
			var endY = curr.Y + dy2 / len2 * r;

			sb.Append(" L").Append(startX).Append(',').Append(startY);
			sb.Append(" Q").Append(curr.X).Append(',').Append(curr.Y)
				.Append(' ').Append(endX).Append(',').Append(endY);
		}

		sb.Append(" L").Append(points[^1].X).Append(',').Append(points[^1].Y);
	}

	private static void AppendEdgeLabel(StringBuilder sb, PositionedEdge edge, string font)
	{
		var mid = edge.LabelPosition ?? EdgeMidpoint(edge.Points);
		var label = edge.Label!;
		var padding = 8.0;

		var metrics = TextMetrics.MeasureMultiline(
			label.AsSpan(),
			RenderConstants.FontSizes.EdgeLabel,
			RenderConstants.FontWeights.EdgeLabel);

		sb.Append("\n<g class=\"edge-label\" data-from=\"");
		MultilineUtils.AppendEscapedAttr(sb, edge.Source.AsSpan());
		sb.Append("\" data-to=\"");
		MultilineUtils.AppendEscapedAttr(sb, edge.Target.AsSpan());
		sb.Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, label.AsSpan());
		sb.Append("\">\n  ");

		var lr = RenderConstants.Radii.EdgeLabel;
		MultilineUtils.AppendMultilineTextWithBackground(
			sb, label, mid.X, mid.Y,
			metrics.Width, metrics.Height,
			RenderConstants.FontSizes.EdgeLabel,
			padding,
			$"text-anchor=\"middle\" font-size=\"{RenderConstants.FontSizes.EdgeLabel}\" font-weight=\"{RenderConstants.FontWeights.EdgeLabel}\" fill=\"var(--_text-sec)\"",
			$"rx=\"{lr}\" ry=\"{lr}\" fill=\"var(--bg)\" stroke=\"var(--_inner-stroke)\" stroke-width=\"1\"");

		sb.Append("\n</g>");
	}

	private static Point EdgeMidpoint(IReadOnlyList<Point> points)
	{
		if (points.Count == 0) return new Point(0, 0);
		if (points.Count == 1) return points[0];

		var totalLength = 0.0;
		for (var i = 1; i < points.Count; i++)
			totalLength += Dist(points[i - 1], points[i]);

		var remaining = totalLength / 2;
		for (var i = 1; i < points.Count; i++)
		{
			var segLen = Dist(points[i - 1], points[i]);
			if (remaining <= segLen)
			{
				var t = remaining / segLen;
				return new Point(
					points[i - 1].X + t * (points[i].X - points[i - 1].X),
					points[i - 1].Y + t * (points[i].Y - points[i - 1].Y));
			}
			remaining -= segLen;
		}

		return points[^1];
	}

	private static double Dist(Point a, Point b) =>
		Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

	// ========================================================================
	// Node rendering
	// ========================================================================

	private static void AppendNode(StringBuilder sb, PositionedNode node, string font, StrictModeOptions? strict = null)
	{
		sb.Append("\n<g class=\"node");
		if (node.CssClassName is not null)
		{
			var isExternal = strict?.AllowedClasses
				.Any(c => c.Name == node.CssClassName && c.IsExternal) ?? false;
			sb.Append(isExternal ? " " : " cls-").Append(node.CssClassName);
		}
		sb.Append("\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, node.Id.AsSpan());
		sb.Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, node.Label.AsSpan());
		sb.Append("\" data-shape=\"").Append(node.Shape.ToString().ToLowerInvariant()).Append("\">\n  ");

		AppendNodeShape(sb, node);
		sb.Append("\n  ");
		AppendNodeLabel(sb, node);
		sb.Append("\n</g>");
	}

	private static void AppendNodeShape(StringBuilder sb, PositionedNode node)
	{
		var (x, y, w, h) = (node.X, node.Y, node.Width, node.Height);
		var fill = node.InlineStyle?.GetValueOrDefault("fill") ?? "var(--_node-fill)";
		var stroke = node.InlineStyle?.GetValueOrDefault("stroke") ?? "var(--_node-stroke)";
		var sw = node.InlineStyle?.GetValueOrDefault("stroke-width") ?? RenderConstants.StrokeWidths.InnerBox.ToString();

		switch (node.Shape)
		{
			case NodeShape.Rectangle:
				AppendRect(sb, x, y, w, h, RenderConstants.Radii.Rectangle.ToString(), fill, stroke, sw);
				break;
			case NodeShape.Rounded:
				AppendRect(sb, x, y, w, h, RenderConstants.Radii.Rounded.ToString(), fill, stroke, sw);
				break;
			case NodeShape.Stadium:
				AppendRect(sb, x, y, w, h, (h / 2).ToString(), fill, stroke, sw);
				break;
			case NodeShape.Diamond:
				AppendDiamond(sb, x, y, w, h, fill, stroke, sw);
				break;
			case NodeShape.Circle:
				AppendCircle(sb, x, y, w, h, fill, stroke, sw);
				break;
			case NodeShape.DoubleCircle:
				AppendDoubleCircle(sb, x, y, w, h, fill, stroke, sw);
				break;
			case NodeShape.Subroutine:
				AppendSubroutine(sb, x, y, w, h, fill, stroke, sw);
				break;
			case NodeShape.Hexagon:
				AppendHexagon(sb, x, y, w, h, fill, stroke, sw);
				break;
			case NodeShape.Cylinder:
				AppendCylinder(sb, x, y, w, h, fill, stroke, sw);
				break;
			case NodeShape.Asymmetric:
				AppendAsymmetric(sb, x, y, w, h, fill, stroke, sw);
				break;
			case NodeShape.Trapezoid:
				AppendTrapezoid(sb, x, y, w, h, fill, stroke, sw);
				break;
			case NodeShape.TrapezoidAlt:
				AppendTrapezoidAlt(sb, x, y, w, h, fill, stroke, sw);
				break;
			case NodeShape.StateStart:
				AppendStateStart(sb, x, y, w, h);
				break;
			case NodeShape.StateEnd:
				AppendStateEnd(sb, x, y, w, h);
				break;
		}
	}

	private static void AppendRect(StringBuilder sb, double x, double y, double w, double h, string rx, string fill, string stroke, string sw) =>
		sb.Append("<rect x=\"").Append(x).Append("\" y=\"").Append(y)
			.Append("\" width=\"").Append(w).Append("\" height=\"").Append(h)
			.Append("\" rx=\"").Append(rx).Append("\" ry=\"").Append(rx)
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(sw).Append("\" />");

	private static void AppendDiamond(StringBuilder sb, double x, double y, double w, double h, string fill, string stroke, string sw)
	{
		var cx = x + w / 2;
		var cy = y + h / 2;
		var hw = w / 2;
		var hh = h / 2;
		sb.Append("<polygon points=\"")
			.Append(cx).Append(',').Append(cy - hh).Append(' ')
			.Append(cx + hw).Append(',').Append(cy).Append(' ')
			.Append(cx).Append(',').Append(cy + hh).Append(' ')
			.Append(cx - hw).Append(',').Append(cy)
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(sw).Append("\" />");
	}

	private static void AppendCircle(StringBuilder sb, double x, double y, double w, double h, string fill, string stroke, string sw)
	{
		var cx = x + w / 2;
		var cy = y + h / 2;
		var r = Math.Min(w, h) / 2;
		sb.Append("<circle cx=\"").Append(cx).Append("\" cy=\"").Append(cy)
			.Append("\" r=\"").Append(r)
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(sw).Append("\" />");
	}

	private static void AppendDoubleCircle(StringBuilder sb, double x, double y, double w, double h, string fill, string stroke, string sw)
	{
		var cx = x + w / 2;
		var cy = y + h / 2;
		var outerR = Math.Min(w, h) / 2;
		var innerR = outerR - 5;
		sb.Append("<circle cx=\"").Append(cx).Append("\" cy=\"").Append(cy)
			.Append("\" r=\"").Append(outerR)
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(sw).Append("\" />\n");
		sb.Append("<circle cx=\"").Append(cx).Append("\" cy=\"").Append(cy)
			.Append("\" r=\"").Append(innerR)
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(sw).Append("\" />");
	}

	private static void AppendSubroutine(StringBuilder sb, double x, double y, double w, double h, string fill, string stroke, string sw)
	{
		const int inset = 8;
		AppendRect(sb, x, y, w, h, RenderConstants.Radii.Rectangle.ToString(), fill, stroke, sw);
		sb.Append("\n<line x1=\"").Append(x + inset).Append("\" y1=\"").Append(y)
			.Append("\" x2=\"").Append(x + inset).Append("\" y2=\"").Append(y + h)
			.Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"").Append(sw).Append("\" />\n");
		sb.Append("<line x1=\"").Append(x + w - inset).Append("\" y1=\"").Append(y)
			.Append("\" x2=\"").Append(x + w - inset).Append("\" y2=\"").Append(y + h)
			.Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"").Append(sw).Append("\" />");
	}

	private static void AppendHexagon(StringBuilder sb, double x, double y, double w, double h, string fill, string stroke, string sw)
	{
		var inset = h / 4;
		sb.Append("<polygon points=\"")
			.Append(x + inset).Append(',').Append(y).Append(' ')
			.Append(x + w - inset).Append(',').Append(y).Append(' ')
			.Append(x + w).Append(',').Append(y + h / 2).Append(' ')
			.Append(x + w - inset).Append(',').Append(y + h).Append(' ')
			.Append(x + inset).Append(',').Append(y + h).Append(' ')
			.Append(x).Append(',').Append(y + h / 2)
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(sw).Append("\" />");
	}

	private static void AppendCylinder(StringBuilder sb, double x, double y, double w, double h, string fill, string stroke, string sw)
	{
		const int ry = 7;
		var cx = x + w / 2;
		var bodyTop = y + ry;
		var bodyH = h - 2 * ry;

		sb.Append("<rect x=\"").Append(x).Append("\" y=\"").Append(bodyTop)
			.Append("\" width=\"").Append(w).Append("\" height=\"").Append(bodyH)
			.Append("\" fill=\"").Append(fill).Append("\" stroke=\"none\" />\n");
		sb.Append("<line x1=\"").Append(x).Append("\" y1=\"").Append(bodyTop)
			.Append("\" x2=\"").Append(x).Append("\" y2=\"").Append(bodyTop + bodyH)
			.Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"").Append(sw).Append("\" />\n");
		sb.Append("<line x1=\"").Append(x + w).Append("\" y1=\"").Append(bodyTop)
			.Append("\" x2=\"").Append(x + w).Append("\" y2=\"").Append(bodyTop + bodyH)
			.Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"").Append(sw).Append("\" />\n");
		sb.Append("<ellipse cx=\"").Append(cx).Append("\" cy=\"").Append(y + h - ry)
			.Append("\" rx=\"").Append(w / 2).Append("\" ry=\"").Append(ry)
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(sw).Append("\" />\n");
		sb.Append("<ellipse cx=\"").Append(cx).Append("\" cy=\"").Append(bodyTop)
			.Append("\" rx=\"").Append(w / 2).Append("\" ry=\"").Append(ry)
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(sw).Append("\" />");
	}

	private static void AppendAsymmetric(StringBuilder sb, double x, double y, double w, double h, string fill, string stroke, string sw)
	{
		const int indent = 12;
		sb.Append("<polygon points=\"")
			.Append(x + indent).Append(',').Append(y).Append(' ')
			.Append(x + w).Append(',').Append(y).Append(' ')
			.Append(x + w).Append(',').Append(y + h).Append(' ')
			.Append(x + indent).Append(',').Append(y + h).Append(' ')
			.Append(x).Append(',').Append(y + h / 2)
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(sw).Append("\" />");
	}

	private static void AppendTrapezoid(StringBuilder sb, double x, double y, double w, double h, string fill, string stroke, string sw)
	{
		var inset = w * 0.15;
		sb.Append("<polygon points=\"")
			.Append(x + inset).Append(',').Append(y).Append(' ')
			.Append(x + w - inset).Append(',').Append(y).Append(' ')
			.Append(x + w).Append(',').Append(y + h).Append(' ')
			.Append(x).Append(',').Append(y + h)
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(sw).Append("\" />");
	}

	private static void AppendTrapezoidAlt(StringBuilder sb, double x, double y, double w, double h, string fill, string stroke, string sw)
	{
		var inset = w * 0.15;
		sb.Append("<polygon points=\"")
			.Append(x).Append(',').Append(y).Append(' ')
			.Append(x + w).Append(',').Append(y).Append(' ')
			.Append(x + w - inset).Append(',').Append(y + h).Append(' ')
			.Append(x + inset).Append(',').Append(y + h)
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(sw).Append("\" />");
	}

	private static void AppendStateStart(StringBuilder sb, double x, double y, double w, double h)
	{
		var cx = x + w / 2;
		var cy = y + h / 2;
		var r = Math.Min(w, h) / 2 - 2;
		sb.Append("<circle cx=\"").Append(cx).Append("\" cy=\"").Append(cy)
			.Append("\" r=\"").Append(r)
			.Append("\" fill=\"var(--_text)\" stroke=\"none\" />");
	}

	private static void AppendStateEnd(StringBuilder sb, double x, double y, double w, double h)
	{
		var cx = x + w / 2;
		var cy = y + h / 2;
		var outerR = Math.Min(w, h) / 2 - 2;
		var innerR = outerR - 4;
		sb.Append("<circle cx=\"").Append(cx).Append("\" cy=\"").Append(cy)
			.Append("\" r=\"").Append(outerR)
			.Append("\" fill=\"none\" stroke=\"var(--_text)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.InnerBox * 2).Append("\" />\n");
		sb.Append("<circle cx=\"").Append(cx).Append("\" cy=\"").Append(cy)
			.Append("\" r=\"").Append(innerR)
			.Append("\" fill=\"var(--_text)\" stroke=\"none\" />");
	}

	private static void AppendNodeLabel(StringBuilder sb, PositionedNode node)
	{
		if (node.Shape is NodeShape.StateStart or NodeShape.StateEnd && string.IsNullOrEmpty(node.Label))
			return;

		var cx = node.X + node.Width / 2;
		var cy = node.Y + node.Height / 2;
		var textColor = node.InlineStyle?.GetValueOrDefault("color") ?? "var(--_text)";

		MultilineUtils.AppendMultilineText(
			sb, node.Label, cx, cy,
			RenderConstants.FontSizes.NodeLabel,
			$"text-anchor=\"middle\" font-size=\"{RenderConstants.FontSizes.NodeLabel}\" font-weight=\"{RenderConstants.FontWeights.NodeLabel}\" fill=\"{textColor}\"");
	}
}
