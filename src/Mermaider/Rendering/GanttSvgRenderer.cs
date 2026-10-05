using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Gantt chart on the design system: colour = section family; done = family soft + outline, active = solid family base
/// with an accent ring, critical = failure role, planned = the family's solid mark. A neutral plot panel carries
/// <c>--_line-soft</c> gridlines; section names are eyebrows in family ink, task names body text; a legend states the rule.
/// </summary>
internal static class GanttSvgRenderer
{
	private const double Pad = 24;
	private const double TitleCy = Pad + 14;
	private const double HeaderHeight = 48;
	private const double PanelInsetX = 14;
	private const double PanelInsetTop = 10;
	private const double PanelInsetBottom = 20;
	private const double RowHeight = 36;
	private const double BarHeight = 20;
	private const double MinLabelColumn = 160;
	private const double LabelGap = 28;
	private const double ChartMinWidth = 480;
	private const double ChartRightInset = 12;
	private const double AxisLabelGap = 20;
	private const double AxisLabelBand = 32;
	private const double MilestoneR = 8;
	private const double MilestoneLabelGap = 8;
	private const double LegendGap = 18;
	private const double LegendSwatch = 12;
	private const double MinTickSpacing = 72;

	internal static string Render(GanttDiagram diagram, SvgRenderContext context)
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

	private enum TaskState
	{
		Planned,
		Done,
		Active,
		Critical,
	}

	internal static StringBuilder RenderToBuilder(GanttDiagram diagram, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var ds = DesignSystem.For(context);

		var hasTitle = diagram.Title is { Length: > 0 };

		var min = DateTime.MaxValue;
		var max = DateTime.MinValue;
		var taskCount = 0;
		var labelColumn = MinLabelColumn;
		var states = new HashSet<TaskState>();
		foreach (var section in diagram.Sections)
		{
			if (section.Name is { Length: > 0 } name)
				labelColumn = Math.Max(labelColumn, EyebrowWidth(name.ToUpperInvariant()) + 24 + LabelGap);

			foreach (var task in section.Tasks)
			{
				taskCount++;
				if (task.Start < min)
					min = task.Start;
				if (task.End > max)
					max = task.End;
				if (!IsMilestone(task))
					labelColumn = Math.Max(labelColumn, BodyWidth(task.Name) + LabelGap);

				_ = states.Add(Classify(task.Tags));
			}
		}

		if (taskCount == 0 || min == DateTime.MaxValue)
		{
			var emptyH = (hasTitle ? HeaderHeight : 0) + (Pad * 2) + 40;
			StyleBlock.AppendSvgOpenTag(sb, 400, emptyH, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
			StyleBlock.AppendStyleBlock(sb, context.Styles);
			if (hasTitle)
				ds.AppendTitle(sb, Pad, TitleCy, diagram.Title!);
			ds.Close(sb);
			return sb;
		}

		if (max <= min)
			max = min.AddDays(1);

		var chartWidth = Math.Max(ChartMinWidth, Math.Min(900, (max - min).TotalDays * 48));

		// ticks at least MinTickSpacing apart; the time domain ends on a whole tick so the last tick is labelled
		var tickDays = ChooseTick((max - min).TotalDays, chartWidth);
		max = min.AddDays(Math.Ceiling(((max - min).TotalDays / tickDays) - 1e-9) * tickDays);
		var span = max - min;

		// legend: only when there is more than one state to tell apart
		var legend = BuildLegend(states);
		var legendWidth = 0.0;
		foreach (var item in legend)
			legendWidth += LegendSwatch + 8 + DesignSystem.StateLegendLabelWidth(LegendLabel(item)) + LegendGap;
		if (legendWidth > 0)
			legendWidth -= LegendGap;

		var hasHeader = hasTitle || legend.Count > 0;
		var panelTop = hasHeader ? Pad + HeaderHeight : Pad;
		var panelX = Pad;
		var chartLeft = panelX + PanelInsetX + labelColumn;

		var contentHeight = 0.0;
		foreach (var section in diagram.Sections)
		{
			if (section.Name is { Length: > 0 })
				contentHeight += RowHeight;
			contentHeight += section.Tasks.Count * RowHeight;
		}

		// milestone labels sit right of their diamond and may run past the plot
		var overflow = 0.0;
		foreach (var section in diagram.Sections)
		{
			foreach (var task in section.Tasks)
			{
				if (!IsMilestone(task))
					continue;
				var frac = Math.Clamp((task.Start - min).TotalDays / span.TotalDays, 0, 1);
				var right = (frac * chartWidth) + MilestoneR + MilestoneLabelGap + BodyWidth(task.Name);
				overflow = Math.Max(overflow, right - chartWidth);
			}
		}

		var panelWidth = PanelInsetX + labelColumn + chartWidth + ChartRightInset + Math.Max(0, overflow);
		var panelHeight = PanelInsetTop + contentHeight + PanelInsetBottom;
		var headerWidth = (hasTitle ? DesignSystem.TitleIndent + TextMetrics.MeasureTextWidth(diagram.Title!, DesignSystem.Px(TypeRole.Title), 700) + 32 : 0) + legendWidth;
		var width = Math.Max(panelX + panelWidth + Pad, Pad + headerWidth + Pad);
		var height = panelTop + panelHeight + AxisLabelBand + Pad;

		StyleBlock.AppendSvgOpenTag(sb, width, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles);

		if (hasTitle)
			ds.AppendTitle(sb, Pad, TitleCy, diagram.Title!);

		if (legend.Count > 0)
			AppendLegend(sb, ds, legend, width - Pad - legendWidth, TitleCy);

		_ = sb.Append('\n');
		ds.AppendPanel(sb, panelX, panelTop, panelWidth, panelHeight);

		var plotBottom = panelTop + panelHeight;
		AppendTimeAxis(sb, ds, min, max, tickDays, chartLeft, panelTop, chartWidth, plotBottom);

		var y = panelTop + PanelInsetTop;
		var labelX = panelX + PanelInsetX;
		for (var s = 0; s < diagram.Sections.Count; s++)
		{
			var section = diagram.Sections[s];
			var family = ds.Cluster(s);
			_ = sb.Append("\n<g class=\"gantt-section\" data-section=\"").Append(s.ToString(CultureInfo.InvariantCulture)).Append('"');
			if (section.Name is { Length: > 0 })
			{
				_ = sb.Append(" data-label=\"");
				MultilineUtils.AppendEscapedAttr(sb, section.Name.AsSpan());
				_ = sb.Append('"');
			}

			_ = sb.Append('>');
			if (section.Name is { Length: > 0 })
			{
				var cy = y + (RowHeight / 2);
				var caps = section.Name.ToUpperInvariant();
				_ = sb.Append("\n  ");
				ds.AppendText(sb, caps, labelX, cy, TypeRole.Eyebrow, family.Ink, anchor: "start");
				var ruleStart = labelX + EyebrowWidth(caps) + 12;
				var ruleEnd = chartLeft - PanelInsetX;
				if (ruleEnd > ruleStart + 8)
				{
					_ = sb.Append("\n  <line x1=\"").Append(DesignSystem.Num(ruleStart)).Append("\" y1=\"").Append(DesignSystem.Num(cy))
						.Append("\" x2=\"").Append(DesignSystem.Num(ruleEnd)).Append("\" y2=\"").Append(DesignSystem.Num(cy))
						.Append("\" stroke=\"").Append(family.Edge).Append("\" stroke-width=\"1.25\" />");
				}

				y += RowHeight;
			}

			foreach (var task in section.Tasks)
			{
				AppendTaskRow(sb, ds, task, family, y, labelX, chartLeft, chartWidth, min, max);
				y += RowHeight;
			}

			_ = sb.Append("\n</g>");
		}

		ds.Close(sb);
		return sb;
	}

	private static double ChooseTick(double spanDays, double chartWidth)
	{
		foreach (var days in (ReadOnlySpan<double>)[1, 2, 7, 14, 30, 60, 90, 180, 365])
		{
			if (days / spanDays * chartWidth >= MinTickSpacing)
				return days;
		}

		return 365;
	}

	private static double EyebrowWidth(string caps) =>
		DesignSystem.XsWidth(caps, 600, letterSpacingPerChar: 1.0);

	private static double BodyWidth(string text) =>
		TextMetrics.MeasureTextWidth(text, DesignSystem.Px(TypeRole.Body), 400);

	private static bool IsMilestone(GanttTask task) =>
		(task.Tags & GanttTaskTags.Milestone) != 0;

	private static TaskState Classify(GanttTaskTags tags)
	{
		if ((tags & GanttTaskTags.Crit) != 0)
			return TaskState.Critical;
		if ((tags & GanttTaskTags.Active) != 0)
			return TaskState.Active;
		if ((tags & GanttTaskTags.Done) != 0)
			return TaskState.Done;
		return TaskState.Planned;
	}

	private static List<TaskState> BuildLegend(HashSet<TaskState> states)
	{
		var list = new List<TaskState>(4);
		if (states.Count < 2)
			return list;
		foreach (var s in (ReadOnlySpan<TaskState>)[TaskState.Done, TaskState.Active, TaskState.Critical, TaskState.Planned])
		{
			if (states.Contains(s))
				list.Add(s);
		}

		return list;
	}

	private static string LegendLabel(TaskState state) => state switch
	{
		TaskState.Done => "Done",
		TaskState.Active => "Active",
		TaskState.Critical => "Critical",
		_ => "Planned",
	};

	private static void AppendLegend(StringBuilder sb, DesignSystem ds, List<TaskState> legend, double x, double cy)
	{
		var p0 = ds.Cluster(0);
		var failure = ds.Role(ColorRole.Failure);
		_ = sb.Append("\n<g class=\"legend\">");
		foreach (var state in legend)
		{
			var (fill, stroke, ring) = state switch
			{
				TaskState.Done => (p0.Soft, p0.Stroke, false),
				TaskState.Active => (p0.Base, "none", true),
				TaskState.Critical => (ds.MarkFill(failure), ds.MarkStroke(failure), false),
				_ => (ds.MarkFill(p0), ds.MarkStroke(p0), false),
			};
			_ = sb.Append("\n  ");
			x += ds.AppendStateLegendItem(sb, x, cy, LegendLabel(state), fill, stroke, ring) + LegendGap;
		}

		_ = sb.Append("\n</g>");
	}

	private static void AppendTimeAxis(
		StringBuilder sb, DesignSystem ds, DateTime min, DateTime max, double tickDays,
		double chartLeft, double panelTop, double chartWidth, double plotBottom)
	{
		var span = max - min;

		_ = sb.Append("\n<g class=\"gantt-axis\">");
		for (var d = 0.0; d <= span.TotalDays + 0.001; d += tickDays)
		{
			var t = min.AddDays(d);
			var x = chartLeft + (d / span.TotalDays * chartWidth);
			_ = sb.Append("\n  ");
			ds.AppendGridLine(sb, x, panelTop + 8, x, plotBottom);

			var label = tickDays >= 28
				? t.ToString("MMM yyyy", CultureInfo.InvariantCulture)
				: t.ToString("MMM d", CultureInfo.InvariantCulture);
			_ = sb.Append("\n  ");
			ds.AppendText(sb, label, x, plotBottom + AxisLabelGap, TypeRole.Meta, anchor: "middle");
		}

		_ = sb.Append("\n  ");
		DesignSystem.AppendAxisLine(sb, chartLeft, plotBottom, chartLeft + chartWidth, plotBottom);
		_ = sb.Append("\n</g>");
	}

	private static void AppendTaskRow(
		StringBuilder sb, DesignSystem ds, GanttTask task, ColorFamily sectionFamily, double rowY, double labelX,
		double chartLeft, double chartWidth, DateTime min, DateTime max)
	{
		var span = (max - min).TotalDays;
		if (span <= 0)
			span = 1;

		var startFrac = Math.Clamp((task.Start - min).TotalDays / span, 0, 1);
		var endFrac = Math.Clamp((task.End - min).TotalDays / span, 0, 1);
		if (endFrac < startFrac)
			endFrac = startFrac;

		var x = chartLeft + (startFrac * chartWidth);
		var w = Math.Max((endFrac - startFrac) * chartWidth, 4);
		var cy = rowY + (RowHeight / 2);

		var isCrit = (task.Tags & GanttTaskTags.Crit) != 0;
		var isDone = (task.Tags & GanttTaskTags.Done) != 0;
		var isActive = (task.Tags & GanttTaskTags.Active) != 0;
		var family = isCrit ? ds.Role(ColorRole.Failure) : sectionFamily;

		// done = soft + outline; active = solid base (+ accent ring below); critical / planned = the family's solid mark
		var (fill, stroke) = isDone
			? (family.Soft, family.Stroke)
			: isActive
				? (family.Base, "none")
				: (ds.MarkFill(family), ds.MarkStroke(family));

		var state = isCrit ? "critical" : isActive ? "active" : isDone ? "done" : "planned";
		_ = sb.Append("\n<g class=\"gantt-task\" data-state=\"").Append(state).Append(IsMilestone(task) ? "\" data-milestone=\"true\">" : "\">");

		if (IsMilestone(task))
		{
			const double r = MilestoneR;
			if (isActive)
			{
				const double rr = r + DesignSystem.RingGap + 1;
				_ = sb.Append("\n  ");
				AppendDiamond(sb, x, cy, rr, "none", ColorFamily.AccentBase, DesignSystem.RingWidth);
			}

			_ = sb.Append("\n  ");
			AppendDiamond(sb, x, cy, r, fill, stroke, 1.25);
			_ = sb.Append("\n  ");
			ds.AppendText(sb, task.Name, x + r + MilestoneLabelGap, cy, TypeRole.Body, anchor: "start");
		}
		else
		{
			_ = sb.Append("\n  ");
			ds.AppendText(sb, task.Name, labelX, cy, TypeRole.Body, anchor: "start");

			var barY = cy - (BarHeight / 2);
			var radius = Math.Min(ds.BarRadius(BarHeight), w / 2);
			_ = sb.Append("\n  ");
			DesignSystem.AppendRect(sb, x, barY, w, BarHeight, radius, fill, stroke);
			if (isActive)
			{
				_ = sb.Append("\n  ");
				DesignSystem.AppendAccentRing(sb, x, barY, w, BarHeight, radius);
			}
		}

		_ = sb.Append("\n</g>");
	}

	private static void AppendDiamond(StringBuilder sb, double cx, double cy, double r, string fill, string stroke, double strokeWidth)
	{
		_ = sb.Append("<polygon points=\"")
			.Append(DesignSystem.Num(cx)).Append(',').Append(DesignSystem.Num(cy - r)).Append(' ')
			.Append(DesignSystem.Num(cx + r)).Append(',').Append(DesignSystem.Num(cy)).Append(' ')
			.Append(DesignSystem.Num(cx)).Append(',').Append(DesignSystem.Num(cy + r)).Append(' ')
			.Append(DesignSystem.Num(cx - r)).Append(',').Append(DesignSystem.Num(cy))
			.Append("\" fill=\"").Append(fill).Append('"');
		if (stroke != "none")
			_ = sb.Append(" stroke=\"").Append(stroke).Append("\" stroke-width=\"").Append(DesignSystem.Num(strokeWidth)).Append("\" stroke-linejoin=\"round\"");
		_ = sb.Append(" />");
	}
}
