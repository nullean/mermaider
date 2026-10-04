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
	private static readonly string EdgeLabelAttrs = TextAttrs.EdgeLabelCenterFill + "var(--_text)\"";

	/// <summary>
	/// Reads a user-supplied inline style value (from <c>style</c>/<c>classDef</c>/<c>linkStyle</c>)
	/// and escapes it for safe emission into a double-quoted SVG attribute. The output sanitizer
	/// subsequently applies the same explicit value allowlists used for every SVG attribute.
	/// </summary>
	private static string? InlineStyleValue(IReadOnlyDictionary<string, string>? style, string key) =>
		style?.GetValueOrDefault(key) is { } value ? MultilineUtils.EscapeAttr(value) : null;

	private static readonly string GroupHeaderAttrs = TextAttrs.GroupHeaderFill + "var(--_text-sec)\"";

	internal static string Render(PositionedGraph graph, SvgRenderContext context)
	{
		var sb = RenderToBuilder(graph, context);
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

	internal static StringBuilder RenderToBuilder(PositionedGraph graph, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		StyleBlock.AppendSvgOpenTag(sb, graph.Width, graph.Height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType, graph.MinX);
		StyleBlock.AppendStyleBlock(sb, context.Styles.Font, context.Styles.Strict, context.Styles.FontScale, context.Styles.MonoFont);
		AppendArrowDefs(sb);

		var palette = context.DiagramType == DiagramType.Flowchart ? BuildFlowPalette(graph, context.Styles.Colors) : null;

		foreach (var group in graph.Groups)
			AppendGroupBody(sb, group, palette, 0);

		foreach (var edge in graph.Edges)
		{
			if (edge.Style != EdgeStyle.Invisible)
				AppendEdge(sb, edge, context.EdgeRadius);
		}

		foreach (var group in graph.Groups)
			AppendGroupHeader(sb, group, palette, 0);

		foreach (var edge in graph.Edges)
		{
			if (edge.Style != EdgeStyle.Invisible && edge.Label is not null)
				AppendEdgeLabel(sb, edge);
		}

		foreach (var node in graph.Nodes)
			AppendNode(sb, node, context.Styles.Strict, palette);

		foreach (var note in graph.Notes)
			AppendNote(sb, note);

		_ = sb.Append("\n</svg>");
		return sb;
	}

	/// <summary>Cluster colours for flowcharts: each connected cluster of nodes (edges + shared subgraph) gets one palette colour.</summary>
	private sealed class FlowPalette(DiagramColors colors, Dictionary<string, int> nodeCluster, Dictionary<string, int> groupCluster)
	{
		internal string NodeFill(string id) => $"color-mix(in srgb, {Color(nodeCluster[id])} 16%, var(--bg))";
		internal string NodeStroke(string id) => ColorUtils.AdjustLightness(Color(nodeCluster[id]), -0.12);
		internal bool Has(string id) => nodeCluster.ContainsKey(id);
		internal string GroupFill(string id, int depth) => $"color-mix(in srgb, {Color(GroupCluster(id))} {6 + (depth * 3)}%, var(--bg))";
		internal string GroupHeader(string id, int depth) => $"color-mix(in srgb, {Color(GroupCluster(id))} {18 + (depth * 4)}%, var(--bg))";
		internal string GroupStroke(string id) => ColorUtils.AdjustLightness(Color(GroupCluster(id)), -0.02);
		private int GroupCluster(string id) => groupCluster.GetValueOrDefault(id);
		private string Color(int cluster) => colors.PaletteAt(cluster);
	}

	private static FlowPalette BuildFlowPalette(PositionedGraph graph, DiagramColors colors)
	{
		var parent = graph.Nodes.ToDictionary(n => n.Id, n => n.Id, StringComparer.Ordinal);
		string Find(string id)
		{
			while (parent[id] != id)
			{
				parent[id] = parent[parent[id]];
				id = parent[id];
			}
			return id;
		}

		void Union(string a, string b)
		{
			if (parent.ContainsKey(a) && parent.ContainsKey(b))
				parent[Find(a)] = Find(b);
		}

		foreach (var e in graph.Edges)
			Union(e.Source, e.Target);

		bool Inside(PositionedNode n, PositionedGroup g) =>
			n.X + (n.Width / 2) >= g.X && n.X + (n.Width / 2) <= g.X + g.Width && n.Y + (n.Height / 2) >= g.Y && n.Y + (n.Height / 2) <= g.Y + g.Height;

		void UnionGroup(PositionedGroup g, string? anchor)
		{
			foreach (var n in graph.Nodes)
			{
				if (!Inside(n, g))
					continue;
				if (anchor is null)
					anchor = n.Id;
				else
					Union(anchor, n.Id);
			}
			foreach (var c in g.Children)
				UnionGroup(c, anchor);
		}

		foreach (var g in graph.Groups)
			UnionGroup(g, null);

		var index = new Dictionary<string, int>(StringComparer.Ordinal);
		var nodeCluster = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var n in graph.Nodes)
		{
			var root = Find(n.Id);
			if (!index.TryGetValue(root, out var i))
				index[root] = i = index.Count;
			nodeCluster[n.Id] = i;
		}

		var groupCluster = new Dictionary<string, int>(StringComparer.Ordinal);
		void AssignGroups(PositionedGroup g)
		{
			var first = graph.Nodes.FirstOrDefault(n => Inside(n, g));
			groupCluster[g.Id] = first is not null ? nodeCluster[first.Id] : 0;
			foreach (var c in g.Children)
				AssignGroups(c);
		}

		foreach (var g in graph.Groups)
			AssignGroups(g);
		return new FlowPalette(colors, nodeCluster, groupCluster);
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
			.Append("\" fill=\"var(--_line)\" stroke=\"var(--_line)\" stroke-width=\"0.75\" stroke-linejoin=\"round\" />\n");
		_ = sb.Append("  </marker>\n");

		_ = sb.Append("  <marker id=\"arrowhead-start\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"0\" refY=\"").Append(h / 2.0)
			.Append("\" orient=\"auto\">\n");
		_ = sb.Append("    <polygon points=\"").Append(w).Append(" 0, 0 ").Append(h / 2.0)
			.Append(", ").Append(w).Append(' ').Append(h)
			.Append("\" fill=\"var(--_line)\" stroke=\"var(--_line)\" stroke-width=\"0.75\" stroke-linejoin=\"round\" />\n");
		_ = sb.Append("  </marker>\n");
		_ = sb.Append("</defs>\n");
	}

	// ========================================================================
	// Group rendering
	// ========================================================================

	private static void AppendGroupBody(StringBuilder sb, PositionedGroup group, FlowPalette? palette, int depth)
	{
		var r = Radii.Group;
		var fill = InlineStyleValue(group.InlineStyle, "fill") ?? palette?.GroupFill(group.Id, depth) ?? "var(--_group-fill)";
		var stroke = InlineStyleValue(group.InlineStyle, "stroke") ?? palette?.GroupStroke(group.Id) ?? "var(--_group-stroke)";
		var sw = InlineStyleValue(group.InlineStyle, "stroke-width")
			?? StrokeWidths.OuterBox.ToString(CultureInfo.InvariantCulture);

		_ = sb.Append("\n<g class=\"subgraph\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, group.Id.AsSpan());
		_ = sb.Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, group.Label.AsSpan());
		_ = sb.Append("\">\n");

		_ = sb.Append("  <rect x=\"").Append(group.X).Append("\" y=\"").Append(group.Y)
			.Append("\" width=\"").Append(group.Width).Append("\" height=\"").Append(group.Height)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(sw).Append("\" />\n");

		_ = sb.Append("</g>\n");

		foreach (var child in group.Children)
			AppendGroupBody(sb, child, palette, depth + 1);
	}

	private static void AppendGroupHeader(StringBuilder sb, PositionedGroup group, FlowPalette? palette, int depth)
	{
		var headerHeight = FontSizes.GroupHeader + 16;
		var r = Radii.Group;

		_ = sb.Append("  <rect x=\"").Append(group.X).Append("\" y=\"").Append(group.Y)
			.Append("\" width=\"").Append(group.Width).Append("\" height=\"").Append(headerHeight)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"").Append(palette?.GroupHeader(group.Id, depth) ?? "var(--_group-hdr)").Append("\" stroke=\"").Append(palette?.GroupStroke(group.Id) ?? "var(--_group-stroke)").Append("\" stroke-width=\"")
			.Append(StrokeWidths.OuterBox).Append("\" />\n");

		_ = sb.Append("  ");
		MultilineUtils.AppendMultilineText(
			sb, group.Label,
			group.X + 12, group.Y + (headerHeight / 2.0),
			FontSizes.GroupHeader,
			GroupHeaderAttrs);
		_ = sb.Append('\n');

		foreach (var child in group.Children)
			AppendGroupHeader(sb, child, palette, depth + 1);
	}

	// ========================================================================
	// Edge rendering
	// ========================================================================

	private static void AppendEdge(StringBuilder sb, PositionedEdge edge, double cornerRadius)
	{
		if (edge.Points.Count < 2)
			return;

		var inlineStroke = InlineStyleValue(edge.InlineStyle, "stroke");
		var inlineStrokeWidth = InlineStyleValue(edge.InlineStyle, "stroke-width");
		var inlineDashArray = InlineStyleValue(edge.InlineStyle, "stroke-dasharray");

		var dashArray = inlineDashArray is not null
			? $" stroke-dasharray=\"{inlineDashArray}\""
			: edge.Style == EdgeStyle.Dotted ? " stroke-dasharray=\"4 4\"" : "";

		var strokeWidth = inlineStrokeWidth
			?? (edge.Style == EdgeStyle.Thick
				? (StrokeWidths.Connector * 2).ToString(System.Globalization.CultureInfo.InvariantCulture)
				: StrokeWidths.Connector.ToString(System.Globalization.CultureInfo.InvariantCulture));

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
		if (IsOrthogonal(edge.Points))
			BuildOrthogonalPath(sb, edge.Points, cornerRadius);
		else
			BuildRoundedPath(sb, edge.Points, cornerRadius);
		_ = sb.Append("\" fill=\"none\" stroke=\"").Append(inlineStroke ?? "var(--_line)")
			.Append("\" stroke-width=\"").Append(strokeWidth).Append('"').Append(dashArray);

		if (edge.HasArrowEnd)
			_ = sb.Append(" marker-end=\"url(#arrowhead)\"");
		if (edge.HasArrowStart)
			_ = sb.Append(" marker-start=\"url(#arrowhead-start)\"");

		_ = sb.Append(" />");
	}

	internal static bool IsOrthogonal(IReadOnlyList<Point> pts)
	{
		for (var i = 1; i < pts.Count; i++)
		{
			if (Math.Abs(pts[i].X - pts[i - 1].X) > 0.5 && Math.Abs(pts[i].Y - pts[i - 1].Y) > 0.5)
				return false;
		}
		return true;
	}

	/// <summary>Axis-aligned polyline with uniformly rounded corners; no bezier S-curves, so every edge shares one visual style.</summary>
	internal static void BuildOrthogonalPath(StringBuilder sb, IReadOnlyList<Point> points, double radius)
	{
		if (points.Count < 2)
			return;
		var p0 = points[0];
		_ = sb.Append('M').Append(p0.X).Append(',').Append(p0.Y);
		for (var i = 1; i < points.Count - 1; i++)
		{
			var prev = points[i - 1];
			var cur = points[i];
			var next = points[i + 1];
			var inDx = Math.Sign(cur.X - prev.X);
			var inDy = Math.Sign(cur.Y - prev.Y);
			var outDx = Math.Sign(next.X - cur.X);
			var outDy = Math.Sign(next.Y - cur.Y);
			if (inDx == outDx && inDy == outDy)
				continue; // collinear point
			var inLen = Math.Abs(cur.X - prev.X) + Math.Abs(cur.Y - prev.Y);
			var outLen = Math.Abs(next.X - cur.X) + Math.Abs(next.Y - cur.Y);
			var r = Math.Min(radius, Math.Min(inLen, outLen) / 2);
			if (r < 0.5)
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

	internal static void BuildRoundedPath(StringBuilder sb, IReadOnlyList<Point> points, double radius)
	{
		if (points.Count < 2)
			return;

		// 4-point Z/S-bend cross-column edge: render as a smooth cubic bezier so that
		// crossing edges (e.g. E→H and F→G) appear as curves that cross cleanly in the
		// middle, matching mermaid.js visual style instead of two parallel horizontal bars.
		if (points.Count == 4)
		{
			var x0 = points[0].X;
			var y0 = points[0].Y;
			var x1 = points[1].X;
			var y1 = points[1].Y;
			var x2 = points[2].X;
			var y2 = points[2].Y;
			var x3 = points[3].X;
			var y3 = points[3].Y;
			var isVerticalZBend = Math.Abs(x0 - x1) < 3.0
				&& Math.Abs(y1 - y2) < 3.0
				&& Math.Abs(x2 - x3) < 3.0
				&& Math.Abs(x0 - x2) > 3.0;
			if (isVerticalZBend)
			{
				var ym = (y0 + y3) / 2.0;
				_ = sb.Append('M').Append(x0).Append(',').Append(y0)
					.Append(" C").Append(x0).Append(',').Append(ym)
					.Append(' ').Append(x3).Append(',').Append(ym)
					.Append(' ').Append(x3).Append(',').Append(y3);
				return;
			}
			// Only apply to downward-going cross-column paths (y increases = forward edges
			// in TD/subgraph) with distinct source and target X (degenerate when x0==x3).
			var isHorizontalZBend = Math.Abs(y0 - y1) < 1.0
				&& Math.Abs(x1 - x2) < 1.0
				&& Math.Abs(y2 - y3) < 1.0
				&& y2 > y0 + 1.0
				&& Math.Abs(x0 - x3) > 1.0;
			if (isHorizontalZBend)
			{
				var xm = (x0 + x3) / 2.0;
				_ = sb.Append('M').Append(x0).Append(',').Append(y0)
					.Append(" C").Append(xm).Append(',').Append(y0)
					.Append(' ').Append(xm).Append(',').Append(y3)
					.Append(' ').Append(x3).Append(',').Append(y3);
				return;
			}
		}

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

			// Skip rounded corner when segments are collinear (cross product ≈ 0 means
			// virtual-node waypoints on straight lines don't emit spurious Q commands).
			var cross = (dx1 * dy2) - (dy1 * dx2);
			if (Math.Abs(cross) < 0.1)
				continue;

			var r = Math.Min(radius, Math.Min(len1 / 2, len2 / 2));

			if (r < 0.1)
			{
				_ = sb.Append(" L").Append(curr.X).Append(',').Append(curr.Y);
				continue;
			}

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

		var metrics = TextMetrics.MeasureMultiline(
			label.AsSpan(),
			FontSizes.EdgeLabel,
			FontWeights.EdgeLabel);

		var labelColor = InlineStyleValue(edge.InlineStyle, "color");
		var textAttrs = labelColor is not null
			? TextAttrs.EdgeLabelCenterFill + labelColor + "\""
			: EdgeLabelAttrs;

		_ = sb.Append("\n<g class=\"edge-label\" data-from=\"");
		MultilineUtils.AppendEscapedAttr(sb, edge.Source.AsSpan());
		_ = sb.Append("\" data-to=\"");
		MultilineUtils.AppendEscapedAttr(sb, edge.Target.AsSpan());
		_ = sb.Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, label.AsSpan());
		_ = sb.Append("\">\n  ");

		// Pill on the line: border as thick as the lines, ~2px clear padding (text-width estimate corrected like ER labels).
		var bgW = ErSvgRenderer.LabelBoxWidth(metrics.Width);
		var bgH = metrics.Height + ErSvgRenderer.LabelPadY;
		var lr = Math.Min(Radii.EdgeLabel, bgH / 2);
		_ = sb.Append("<rect x=\"").Append(mid.X - (bgW / 2)).Append("\" y=\"").Append(mid.Y - (bgH / 2))
			.Append("\" width=\"").Append(bgW).Append("\" height=\"").Append(bgH)
			.Append("\" rx=\"").Append(lr).Append("\" ry=\"").Append(lr)
			.Append("\" fill=\"var(--bg)\" stroke=\"var(--_line)\" stroke-width=\"").Append(StrokeWidths.Connector).Append("\" />\n  ");
		MultilineUtils.AppendMultilineText(sb, label, mid.X, mid.Y, FontSizes.EdgeLabel, textAttrs);

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

	private static void AppendNode(StringBuilder sb, PositionedNode node, StrictStylingOptions? strict = null, FlowPalette? palette = null)
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

		AppendNodeShape(sb, node, palette);
		_ = sb.Append("\n  ");
		AppendNodeLabel(sb, node);
		_ = sb.Append("\n</g>");
	}

	private static void AppendNodeShape(StringBuilder sb, PositionedNode node, FlowPalette? palette)
	{
		var (x, y, w, h) = (node.X, node.Y, node.Width, node.Height);
		var clustered = palette is not null && palette.Has(node.Id);
		var fill = InlineStyleValue(node.InlineStyle, "fill") ?? (clustered ? palette!.NodeFill(node.Id) : "var(--_node-fill)");
		var stroke = InlineStyleValue(node.InlineStyle, "stroke") ?? (clustered ? palette!.NodeStroke(node.Id) : "var(--_node-stroke)");
		var sw = InlineStyleValue(node.InlineStyle, "stroke-width") ?? StrokeWidths.InnerBox.ToString(CultureInfo.InvariantCulture);

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
			case NodeShape.Parallelogram:
				AppendParallelogram(sb, x, y, w, h, fill, stroke, sw, leansRight: true);
				break;
			case NodeShape.ParallelogramAlt:
				AppendParallelogram(sb, x, y, w, h, fill, stroke, sw, leansRight: false);
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

	/// <summary>A rectangle sheared along the x axis: <c>[/text/]</c> leans right, <c>[\text\]</c> leans left.</summary>
	private static void AppendParallelogram(StringBuilder sb, double x, double y, double w, double h, string fill, string stroke, string sw, bool leansRight)
	{
		var lean = w * 0.15;
		var (topLeft, topRight, bottomRight, bottomLeft) = leansRight
			? (x + lean, x + w, x + w - lean, x)
			: (x, x + w - lean, x + w, x + lean);
		_ = sb.Append("<polygon points=\"")
			.Append(topLeft).Append(',').Append(y).Append(' ')
			.Append(topRight).Append(',').Append(y).Append(' ')
			.Append(bottomRight).Append(',').Append(y + h).Append(' ')
			.Append(bottomLeft).Append(',').Append(y + h)
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
		_ = sb.Append("\n<g class=\"note\">");
		if (note.LineFrom is { } lf && note.LineTo is { } lt)
		{
			_ = sb.Append("\n  <line x1=\"").Append(lf.X).Append("\" y1=\"").Append(lf.Y)
				.Append("\" x2=\"").Append(lt.X).Append("\" y2=\"").Append(lt.Y)
				.Append("\" stroke=\"var(--_accent-stroke)\" stroke-width=\"1.5\" stroke-dasharray=\"4 3\" />");
		}
		_ = sb.Append("\n  <rect x=\"").Append(note.X).Append("\" y=\"").Append(note.Y)
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
		var textColor = InlineStyleValue(node.InlineStyle, "color") ?? "var(--_text)";

		if (node.IsMarkdown)
			AppendMarkdownLabel(sb, node.Label, cx, cy, FontSizes.NodeLabel, FsVar.M, textColor);
		else
			MultilineUtils.AppendMultilineText(
				sb, node.Label, cx, cy,
				FontSizes.NodeLabel,
				TextAttrs.NodeLabelCenterFill + textColor + "\"");
	}

	private static void AppendMarkdownLabel(StringBuilder sb, string label, double cx, double cy, int fontSizePx, string fontSizeVar, string fill)
	{
		var lines = label.Split('\n');
		var lineHeight = fontSizePx * 1.3;
		var totalHeight = lines.Length * lineHeight;
		var startY = cy - (totalHeight / 2) + (lineHeight / 2);

		_ = sb.Append("<text text-anchor=\"middle\" font-size=\"").Append(fontSizeVar)
			.Append("\" fill=\"").Append(fill).Append("\">");

		var inBold = false;
		var inItalic = false;

		for (var li = 0; li < lines.Length; li++)
		{
			var line = lines[li];
			var y = startY + (li * lineHeight);

			_ = sb.Append("<tspan x=\"").Append(cx).Append("\" y=\"").Append(y)
				.Append("\" dy=\"").Append(RenderConstants.TextBaselineShift).Append("\">");

			if (inBold)
				_ = sb.Append("<tspan font-weight=\"bold\">");
			if (inItalic)
				_ = sb.Append("<tspan font-style=\"italic\">");

			var pos = 0;
			while (pos < line.Length)
			{
				if (pos + 1 < line.Length && line[pos] == '*' && line[pos + 1] == '*')
				{
					if (inBold)
					{
						_ = sb.Append("</tspan>");
						inBold = false;
						pos += 2;
						continue;
					}

					var end = line.IndexOf("**", pos + 2, StringComparison.Ordinal);
					if (end > 0)
					{
						_ = sb.Append("<tspan font-weight=\"bold\">");
						MultilineUtils.AppendEscapedXml(sb, line.AsSpan(pos + 2, end - pos - 2));
						_ = sb.Append("</tspan>");
						pos = end + 2;
					}
					else
					{
						_ = sb.Append("<tspan font-weight=\"bold\">");
						MultilineUtils.AppendEscapedXml(sb, line.AsSpan(pos + 2));
						inBold = true;
						pos = line.Length;
					}
					continue;
				}

				if (line[pos] == '*')
				{
					if (inItalic)
					{
						_ = sb.Append("</tspan>");
						inItalic = false;
						pos++;
						continue;
					}

					var end = line.IndexOf('*', pos + 1);
					if (end > 0)
					{
						_ = sb.Append("<tspan font-style=\"italic\">");
						MultilineUtils.AppendEscapedXml(sb, line.AsSpan(pos + 1, end - pos - 1));
						_ = sb.Append("</tspan>");
						pos = end + 1;
					}
					else
					{
						_ = sb.Append("<tspan font-style=\"italic\">");
						MultilineUtils.AppendEscapedXml(sb, line.AsSpan(pos + 1));
						inItalic = true;
						pos = line.Length;
					}
					continue;
				}

				var next = line.IndexOf('*', pos);
				var segment = next < 0 ? line.AsSpan(pos) : line.AsSpan(pos, next - pos);
				MultilineUtils.AppendEscapedXml(sb, segment);
				pos += segment.Length;
			}

			if (inItalic)
				_ = sb.Append("</tspan>");
			if (inBold)
				_ = sb.Append("</tspan>");

			_ = sb.Append("</tspan>");
		}

		if (inBold)
			inBold = false;
		if (inItalic)
			inItalic = false;

		_ = sb.Append("</text>");
	}
}
