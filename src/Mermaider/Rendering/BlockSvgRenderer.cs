using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class BlockSvgRenderer
{
	private const double MinCellW = 80;
	private const double MinCellH = 44;
	private const double CellPadX = 20;
	private const double CellPadY = 12;

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
		var ds = DesignSystem.For(context);

		var hasTitle = diagram.Title is { Length: > 0 };
		var top = DesignSystem.BoardTop(hasTitle);
		var columns = Math.Max(1, diagram.Columns);
		var nodeCount = diagram.Nodes.Count;
		var basePad = DesignSystem.BoardMargin;

		if (nodeCount == 0)
		{
			var emptyW = (basePad * 2) + Math.Max(MinCellW, hasTitle ? TitleWidth(diagram.Title!) : 0);
			var emptyH = top + basePad + MinCellH;
			StyleBlock.AppendSvgOpenTag(sb, emptyW, emptyH, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
			StyleBlock.AppendStyleBlock(sb, context.Styles);
			if (hasTitle)
				ds.AppendTitle(sb, basePad, DesignSystem.BoardTitleCy, diagram.Title!);
			ds.Close(sb);
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
			var m = TextMetrics.MeasureMultiline(label.AsSpan(), DesignSystem.Px(TypeRole.Caption), 500);
			maxLabelW = Math.Max(maxLabelW, m.Width);
			maxLabelH = Math.Max(maxLabelH, m.Height);
		}

		var gapX = !hasEdges ? 16 : Math.Max(36, maxLabelW > 0 ? DesignSystem.LabelBoxWidth(maxLabelW) + 24 : 0);
		var gapY = !hasEdges ? 16 : Math.Max(36, maxLabelH > 0 ? maxLabelH + 36 : 0);
		var pad = hasEdges ? Math.Max(basePad, (gapX / 2) + 8) : basePad;

		var labelPx = DesignSystem.Px(TypeRole.Label);
		var labelWeight = ds.Weight(TypeRole.Label);
		var cellW = MinCellW;
		var cellH = MinCellH;
		foreach (var node in diagram.Nodes)
		{
			if (node.IsSpace)
				continue;
			var span = Math.Min(Math.Max(1, node.Span), columns);
			// measured at the heaviest label weight so the grid does not depend on the preset
			var metrics = TextMetrics.MeasureMultiline(node.Label.AsSpan(), labelPx, Math.Max(labelWeight, 600));
			// a block spanning n columns shares its width with the gaps it covers
			var w = (metrics.Width + (CellPadX * 2) - ((span - 1) * gapX)) / span;
			var h = metrics.Height + (CellPadY * 2);
			cellW = Math.Max(cellW, w);
			cellH = Math.Max(cellH, h);
		}

		var originX = pad;
		// below the title row, with room for a route that runs through the channel above the first row
		var originY = hasTitle ? Math.Max(top + (hasEdges ? gapY / 2 : 0), pad) : pad;
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
		if (hasTitle)
			width = Math.Max(width, (basePad * 2) + TitleWidth(diagram.Title!));
		var height = originY + pad + (rows * cellH) + ((rows - 1) * gapY);

		var byId = new Dictionary<string, Cell>(StringComparer.Ordinal);
		foreach (var c in cells.Where(c => !c.Node.IsSpace))
			_ = byId.TryAdd(c.Node.Id, c);

		// Same language as the other diagrams: connected blocks share a cluster family.
		var palette = ClusterPalette.Build(
			byId.Values.Select(c => new ClusterBox(c.Node.Id, c.X, c.Y, c.W, c.H)).ToList(),
			diagram.Edges.Where(e => byId.ContainsKey(e.From) && byId.ContainsKey(e.To)).Select(e => (e.From, e.To)),
			[],
			context.Styles.Colors).WithTint(ds.TintStrength);

		StyleBlock.AppendSvgOpenTag(sb, width, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles);

		if (hasTitle)
			ds.AppendTitle(sb, basePad, DesignSystem.BoardTitleCy, diagram.Title!);

		var geometry = new Grid(originX, originY, cellW, cellH, gapX, gapY, rows, columns);
		var labels = new List<(BlockEdge Edge, Point At)>();
		foreach (var edge in diagram.Edges)
		{
			if (!byId.TryGetValue(edge.From, out var from) || !byId.TryGetValue(edge.To, out var to))
				continue;
			if (edge.From.Equals(edge.To, StringComparison.Ordinal))
				continue;

			var points = Simplify(Route(from, to, byId.Values, geometry));
			AppendEdge(sb, ds, edge, points, context.EdgeRadius);
			if (edge.Label is { Length: > 0 })
				labels.Add((edge, VisualLanguage.PathMidpoint(points)));
		}

		foreach (var cell in cells)
		{
			if (!cell.Node.IsSpace)
				AppendBlock(sb, ds, cell, palette);
		}

		// labels last so a pill is never covered by a block or another edge
		foreach (var (edge, at) in labels)
		{
			_ = sb.Append("\n<g class=\"edge-label\" data-from=\"");
			MultilineUtils.AppendEscapedAttr(sb, edge.From.AsSpan());
			_ = sb.Append("\" data-to=\"");
			MultilineUtils.AppendEscapedAttr(sb, edge.To.AsSpan());
			_ = sb.Append("\">\n  ");
			ds.AppendEdgeLabel(sb, at.X, at.Y, edge.Label!);
			_ = sb.Append("\n</g>");
		}

		ds.Close(sb);
		return sb;
	}

	private static double TitleWidth(string title) =>
		DesignSystem.TitleIndent + TextMetrics.MeasureTextWidth(title, DesignSystem.Px(TypeRole.Title), 700);

	private static void AppendBlock(StringBuilder sb, DesignSystem ds, Cell cell, ClusterPalette palette)
	{
		var node = cell.Node;
		var family = palette.Family(node.Id);
		var radius = node.Rounded ? Math.Min(ds.Spec.NodeRadius + 6, cell.H / 2) : ds.Spec.NodeRadius;
		_ = sb.Append("\n<g class=\"node\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, node.Id.AsSpan());
		_ = sb.Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, node.Label.AsSpan());
		_ = sb.Append("\" data-shape=\"").Append(node.Rounded ? "rounded" : "rectangle").Append("\">\n  ");
		ds.AppendBox(sb, cell.X, cell.Y, cell.W, cell.H, family, radius);
		_ = sb.Append("\n  ");
		ds.AppendText(sb, node.Label, cell.Cx, cell.Cy, TypeRole.Label);
		_ = sb.Append("\n</g>");
	}

	private readonly record struct Grid(double OriginX, double OriginY, double CellW, double CellH, double GapX, double GapY, int Rows, int Columns);

	private static void AppendEdge(StringBuilder sb, DesignSystem ds, BlockEdge edge, List<Point> points, double cornerRadius)
	{
		_ = sb.Append("\n<path class=\"block-edge\" data-from=\"");
		MultilineUtils.AppendEscapedAttr(sb, edge.From.AsSpan());
		_ = sb.Append("\" data-to=\"");
		MultilineUtils.AppendEscapedAttr(sb, edge.To.AsSpan());
		_ = sb.Append("\" d=\"");
		if (SvgRenderer.IsOrthogonal(points))
			SvgRenderer.BuildOrthogonalPath(sb, points, cornerRadius);
		else
			SvgRenderer.BuildRoundedPath(sb, points, cornerRadius);
		_ = sb.Append("\" fill=\"none\" stroke=\"").Append(DesignSystem.EdgeColor).Append("\" stroke-width=\"").Append(ds.EdgeWidth)
			.Append("\" marker-end=\"").Append(ds.Marker(MarkerShape.Arrow)).Append("\" />");
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
