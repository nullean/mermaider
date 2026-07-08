using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class BlockSvgRenderer
{
	private const double Pad = 24;
	private const double Gap = 16;
	private const double TitleH = 32;
	private const double MinCellW = 80;
	private const double MinCellH = 48;
	private const double CellPadX = 20;
	private const double CellPadY = 14;
	private const double FontSizePx = RenderConstants.FontSizes.NodeLabel;
	private const string LabelFontSize = RenderConstants.FsVar.M;
	private const string TitleFontSize = RenderConstants.FsVar.L;

	internal static string Render(BlockDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = RenderToBuilder(diagram, colors, font, transparent, strict, accessibility, diagramType);
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

	internal static StringBuilder RenderToBuilder(BlockDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		var hasTitle = diagram.Title is { Length: > 0 };
		var titleOffset = hasTitle ? TitleH : 0;
		var columns = Math.Max(1, diagram.Columns);
		var nodeCount = diagram.Nodes.Count;

		if (nodeCount == 0)
		{
			var emptyW = (Pad * 2) + MinCellW;
			var emptyH = titleOffset + (Pad * 2) + MinCellH;
			StyleBlock.AppendSvgOpenTag(sb, emptyW, emptyH, colors, transparent, accessibility, diagramType);
			StyleBlock.AppendStyleBlock(sb, font, strict);
			_ = sb.Append("\n<defs>\n</defs>\n");
			if (hasTitle)
				AppendTitle(sb, diagram.Title!, emptyW * 0.5);
			_ = sb.Append("\n</svg>");
			return sb;
		}

		var cellW = MinCellW;
		var cellH = MinCellH;
		foreach (var node in diagram.Nodes)
		{
			if (node.IsSpace)
				continue;
			var metrics = TextMetrics.MeasureMultiline(
				node.Label.AsSpan(), FontSizePx, RenderConstants.FontWeights.NodeLabel);
			var w = metrics.Width + (CellPadX * 2);
			var h = metrics.Height + (CellPadY * 2);
			if (w > cellW)
				cellW = w;
			if (h > cellH)
				cellH = h;
		}

		var rows = (nodeCount + columns - 1) / columns;
		var gridW = (columns * cellW) + ((columns - 1) * Gap);
		var gridH = (rows * cellH) + ((rows - 1) * Gap);
		var width = (Pad * 2) + gridW;
		var height = titleOffset + (Pad * 2) + gridH;

		var positions = new Dictionary<string, (double Cx, double Cy, double X, double Y)>(StringComparer.Ordinal);
		var originX = Pad;
		var originY = titleOffset + Pad;

		StyleBlock.AppendSvgOpenTag(sb, width, height, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict);
		_ = sb.Append("\n<defs>\n");
		_ = sb.Append("<marker id=\"block-arrow\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\" markerWidth=\"7\" markerHeight=\"7\" orient=\"auto-start-reverse\">")
			.Append("<path d=\"M 0 0 L 10 5 L 0 10 z\" fill=\"var(--_arrow)\" />")
			.Append("</marker>\n");
		_ = sb.Append("</defs>\n");

		if (hasTitle)
			AppendTitle(sb, diagram.Title!, width * 0.5);

		for (var idx = 0; idx < nodeCount; idx++)
		{
			var node = diagram.Nodes[idx];
			var col = idx % columns;
			var row = idx / columns;
			var x = originX + (col * (cellW + Gap));
			var y = originY + (row * (cellH + Gap));
			var cx = x + (cellW / 2);
			var cy = y + (cellH / 2);
			positions[node.Id] = (cx, cy, x, y);

			if (node.IsSpace)
				continue;

			var rx = node.Rounded ? RenderConstants.Radii.Rounded : RenderConstants.Radii.Rectangle;
			_ = sb.Append("\n<rect x=\"").Append(F(x)).Append("\" y=\"").Append(F(y))
				.Append("\" width=\"").Append(F(cellW)).Append("\" height=\"").Append(F(cellH))
				.Append("\" rx=\"").Append(rx).Append("\" ry=\"").Append(rx)
				.Append("\" fill=\"var(--_node-fill)\" stroke=\"var(--_node-stroke)\" stroke-width=\"")
				.Append(F(RenderConstants.StrokeWidths.OuterBox)).Append("\" />");

			_ = sb.Append("\n<text x=\"").Append(F(cx)).Append("\" y=\"").Append(F(cy))
				.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
				.Append("\" font-size=\"").Append(LabelFontSize)
				.Append("\" font-weight=\"").Append(RenderConstants.FontWeights.NodeLabel)
				.Append("\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, node.Label.AsSpan());
			_ = sb.Append("</text>");
		}

		foreach (var edge in diagram.Edges)
		{
			if (!positions.TryGetValue(edge.From, out var from) || !positions.TryGetValue(edge.To, out var to))
				continue;
			if (edge.From.Equals(edge.To, StringComparison.Ordinal))
				continue;

			AppendEdge(sb, from, to, cellW, cellH);
		}

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static void AppendTitle(StringBuilder sb, string title, double centerX)
	{
		_ = sb.Append("\n<text x=\"").Append(F(centerX)).Append("\" y=\"22\" text-anchor=\"middle\" font-size=\"")
			.Append(TitleFontSize).Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, title.AsSpan());
		_ = sb.Append("</text>");
	}

	private static void AppendEdge(
		StringBuilder sb,
		(double Cx, double Cy, double X, double Y) from,
		(double Cx, double Cy, double X, double Y) to,
		double cellW,
		double cellH)
	{
		// Connect nearest mid-sides of the two cells for a simple rectilinear-ish straight line.
		var (x1, y1) = EdgeAnchor(from, to.Cx, to.Cy, cellW, cellH);
		var (x2, y2) = EdgeAnchor(to, from.Cx, from.Cy, cellW, cellH);

		_ = sb.Append("\n<line x1=\"").Append(F(x1)).Append("\" y1=\"").Append(F(y1))
			.Append("\" x2=\"").Append(F(x2)).Append("\" y2=\"").Append(F(y2))
			.Append("\" stroke=\"var(--_line)\" stroke-width=\"1.75\" marker-end=\"url(#block-arrow)\" />");
	}

	private static (double X, double Y) EdgeAnchor(
		(double Cx, double Cy, double X, double Y) box,
		double towardX,
		double towardY,
		double cellW,
		double cellH)
	{
		var dx = towardX - box.Cx;
		var dy = towardY - box.Cy;
		if (Math.Abs(dx) >= Math.Abs(dy))
		{
			// left or right side
			var x = dx >= 0 ? box.X + cellW : box.X;
			return (x, box.Cy);
		}
		// top or bottom
		var y = dy >= 0 ? box.Y + cellH : box.Y;
		return (box.Cx, y);
	}

	private static string F(double value) =>
		value.ToString("0.##", CultureInfo.InvariantCulture);
}
