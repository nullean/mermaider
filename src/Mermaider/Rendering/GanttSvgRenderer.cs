using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class GanttSvgRenderer
{
	private const double LeftPad = 16;
	private const double RightPad = 24;
	private const double TopPad = 16;
	private const double BottomPad = 40;
	private const double LabelWidth = 180;
	private const double RowHeight = 36;
	private const double MilestoneRowHeight = 24;
	private const double BarHeight = 20;
	private const double SectionHeaderHeight = 28;
	private const double TitleHeight = 36;
	private const double AxisHeight = 28;
	private const double ChartMinWidth = 480;
	private const double MilestoneDiamondR = 7.0;
	private const double MilestoneLabelGap = 6.0;

	private const string TitleFontSize = RenderConstants.FsVar.L;
	private const string LabelFontSize = RenderConstants.FsVar.S;
	private const string AxisFontSize = RenderConstants.FsVar.Xs;

	private static readonly string ColorDefault = CategoricalPalette.Blue;
	private const string ColorDone = "var(--_text-muted)";
	private const string ColorActive = "var(--_arrow)";
	private static readonly string ColorCrit = CategoricalPalette.Red;
	private static readonly string ColorCritDone = CategoricalPalette.Purple;
	private static readonly string ColorMilestone = CategoricalPalette.Yellow;

	internal static string Render(GanttDiagram diagram, DiagramColors colors, string font, string? monoFont = null, bool transparent = false, StrictStylingOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
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

	internal static StringBuilder RenderToBuilder(GanttDiagram diagram, DiagramColors colors, string font, string? monoFont = null, bool transparent = false, StrictStylingOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		var hasTitle = diagram.Title is { Length: > 0 };
		var titleOffset = hasTitle ? TitleHeight : 0;

		var min = DateTime.MaxValue;
		var max = DateTime.MinValue;
		var taskCount = 0;
		foreach (var section in diagram.Sections)
		{
			foreach (var task in section.Tasks)
			{
				taskCount++;
				if (task.Start < min)
					min = task.Start;
				if (task.End > max)
					max = task.End;
			}
		}

		if (taskCount == 0 || min == DateTime.MaxValue)
		{
			var emptyH = titleOffset + TopPad + BottomPad + 40;
			StyleBlock.AppendSvgOpenTag(sb, 400, emptyH, colors, transparent, accessibility, diagramType);
			StyleBlock.AppendStyleBlock(sb, font, strict, monoFont: monoFont);
			if (hasTitle)
				AppendTitle(sb, diagram.Title!, 200);
			_ = sb.Append("\n</svg>");
			return sb;
		}

		if (max <= min)
			max = min.AddDays(1);

		var span = max - min;
		var chartWidth = Math.Max(ChartMinWidth, Math.Min(900, span.TotalDays * 48));
		var width = LeftPad + LabelWidth + chartWidth + RightPad;

		var contentHeight = 0.0;
		foreach (var section in diagram.Sections)
		{
			if (section.Name is { Length: > 0 })
				contentHeight += SectionHeaderHeight;
			foreach (var task in section.Tasks)
				contentHeight += IsMilestone(task) ? MilestoneRowHeight : RowHeight;
		}

		var height = TopPad + titleOffset + contentHeight + AxisHeight + BottomPad;
		var chartLeft = LeftPad + LabelWidth;
		var chartTop = TopPad + titleOffset;

		StyleBlock.AppendSvgOpenTag(sb, width, height, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict, monoFont: monoFont);
		_ = sb.Append("\n<defs>\n</defs>\n");

		if (hasTitle)
			AppendTitle(sb, diagram.Title!, width / 2);

		AppendTimeAxis(sb, min, max, chartLeft, chartTop, chartWidth, contentHeight);

		var y = chartTop;
		foreach (var section in diagram.Sections)
		{
			if (section.Name is { Length: > 0 })
			{
				_ = sb.Append("\n<text x=\"").Append(LeftPad.SvgFormat())
					.Append("\" y=\"").Append((y + (SectionHeaderHeight * 0.65)).SvgFormat())
					.Append("\" font-size=\"").Append(LabelFontSize)
					.Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
				MultilineUtils.AppendEscapedXml(sb, section.Name.AsSpan());
				_ = sb.Append("</text>");
				y += SectionHeaderHeight;
			}

			foreach (var task in section.Tasks)
			{
				var rowH = IsMilestone(task) ? MilestoneRowHeight : RowHeight;
				AppendTaskRow(sb, task, y, rowH, chartLeft, chartWidth, min, max);
				y += rowH;
			}
		}

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static bool IsMilestone(GanttTask task) =>
		(task.Tags & GanttTaskTags.Milestone) != 0;

	private static void AppendTitle(StringBuilder sb, string title, double centerX)
	{
		_ = sb.Append("\n<text x=\"").Append(centerX.SvgFormat())
			.Append("\" y=\"24\" text-anchor=\"middle\" font-size=\"")
			.Append(TitleFontSize).Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, title.AsSpan());
		_ = sb.Append("</text>");
	}

	private static void AppendTimeAxis(
		StringBuilder sb, DateTime min, DateTime max,
		double chartLeft, double chartTop, double chartWidth, double contentHeight)
	{
		var span = max - min;
		var tickDays = span.TotalDays switch
		{
			<= 14 => 1.0,
			<= 60 => 7.0,
			<= 180 => 14.0,
			_ => 30.0,
		};

		var axisY = chartTop + contentHeight + 4;

		_ = sb.Append("\n<line x1=\"").Append(chartLeft.SvgFormat()).Append("\" y1=\"").Append(axisY.SvgFormat())
			.Append("\" x2=\"").Append((chartLeft + chartWidth).SvgFormat()).Append("\" y2=\"").Append(axisY.SvgFormat())
			.Append("\" stroke=\"var(--_line)\" stroke-width=\"1\" />");

		_ = sb.Append("\n<rect x=\"").Append(chartLeft.SvgFormat()).Append("\" y=\"").Append(chartTop.SvgFormat())
			.Append("\" width=\"").Append(chartWidth.SvgFormat()).Append("\" height=\"").Append(contentHeight.SvgFormat())
			.Append("\" fill=\"var(--_node-fill)\" opacity=\"0.5\" />");

		for (var d = 0.0; d <= span.TotalDays + 0.001; d += tickDays)
		{
			var t = min.AddDays(d);
			var frac = d / span.TotalDays;
			var x = chartLeft + (frac * chartWidth);
			_ = sb.Append("\n<line x1=\"").Append(x.SvgFormat()).Append("\" y1=\"").Append(chartTop.SvgFormat())
				.Append("\" x2=\"").Append(x.SvgFormat()).Append("\" y2=\"").Append(axisY.SvgFormat())
				.Append("\" stroke=\"var(--_line)\" stroke-width=\"0.5\" opacity=\"0.35\" />");

			var label = tickDays >= 28
				? t.ToString("MMM yyyy", CultureInfo.InvariantCulture)
				: t.ToString("MMM d", CultureInfo.InvariantCulture);
			_ = sb.Append("\n<text x=\"").Append(x.SvgFormat()).Append("\" y=\"").Append((axisY + 16).SvgFormat())
				.Append("\" text-anchor=\"middle\" font-size=\"").Append(AxisFontSize)
				.Append("\" fill=\"var(--_text-muted)\">");
			MultilineUtils.AppendEscapedXml(sb, label.AsSpan());
			_ = sb.Append("</text>");
		}
	}

	private static void AppendTaskRow(
		StringBuilder sb, GanttTask task, double rowY, double rowH,
		double chartLeft, double chartWidth, DateTime min, DateTime max)
	{
		var span = (max - min).TotalDays;
		if (span <= 0)
			span = 1;

		var startFrac = (task.Start - min).TotalDays / span;
		var endFrac = (task.End - min).TotalDays / span;
		startFrac = Math.Clamp(startFrac, 0, 1);
		endFrac = Math.Clamp(endFrac, 0, 1);
		if (endFrac < startFrac)
			endFrac = startFrac;

		var x = chartLeft + (startFrac * chartWidth);
		var w = Math.Max((endFrac - startFrac) * chartWidth, 4);
		var fill = ColorFor(task.Tags);

		if (IsMilestone(task))
		{
			// Row tint band
			_ = sb.Append("\n<rect x=\"").Append(chartLeft.SvgFormat()).Append("\" y=\"").Append(rowY.SvgFormat())
				.Append("\" width=\"").Append(chartWidth.SvgFormat()).Append("\" height=\"").Append(rowH.SvgFormat())
				.Append("\" fill=\"").Append(ColorMilestone).Append("\" opacity=\"0.12\" />");

			var cx = x + (Math.Max(w, 4) / 2);
			var cy = rowY + (rowH / 2);
			const double r = MilestoneDiamondR;
			_ = sb.Append("\n<polygon points=\"")
				.Append(cx.SvgFormat()).Append(',').Append((cy - r).SvgFormat()).Append(' ')
				.Append((cx + r).SvgFormat()).Append(',').Append(cy.SvgFormat()).Append(' ')
				.Append(cx.SvgFormat()).Append(',').Append((cy + r).SvgFormat()).Append(' ')
				.Append((cx - r).SvgFormat()).Append(',').Append(cy.SvgFormat())
				.Append("\" fill=\"").Append(fill).Append("\" />");

			// Inline label to the right of the diamond
			var labelX = cx + r + MilestoneLabelGap;
			_ = sb.Append("\n<text x=\"").Append(labelX.SvgFormat())
				.Append("\" y=\"").Append(cy.SvgFormat())
				.Append("\" text-anchor=\"start\" dy=\"").Append(RenderConstants.TextBaselineShift)
				.Append("\" font-size=\"").Append(LabelFontSize)
				.Append("\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, task.Name.AsSpan());
			_ = sb.Append("</text>");
		}
		else
		{
			// Left-column label for normal tasks
			_ = sb.Append("\n<text x=\"").Append(LeftPad.SvgFormat())
				.Append("\" y=\"").Append((rowY + (rowH * 0.6)).SvgFormat())
				.Append("\" font-size=\"").Append(LabelFontSize)
				.Append("\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, task.Name.AsSpan());
			_ = sb.Append("</text>");

			var barY = rowY + ((rowH - BarHeight) / 2);
			_ = sb.Append("\n<rect x=\"").Append(x.SvgFormat()).Append("\" y=\"").Append(barY.SvgFormat())
				.Append("\" width=\"").Append(w.SvgFormat()).Append("\" height=\"").Append(BarHeight)
				.Append("\" rx=\"4\" ry=\"4\" fill=\"").Append(fill).Append("\" />");
		}
	}

	private static string ColorFor(GanttTaskTags tags)
	{
		var isCrit = (tags & GanttTaskTags.Crit) != 0;
		var isDone = (tags & GanttTaskTags.Done) != 0;
		var isActive = (tags & GanttTaskTags.Active) != 0;
		var isMilestone = (tags & GanttTaskTags.Milestone) != 0;

		if (isMilestone)
			return ColorMilestone;
		if (isCrit && isDone)
			return ColorCritDone;
		if (isCrit)
			return ColorCrit;
		if (isDone)
			return ColorDone;
		if (isActive)
			return ColorActive;
		return ColorDefault;
	}

}
