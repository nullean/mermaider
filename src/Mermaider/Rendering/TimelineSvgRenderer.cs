using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class TimelineSvgRenderer
{
	private const double PeriodWidth = 160;
	private const double PeriodGap = 24;
	private const double MinBoxHeight = 36;
	private const double BoxPadY = 8;
	private const double BoxPadX = 12;
	private const double EventGap = 8;
	private const double SectionPad = 12;
	private const double SectionGap = 12;
	private const double AxisGap = 40;
	private const double EventsBelowAxis = 32;
	private const double MomentRadius = 6;

	private readonly record struct Column(TimelinePeriod Period, double X, ColorFamily Family);

	internal static string Render(TimelineDiagram diagram, SvgRenderContext context)
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

	internal static StringBuilder RenderToBuilder(TimelineDiagram diagram, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var ds = DesignSystem.For(context);
		var margin = DesignSystem.BoardMargin;

		var hasTitle = diagram.Title is { Length: > 0 };
		var top = DesignSystem.BoardTop(hasTitle);
		var titleW = hasTitle ? DesignSystem.TitleIndent + TextMetrics.MeasureTextWidth(diagram.Title!, DesignSystem.Px(TypeRole.Title), 700) : 0;

		var totalPeriods = diagram.Sections.Sum(s => s.Periods.Count);
		if (totalPeriods == 0)
		{
			StyleBlock.AppendSvgOpenTag(sb, Math.Max(200, titleW + (margin * 2)), top + 60, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
			StyleBlock.AppendStyleBlock(sb, context.Styles);
			if (hasTitle)
				ds.AppendTitle(sb, margin, DesignSystem.BoardTitleCy, diagram.Title!);
			ds.Close(sb);
			return sb;
		}

		// horizontal layout: a named section is the shared container around its periods; its family alternates p1, p2 … in
		// document order. Periods outside any section each take the next family.
		var anyNamed = diagram.Sections.Any(s => s.Name is { Length: > 0 });
		var columns = new List<Column>(totalPeriods);
		var sections = new List<(double X, double W, string Name, ColorFamily Family)>();
		var familyIndex = 1;
		var x = margin;
		foreach (var section in diagram.Sections)
		{
			if (section.Periods.Count == 0)
				continue;
			var named = section.Name is { Length: > 0 };
			if (named)
			{
				var family = ds.Cluster(familyIndex++);
				var sx = x;
				x += SectionPad;
				foreach (var period in section.Periods)
				{
					columns.Add(new Column(period, x, family));
					x += PeriodWidth + PeriodGap;
				}

				x = x - PeriodGap + SectionPad;
				sections.Add((sx, x - sx, section.Name!, family));
				x += SectionGap;
			}
			else
			{
				foreach (var period in section.Periods)
				{
					columns.Add(new Column(period, x, ds.Cluster(familyIndex++)));
					x += PeriodWidth + PeriodGap;
				}
			}
		}

		var contentRight = x - Math.Max(SectionGap, PeriodGap);

		var periodLines = columns.Select(c => DesignSystem.Wrap(c.Period.Label, PeriodWidth - (BoxPadX * 2), TypeRole.Label, 700)).ToList();
		var periodH = Math.Max(MinBoxHeight, (periodLines.Max(l => l.Count) * LineHeight(TypeRole.Label)) + (BoxPadY * 2));
		var periodTop = anyNamed ? top + DesignSystem.BoardHeaderSpace : top;
		var axisY = periodTop + periodH + AxisGap;
		var eventTop = axisY + EventsBelowAxis;
		var eventsBottom = eventTop;
		foreach (var c in columns)
		{
			var y = eventTop;
			foreach (var evt in c.Period.Events)
				y += EventHeight(evt) + EventGap;
			eventsBottom = Math.Max(eventsBottom, y - (c.Period.Events.Count > 0 ? EventGap : 0));
		}

		var bottom = eventsBottom + 16;
		var width = Math.Max(contentRight + margin, titleW + (margin * 2));
		var height = bottom + margin;

		StyleBlock.AppendSvgOpenTag(sb, width, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles);

		if (hasTitle)
			ds.AppendTitle(sb, margin, DesignSystem.BoardTitleCy, diagram.Title!);

		foreach (var (sx, sw, name, family) in sections)
		{
			var data = new StringBuilder("data-label=\"");
			MultilineUtils.AppendEscapedAttr(data, name.AsSpan());
			_ = data.Append('"');
			ds.AppendContainerBody(sb, sx, top, sw, bottom - top, family, 0, "subgraph", data.ToString());
		}

		foreach (var (sx, sw, name, family) in sections)
			ds.AppendContainerHeader(sb, sx, top, sw, name, family);

		// drop-lines through each moment, under everything else
		foreach (var c in columns)
		{
			var cx = c.X + (PeriodWidth / 2);
			_ = sb.Append("\n<line x1=\"").Append(cx.SvgFormat()).Append("\" y1=\"").Append((periodTop + periodH).SvgFormat())
				.Append("\" x2=\"").Append(cx.SvgFormat()).Append("\" y2=\"").Append(eventsBottom.SvgFormat())
				.Append("\" stroke=\"").Append(c.Family.Edge).Append("\" stroke-width=\"1.25\" stroke-dasharray=\"").Append(DesignSystem.DashArray).Append("\" />");
		}

		// the time axis is the accent story line
		var axisEnd = contentRight + 8;
		ds.AppendStoryLine(sb, [new Point(margin, axisY), new Point(axisEnd - 6, axisY)], "timeline-axis");
		ds.AppendStoryArrow(sb, axisEnd, axisY);

		for (var i = 0; i < columns.Count; i++)
			AppendPeriod(sb, ds, columns[i], periodLines[i], periodTop, periodH, axisY, eventTop);

		ds.Close(sb);
		return sb;
	}

	private static double LineHeight(TypeRole role) => DesignSystem.TextHeight(1, role);

	private static void AppendPeriod(StringBuilder sb, DesignSystem ds, Column column, List<string> labelLines, double periodTop, double periodH, double axisY, double eventTop)
	{
		var cx = column.X + (PeriodWidth / 2);
		var family = column.Family;

		_ = sb.Append("\n<g class=\"node timeline-period\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, column.Period.Label.AsSpan());
		_ = sb.Append("\">\n  ");
		ds.AppendBox(sb, column.X, periodTop, PeriodWidth, periodH, family);
		_ = sb.Append("\n  ");
		ds.AppendText(sb, string.Join('\n', labelLines), cx, periodTop + (periodH / 2), TypeRole.Label, weight: ds.Spec.HeadingWeight);
		_ = sb.Append("\n</g>");

		ds.AppendMoment(sb, cx, axisY, family, MomentRadius);

		var eventY = eventTop;
		foreach (var evt in column.Period.Events)
		{
			var boxH = EventHeight(evt);
			_ = sb.Append("\n<g class=\"node timeline-event\">\n  ");
			ds.AppendBox(sb, column.X, eventY, PeriodWidth, boxH, family);
			_ = sb.Append("\n  ");
			ds.AppendText(sb, string.Join('\n', EventLines(evt)), cx, eventY + (boxH / 2), TypeRole.Body);
			_ = sb.Append("\n</g>");
			eventY += boxH + EventGap;
		}
	}

	private static double EventHeight(string evt) =>
		Math.Max(MinBoxHeight, (EventLines(evt).Count * LineHeight(TypeRole.Body)) + (BoxPadY * 2));

	private static List<string> EventLines(string text) => DesignSystem.Wrap(text, PeriodWidth - (BoxPadX * 2), TypeRole.Body);
}
