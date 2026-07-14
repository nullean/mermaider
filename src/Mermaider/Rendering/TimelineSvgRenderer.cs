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
	private const string TitleFontSize = RenderConstants.FsVar.L;
	private const string PeriodFontSize = RenderConstants.FsVar.S;
	private const string EventFontSize = RenderConstants.FsVar.S;
	private const string SectionFontSize = RenderConstants.FsVar.S;

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
			var color = SectionColors[sectionColorIndex % SectionColors.Length];

			if (section.Name is { Length: > 0 })
			{
				_ = sb.Append("\n<rect x=\"").Append((sectionStartX - SectionPadX).SvgFormat())
					.Append("\" y=\"").Append((axisTop - 30).SvgFormat())
					.Append("\" width=\"").Append((sectionWidth + (SectionPadX * 2)).SvgFormat())
					.Append("\" height=\"").Append((height - axisTop + 20).SvgFormat())
					.Append("\" rx=\"6\" ry=\"6\" fill=\"").Append(color)
					.Append("\" opacity=\"0.08\" />");

				_ = sb.Append("\n<text x=\"").Append((sectionStartX + (sectionWidth / 2)).SvgFormat())
					.Append("\" y=\"").Append((axisTop - 16).SvgFormat())
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
		_ = sb.Append("\n<line x1=\"").Append(lineStartX.SvgFormat()).Append("\" y1=\"").Append(axisTop.SvgFormat())
			.Append("\" x2=\"").Append(lineEndX.SvgFormat()).Append("\" y2=\"").Append(axisTop.SvgFormat())
			.Append("\" stroke=\"var(--_line)\" stroke-width=\"2\" />");

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static void AppendPeriod(StringBuilder sb, TimelinePeriod period, double cx, double axisTop, string color)
	{
		_ = sb.Append("\n<circle cx=\"").Append(cx.SvgFormat()).Append("\" cy=\"").Append(axisTop.SvgFormat())
			.Append("\" r=\"").Append(MarkerRadius)
			.Append("\" fill=\"").Append(color).Append("\" stroke=\"var(--bg)\" stroke-width=\"2\" />");

		_ = sb.Append("\n<text x=\"").Append(cx.SvgFormat()).Append("\" y=\"").Append((axisTop - 14).SvgFormat())
			.Append("\" text-anchor=\"middle\" font-size=\"").Append(PeriodFontSize)
			.Append("\" font-weight=\"600\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, period.Label.AsSpan());
		_ = sb.Append("</text>");

		var eventY = axisTop + 30;
		foreach (var evt in period.Events)
		{
			var boxX = cx - (PeriodWidth / 2) + 10;
			var boxW = PeriodWidth - 20;

			_ = sb.Append("\n<rect x=\"").Append(boxX.SvgFormat()).Append("\" y=\"").Append(eventY.SvgFormat())
				.Append("\" width=\"").Append(boxW.SvgFormat()).Append("\" height=\"").Append(EventBoxHeight)
				.Append("\" rx=\"6\" ry=\"6\" fill=\"").Append(color)
				.Append("\" opacity=\"0.15\" />");

			_ = sb.Append("\n<text x=\"").Append(cx.SvgFormat()).Append("\" y=\"").Append((eventY + (EventBoxHeight / 2)).SvgFormat())
				.Append("\" text-anchor=\"middle\" dy=\"0.35em\" font-size=\"").Append(EventFontSize)
				.Append("\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, evt.AsSpan());
			_ = sb.Append("</text>");

			eventY += EventBoxHeight + EventGap;
		}
	}

}
