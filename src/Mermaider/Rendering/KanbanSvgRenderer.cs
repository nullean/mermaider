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
	private const double PriorityBorderWidth = 4;
	private const double BadgePadX = 7;
	private const double BadgePadY = 3;
	private const double BadgeRx = 7;

	// Measurement px must match the tier each font var resolves to at default scale:
	//   --fs-xs = 12px, --fs-s = 14px, --fs-m = 16px, --fs-l = 18px
	private const double HeaderFontSizePx = 16; // --fs-m
	private const double CardFontSizePx = 14;   // --fs-s
	private const double MetaFontSizePx = 12;   // --fs-xs
	private const double MetaLineGap = 2;

	private const string TitleFontSize = RenderConstants.FsVar.L;
	private const string HeaderFontSize = RenderConstants.FsVar.M;
	private const string CardFontSize = RenderConstants.FsVar.S;
	private const string MetaFontSize = RenderConstants.FsVar.Xs;

	// Priority colors — from the single shared categorical palette
	private static readonly string PriorityVeryHigh = CategoricalPalette.Red;
	private static readonly string PriorityHigh = CategoricalPalette.Orange;
	private static readonly string PriorityLow = CategoricalPalette.Green;
	private const string PriorityVeryLow = "var(--_text-muted)";

	internal static string Render(KanbanDiagram diagram, DiagramColors colors, string font, string? monoFont = null, bool transparent = false, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = RenderToBuilder(diagram, colors, font, monoFont, transparent, strict, accessibility, diagramType);
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

	internal static StringBuilder RenderToBuilder(KanbanDiagram diagram, DiagramColors colors, string font, string? monoFont = null, bool transparent = false, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		var hasTitle = diagram.Title is { Length: > 0 };
		var titleOffset = hasTitle ? 40.0 : 0;

		if (diagram.Columns.Count == 0)
		{
			StyleBlock.AppendSvgOpenTag(sb, 200, 100 + titleOffset, colors, transparent, accessibility, diagramType);
			StyleBlock.AppendStyleBlock(sb, font, strict, monoFont: monoFont);
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
			var contentWidth = TextMetrics.MeasureTextWidth(col.Title, HeaderFontSizePx, 700);
			// Reserve space for the count badge (rough estimate)
			contentWidth += TextMetrics.MeasureTextWidth(col.Tasks.Count.ToString(CultureInfo.InvariantCulture), MetaFontSizePx, 600) + (BadgePadX * 2) + 8;
			foreach (var task in col.Tasks)
			{
				// Card text is inset by PriorityBorderWidth when a priority border is present; measure with that offset
				var cardTextWidth = TextMetrics.MeasureTextWidth(task.Title, CardFontSizePx, 500);
				foreach (var meta in EnumerateMetaLines(task))
					cardTextWidth = Math.Max(cardTextWidth, TextMetrics.MeasureTextWidth(meta, MetaFontSizePx, 400));
				contentWidth = Math.Max(contentWidth, cardTextWidth + (HasPriorityBorder(task) ? PriorityBorderWidth : 0));
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
		StyleBlock.AppendStyleBlock(sb, font, strict, monoFont: monoFont);
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
		_ = sb.Append("\n<text x=\"").Append(cx.SvgFormat()).Append("\" y=\"").Append(y.SvgFormat())
			.Append("\" text-anchor=\"middle\" font-size=\"").Append(TitleFontSize)
			.Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, title.AsSpan());
		_ = sb.Append("</text>");
	}

	private static void AppendColumn(StringBuilder sb, KanbanColumn column, double x, double y, double width, double height)
	{
		// Column group (enables drop-shadow via .kanban-column CSS class)
		_ = sb.Append("\n<g class=\"kanban-column\">");

		// Column background — group-fill (very light) so cards lift off the surface
		_ = sb.Append("\n<rect x=\"").Append(x.SvgFormat()).Append("\" y=\"").Append(y.SvgFormat())
			.Append("\" width=\"").Append(width.SvgFormat()).Append("\" height=\"").Append(height.SvgFormat())
			.Append("\" rx=\"").Append(RenderConstants.Radii.Group)
			.Append("\" ry=\"").Append(RenderConstants.Radii.Group)
			.Append("\" fill=\"var(--_group-fill)\" stroke=\"var(--_group-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox.SvgFormat()).Append("\" />");

		// Header background — group-header color (top-rounded via two rects matching group rx=8)
		_ = sb.Append("\n<rect x=\"").Append(x.SvgFormat()).Append("\" y=\"").Append(y.SvgFormat())
			.Append("\" width=\"").Append(width.SvgFormat()).Append("\" height=\"").Append(HeaderHeight.SvgFormat())
			.Append("\" rx=\"").Append(RenderConstants.Radii.Group)
			.Append("\" ry=\"").Append(RenderConstants.Radii.Group)
			.Append("\" fill=\"var(--_group-hdr)\" />");
		_ = sb.Append("\n<rect x=\"").Append(x.SvgFormat()).Append("\" y=\"").Append((y + HeaderHeight - RenderConstants.Radii.Group).SvgFormat())
			.Append("\" width=\"").Append(width.SvgFormat()).Append("\" height=\"").Append(RenderConstants.Radii.Group)
			.Append("\" fill=\"var(--_group-hdr)\" />");

		// Divider line under header
		_ = sb.Append("\n<line x1=\"").Append(x.SvgFormat()).Append("\" y1=\"").Append((y + HeaderHeight).SvgFormat())
			.Append("\" x2=\"").Append((x + width).SvgFormat()).Append("\" y2=\"").Append((y + HeaderHeight).SvgFormat())
			.Append("\" stroke=\"var(--_group-stroke)\" stroke-width=\"1\" />");

		// Header title — --fs-m weight 700, neutral text color
		var headerTextX = x + ColumnPad;
		var headerCy = y + (HeaderHeight / 2);
		_ = sb.Append("\n<text x=\"").Append(headerTextX.SvgFormat()).Append("\" y=\"").Append(headerCy.SvgFormat())
			.Append("\" text-anchor=\"start\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(HeaderFontSize)
			.Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, column.Title.AsSpan());
		_ = sb.Append("</text>");

		// Count badge — small rounded pill, right-aligned in header
		AppendCountBadge(sb, column.Tasks.Count, x, y, width);

		_ = sb.Append("\n</g>");

		var cardY = y + HeaderHeight + ColumnPad;
		var cardW = width - (ColumnPad * 2);
		foreach (var task in column.Tasks)
		{
			var cardH = MeasureCardHeight(task);
			AppendCard(sb, task, x + ColumnPad, cardY, cardW, cardH);
			cardY += cardH + CardGap;
		}
	}

	private static void AppendCountBadge(StringBuilder sb, int count, double colX, double colY, double colWidth)
	{
		var countLabel = count.ToString(CultureInfo.InvariantCulture);
		var textW = TextMetrics.MeasureTextWidth(countLabel, MetaFontSizePx, 600);
		var badgeW = textW + (BadgePadX * 2);
		var badgeH = MetaFontSizePx + (BadgePadY * 2);
		var badgeX = colX + colWidth - ColumnPad - badgeW;
		var badgeCy = colY + (HeaderHeight / 2);
		var badgeY = badgeCy - (badgeH / 2);

		_ = sb.Append("\n<rect x=\"").Append(badgeX.SvgFormat()).Append("\" y=\"").Append(badgeY.SvgFormat())
			.Append("\" width=\"").Append(badgeW.SvgFormat()).Append("\" height=\"").Append(badgeH.SvgFormat())
			.Append("\" rx=\"").Append(BadgeRx).Append("\" ry=\"").Append(BadgeRx)
			.Append("\" fill=\"var(--_key-badge)\" />");

		_ = sb.Append("\n<text x=\"").Append((badgeX + (badgeW / 2)).SvgFormat()).Append("\" y=\"").Append(badgeCy.SvgFormat())
			.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(MetaFontSize)
			.Append("\" font-weight=\"600\" fill=\"var(--_text-sec)\">");
		MultilineUtils.AppendEscapedXml(sb, countLabel.AsSpan());
		_ = sb.Append("</text>");
	}

	private static void AppendCard(StringBuilder sb, KanbanTask task, double x, double y, double width, double height)
	{
		// Card group (enables drop-shadow via .kanban-card CSS class)
		_ = sb.Append("\n<g class=\"kanban-card\">");

		_ = sb.Append("\n<rect x=\"").Append(x.SvgFormat()).Append("\" y=\"").Append(y.SvgFormat())
			.Append("\" width=\"").Append(width.SvgFormat()).Append("\" height=\"").Append(height.SvgFormat())
			.Append("\" rx=\"").Append(RenderConstants.Radii.Rectangle)
			.Append("\" ry=\"").Append(RenderConstants.Radii.Rectangle)
			.Append("\" fill=\"var(--_node-fill)\" stroke=\"var(--_node-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox.SvgFormat()).Append("\" />");

		// Priority left border
		var priorityColor = PriorityColor(task.Priority);
		if (priorityColor is not null)
		{
			_ = sb.Append("\n<rect x=\"").Append(x.SvgFormat()).Append("\" y=\"").Append((y + 2).SvgFormat())
				.Append("\" width=\"").Append(PriorityBorderWidth.SvgFormat())
				.Append("\" height=\"").Append((height - 4).SvgFormat())
				.Append("\" rx=\"").Append(RenderConstants.Radii.Rectangle)
				.Append("\" ry=\"").Append(RenderConstants.Radii.Rectangle)
				.Append("\" fill=\"").Append(priorityColor).Append("\" />");
		}

		// Text inset accounts for priority border
		var textX = x + CardPadX + (priorityColor is not null ? PriorityBorderWidth : 0);
		var textY = y + CardPadY + (CardFontSizePx * 0.85);

		_ = sb.Append("\n<text x=\"").Append(textX.SvgFormat()).Append("\" y=\"").Append(textY.SvgFormat())
			.Append("\" font-size=\"").Append(CardFontSize)
			.Append("\" font-weight=\"500\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, task.Title.AsSpan());
		_ = sb.Append("</text>");

		var metaY = textY + CardFontSizePx + MetaLineGap + 2;
		foreach (var line in EnumerateMetaLines(task))
		{
			_ = sb.Append("\n<text x=\"").Append(textX.SvgFormat()).Append("\" y=\"").Append(metaY.SvgFormat())
				.Append("\" font-size=\"").Append(MetaFontSize)
				.Append("\" font-weight=\"400\" fill=\"var(--_text-muted)\">");
			MultilineUtils.AppendEscapedXml(sb, line.AsSpan());
			_ = sb.Append("</text>");
			metaY += MetaFontSizePx + MetaLineGap;
		}

		_ = sb.Append("\n</g>");
	}

	private static double MeasureCardHeight(KanbanTask task)
	{
		var h = (CardPadY * 2) + CardFontSizePx;
		var metaCount = 0;
		if (task.Ticket is { Length: > 0 })
			metaCount++;
		if (task.Assigned is { Length: > 0 })
			metaCount++;
		// Priority is shown as a border, not a text line — no longer counted here
		if (metaCount > 0)
			h += 4 + (metaCount * (MetaFontSizePx + MetaLineGap));
		return h;
	}

	private static bool HasPriorityBorder(KanbanTask task) =>
		PriorityColor(task.Priority) is not null;

	private static string? PriorityColor(string? priority) =>
		priority?.Trim().ToUpperInvariant() switch
		{
			"VERY HIGH" => PriorityVeryHigh,
			"HIGH" => PriorityHigh,
			"LOW" => PriorityLow,
			"VERY LOW" => PriorityVeryLow,
			_ => null,
		};

	private static IEnumerable<string> EnumerateMetaLines(KanbanTask task)
	{
		if (task.Ticket is { Length: > 0 })
			yield return task.Ticket;
		if (task.Assigned is { Length: > 0 })
			yield return task.Assigned;
		// Priority is conveyed by the left border, not a text line
	}

}
