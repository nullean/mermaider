using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class KanbanSvgRenderer
{
	private const double ColumnGap = 16;
	private const double CardGap = 8;
	private const double ColumnPad = 12;
	private const double ColumnBottomPad = 16;
	private const double CardPadY = 10;
	private const double CardDotX = 16;
	private const double CardTextX = 28;
	private const double CardPadRight = 12;
	private const double MetaGap = 4;
	private const double MinColumnWidth = 176;
	private const double MaxColumnWidth = 280;

	internal static string Render(KanbanDiagram diagram, SvgRenderContext context)
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

	internal static StringBuilder RenderToBuilder(KanbanDiagram diagram, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var ds = DesignSystem.For(context);
		var margin = DesignSystem.BoardMargin;

		var hasTitle = diagram.Title is { Length: > 0 };
		var top = DesignSystem.BoardTop(hasTitle);
		var titleW = hasTitle ? DesignSystem.TitleIndent + TextMetrics.MeasureTextWidth(diagram.Title!, DesignSystem.Px(TypeRole.Title), 700) : 0;

		if (diagram.Columns.Count == 0)
		{
			StyleBlock.AppendSvgOpenTag(sb, Math.Max(200, titleW + (margin * 2)), top + 60, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
			StyleBlock.AppendStyleBlock(sb, context.Styles);
			if (hasTitle)
				ds.AppendTitle(sb, margin, DesignSystem.BoardTitleCy, diagram.Title!);
			ds.Close(sb);
			return sb;
		}

		var columnWidths = new double[diagram.Columns.Count];
		var maxColumnHeight = 0.0;

		for (var i = 0; i < diagram.Columns.Count; i++)
		{
			var col = diagram.Columns[i];
			// header: chip / strip title (measured at the heaviest weight any preset uses) + count badge
			var headerW = TextMetrics.MeasureTextWidth(col.Title, DesignSystem.Px(TypeRole.Subheading), 700) + 34
				+ TextMetrics.MeasureTextWidth("[" + col.Tasks.Count.ToString(CultureInfo.InvariantCulture) + "]", DesignSystem.Px(TypeRole.Tag), 600) + 12 + 12;
			var contentWidth = headerW - (ColumnPad * 2);
			foreach (var task in col.Tasks)
			{
				var textW = TextMetrics.MeasureTextWidth(task.Title, DesignSystem.Px(TypeRole.Label), 600);
				foreach (var meta in MetaLines(task))
					textW = Math.Max(textW, TextMetrics.MeasureTextWidth(meta, DesignSystem.Px(TypeRole.Meta), 400));
				contentWidth = Math.Max(contentWidth, (textW / 0.94) + CardTextX + CardPadRight);
			}

			var colW = Math.Clamp(contentWidth + (ColumnPad * 2), MinColumnWidth, MaxColumnWidth);
			columnWidths[i] = colW;

			var cardsH = 0.0;
			foreach (var task in col.Tasks)
				cardsH += CardHeight(task, colW - (ColumnPad * 2)) + CardGap;
			if (col.Tasks.Count > 0)
				cardsH -= CardGap;

			maxColumnHeight = Math.Max(maxColumnHeight, DesignSystem.BoardHeaderSpace + cardsH + ColumnBottomPad);
		}

		var totalWidth = (margin * 2) + columnWidths.Sum() + (ColumnGap * (columnWidths.Length - 1));
		totalWidth = Math.Max(totalWidth, titleW + (margin * 2));
		var height = top + maxColumnHeight + margin;

		StyleBlock.AppendSvgOpenTag(sb, totalWidth, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles);

		if (hasTitle)
			ds.AppendTitle(sb, margin, DesignSystem.BoardTitleCy, diagram.Title!);

		// Columns are the shared container; their families alternate p1, p2 … in document order and the cards carry them.
		var x = margin;
		for (var i = 0; i < diagram.Columns.Count; i++)
		{
			AppendColumn(sb, ds, diagram.Columns[i], x, top, columnWidths[i], maxColumnHeight, ds.Cluster(i + 1));
			x += columnWidths[i] + ColumnGap;
		}

		ds.Close(sb);
		return sb;
	}

	private static void AppendColumn(StringBuilder sb, DesignSystem ds, KanbanColumn column, double x, double y, double width, double height, ColorFamily family)
	{
		var data = new StringBuilder("data-id=\"");
		MultilineUtils.AppendEscapedAttr(data, column.Id.AsSpan());
		_ = data.Append("\" data-count=\"").Append(column.Tasks.Count.ToString(CultureInfo.InvariantCulture)).Append('"');
		ds.AppendContainerBody(sb, x, y, width, height, family, 0, "kanban-column", data.ToString());
		ds.AppendContainerHeader(sb, x, y, width, column.Title, family, count: column.Tasks.Count.ToString(CultureInfo.InvariantCulture));

		var cardY = y + DesignSystem.BoardHeaderSpace;
		var cardW = width - (ColumnPad * 2);
		foreach (var task in column.Tasks)
		{
			var cardH = CardHeight(task, cardW);
			AppendCard(sb, ds, task, x + ColumnPad, cardY, cardW, cardH, family);
			cardY += cardH + CardGap;
		}
	}

	/// <summary>The card outline: a hairline on paper (Quiet), the line colour (Blueprint), none (Tonal: the card floats).</summary>
	private static string CardStroke(DesignSystem ds) => ds.Spec.OutlineWidth <= 0
		? "none"
		: ds.Spec.Fill == FillKind.Knockout ? "var(--_line)" : "var(--_line-soft)";

	private static void AppendCard(StringBuilder sb, DesignSystem ds, KanbanTask task, double x, double y, double width, double height, ColorFamily family)
	{
		// .kanban-card keeps the node elevation rule
		_ = sb.Append("\n<g class=\"kanban-card\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, task.Id.AsSpan());
		_ = sb.Append('"');
		if (Priority(task) is { } p)
			_ = sb.Append(" data-priority=\"").Append(p.Word.ToLowerInvariant().Replace(' ', '-')).Append('"');
		_ = sb.Append(">\n  ");

		var r = DesignSystem.Num(ds.Spec.NodeRadius);
		_ = sb.Append("<rect x=\"").Append(x.SvgFormat()).Append("\" y=\"").Append(y.SvgFormat())
			.Append("\" width=\"").Append(width.SvgFormat()).Append("\" height=\"").Append(height.SvgFormat())
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"var(--bg)\" stroke=\"").Append(CardStroke(ds)).Append("\" stroke-width=\"").Append(ds.NodeStrokeWidth).Append("\" />");

		var lines = TitleLines(task, width);
		var lineH = DesignSystem.TextHeight(1, TypeRole.Label);
		var firstCy = y + CardPadY + (lineH / 2);

		// the column family dot leads the title
		_ = sb.Append("\n  ");
		DesignSystem.AppendDot(sb, x + CardDotX, firstCy, family.Stroke);
		var textX = x + CardTextX;
		for (var li = 0; li < lines.Count; li++)
		{
			_ = sb.Append("\n  ");
			ds.AppendText(sb, lines[li], textX, firstCy + (li * lineH), TypeRole.Label, anchor: "start");
		}

		var metaH = DesignSystem.TextHeight(1, TypeRole.Meta);
		var metaCy = y + CardPadY + (lines.Count * lineH) + MetaGap + (metaH / 2);
		foreach (var meta in MetaLines(task))
		{
			_ = sb.Append("\n  ");
			ds.AppendText(sb, meta, textX, metaCy, TypeRole.Meta, anchor: "start");
			metaCy += metaH;
		}

		// priority: a role dot and the word, so it never reads through colour alone
		if (Priority(task) is { } priority)
		{
			var role = priority.Role is { } rr ? ds.Role(rr) : ds.Neutral;
			_ = sb.Append("\n  ");
			DesignSystem.AppendDot(sb, textX + 3.5, metaCy, role.Stroke);
			_ = sb.Append("\n  ");
			ds.AppendText(sb, priority.Word, textX + 12, metaCy, TypeRole.Tag, role.Ink, anchor: "start");
		}

		_ = sb.Append("\n</g>");
	}

	private static List<string> TitleLines(KanbanTask task, double cardWidth) =>
		DesignSystem.Wrap(task.Title, cardWidth - CardTextX - CardPadRight, TypeRole.Label);

	private static double CardHeight(KanbanTask task, double cardWidth)
	{
		var h = (CardPadY * 2) + DesignSystem.TextHeight(TitleLines(task, cardWidth).Count, TypeRole.Label);
		var metaCount = MetaLines(task).Count() + (Priority(task) is null ? 0 : 1);
		if (metaCount > 0)
			h += MetaGap + (metaCount * DesignSystem.TextHeight(1, TypeRole.Meta));
		return Math.Max(38, h);
	}

	/// <summary>
	/// Priority reads through the role families (very high = failure, high = warning, medium = info, low = success, very low =
	/// neutral) and always carries its word.
	/// </summary>
	private static (string Word, ColorRole? Role)? Priority(KanbanTask task) =>
		task.Priority?.Trim().ToUpperInvariant() switch
		{
			"VERY HIGH" => ("Very high", ColorRole.Failure),
			"HIGH" => ("High", ColorRole.Warning),
			"MEDIUM" => ("Medium", ColorRole.Info),
			"LOW" => ("Low", ColorRole.Success),
			"VERY LOW" => ("Very low", null),
			_ => null,
		};

	private static IEnumerable<string> MetaLines(KanbanTask task)
	{
		if (task.Ticket is { Length: > 0 })
			yield return task.Ticket;
		if (task.Assigned is { Length: > 0 })
			yield return task.Assigned;
	}
}
