using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class TimelineSvgRenderer
{
	private const double PeriodWidth = 180;
	private const double PeriodGap = 24;
	private const double PeriodBoxHeight = 36;
	private const double EventBoxHeight = 28;
	private const double EventLineHeight = 18;
	private const double EventFontPx = 14;
	private const double EventGap = 6;
	private const double MarkerRadius = 6;
	private const double SectionPadX = 8;
	private const double LeftMargin = 40;
	private const string TitleFontSize = RenderConstants.FsVar.L;
	private const string PeriodFontSize = RenderConstants.FsVar.S;
	private const string EventFontSize = RenderConstants.FsVar.S;
	private const string SectionFontSize = RenderConstants.FsVar.S;

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

		var hasTitle = diagram.Title is { Length: > 0 };
		var titleOffset = hasTitle ? 40.0 : 0;

		var totalPeriods = diagram.Sections.Sum(s => s.Periods.Count);
		if (totalPeriods == 0)
		{
			StyleBlock.AppendSvgOpenTag(sb, 200, 100, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
			StyleBlock.AppendStyleBlock(sb, context.Styles.Font, context.Styles.Strict, context.Styles.FontScale, context.Styles.MonoFont);
			_ = sb.Append("\n</svg>");
			return sb;
		}

		var anyNamed = diagram.Sections.Any(s => s.Name is { Length: > 0 });
		var eventArea = 20.0;
		foreach (var period in diagram.Sections.SelectMany(s => s.Periods))
			eventArea = Math.Max(eventArea, period.Events.Sum(e => EventHeight(e) + EventGap) + 20);

		// vertical layout: [section titles] period boxes, the axis below them, then the events
		var groupTop = titleOffset + 4;
		var periodTop = titleOffset + (anyNamed ? 38 : 12);
		var axisY = periodTop + PeriodBoxHeight + 28;
		var eventTop = axisY + 26;
		var bottom = eventTop + eventArea;

		var width = LeftMargin + (totalPeriods * (PeriodWidth + PeriodGap)) + 16;
		var height = bottom + 16;

		StyleBlock.AppendSvgOpenTag(sb, width, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles.Font, context.Styles.Strict, context.Styles.FontScale, context.Styles.MonoFont);
		_ = sb.Append("\n<defs>\n  <marker id=\"timeline-arrow\" refX=\"6\" refY=\"3\" markerWidth=\"8\" markerHeight=\"6\" orient=\"auto\">")
			.Append("\n    <path d=\"M 0,0 V 6 L8,3 Z\" fill=\"var(--_line)\" />")
			.Append("\n  </marker>\n</defs>\n");

		if (hasTitle)
		{
			_ = sb.Append("\n<text x=\"").Append((width / 2).SvgFormat())
				.Append("\" y=\"28\" text-anchor=\"middle\" font-size=\"")
				.Append(TitleFontSize).Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, diagram.Title.AsSpan());
			_ = sb.Append("</text>");
		}

		// Same language as the other diagrams: a named section is a tinted group whose colour wins;
		// periods outside any section each take the next colour of the auto palette.
		var palette = context.Styles.Colors.AutoPalette();
		var periodColors = new List<string>();
		var groups = new List<(double X, double W, string Name, string Color)>();
		var colorIndex = 0;
		foreach (var section in diagram.Sections)
		{
			var named = section.Name is { Length: > 0 };
			var sectionColor = palette[colorIndex % palette.Length];
			if (named)
			{
				groups.Add((
					LeftMargin + (periodColors.Count * (PeriodWidth + PeriodGap)) - SectionPadX,
					(section.Periods.Count * (PeriodWidth + PeriodGap)) - PeriodGap + (SectionPadX * 2),
					section.Name!,
					sectionColor));
				colorIndex++;
			}

			foreach (var unused in section.Periods)
			{
				periodColors.Add(named ? sectionColor : palette[colorIndex % palette.Length]);
				if (!named)
					colorIndex++;
			}
		}

		foreach (var (gx, gw, name, color) in groups)
		{
			_ = sb.Append("\n<rect x=\"").Append(gx.SvgFormat()).Append("\" y=\"").Append(groupTop.SvgFormat())
				.Append("\" width=\"").Append(gw.SvgFormat()).Append("\" height=\"").Append((bottom - groupTop).SvgFormat())
				.Append("\" rx=\"").Append(RenderConstants.Radii.Group).Append("\" ry=\"").Append(RenderConstants.Radii.Group)
				.Append("\" fill=\"").Append(VisualLanguage.GroupFill(color, 0))
				.Append("\" stroke=\"").Append(VisualLanguage.GroupBorder(color)).Append("\" stroke-width=\"")
				.Append(RenderConstants.StrokeWidths.OuterBox.SvgFormat()).Append("\" />");

			_ = sb.Append("\n<text x=\"").Append((gx + (gw / 2)).SvgFormat()).Append("\" y=\"").Append((groupTop + 18).SvgFormat())
				.Append("\" text-anchor=\"middle\" font-size=\"").Append(SectionFontSize)
				.Append("\" font-weight=\"600\" fill=\"").Append(VisualLanguage.GroupBorder(color)).Append("\">");
			MultilineUtils.AppendEscapedXml(sb, name.AsSpan());
			_ = sb.Append("</text>");
		}

		// subtle drop-lines through each moment, under everything else
		var index = 0;
		foreach (var section in diagram.Sections)
		{
			foreach (var unused in section.Periods)
			{
				var cx = LeftMargin + (index * (PeriodWidth + PeriodGap)) + (PeriodWidth / 2);
				_ = sb.Append("\n<line x1=\"").Append(cx.SvgFormat()).Append("\" y1=\"").Append((periodTop + PeriodBoxHeight).SvgFormat())
					.Append("\" x2=\"").Append(cx.SvgFormat()).Append("\" y2=\"").Append((bottom - 6).SvgFormat())
					.Append("\" stroke=\"").Append(VisualLanguage.Tint(periodColors[index], 45))
					.Append("\" stroke-width=\"1.5\" stroke-dasharray=\"4 4\" />");
				index++;
			}
		}

		// the axis: its own line below the period titles
		_ = sb.Append("\n<line x1=\"").Append((LeftMargin - 12).SvgFormat()).Append("\" y1=\"").Append(axisY.SvgFormat())
			.Append("\" x2=\"").Append((width - 8).SvgFormat()).Append("\" y2=\"").Append(axisY.SvgFormat())
			.Append("\" stroke=\"var(--_line)\" stroke-width=\"3\" marker-end=\"url(#timeline-arrow)\" />");

		index = 0;
		foreach (var section in diagram.Sections)
		{
			foreach (var period in section.Periods)
			{
				var cx = LeftMargin + (index * (PeriodWidth + PeriodGap)) + (PeriodWidth / 2);
				AppendPeriod(sb, period, cx, periodTop, axisY, eventTop, periodColors[index]);
				index++;
			}
		}

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static void AppendPeriod(StringBuilder sb, TimelinePeriod period, double cx, double periodTop, double axisY, double eventTop, string color)
	{
		var border = VisualLanguage.Border(color);
		var boxX = cx - (PeriodWidth / 2);

		// the period title: a tinted box with the stronger header tint
		_ = sb.Append("\n<rect x=\"").Append(boxX.SvgFormat()).Append("\" y=\"").Append(periodTop.SvgFormat())
			.Append("\" width=\"").Append(PeriodWidth.SvgFormat()).Append("\" height=\"").Append(PeriodBoxHeight.SvgFormat())
			.Append("\" rx=\"").Append(RenderConstants.Radii.Rectangle).Append("\" ry=\"").Append(RenderConstants.Radii.Rectangle)
			.Append("\" fill=\"").Append(VisualLanguage.Tint(color, VisualLanguage.HeaderTint))
			.Append("\" stroke=\"").Append(border).Append("\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox.SvgFormat()).Append("\" />");

		_ = sb.Append("\n<text x=\"").Append(cx.SvgFormat()).Append("\" y=\"").Append((periodTop + (PeriodBoxHeight / 2)).SvgFormat())
			.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(PeriodFontSize)
			.Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, period.Label.AsSpan());
		_ = sb.Append("</text>");

		// the moment on the axis
		_ = sb.Append("\n<circle cx=\"").Append(cx.SvgFormat()).Append("\" cy=\"").Append(axisY.SvgFormat())
			.Append("\" r=\"").Append(MarkerRadius)
			.Append("\" fill=\"").Append(VisualLanguage.Tint(color, VisualLanguage.HeaderTint))
			.Append("\" stroke=\"").Append(border).Append("\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox.SvgFormat()).Append("\" />");

		var eventY = eventTop;
		foreach (var evt in period.Events)
		{
			var evX = cx - (PeriodWidth / 2) + 10;
			var evW = PeriodWidth - 20;
			var lines = EventLines(evt);
			var boxH = EventHeight(evt);

			_ = sb.Append("\n<rect x=\"").Append(evX.SvgFormat()).Append("\" y=\"").Append(eventY.SvgFormat())
				.Append("\" width=\"").Append(evW.SvgFormat()).Append("\" height=\"").Append(boxH.SvgFormat())
				.Append("\" rx=\"").Append(RenderConstants.Radii.Rectangle).Append("\" ry=\"").Append(RenderConstants.Radii.Rectangle)
				.Append("\" fill=\"").Append(VisualLanguage.Tint(color, VisualLanguage.NodeTint))
				.Append("\" stroke=\"").Append(border).Append("\" stroke-width=\"")
				.Append(RenderConstants.StrokeWidths.OuterBox.SvgFormat()).Append("\" />");

			var textY = eventY + (boxH / 2) - ((lines.Count - 1) * EventLineHeight / 2);
			for (var i = 0; i < lines.Count; i++)
			{
				_ = sb.Append("\n<text x=\"").Append(cx.SvgFormat()).Append("\" y=\"").Append((textY + (i * EventLineHeight)).SvgFormat())
					.Append("\" text-anchor=\"middle\" dy=\"0.35em\" font-size=\"").Append(EventFontSize)
					.Append("\" fill=\"var(--_text)\">");
				MultilineUtils.AppendEscapedXml(sb, lines[i].AsSpan());
				_ = sb.Append("</text>");
			}

			eventY += boxH + EventGap;
		}
	}

	private static double EventHeight(string evt) =>
		Math.Max(EventBoxHeight, (EventLines(evt).Count * EventLineHeight) + 10);

	// Long events wrap inside their box; browsers set text a little wider than TextMetrics, so wrap a bit early.
	private static List<string> EventLines(string text)
	{
		var maxW = (PeriodWidth - 20 - 12) * 0.95;
		var lines = new List<string>();
		var current = "";
		foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			var candidate = current.Length == 0 ? word : current + " " + word;
			if (current.Length > 0 && TextMetrics.MeasureTextWidth(candidate, EventFontPx, 400) > maxW)
			{
				lines.Add(current);
				current = word;
			}
			else
			{
				current = candidate;
			}
		}

		if (current.Length > 0)
			lines.Add(current);
		return lines.Count == 0 ? [""] : lines;
	}
}
