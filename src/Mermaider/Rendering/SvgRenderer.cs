using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;
using static Mermaider.Rendering.RenderConstants;

namespace Mermaider.Rendering;

/// <summary>
/// Converts a <see cref="PositionedGraph"/> to an SVG string via pooled StringBuilder.
/// Pure string concatenation, no DOM.
/// </summary>
internal static class SvgRenderer
{
	private static readonly string EdgeLabelBgAttrs =
		$"rx=\"{Radii.EdgeLabel}\" ry=\"{Radii.EdgeLabel}\" fill=\"var(--bg)\" stroke=\"var(--_inner-stroke)\" stroke-width=\"1\"";

	private static readonly string EdgeLabelSecAttrs = TextAttrs.EdgeLabelCenterFill + "var(--_text-sec)\"";

	private static readonly string GroupHeaderAttrs = TextAttrs.GroupHeaderFill + "var(--_text-sec)\"";

	internal static string Render(PositionedGraph graph, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null)
	{
		var sb = RenderToBuilder(graph, colors, font, transparent, strict);
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

	internal static StringBuilder RenderToBuilder(PositionedGraph graph, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		StyleBlock.AppendSvgOpenTag(sb, graph.Width, graph.Height, colors, transparent);
		StyleBlock.AppendStyleBlock(sb, font, strict);
		AppendArrowDefs(sb);

		foreach (var group in graph.Groups)
			AppendGroupBody(sb, group);

		foreach (var edge in graph.Edges)
		{
			if (edge.Style != EdgeStyle.Invisible)
				AppendEdge(sb, edge);
		}

		foreach (var group in graph.Groups)
			AppendGroupHeader(sb, group, font);

		foreach (var edge in graph.Edges)
		{
			if (edge.Style != EdgeStyle.Invisible && edge.Label is not null)
				AppendEdgeLabel(sb, edge);
		}

		foreach (var node in graph.Nodes)
			AppendNode(sb, node, strict);

		foreach (var note in graph.Notes)
			AppendNote(sb, note);

		_ = sb.Append("\n</svg>");
		return sb;
	}

	// ========================================================================
	// Arrow marker defs
	// ========================================================================

	private static void AppendArrowDefs(StringBuilder sb)
	{
		var s = ArrowHead.Size;
		var w = s;
		var h = s;

		_ = sb.Append("\n<defs>\n");
		_ = sb.Append("  <marker id=\"arrowhead\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"").Append(w)
			.Append("\" refY=\"").Append(h / 2.0)
			.Append("\" orient=\"auto\">\n");
		_ = sb.Append("    <polygon points=\"0 0, ").Append(w).Append(' ').Append(h / 2.0)
			.Append(", 0 ").Append(h)
			.Append("\" fill=\"var(--_arrow)\" stroke=\"var(--_arrow)\" stroke-width=\"0.75\" stroke-linejoin=\"round\" />\n");
		_ = sb.Append("  </marker>\n");

		_ = sb.Append("  <marker id=\"arrowhead-start\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"1\" refY=\"").Append(h / 2.0)
			.Append("\" orient=\"auto-start-reverse\">\n");
		_ = sb.Append("    <polygon points=\"").Append(w).Append(" 0, 0 ").Append(h / 2.0)
			.Append(", ").Append(w).Append(' ').Append(h)
			.Append("\" fill=\"var(--_arrow)\" stroke=\"var(--_arrow)\" stroke-width=\"0.75\" stroke-linejoin=\"round\" />\n");
		_ = sb.Append("  </marker>\n");
		_ = sb.Append("</defs>\n");
	}

	// ========================================================================
	// Group rendering
	// ========================================================================

	private static void AppendGroupBody(StringBuilder sb, PositionedGroup group)
	{
		var r = Radii.Group;

		_ = sb.Append("\n<g class=\"subgraph\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, group.Id.AsSpan());
		_ = sb.Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, group.Label.AsSpan());
		_ = sb.Append("\">\n");

		_ = sb.Append("  <rect x=\"").Append(group.X).Append("\" y=\"").Append(group.Y)
			.Append("\" width=\"").Append(group.Width).Append("\" height=\"").Append(group.Height)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"var(--_group-fill)\" stroke=\"var(--_group-stroke)\" stroke-width=\"")
			.Append(StrokeWidths.OuterBox).Append("\" />\n");

		_ = sb.Append("</g>\n");

		foreach (var child in group.Children)
			AppendGroupBody(sb, child);
	}

	private static void AppendGroupHeader(StringBuilder sb, PositionedGroup group, string font)
	{
		var headerHeight = FontSizes.GroupHeader + 16;
		var r = Radii.Group;

		_ = sb.Append("  <rect x=\"").Append(group.X).Append("\" y=\"").Append(group.Y)
			.Append("\" width=\"").Append(group.Width).Append("\" height=\"").Append(headerHeight)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"var(--_group-hdr)\" stroke=\"var(--_group-stroke)\" stroke-width=\"")
			.Append(StrokeWidths.OuterBox).Append("\" />\n");

		_ = sb.Append("  ");
		MultilineUtils.AppendMultilineText(
			sb, group.Label,
			group.X + 12, group.Y + (headerHeight / 2.0),
			FontSizes.GroupHeader,
			GroupHeaderAttrs);
		_ = sb.Append('\n');

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
			? StrokeWidths.Connector + 1
			: StrokeWidths.Connector;

		_ = sb.Append("\n<path class=\"edge\" data-from=\"");
		MultilineUtils.AppendEscapedAttr(sb, edge.Source.AsSpan());
		_ = sb.Append("\" data-to=\"");
		MultilineUtils.AppendEscapedAttr(sb, edge.Target.AsSpan());
		_ = sb.Append("\" data-style=\"").Append(edge.Style.ToLower());
		_ = sb.Append("\" data-arrow-start=\"").Append(edge.HasArrowStart ? "true" : "false");
		_ = sb.Append("\" data-arrow-end=\"").Append(edge.HasArrowEnd ? "true" : "false");
		_ = sb.Append('"');

		if (edge.Label is not null)
		{
			_ = sb.Append(" data-label=\"");
			MultilineUtils.AppendEscapedAttr(sb, edge.Label.AsSpan());
			_ = sb.Append('"');
		}

		_ = sb.Append(" d=\"");
		BuildRoundedPath(sb, edge.Points, CornerRadius);
		_ = sb.Append("\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"")
			.Append(strokeWidth).Append('"').Append(dashArray);

		if (edge.HasArrowEnd)
			_ = sb.Append(" marker-end=\"url(#arrowhead)\"");
		if (edge.HasArrowStart)
			_ = sb.Append(" marker-start=\"url(#arrowhead-start)\"");

		_ = sb.Append(" />");
	}

	internal static void BuildRoundedPath(StringBuilder sb, IReadOnlyList<Point> points, double radius)
	{
		if (points.Count < 2)
			return;

		_ = sb.Append('M').Append(points[0].X).Append(',').Append(points[0].Y);

		if (points.Count == 2)
		{
			_ = sb.Append(" L").Append(points[1].X).Append(',').Append(points[1].Y);
			return;
		}

		for (var i = 1; i < points.Count - 1; i++)
		{
			var prev = points[i - 1];
			var curr = points[i];
			var next = points[i + 1];

			var dx1 = curr.X - prev.X;
			var dy1 = curr.Y - prev.Y;
			var len1 = Math.Sqrt((dx1 * dx1) + (dy1 * dy1));

			var dx2 = next.X - curr.X;
			var dy2 = next.Y - curr.Y;
			var len2 = Math.Sqrt((dx2 * dx2) + (dy2 * dy2));

			if (len1 < 0.1 || len2 < 0.1)
			{
				_ = sb.Append(" L").Append(curr.X).Append(',').Append(curr.Y);
				continue;
			}

			var r = Math.Min(radius, Math.Min(len1 / 2, len2 / 2));

			var startX = curr.X - (dx1 / len1 * r);
			var startY = curr.Y - (dy1 / len1 * r);
			var endX = curr.X + (dx2 / len2 * r);
			var endY = curr.Y + (dy2 / len2 * r);

			_ = sb.Append(" L").Append(startX).Append(',').Append(startY);
			_ = sb.Append(" Q").Append(curr.X).Append(',').Append(curr.Y)
				.Append(' ').Append(endX).Append(',').Append(endY);
		}

		_ = sb.Append(" L").Append(points[^1].X).Append(',').Append(points[^1].Y);
	}

	private static void AppendEdgeLabel(StringBuilder sb, PositionedEdge edge)
	{
		var mid = edge.LabelPosition ?? EdgeMidpoint(edge.Points);
		var label = edge.Label!;
		var padding = 8.0;

		var metrics = TextMetrics.MeasureMultiline(
			label.AsSpan(),
			FontSizes.EdgeLabel,
			FontWeights.EdgeLabel);

		_ = sb.Append("\n<g class=\"edge-label\" data-from=\"");
		MultilineUtils.AppendEscapedAttr(sb, edge.Source.AsSpan());
		_ = sb.Append("\" data-to=\"");
		MultilineUtils.AppendEscapedAttr(sb, edge.Target.AsSpan());
		_ = sb.Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, label.AsSpan());
		_ = sb.Append("\">\n  ");

		MultilineUtils.AppendMultilineTextWithBackground(
			sb, label, mid.X, mid.Y,
			metrics.Width, metrics.Height,
			FontSizes.EdgeLabel,
			padding,
			EdgeLabelSecAttrs,
			EdgeLabelBgAttrs);

		_ = sb.Append("\n</g>");
	}

	private static Point EdgeMidpoint(IReadOnlyList<Point> points)
	{
		if (points.Count == 0)
			return new Point(0, 0);
		if (points.Count == 1)
			return points[0];

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
					points[i - 1].X + (t * (points[i].X - points[i - 1].X)),
					points[i - 1].Y + (t * (points[i].Y - points[i - 1].Y)));
			}
			remaining -= segLen;
		}

		return points[^1];
	}

	private static double Dist(Point a, Point b) =>
		Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));

	// ========================================================================
	// Node rendering
	// ========================================================================

	private static void AppendNode(StringBuilder sb, PositionedNode node, StrictModeOptions? strict = null)
	{
		_ = sb.Append("\n<g class=\"node");
		if (node.CssClassName is not null)
		{
			var isExternal = strict?.AllowedClasses
				.Any(c => c.Name == node.CssClassName && c.IsExternal) ?? false;
			_ = sb.Append(isExternal ? " " : " cls-").Append(node.CssClassName);
		}
		_ = sb.Append("\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, node.Id.AsSpan());
		_ = sb.Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, node.Label.AsSpan());
		_ = sb.Append("\" data-shape=\"").Append(node.Shape.ToLower()).Append("\">\n  ");

		AppendNodeShape(sb, node);
		_ = sb.Append("\n  ");
		AppendNodeLabel(sb, node);
		_ = sb.Append("\n</g>");
	}

	private static void AppendNodeShape(StringBuilder sb, PositionedNode node)
	{
		var (x, y, w, h) = (node.X, node.Y, node.Width, node.Height);
		var fill = node.InlineStyle?.GetValueOrDefault("fill") ?? "var(--_node-fill)";
		var stroke = node.InlineStyle?.GetValueOrDefault("stroke") ?? "var(--_node-stroke)";
		var sw = node.InlineStyle?.GetValueOrDefault("stroke-width") ?? StrokeWidths.InnerBox.ToString(CultureInfo.InvariantCulture);

		switch (node.Shape)
		{
			case NodeShape.Rectangle:
				AppendRect(sb, x, y, w, h, Radii.Rectangle.ToString(CultureInfo.InvariantCulture), fill, stroke, sw);
				break;
			case NodeShape.Rounded:
				AppendRect(sb, x, y, w, h, Radii.Rounded.ToString(CultureInfo.InvariantCulture), fill, stroke, sw);
				break;
			case NodeShape.Stadium:
				AppendRect(sb, x, y, w, h, (h / 2).ToString(CultureInfo.InvariantCulture), fill, stroke, sw);
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
			case NodeShape.ForkJoin:
				AppendForkJoin(sb, x, y, w, h);
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
		var cx = x + (w / 2);
		var cy = y + (h / 2);
		var hw = w / 2;
		var hh = h / 2;
		_ = sb.Append("<polygon points=\"")
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
		var cx = x + (w / 2);
		var cy = y + (h / 2);
		var r = Math.Min(w, h) / 2;
		_ = sb.Append("<circle cx=\"").Append(cx).Append("\" cy=\"").Append(cy)
			.Append("\" r=\"").Append(r)
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(sw).Append("\" />");
	}

	private static void AppendDoubleCircle(StringBuilder sb, double x, double y, double w, double h, string fill, string stroke, string sw)
	{
		var cx = x + (w / 2);
		var cy = y + (h / 2);
		var outerR = Math.Min(w, h) / 2;
		var innerR = outerR - 5;
		_ = sb.Append("<circle cx=\"").Append(cx).Append("\" cy=\"").Append(cy)
			.Append("\" r=\"").Append(outerR)
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(sw).Append("\" />\n");
		_ = sb.Append("<circle cx=\"").Append(cx).Append("\" cy=\"").Append(cy)
			.Append("\" r=\"").Append(innerR)
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(sw).Append("\" />");
	}

	private static void AppendSubroutine(StringBuilder sb, double x, double y, double w, double h, string fill, string stroke, string sw)
	{
		const int inset = 8;
		AppendRect(sb, x, y, w, h, rx: Radii.Rectangle.ToString(CultureInfo.InvariantCulture), fill, stroke, sw);
		_ = sb.Append("\n<line x1=\"").Append(x + inset).Append("\" y1=\"").Append(y)
			.Append("\" x2=\"").Append(x + inset).Append("\" y2=\"").Append(y + h)
			.Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"").Append(sw).Append("\" />\n");
		_ = sb.Append("<line x1=\"").Append(x + w - inset).Append("\" y1=\"").Append(y)
			.Append("\" x2=\"").Append(x + w - inset).Append("\" y2=\"").Append(y + h)
			.Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"").Append(sw).Append("\" />");
	}

	private static void AppendHexagon(StringBuilder sb, double x, double y, double w, double h, string fill, string stroke, string sw)
	{
		var inset = h / 4;
		_ = sb.Append("<polygon points=\"")
			.Append(x + inset).Append(',').Append(y).Append(' ')
			.Append(x + w - inset).Append(',').Append(y).Append(' ')
			.Append(x + w).Append(',').Append(y + (h / 2)).Append(' ')
			.Append(x + w - inset).Append(',').Append(y + h).Append(' ')
			.Append(x + inset).Append(',').Append(y + h).Append(' ')
			.Append(x).Append(',').Append(y + (h / 2))
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(sw).Append("\" />");
	}

	private static void AppendCylinder(StringBuilder sb, double x, double y, double w, double h, string fill, string stroke, string sw)
	{
		const int ry = 7;
		var cx = x + (w / 2);
		var bodyTop = y + ry;
		var bodyH = h - (2 * ry);

		_ = sb.Append("<rect x=\"").Append(x).Append("\" y=\"").Append(bodyTop)
			.Append("\" width=\"").Append(w).Append("\" height=\"").Append(bodyH)
			.Append("\" fill=\"").Append(fill).Append("\" stroke=\"none\" />\n");
		_ = sb.Append("<line x1=\"").Append(x).Append("\" y1=\"").Append(bodyTop)
			.Append("\" x2=\"").Append(x).Append("\" y2=\"").Append(bodyTop + bodyH)
			.Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"").Append(sw).Append("\" />\n");
		_ = sb.Append("<line x1=\"").Append(x + w).Append("\" y1=\"").Append(bodyTop)
			.Append("\" x2=\"").Append(x + w).Append("\" y2=\"").Append(bodyTop + bodyH)
			.Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"").Append(sw).Append("\" />\n");
		_ = sb.Append("<ellipse cx=\"").Append(cx).Append("\" cy=\"").Append(y + h - ry)
			.Append("\" rx=\"").Append(w / 2).Append("\" ry=\"").Append(ry)
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(sw).Append("\" />\n");
		_ = sb.Append("<ellipse cx=\"").Append(cx).Append("\" cy=\"").Append(bodyTop)
			.Append("\" rx=\"").Append(w / 2).Append("\" ry=\"").Append(ry)
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(sw).Append("\" />");
	}

	private static void AppendAsymmetric(StringBuilder sb, double x, double y, double w, double h, string fill, string stroke, string sw)
	{
		const int indent = 12;
		_ = sb.Append("<polygon points=\"")
			.Append(x + indent).Append(',').Append(y).Append(' ')
			.Append(x + w).Append(',').Append(y).Append(' ')
			.Append(x + w).Append(',').Append(y + h).Append(' ')
			.Append(x + indent).Append(',').Append(y + h).Append(' ')
			.Append(x).Append(',').Append(y + (h / 2))
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(sw).Append("\" />");
	}

	private static void AppendTrapezoid(StringBuilder sb, double x, double y, double w, double h, string fill, string stroke, string sw)
	{
		var inset = w * 0.15;
		_ = sb.Append("<polygon points=\"")
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
		_ = sb.Append("<polygon points=\"")
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
		var cx = x + (w / 2);
		var cy = y + (h / 2);
		var r = (Math.Min(w, h) / 2) - 2;
		_ = sb.Append("<circle cx=\"").Append(cx).Append("\" cy=\"").Append(cy)
			.Append("\" r=\"").Append(r)
			.Append("\" fill=\"var(--_text)\" stroke=\"none\" />");
	}

	private static void AppendStateEnd(StringBuilder sb, double x, double y, double w, double h)
	{
		var cx = x + (w / 2);
		var cy = y + (h / 2);
		var outerR = (Math.Min(w, h) / 2) - 2;
		var innerR = outerR - 4;
		_ = sb.Append("<circle cx=\"").Append(cx).Append("\" cy=\"").Append(cy)
			.Append("\" r=\"").Append(outerR)
			.Append("\" fill=\"none\" stroke=\"var(--_text)\" stroke-width=\"")
			.Append(StrokeWidths.InnerBox * 2).Append("\" />\n");
		_ = sb.Append("<circle cx=\"").Append(cx).Append("\" cy=\"").Append(cy)
			.Append("\" r=\"").Append(innerR)
			.Append("\" fill=\"var(--_text)\" stroke=\"none\" />");
	}

	private static readonly string NoteTextAttrs = TextAttrs.EdgeLabelCenterFill + "var(--_accent-text)\"";

	private static void AppendNote(StringBuilder sb, PositionedGraphNote note)
	{
		_ = sb.Append("\n<g class=\"note\">\n");
		_ = sb.Append("  <rect x=\"").Append(note.X).Append("\" y=\"").Append(note.Y)
			.Append("\" width=\"").Append(note.Width).Append("\" height=\"").Append(note.Height)
			.Append("\" rx=\"6\" ry=\"6\"")
			.Append(" fill=\"var(--_accent-fill)\" stroke=\"var(--_accent-stroke)\" stroke-width=\"")
			.Append(StrokeWidths.InnerBox).Append("\" />\n  ");

		MultilineUtils.AppendMultilineText(
			sb, note.Text,
			note.X + (note.Width / 2), note.Y + (note.Height / 2),
			FontSizes.EdgeLabel,
			NoteTextAttrs);
		_ = sb.Append("\n</g>");
	}

	private static void AppendForkJoin(StringBuilder sb, double x, double y, double w, double h)
	{
		_ = sb.Append("<rect x=\"").Append(x).Append("\" y=\"").Append(y)
			.Append("\" width=\"").Append(w).Append("\" height=\"").Append(h)
			.Append("\" rx=\"2\" ry=\"2\" fill=\"var(--_text)\" stroke=\"none\" />");
	}

	private static void AppendNodeLabel(StringBuilder sb, PositionedNode node)
	{
		if (node.Shape is NodeShape.StateStart or NodeShape.StateEnd or NodeShape.ForkJoin && string.IsNullOrEmpty(node.Label))
			return;

		var cx = node.X + (node.Width / 2);
		var cy = node.Y + (node.Height / 2);
		var textColor = node.InlineStyle?.GetValueOrDefault("color") ?? "var(--_text)";

		if (node.IsMarkdown)
			AppendMarkdownLabel(sb, node.Label, cx, cy, FontSizes.NodeLabel, textColor);
		else
			MultilineUtils.AppendMultilineText(
				sb, node.Label, cx, cy,
				FontSizes.NodeLabel,
				TextAttrs.NodeLabelCenterFill + textColor + "\"");
	}

	private static void AppendMarkdownLabel(StringBuilder sb, string label, double cx, double cy, int fontSize, string fill)
	{
		var lines = label.Split('\n');
		var lineHeight = fontSize * 1.3;
		var totalHeight = lines.Length * lineHeight;
		var startY = cy - (totalHeight / 2) + (lineHeight / 2);

		_ = sb.Append("<text text-anchor=\"middle\" font-size=\"").Append(fontSize)
			.Append("\" fill=\"").Append(fill).Append("\">");

		for (var li = 0; li < lines.Length; li++)
		{
			var line = lines[li];
			var y = startY + (li * lineHeight);

			_ = sb.Append("<tspan x=\"").Append(cx).Append("\" y=\"").Append(y)
				.Append("\" dy=\"").Append(RenderConstants.TextBaselineShift).Append("\">");

			var pos = 0;
			while (pos < line.Length)
			{
				if (pos + 1 < line.Length && line[pos] == '*' && line[pos + 1] == '*')
				{
					var end = line.IndexOf("**", pos + 2, StringComparison.Ordinal);
					if (end > 0)
					{
						_ = sb.Append("<tspan font-weight=\"bold\">");
						MultilineUtils.AppendEscapedXml(sb, line.AsSpan(pos + 2, end - pos - 2));
						_ = sb.Append("</tspan>");
						pos = end + 2;
						continue;
					}
				}

				if (line[pos] == '*')
				{
					var end = line.IndexOf('*', pos + 1);
					if (end > 0)
					{
						_ = sb.Append("<tspan font-style=\"italic\">");
						MultilineUtils.AppendEscapedXml(sb, line.AsSpan(pos + 1, end - pos - 1));
						_ = sb.Append("</tspan>");
						pos = end + 1;
						continue;
					}
				}

				var next = line.IndexOf('*', pos);
				var segment = next < 0 ? line.AsSpan(pos) : line.AsSpan(pos, next - pos);
				MultilineUtils.AppendEscapedXml(sb, segment);
				pos += segment.Length;
			}

			_ = sb.Append("</tspan>");
		}

		_ = sb.Append("</text>");
	}
}
