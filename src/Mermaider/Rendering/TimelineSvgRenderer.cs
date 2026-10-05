using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class TimelineSvgRenderer
{
	private const double PeriodWidth = 180;
	private const double PeriodGap = 24;
	private const double EventBoxHeight = 28;
	private const double EventLineHeight = 18;
	private const double EventFontPx = 14;
	private const double EventGap = 6;
	private const double TimelineY = 80;
	private const double MarkerRadius = 8;
	private const double EventStartY = 110;
	private const double SectionPadX = 8;
	private const double SectionPadY = 10;
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

		var totalPeriods = 0;
		foreach (var section in diagram.Sections)
		{
			totalPeriods += section.Periods.Count;
		}

		if (totalPeriods == 0)
		{
			StyleBlock.AppendSvgOpenTag(sb, 200, 100, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
			StyleBlock.AppendStyleBlock(sb, context.Styles.Font, context.Styles.Strict, context.Styles.FontScale, context.Styles.MonoFont);
			_ = sb.Append("\n</svg>");
			return sb;
		}

		var width = 40 + (totalPeriods * (PeriodWidth + PeriodGap)) + 20;
		var eventAreaHeight = 20.0;
		foreach (var section in diagram.Sections)
		{
			foreach (var period in section.Periods)
			{
				var h = period.Events.Sum(e => EventHeight(e) + EventGap) + 20;
				if (h > eventAreaHeight)
					eventAreaHeight = h;
			}
		}

		var height = titleOffset + TimelineY + 66 + eventAreaHeight + SectionPadY;

		StyleBlock.AppendSvgOpenTag(sb, width, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles.Font, context.Styles.Strict, context.Styles.FontScale, context.Styles.MonoFont);
		_ = sb.Append("\n<defs>\n</defs>\n");

		if (hasTitle)
		{
			_ = sb.Append("\n<text x=\"").Append((width / 2).SvgFormat())
				.Append("\" y=\"28\" text-anchor=\"middle\" font-size=\"")
				.Append(TitleFontSize).Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, diagram.Title.AsSpan());
			_ = sb.Append("</text>");
		}

		var axisTop = titleOffset + TimelineY;

		var periodIndex = 0;
		var sectionColorIndex = 0;

		foreach (var section in diagram.Sections)
		{
			var sectionStartX = 40 + (periodIndex * (PeriodWidth + PeriodGap));
			var sectionWidth = (section.Periods.Count * (PeriodWidth + PeriodGap)) - PeriodGap;
			var palette = context.Styles.Colors.AutoPalette();
			var color = palette[sectionColorIndex % palette.Length];

			if (section.Name is { Length: > 0 })
			{
				_ = sb.Append("\n<rect x=\"").Append((sectionStartX - SectionPadX).SvgFormat())
					.Append("\" y=\"").Append((axisTop - 30).SvgFormat())
					.Append("\" width=\"").Append((sectionWidth + (SectionPadX * 2)).SvgFormat())
					.Append("\" height=\"").Append((height - axisTop + 20 - 8).SvgFormat())
					.Append("\" rx=\"").Append(RenderConstants.Radii.Group).Append("\" ry=\"").Append(RenderConstants.Radii.Group)
					.Append("\" fill=\"").Append(VisualLanguage.GroupFill(color, 0))
					.Append("\" stroke=\"").Append(VisualLanguage.GroupBorder(color)).Append("\" stroke-width=\"")
					.Append(RenderConstants.StrokeWidths.OuterBox.SvgFormat()).Append("\" />");

				_ = sb.Append("\n<text x=\"").Append((sectionStartX + (sectionWidth / 2)).SvgFormat())
					.Append("\" y=\"").Append((axisTop - 14).SvgFormat())
					.Append("\" text-anchor=\"middle\" font-size=\"").Append(SectionFontSize)
					.Append("\" font-weight=\"600\" fill=\"").Append(VisualLanguage.GroupBorder(color)).Append("\">");
				MultilineUtils.AppendEscapedXml(sb, section.Name.AsSpan());
				_ = sb.Append("</text>");
			}

			foreach (var period in section.Periods)
			{
				var cx = 40 + (periodIndex * (PeriodWidth + PeriodGap)) + (PeriodWidth / 2);
				AppendPeriod(sb, period, cx, axisTop, color);
				periodIndex++;
			}

			sectionColorIndex++;
		}

		var lineStartX = 40 + (PeriodWidth / 2) - 10;
		var lineEndX = 40 + ((totalPeriods - 1) * (PeriodWidth + PeriodGap)) + (PeriodWidth / 2) + 10;
		_ = sb.Append("\n<line x1=\"").Append(lineStartX.SvgFormat()).Append("\" y1=\"").Append(axisTop.SvgFormat())
			.Append("\" x2=\"").Append(lineEndX.SvgFormat()).Append("\" y2=\"").Append(axisTop.SvgFormat())
			.Append("\" stroke=\"var(--_line)\" stroke-width=\"2\" />");

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static void AppendPeriod(StringBuilder sb, TimelinePeriod period, double cx, double axisTop, string color)
	{
		var border = VisualLanguage.Border(color);
		_ = sb.Append("\n<circle cx=\"").Append(cx.SvgFormat()).Append("\" cy=\"").Append(axisTop.SvgFormat())
			.Append("\" r=\"").Append(MarkerRadius)
			.Append("\" fill=\"").Append(VisualLanguage.Tint(color, VisualLanguage.HeaderTint))
			.Append("\" stroke=\"").Append(border).Append("\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox.SvgFormat()).Append("\" />");

		_ = sb.Append("\n<text x=\"").Append(cx.SvgFormat()).Append("\" y=\"").Append((axisTop + 28).SvgFormat())
			.Append("\" text-anchor=\"middle\" font-size=\"").Append(PeriodFontSize)
			.Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, period.Label.AsSpan());
		_ = sb.Append("</text>");

		var eventY = axisTop + 44;
		foreach (var evt in period.Events)
		{
			var boxX = cx - (PeriodWidth / 2) + 10;
			var boxW = PeriodWidth - 20;
			var lines = EventLines(evt);
			var boxH = EventHeight(evt);

			_ = sb.Append("\n<rect x=\"").Append(boxX.SvgFormat()).Append("\" y=\"").Append(eventY.SvgFormat())
				.Append("\" width=\"").Append(boxW.SvgFormat()).Append("\" height=\"").Append(boxH.SvgFormat())
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
