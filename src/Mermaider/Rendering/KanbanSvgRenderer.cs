using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class KanbanSvgRenderer
{
	private const double Pad = 24;
	private const double ColumnGap = 16;
	private const double CardGap = 10;
	private const double ColumnPad = 12;
	private const double HeaderHeight = 36;
	private const double CardPadX = 12;
	private const double CardPadY = 10;
	private const double MinColumnWidth = 160;
	private const double MaxColumnWidth = 280;
	private const double HeaderFontSizePx = 13;
	private const double CardFontSizePx = 13;
	private const double MetaFontSizePx = 11;
	private const double MetaLineGap = 2;
	private const string TitleFontSize = RenderConstants.FsVar.L;
	private const string HeaderFontSize = RenderConstants.FsVar.S;
	private const string CardFontSize = RenderConstants.FsVar.S;
	private const string MetaFontSize = RenderConstants.FsVar.Xs;

	internal static string Render(KanbanDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
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

	internal static StringBuilder RenderToBuilder(KanbanDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		var hasTitle = diagram.Title is { Length: > 0 };
		var titleOffset = hasTitle ? 40.0 : 0;

		if (diagram.Columns.Count == 0)
		{
			StyleBlock.AppendSvgOpenTag(sb, 200, 100 + titleOffset, colors, transparent, accessibility, diagramType);
			StyleBlock.AppendStyleBlock(sb, font, strict);
			if (hasTitle)
				AppendBoardTitle(sb, diagram.Title!, 100, 28);
			_ = sb.Append("\n</svg>");
			return sb;
		}

		var columnWidths = new double[diagram.Columns.Count];
		var columnHeights = new double[diagram.Columns.Count];
		var maxColumnHeight = 0.0;

		for (var i = 0; i < diagram.Columns.Count; i++)
		{
			var col = diagram.Columns[i];
			var contentWidth = TextMetrics.MeasureTextWidth(col.Title, HeaderFontSizePx, 600);
			foreach (var task in col.Tasks)
			{
				contentWidth = Math.Max(contentWidth, TextMetrics.MeasureTextWidth(task.Title, CardFontSizePx, 500));
				foreach (var meta in EnumerateMetaLines(task))
					contentWidth = Math.Max(contentWidth, TextMetrics.MeasureTextWidth(meta, MetaFontSizePx, 400));
			}

			var colW = Math.Clamp(contentWidth + (CardPadX * 2) + (ColumnPad * 2), MinColumnWidth, MaxColumnWidth);
			columnWidths[i] = colW;

			var cardsH = 0.0;
			foreach (var task in col.Tasks)
			{
				cardsH += MeasureCardHeight(task) + CardGap;
			}
			if (col.Tasks.Count > 0)
				cardsH -= CardGap;

			var colH = HeaderHeight + ColumnPad + cardsH + ColumnPad;
			columnHeights[i] = colH;
			if (colH > maxColumnHeight)
				maxColumnHeight = colH;
		}

		var totalWidth = Pad;
		foreach (var w in columnWidths)
			totalWidth += w + ColumnGap;
		totalWidth = totalWidth - ColumnGap + Pad;

		var height = titleOffset + Pad + maxColumnHeight + Pad;

		StyleBlock.AppendSvgOpenTag(sb, totalWidth, height, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict);
		_ = sb.Append("\n<defs>\n</defs>\n");

		if (hasTitle)
			AppendBoardTitle(sb, diagram.Title!, totalWidth / 2, 28);

		var x = Pad;
		var y = titleOffset + Pad;

		for (var i = 0; i < diagram.Columns.Count; i++)
		{
			AppendColumn(sb, diagram.Columns[i], x, y, columnWidths[i], maxColumnHeight);
			x += columnWidths[i] + ColumnGap;
		}

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static void AppendBoardTitle(StringBuilder sb, string title, double cx, double y)
	{
		_ = sb.Append("\n<text x=\"").Append(SvgFormat.F(cx)).Append("\" y=\"").Append(SvgFormat.F(y))
			.Append("\" text-anchor=\"middle\" font-size=\"").Append(TitleFontSize)
			.Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, title.AsSpan());
		_ = sb.Append("</text>");
	}

	private static void AppendColumn(StringBuilder sb, KanbanColumn column, double x, double y, double width, double height)
	{
		// Column background
		_ = sb.Append("\n<rect x=\"").Append(SvgFormat.F(x)).Append("\" y=\"").Append(SvgFormat.F(y))
			.Append("\" width=\"").Append(SvgFormat.F(width)).Append("\" height=\"").Append(SvgFormat.F(height))
			.Append("\" rx=\"8\" ry=\"8\" fill=\"var(--_node-fill)\" stroke=\"var(--_line)\" stroke-width=\"1\" />");

		// Accent header bar (top rounded via clip of full rounded rect + flat strip)
		_ = sb.Append("\n<rect x=\"").Append(SvgFormat.F(x)).Append("\" y=\"").Append(SvgFormat.F(y))
			.Append("\" width=\"").Append(SvgFormat.F(width)).Append("\" height=\"").Append(SvgFormat.F(HeaderHeight))
			.Append("\" rx=\"8\" ry=\"8\" fill=\"var(--_accent-fill)\" />");
		_ = sb.Append("\n<rect x=\"").Append(SvgFormat.F(x)).Append("\" y=\"").Append(SvgFormat.F(y + HeaderHeight - 8))
			.Append("\" width=\"").Append(SvgFormat.F(width)).Append("\" height=\"8\" fill=\"var(--_accent-fill)\" />");

		var headerCx = x + (width / 2);
		var headerCy = y + (HeaderHeight / 2);
		_ = sb.Append("\n<text x=\"").Append(SvgFormat.F(headerCx)).Append("\" y=\"").Append(SvgFormat.F(headerCy))
			.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(HeaderFontSize)
			.Append("\" font-weight=\"700\" fill=\"var(--_accent-text)\">");
		MultilineUtils.AppendEscapedXml(sb, column.Title.AsSpan());
		_ = sb.Append("</text>");

		// Task count badge
		var countLabel = column.Tasks.Count.ToString(CultureInfo.InvariantCulture);
		_ = sb.Append("\n<text x=\"").Append(SvgFormat.F(x + width - ColumnPad))
			.Append("\" y=\"").Append(SvgFormat.F(headerCy))
			.Append("\" text-anchor=\"end\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(MetaFontSize)
			.Append("\" font-weight=\"600\" fill=\"var(--_text-muted)\">");
		MultilineUtils.AppendEscapedXml(sb, countLabel.AsSpan());
		_ = sb.Append("</text>");

		var cardY = y + HeaderHeight + ColumnPad;
		var cardW = width - (ColumnPad * 2);
		foreach (var task in column.Tasks)
		{
			var cardH = MeasureCardHeight(task);
			AppendCard(sb, task, x + ColumnPad, cardY, cardW, cardH);
			cardY += cardH + CardGap;
		}
	}

	private static void AppendCard(StringBuilder sb, KanbanTask task, double x, double y, double width, double height)
	{
		_ = sb.Append("\n<rect x=\"").Append(SvgFormat.F(x)).Append("\" y=\"").Append(SvgFormat.F(y))
			.Append("\" width=\"").Append(SvgFormat.F(width)).Append("\" height=\"").Append(SvgFormat.F(height))
			.Append("\" rx=\"6\" ry=\"6\" fill=\"var(--bg)\" stroke=\"var(--_line)\" stroke-width=\"1\" />");

		var textX = x + CardPadX;
		var textY = y + CardPadY + (CardFontSizePx * 0.85);

		_ = sb.Append("\n<text x=\"").Append(SvgFormat.F(textX)).Append("\" y=\"").Append(SvgFormat.F(textY))
			.Append("\" font-size=\"").Append(CardFontSize)
			.Append("\" font-weight=\"500\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, task.Title.AsSpan());
		_ = sb.Append("</text>");

		var metaY = textY + CardFontSizePx + MetaLineGap + 2;
		foreach (var line in EnumerateMetaLines(task))
		{
			_ = sb.Append("\n<text x=\"").Append(SvgFormat.F(textX)).Append("\" y=\"").Append(SvgFormat.F(metaY))
				.Append("\" font-size=\"").Append(MetaFontSize)
				.Append("\" font-weight=\"400\" fill=\"var(--_text-muted)\">");
			MultilineUtils.AppendEscapedXml(sb, line.AsSpan());
			_ = sb.Append("</text>");
			metaY += MetaFontSizePx + MetaLineGap;
		}
	}

	private static double MeasureCardHeight(KanbanTask task)
	{
		var h = (CardPadY * 2) + CardFontSizePx;
		var metaCount = 0;
		if (task.Ticket is { Length: > 0 })
			metaCount++;
		if (task.Assigned is { Length: > 0 })
			metaCount++;
		if (task.Priority is { Length: > 0 })
			metaCount++;
		if (metaCount > 0)
			h += 4 + (metaCount * (MetaFontSizePx + MetaLineGap));
		return h;
	}

	private static IEnumerable<string> EnumerateMetaLines(KanbanTask task)
	{
		if (task.Ticket is { Length: > 0 })
			yield return task.Ticket;
		if (task.Assigned is { Length: > 0 })
			yield return task.Assigned;
		if (task.Priority is { Length: > 0 })
			yield return task.Priority;
	}

}
