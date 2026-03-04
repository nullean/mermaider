using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class TimelineSvgRenderer
{
	private const double PeriodWidth = 160;
	private const double PeriodGap = 20;
	private const double EventBoxHeight = 28;
	private const double EventGap = 6;
	private const double TimelineY = 80;
	private const double MarkerRadius = 8;
	private const double EventStartY = 110;
	private const double SectionPadX = 10;
	private const double SectionPadY = 10;
	private const double TitleFontSize = 16;
	private const double PeriodFontSize = 13;
	private const double EventFontSize = 12;
	private const double SectionFontSize = 11;

	private static readonly string[] SectionColors =
	[
		"#4e79a7", "#f28e2b", "#e15759", "#76b7b2",
		"#59a14f", "#edc948", "#b07aa1", "#ff9da7",
	];

	internal static string Render(TimelineDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
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

	internal static StringBuilder RenderToBuilder(TimelineDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		var hasTitle = diagram.Title is { Length: > 0 };
		var titleOffset = hasTitle ? 40.0 : 0;

		var totalPeriods = 0;
		var maxEvents = 0;
		foreach (var section in diagram.Sections)
		{
			totalPeriods += section.Periods.Count;
			foreach (var period in section.Periods)
			{
				if (period.Events.Count > maxEvents)
					maxEvents = period.Events.Count;
			}
		}

		if (totalPeriods == 0)
		{
			StyleBlock.AppendSvgOpenTag(sb, 200, 100, colors, transparent, accessibility, diagramType);
			StyleBlock.AppendStyleBlock(sb, font, strict);
			_ = sb.Append("\n</svg>");
			return sb;
		}

		var width = 40 + (totalPeriods * (PeriodWidth + PeriodGap)) + 20;
		var eventAreaHeight = (maxEvents * (EventBoxHeight + EventGap)) + 20;
		var height = titleOffset + TimelineY + 50 + eventAreaHeight + SectionPadY;

		StyleBlock.AppendSvgOpenTag(sb, width, height, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict);
		_ = sb.Append("\n<defs>\n</defs>\n");

		if (hasTitle)
		{
			_ = sb.Append("\n<text x=\"").Append(F(width / 2))
				.Append("\" y=\"28\" text-anchor=\"middle\" font-size=\"")
				.Append(TitleFontSize).Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, diagram.Title!.AsSpan());
			_ = sb.Append("</text>");
		}

		var axisTop = titleOffset + TimelineY;

		var periodIndex = 0;
		var sectionColorIndex = 0;

		foreach (var section in diagram.Sections)
		{
			var sectionStartX = 40 + (periodIndex * (PeriodWidth + PeriodGap));
			var sectionWidth = (section.Periods.Count * (PeriodWidth + PeriodGap)) - PeriodGap;
			var color = SectionColors[sectionColorIndex % SectionColors.Length];

			if (section.Name is { Length: > 0 })
			{
				_ = sb.Append("\n<rect x=\"").Append(F(sectionStartX - SectionPadX))
					.Append("\" y=\"").Append(F(axisTop - 30))
					.Append("\" width=\"").Append(F(sectionWidth + (SectionPadX * 2)))
					.Append("\" height=\"").Append(F(height - axisTop + 20))
					.Append("\" rx=\"6\" ry=\"6\" fill=\"").Append(color)
					.Append("\" opacity=\"0.08\" />");

				_ = sb.Append("\n<text x=\"").Append(F(sectionStartX + (sectionWidth / 2)))
					.Append("\" y=\"").Append(F(axisTop - 16))
					.Append("\" text-anchor=\"middle\" font-size=\"").Append(SectionFontSize)
					.Append("\" font-weight=\"600\" fill=\"").Append(color).Append("\">");
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
		_ = sb.Append("\n<line x1=\"").Append(F(lineStartX)).Append("\" y1=\"").Append(F(axisTop))
			.Append("\" x2=\"").Append(F(lineEndX)).Append("\" y2=\"").Append(F(axisTop))
			.Append("\" stroke=\"var(--_line)\" stroke-width=\"2\" />");

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static void AppendPeriod(StringBuilder sb, TimelinePeriod period, double cx, double axisTop, string color)
	{
		_ = sb.Append("\n<circle cx=\"").Append(F(cx)).Append("\" cy=\"").Append(F(axisTop))
			.Append("\" r=\"").Append(MarkerRadius)
			.Append("\" fill=\"").Append(color).Append("\" stroke=\"var(--bg)\" stroke-width=\"2\" />");

		_ = sb.Append("\n<text x=\"").Append(F(cx)).Append("\" y=\"").Append(F(axisTop - 14))
			.Append("\" text-anchor=\"middle\" font-size=\"").Append(PeriodFontSize)
			.Append("\" font-weight=\"600\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, period.Label.AsSpan());
		_ = sb.Append("</text>");

		var eventY = axisTop + 30;
		foreach (var evt in period.Events)
		{
			var boxX = cx - (PeriodWidth / 2) + 10;
			var boxW = PeriodWidth - 20;

			_ = sb.Append("\n<rect x=\"").Append(F(boxX)).Append("\" y=\"").Append(F(eventY))
				.Append("\" width=\"").Append(F(boxW)).Append("\" height=\"").Append(EventBoxHeight)
				.Append("\" rx=\"6\" ry=\"6\" fill=\"").Append(color)
				.Append("\" opacity=\"0.15\" />");

			_ = sb.Append("\n<text x=\"").Append(F(cx)).Append("\" y=\"").Append(F(eventY + (EventBoxHeight / 2)))
				.Append("\" text-anchor=\"middle\" dy=\"0.35em\" font-size=\"").Append(EventFontSize)
				.Append("\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, evt.AsSpan());
			_ = sb.Append("</text>");

			eventY += EventBoxHeight + EventGap;
		}
	}

	private static string F(double value) =>
		value.ToString("0.##", CultureInfo.InvariantCulture);
}
