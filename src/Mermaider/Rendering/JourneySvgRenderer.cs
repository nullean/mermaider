using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Renders a user journey as horizontal task columns with score bars (1–5),
/// section bands, and actor labels — Mermaider theme CSS vars throughout.
/// </summary>
internal static class JourneySvgRenderer
{
	private const double LeftPad = 40;
	private const double RightPad = 24;
	private const double TopPad = 16;
	private const double BottomPad = 28;
	private const double TitleHeight = 36;
	private const double SectionLabelHeight = 22;
	private const double ScoreAxisMax = 5.0;
	private const double BarMaxHeight = 120;
	private const double BarWidth = 36;
	private const double ColumnWidth = 110;
	private const double ColumnGap = 12;
	private const double TaskLabelHeight = 40;
	private const double ActorLabelHeight = 28;
	private const double ScoreAxisWidth = 28;

	private const string TitleFontSize = RenderConstants.FsVar.L;
	private const string LabelFontSize = RenderConstants.FsVar.S;
	private const string SmallFontSize = RenderConstants.FsVar.Xs;

	// Score 1 → red … score 5 → green (same family as other chart palettes)
	private static readonly string[] ScoreColors =
	[
		"#e15759", // 1
		"#f28e2b", // 2
		"#edc948", // 3
		"#76b7b2", // 4
		"#59a14f", // 5
	];

	private static readonly string[] SectionBandColors =
	[
		"#4e79a7", "#f28e2b", "#e15759", "#76b7b2",
		"#59a14f", "#edc948", "#b07aa1", "#ff9da7",
	];

	internal static string Render(JourneyDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
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

	internal static StringBuilder RenderToBuilder(JourneyDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		var hasTitle = diagram.Title is { Length: > 0 };
		var titleOffset = hasTitle ? TitleHeight : 0;

		var totalTasks = 0;
		foreach (var section in diagram.Sections)
			totalTasks += section.Tasks.Count;

		if (totalTasks == 0)
		{
			var emptyH = titleOffset + TopPad + BottomPad + 40;
			StyleBlock.AppendSvgOpenTag(sb, 320, emptyH, colors, transparent, accessibility, diagramType);
			StyleBlock.AppendStyleBlock(sb, font, strict);
			if (hasTitle)
				AppendTitle(sb, diagram.Title!, 160);
			_ = sb.Append("\n</svg>");
			return sb;
		}

		var chartLeft = LeftPad + ScoreAxisWidth;
		var chartWidth = (totalTasks * (ColumnWidth + ColumnGap)) - ColumnGap;
		var width = chartLeft + chartWidth + RightPad;

		var plotTop = TopPad + titleOffset + SectionLabelHeight;
		var plotHeight = BarMaxHeight;
		var taskLabelTop = plotTop + plotHeight + 12;
		var height = taskLabelTop + TaskLabelHeight + ActorLabelHeight + BottomPad;

		StyleBlock.AppendSvgOpenTag(sb, width, height, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict);
		_ = sb.Append("\n<defs>\n</defs>\n");

		if (hasTitle)
			AppendTitle(sb, diagram.Title!, width / 2);

		// Score axis (1–5)
		for (var s = 1; s <= 5; s++)
		{
			var frac = s / ScoreAxisMax;
			var y = plotTop + plotHeight - (frac * plotHeight);
			_ = sb.Append("\n<line x1=\"").Append(F(chartLeft - 4)).Append("\" y1=\"").Append(F(y))
				.Append("\" x2=\"").Append(F(chartLeft + chartWidth)).Append("\" y2=\"").Append(F(y))
				.Append("\" stroke=\"var(--_line)\" stroke-width=\"0.5\" opacity=\"0.25\" />");
			_ = sb.Append("\n<text x=\"").Append(F(LeftPad + 4)).Append("\" y=\"").Append(F(y + 4))
				.Append("\" font-size=\"").Append(SmallFontSize)
				.Append("\" fill=\"var(--_text-muted)\">").Append(s).Append("</text>");
		}

		// Baseline
		_ = sb.Append("\n<line x1=\"").Append(F(chartLeft)).Append("\" y1=\"").Append(F(plotTop + plotHeight))
			.Append("\" x2=\"").Append(F(chartLeft + chartWidth)).Append("\" y2=\"").Append(F(plotTop + plotHeight))
			.Append("\" stroke=\"var(--_line)\" stroke-width=\"1.5\" />");

		var col = 0;
		var sectionIndex = 0;
		foreach (var section in diagram.Sections)
		{
			if (section.Tasks.Count == 0)
				continue;

			var sectionStartX = chartLeft + (col * (ColumnWidth + ColumnGap));
			var sectionWidth = (section.Tasks.Count * (ColumnWidth + ColumnGap)) - ColumnGap;
			var bandColor = SectionBandColors[sectionIndex % SectionBandColors.Length];

			if (section.Name is { Length: > 0 })
			{
				_ = sb.Append("\n<rect x=\"").Append(F(sectionStartX - 4))
					.Append("\" y=\"").Append(F(plotTop - SectionLabelHeight))
					.Append("\" width=\"").Append(F(sectionWidth + 8))
					.Append("\" height=\"").Append(F(plotHeight + SectionLabelHeight + TaskLabelHeight + ActorLabelHeight + 8))
					.Append("\" rx=\"6\" ry=\"6\" fill=\"").Append(bandColor)
					.Append("\" opacity=\"0.08\" />");

				_ = sb.Append("\n<text x=\"").Append(F(sectionStartX + (sectionWidth / 2)))
					.Append("\" y=\"").Append(F(plotTop - 8))
					.Append("\" text-anchor=\"middle\" font-size=\"").Append(LabelFontSize)
					.Append("\" font-weight=\"600\" fill=\"").Append(bandColor).Append("\">");
				MultilineUtils.AppendEscapedXml(sb, section.Name.AsSpan());
				_ = sb.Append("</text>");
			}

			foreach (var task in section.Tasks)
			{
				var cx = chartLeft + (col * (ColumnWidth + ColumnGap)) + (ColumnWidth / 2);
				AppendTask(sb, task, cx, plotTop, plotHeight, taskLabelTop);
				col++;
			}

			sectionIndex++;
		}

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static void AppendTitle(StringBuilder sb, string title, double centerX)
	{
		_ = sb.Append("\n<text x=\"").Append(F(centerX))
			.Append("\" y=\"24\" text-anchor=\"middle\" font-size=\"")
			.Append(TitleFontSize).Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, title.AsSpan());
		_ = sb.Append("</text>");
	}

	private static void AppendTask(StringBuilder sb, JourneyTask task, double cx, double plotTop, double plotHeight, double taskLabelTop)
	{
		var score = Math.Clamp(task.Score, 1, 5);
		var frac = score / ScoreAxisMax;
		var barH = frac * plotHeight;
		var barY = plotTop + plotHeight - barH;
		var barX = cx - (BarWidth / 2);
		var color = ScoreColors[score - 1];

		_ = sb.Append("\n<rect x=\"").Append(F(barX)).Append("\" y=\"").Append(F(barY))
			.Append("\" width=\"").Append(F(BarWidth)).Append("\" height=\"").Append(F(Math.Max(barH, 2)))
			.Append("\" rx=\"4\" ry=\"4\" fill=\"").Append(color).Append("\" />");

		// Score value on bar
		_ = sb.Append("\n<text x=\"").Append(F(cx)).Append("\" y=\"").Append(F(barY - 6))
			.Append("\" text-anchor=\"middle\" font-size=\"").Append(SmallFontSize)
			.Append("\" font-weight=\"600\" fill=\"var(--_text)\">").Append(score).Append("</text>");

		// Task name (wrap-ish: single line truncated visually via full text)
		_ = sb.Append("\n<text x=\"").Append(F(cx)).Append("\" y=\"").Append(F(taskLabelTop + 14))
			.Append("\" text-anchor=\"middle\" font-size=\"").Append(LabelFontSize)
			.Append("\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, task.Name.AsSpan());
		_ = sb.Append("</text>");

		if (task.Actors.Count > 0)
		{
			var actors = string.Join(", ", task.Actors);
			_ = sb.Append("\n<text x=\"").Append(F(cx)).Append("\" y=\"").Append(F(taskLabelTop + 30))
				.Append("\" text-anchor=\"middle\" font-size=\"").Append(SmallFontSize)
				.Append("\" fill=\"var(--_text-muted)\">");
			MultilineUtils.AppendEscapedXml(sb, actors.AsSpan());
			_ = sb.Append("</text>");
		}
	}

	private static string F(double value) =>
		value.ToString("0.##", CultureInfo.InvariantCulture);
}
