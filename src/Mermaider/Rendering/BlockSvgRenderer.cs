using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class BlockSvgRenderer
{
	private const double BasePad = 24;
	private const double TitleH = 32;
	private const double MinCellW = 80;
	private const double MinCellH = 48;
	private const double CellPadX = 20;
	private const double CellPadY = 14;
	private const double FontSizePx = RenderConstants.FontSizes.NodeLabel;
	private const string LabelFontSize = RenderConstants.FsVar.M;
	private const string TitleFontSize = RenderConstants.FsVar.L;

	private readonly record struct Cell(BlockNode Node, double X, double Y, double W, double H, int Row, int Span)
	{
		internal double Right => X + W;
		internal double Bottom => Y + H;
		internal double Cx => X + (W / 2);
		internal double Cy => Y + (H / 2);
	}

	internal static string Render(BlockDiagram diagram, SvgRenderContext context)
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

	internal static StringBuilder RenderToBuilder(BlockDiagram diagram, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		var hasTitle = diagram.Title is { Length: > 0 };
		var titleOffset = hasTitle ? TitleH : 0;
		var columns = Math.Max(1, diagram.Columns);
		var nodeCount = diagram.Nodes.Count;

		if (nodeCount == 0)
		{
			var emptyW = (BasePad * 2) + MinCellW;
			var emptyH = titleOffset + (BasePad * 2) + MinCellH;
			StyleBlock.AppendSvgOpenTag(sb, emptyW, emptyH, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
			StyleBlock.AppendStyleBlock(sb, context.Styles.Font, context.Styles.Strict, context.Styles.FontScale, context.Styles.MonoFont);
			_ = sb.Append("\n<defs>\n</defs>\n");
			if (hasTitle)
				AppendTitle(sb, diagram.Title!, emptyW * 0.5);
			_ = sb.Append("\n</svg>");
			return sb;
		}

		// Edges need room between the cells: channels for the routes, and a pill's worth of space for labels.
		var hasEdges = diagram.Edges.Count > 0;
		var maxLabelW = 0.0;
		var maxLabelH = 0.0;
		foreach (var edge in diagram.Edges)
		{
			if (edge.Label is not { Length: > 0 } label)
				continue;
			var m = TextMetrics.MeasureMultiline(label.AsSpan(), RenderConstants.FontSizes.EdgeLabel, RenderConstants.FontWeights.EdgeLabel);
			maxLabelW = Math.Max(maxLabelW, m.Width);
			maxLabelH = Math.Max(maxLabelH, m.Height);
		}

		var gapX = !hasEdges ? 16 : Math.Max(36, maxLabelW > 0 ? ErSvgRenderer.LabelBoxWidth(maxLabelW) + 24 : 0);
		var gapY = !hasEdges ? 16 : Math.Max(36, maxLabelH > 0 ? maxLabelH + 36 : 0);
		var pad = hasEdges ? Math.Max(BasePad, (gapX / 2) + 8) : BasePad;

		var cellW = MinCellW;
		var cellH = MinCellH;
		foreach (var node in diagram.Nodes)
		{
			if (node.IsSpace)
				continue;
			var span = Math.Min(Math.Max(1, node.Span), columns);
			var metrics = TextMetrics.MeasureMultiline(
				node.Label.AsSpan(), FontSizePx, RenderConstants.FontWeights.NodeLabel);
			// a block spanning n columns shares its width with the gaps it covers
			var w = (metrics.Width + (CellPadX * 2) - ((span - 1) * gapX)) / span;
			var h = metrics.Height + (CellPadY * 2);
			cellW = Math.Max(cellW, w);
			cellH = Math.Max(cellH, h);
		}

		var originX = pad;
		var originY = titleOffset + pad;
		var cells = new List<Cell>(nodeCount);
		var col = 0;
		var row = 0;
		foreach (var node in diagram.Nodes)
		{
			var span = Math.Min(Math.Max(1, node.Span), columns);
			if (col + span > columns)
			{
				row++;
				col = 0;
			}

			cells.Add(new Cell(
				node,
				originX + (col * (cellW + gapX)),
				originY + (row * (cellH + gapY)),
				(span * cellW) + ((span - 1) * gapX),
				cellH,
				row,
				span));
			col += span;
			if (col >= columns)
			{
				row++;
				col = 0;
			}
		}

		var rows = cells.Max(c => c.Row) + 1;
		var width = (pad * 2) + (columns * cellW) + ((columns - 1) * gapX);
		var height = titleOffset + (pad * 2) + (rows * cellH) + ((rows - 1) * gapY);

		var byId = new Dictionary<string, Cell>(StringComparer.Ordinal);
		foreach (var c in cells.Where(c => !c.Node.IsSpace))
			_ = byId.TryAdd(c.Node.Id, c);

		// Same language as the other diagrams: connected blocks share a cluster colour.
		var palette = ClusterPalette.Build(
			byId.Values.Select(c => new ClusterBox(c.Node.Id, c.X, c.Y, c.W, c.H)).ToList(),
			diagram.Edges.Where(e => byId.ContainsKey(e.From) && byId.ContainsKey(e.To)).Select(e => (e.From, e.To)),
			[],
			context.Styles.Colors);

		StyleBlock.AppendSvgOpenTag(sb, width, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles.Font, context.Styles.Strict, context.Styles.FontScale, context.Styles.MonoFont);
		_ = sb.Append("\n<defs>\n");
		_ = sb.Append("<marker id=\"block-arrow\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\" markerWidth=\"7\" markerHeight=\"7\" orient=\"auto-start-reverse\">")
			.Append("<path d=\"M 0 0 L 10 5 L 0 10 z\" fill=\"var(--_line)\" />")
			.Append("</marker>\n");
		_ = sb.Append("</defs>\n");

		if (hasTitle)
			AppendTitle(sb, diagram.Title!, width * 0.5);

		var geometry = new Grid(originX, originY, cellW, cellH, gapX, gapY, rows, columns);
		foreach (var edge in diagram.Edges)
		{
			if (!byId.TryGetValue(edge.From, out var from) || !byId.TryGetValue(edge.To, out var to))
				continue;
			if (edge.From.Equals(edge.To, StringComparison.Ordinal))
				continue;

			AppendEdge(sb, edge, Simplify(Route(from, to, byId.Values, geometry)), context.EdgeRadius);
		}

		foreach (var cell in cells)
		{
			if (!cell.Node.IsSpace)
				AppendBlock(sb, cell, palette);
		}

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static void AppendBlock(StringBuilder sb, Cell cell, ClusterPalette palette)
	{
		var node = cell.Node;
		var rx = node.Rounded ? RenderConstants.Radii.Rounded : RenderConstants.Radii.Rectangle;
		_ = sb.Append("\n<rect x=\"").Append(cell.X.SvgFormat()).Append("\" y=\"").Append(cell.Y.SvgFormat())
			.Append("\" width=\"").Append(cell.W.SvgFormat()).Append("\" height=\"").Append(cell.H.SvgFormat())
			.Append("\" rx=\"").Append(rx).Append("\" ry=\"").Append(rx)
			.Append("\" fill=\"").Append(palette.NodeFill(node.Id)).Append("\" stroke=\"").Append(palette.NodeStroke(node.Id))
			.Append("\" stroke-width=\"").Append(RenderConstants.StrokeWidths.OuterBox.SvgFormat()).Append("\" />");

		_ = sb.Append("\n<text x=\"").Append(cell.Cx.SvgFormat()).Append("\" y=\"").Append(cell.Cy.SvgFormat())
			.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(LabelFontSize)
			.Append("\" font-weight=\"").Append(RenderConstants.FontWeights.NodeLabel)
			.Append("\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, node.Label.AsSpan());
		_ = sb.Append("</text>");
	}

	private static void AppendTitle(StringBuilder sb, string title, double centerX)
	{
		_ = sb.Append("\n<text x=\"").Append(centerX.SvgFormat()).Append("\" y=\"22\" text-anchor=\"middle\" font-size=\"")
			.Append(TitleFontSize).Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, title.AsSpan());
		_ = sb.Append("</text>");
	}

	private readonly record struct Grid(double OriginX, double OriginY, double CellW, double CellH, double GapX, double GapY, int Rows, int Columns);

	private static void AppendEdge(StringBuilder sb, BlockEdge edge, List<Point> points, double cornerRadius)
	{
		_ = sb.Append("\n<path class=\"block-edge\" d=\"");
		if (SvgRenderer.IsOrthogonal(points))
			SvgRenderer.BuildOrthogonalPath(sb, points, cornerRadius);
		else
			SvgRenderer.BuildRoundedPath(sb, points, cornerRadius);
		_ = sb.Append("\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.Connector.SvgFormat())
			.Append("\" marker-end=\"url(#block-arrow)\" />");

		if (edge.Label is not { Length: > 0 } label)
			return;

		var pos = VisualLanguage.PathMidpoint(points);
		var metrics = TextMetrics.MeasureMultiline(label.AsSpan(), RenderConstants.FontSizes.EdgeLabel, RenderConstants.FontWeights.EdgeLabel);
		_ = sb.Append('\n');
		VisualLanguage.AppendLabelPill(sb, pos.X, pos.Y, metrics.Width, metrics.Height);
		_ = sb.Append("\n<text x=\"").Append(pos.X.SvgFormat()).Append("\" y=\"").Append(pos.Y.SvgFormat())
			.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(RenderConstants.FsVar.S)
			.Append("\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, label.AsSpan());
		_ = sb.Append("</text>");
	}

	/// <summary>
	/// Straight between neighbours; otherwise through the channels between the rows and columns so a line never crosses a block.
	/// </summary>
	private static List<Point> Route(Cell a, Cell b, IEnumerable<Cell> all, Grid g)
	{
		var others = all.Where(c => c.Node.Id != a.Node.Id && c.Node.Id != b.Node.Id).ToList();

		var sameRow = a.Row == b.Row;
		var overlapLeft = Math.Max(a.X, b.X);
		var overlapRight = Math.Min(a.Right, b.Right);
		var sameColumn = overlapRight - overlapLeft > 8;

		if (sameRow)
		{
			var forward = b.X > a.X;
			var straight = new List<Point> { new(forward ? a.Right : a.X, a.Cy), new(forward ? b.X : b.Right, b.Cy) };
			if (!Crosses(straight, others))
				return straight;

			// over the top of the row, through the channel above it
			var channel = a.Y - (g.GapY / 2);
			return [new Point(a.Cx, a.Y), new Point(a.Cx, channel), new Point(b.Cx, channel), new Point(b.Cx, b.Y)];
		}

		if (sameColumn)
		{
			var x = (overlapLeft + overlapRight) / 2;
			var down = b.Y > a.Y;
			var straight = new List<Point> { new(x, down ? a.Bottom : a.Y), new(x, down ? b.Y : b.Bottom) };
			if (!Crosses(straight, others))
				return straight;

			// down the channel right of the column
			var channel = Math.Max(a.Right, b.Right) + (g.GapX / 2);
			return [new Point(a.Right, a.Cy), new Point(channel, a.Cy), new Point(channel, b.Cy), new Point(b.Right, b.Cy)];
		}

		// diagonal: leave through the channel under (or over) the source row, run down the column channel beside the target,
		// and enter the target from its top (or bottom) through the channel next to it
		{
			var down = b.Y > a.Y;
			var leaveChannel = down ? a.Bottom + (g.GapY / 2) : a.Y - (g.GapY / 2);
			var enterChannel = down ? b.Y - (g.GapY / 2) : b.Bottom + (g.GapY / 2);
			var columnChannel = b.Cx > a.Cx ? b.X - (g.GapX / 2) : b.Right + (g.GapX / 2);
			return
			[
				new Point(a.Cx, down ? a.Bottom : a.Y),
				new Point(a.Cx, leaveChannel),
				new Point(columnChannel, leaveChannel),
				new Point(columnChannel, enterChannel),
				new Point(b.Cx, enterChannel),
				new Point(b.Cx, down ? b.Y : b.Bottom),
			];
		}
	}

	// drops repeated and collinear points so every bend is a real corner
	private static List<Point> Simplify(List<Point> points)
	{
		var result = new List<Point>(points.Count);
		foreach (var raw in points)
		{
			// equal coordinates must be exactly equal, or the path builder reads float noise as a diagonal
			var p = new Point(Math.Round(raw.X, 2), Math.Round(raw.Y, 2));
			if (result.Count > 0 && Math.Abs(result[^1].X - p.X) < 0.01 && Math.Abs(result[^1].Y - p.Y) < 0.01)
				continue;
			if (result.Count > 1)
			{
				var q = result[^2];
				var r = result[^1];
				var sameX = Math.Abs(q.X - r.X) < 0.01 && Math.Abs(r.X - p.X) < 0.01;
				var sameY = Math.Abs(q.Y - r.Y) < 0.01 && Math.Abs(r.Y - p.Y) < 0.01;
				if (sameX || sameY)
					result.RemoveAt(result.Count - 1);
			}

			result.Add(p);
		}

		return result;
	}

	private static bool Crosses(List<Point> segment, List<Cell> blocks)
	{
		var (x1, y1) = (segment[0].X, segment[0].Y);
		var (x2, y2) = (segment[^1].X, segment[^1].Y);
		var minX = Math.Min(x1, x2);
		var maxX = Math.Max(x1, x2);
		var minY = Math.Min(y1, y2);
		var maxY = Math.Max(y1, y2);
		foreach (var c in blocks)
		{
			if (c.Node.IsSpace)
				continue;
			if (maxX > c.X && minX < c.Right && maxY > c.Y && minY < c.Bottom)
				return true;
		}

		return false;
	}
}
