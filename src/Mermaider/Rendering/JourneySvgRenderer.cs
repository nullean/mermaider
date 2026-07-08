using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Renders a user journey in the mermaid.js layout: section headers, task boxes with
/// actor dots, a horizontal timeline, and face markers hung by score (1–5).
/// Theme text/chrome via CSS vars; actor/score accents use a fixed chart palette.
/// </summary>
internal static class JourneySvgRenderer
{
	private const double LeftPad = 16;
	private const double RightPad = 32;
	private const double TopPad = 16;
	private const double BottomPad = 24;
	private const double TitleHeight = 36;
	private const double LegendRowHeight = 18;
	private const double SectionBoxHeight = 28;
	private const double SectionToTaskGap = 10;
	private const double TaskBoxHeight = 36;
	private const double TaskBoxMinWidth = 88;
	private const double TaskBoxPadX = 14;
	private const double TaskGap = 16;
	private const double SectionGap = 28;
	private const double TimelineGap = 28;
	private const double FaceTrackHeight = 130;
	private const double FaceRadius = 14;
	private const double ActorDotR = 5;
	private const double MaxScore = 5.0;

	private const string TitleFontSize = RenderConstants.FsVar.L;
	private const string LabelFontSize = RenderConstants.FsVar.S;
	private const string SmallFontSize = RenderConstants.FsVar.Xs;

	private static readonly string[] ActorPalette =
	[
		"#59a14f", "#4e79a7", "#f28e2b", "#e15759",
		"#b07aa1", "#76b7b2", "#edc948", "#ff9da7",
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

		// Collect actors (first-seen order) and task column metrics
		var actorColors = BuildActorColors(diagram);
		var legendHeight = actorColors.Count > 0
			? Math.Max(actorColors.Count * LegendRowHeight, 8)
			: 0;

		var flat = Flatten(diagram);
		if (flat.Count == 0)
		{
			var emptyH = titleOffset + TopPad + BottomPad + 48;
			StyleBlock.AppendSvgOpenTag(sb, 360, emptyH, colors, transparent, accessibility, diagramType);
			StyleBlock.AppendStyleBlock(sb, font, strict);
			if (hasTitle)
				AppendTitle(sb, diagram.Title!, 180);
			_ = sb.Append("\n</svg>");
			return sb;
		}

		// Column centers: each task gets a box; width from label estimate
		var columns = new List<Column>(flat.Count);
		var legendWidth = legendHeight > 0 ? 70.0 : 0;
		var x = LeftPad + legendWidth + 12;

		for (var i = 0; i < flat.Count; i++)
		{
			var item = flat[i];
			if (i > 0 && item.IsSectionStart)
				x += SectionGap - TaskGap;

			var boxW = Math.Max(TaskBoxMinWidth, EstimateTextWidth(item.Task.Name) + (TaskBoxPadX * 2));
			var cx = x + (boxW / 2);
			columns.Add(new Column(item, cx, boxW, x));
			x += boxW + TaskGap;
		}

		var contentRight = x - TaskGap;
		var width = contentRight + RightPad;
		// Ensure room for title / legend
		width = Math.Max(width, LeftPad + legendWidth + 280 + RightPad);

		var sectionY = TopPad + titleOffset + 4;
		var taskY = sectionY + SectionBoxHeight + SectionToTaskGap;
		var timelineY = taskY + TaskBoxHeight + TimelineGap;
		var faceTop = timelineY + 8;
		var height = faceTop + FaceTrackHeight + BottomPad;

		StyleBlock.AppendSvgOpenTag(sb, width, height, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict);
		_ = sb.Append("\n<defs>\n</defs>\n");

		if (hasTitle)
			AppendTitle(sb, diagram.Title!, width / 2);

		AppendActorLegend(sb, actorColors, LeftPad, TopPad + titleOffset);

		// Section headers spanning their tasks
		var sectionIndex = 0;
		foreach (var section in diagram.Sections)
		{
			if (section.Tasks.Count == 0 || section.Name is not { Length: > 0 })
			{
				sectionIndex++;
				continue;
			}

			var first = columns.FindIndex(c => c.Item.SectionIndex == sectionIndex);
			var last = columns.FindLastIndex(c => c.Item.SectionIndex == sectionIndex);
			if (first < 0 || last < 0)
			{
				sectionIndex++;
				continue;
			}

			var left = columns[first].BoxLeft;
			var right = columns[last].BoxLeft + columns[last].BoxWidth;
			var secW = right - left;
			var secCx = left + (secW / 2);

			_ = sb.Append("\n<rect x=\"").Append(F(left)).Append("\" y=\"").Append(F(sectionY))
				.Append("\" width=\"").Append(F(secW)).Append("\" height=\"").Append(SectionBoxHeight)
				.Append("\" rx=\"6\" ry=\"6\" fill=\"var(--_node-fill)\" stroke=\"var(--_node-stroke)\" stroke-width=\"1\" />");
			_ = sb.Append("\n<text x=\"").Append(F(secCx)).Append("\" y=\"").Append(F(sectionY + (SectionBoxHeight * 0.65)))
				.Append("\" text-anchor=\"middle\" font-size=\"").Append(LabelFontSize)
				.Append("\" font-weight=\"600\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, section.Name.AsSpan());
			_ = sb.Append("</text>");

			sectionIndex++;
		}

		// Timeline arrow (drawn under task boxes so boxes sit on it visually via drop lines)
		var lineStart = columns[0].Cx;
		var lineEnd = columns[^1].Cx + 20;
		_ = sb.Append("\n<line x1=\"").Append(F(lineStart - 10)).Append("\" y1=\"").Append(F(timelineY))
			.Append("\" x2=\"").Append(F(lineEnd)).Append("\" y2=\"").Append(F(timelineY))
			.Append("\" stroke=\"var(--_line)\" stroke-width=\"2\" />");
		// Arrow head
		_ = sb.Append("\n<polygon points=\"")
			.Append(F(lineEnd)).Append(',').Append(F(timelineY)).Append(' ')
			.Append(F(lineEnd - 10)).Append(',').Append(F(timelineY - 5)).Append(' ')
			.Append(F(lineEnd - 10)).Append(',').Append(F(timelineY + 5))
			.Append("\" fill=\"var(--_line)\" />");

		// Task boxes, actor dots, drop lines, faces
		foreach (var col in columns)
		{
			var task = col.Item.Task;
			var boxX = col.BoxLeft;
			var boxY = taskY;

			_ = sb.Append("\n<rect x=\"").Append(F(boxX)).Append("\" y=\"").Append(F(boxY))
				.Append("\" width=\"").Append(F(col.BoxWidth)).Append("\" height=\"").Append(TaskBoxHeight)
				.Append("\" rx=\"6\" ry=\"6\" fill=\"var(--_node-fill)\" stroke=\"var(--_node-stroke)\" stroke-width=\"1\" />");

			_ = sb.Append("\n<text x=\"").Append(F(col.Cx)).Append("\" y=\"").Append(F(boxY + (TaskBoxHeight * 0.62)))
				.Append("\" text-anchor=\"middle\" font-size=\"").Append(LabelFontSize)
				.Append("\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, task.Name.AsSpan());
			_ = sb.Append("</text>");

			// Actor dots along top edge of task box
			if (task.Actors.Count > 0)
			{
				var dotsW = (task.Actors.Count - 1) * (ActorDotR * 2.4);
				var dotX = col.Cx - (dotsW / 2);
				foreach (var actor in task.Actors)
				{
					var fill = actorColors.GetValueOrDefault(actor, ActorPalette[0]);
					_ = sb.Append("\n<circle cx=\"").Append(F(dotX)).Append("\" cy=\"").Append(F(boxY))
						.Append("\" r=\"").Append(ActorDotR)
						.Append("\" fill=\"").Append(fill)
						.Append("\" stroke=\"var(--bg)\" stroke-width=\"1.5\" />");
					dotX += ActorDotR * 2.4;
				}
			}

			// Drop line from timeline to face
			var score = Math.Clamp(task.Score, 1, 5);
			// score 5 near timeline, score 1 far below
			var faceBand = (FaceRadius * 2) + 8;
			var dropSpan = FaceTrackHeight - faceBand;
			var scoreFrac = (MaxScore - score) / (MaxScore - 1);
			var faceOffset = scoreFrac * dropSpan;
			var faceY = faceTop + faceOffset + FaceRadius;

			_ = sb.Append("\n<line x1=\"").Append(F(col.Cx)).Append("\" y1=\"").Append(F(timelineY))
				.Append("\" x2=\"").Append(F(col.Cx)).Append("\" y2=\"").Append(F(faceY - FaceRadius))
				.Append("\" stroke=\"var(--_line)\" stroke-width=\"1\" stroke-dasharray=\"3 3\" opacity=\"0.55\" />");

			AppendFace(sb, col.Cx, faceY, score);
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

	private static void AppendActorLegend(StringBuilder sb, Dictionary<string, string> actorColors, double x, double y)
	{
		var row = 0;
		foreach (var (actor, color) in actorColors)
		{
			var cy = y + (row * LegendRowHeight) + 8;
			_ = sb.Append("\n<circle cx=\"").Append(F(x + 6)).Append("\" cy=\"").Append(F(cy))
				.Append("\" r=\"").Append(ActorDotR).Append("\" fill=\"").Append(color).Append("\" />");
			_ = sb.Append("\n<text x=\"").Append(F(x + 16)).Append("\" y=\"").Append(F(cy + 4))
				.Append("\" font-size=\"").Append(SmallFontSize)
				.Append("\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, actor.AsSpan());
			_ = sb.Append("</text>");
			row++;
		}
	}

	/// <summary>Draw a simple face (circle + eyes + mouth) for score 1–5.</summary>
	private static void AppendFace(StringBuilder sb, double cx, double cy, int score)
	{
		// Face plate
		_ = sb.Append("\n<circle cx=\"").Append(F(cx)).Append("\" cy=\"").Append(F(cy))
			.Append("\" r=\"").Append(FaceRadius)
			.Append("\" fill=\"var(--_node-fill)\" stroke=\"var(--_node-stroke)\" stroke-width=\"1.25\" />");

		// Eyes
		var eyeY = cy - 3;
		var eyeDx = 4.5;
		_ = sb.Append("\n<circle cx=\"").Append(F(cx - eyeDx)).Append("\" cy=\"").Append(F(eyeY))
			.Append("\" r=\"1.4\" fill=\"var(--_text)\" />");
		_ = sb.Append("\n<circle cx=\"").Append(F(cx + eyeDx)).Append("\" cy=\"").Append(F(eyeY))
			.Append("\" r=\"1.4\" fill=\"var(--_text)\" />");

		// Mouth: smile (5–4), flat (3), frown (2–1)
		var mouthY = cy + 4;
		if (score >= 4)
		{
			// smile arc
			_ = sb.Append("\n<path d=\"M ").Append(F(cx - 5)).Append(' ').Append(F(mouthY))
				.Append(" Q ").Append(F(cx)).Append(' ').Append(F(mouthY + 5))
				.Append(' ').Append(F(cx + 5)).Append(' ').Append(F(mouthY))
				.Append("\" fill=\"none\" stroke=\"var(--_text)\" stroke-width=\"1.4\" stroke-linecap=\"round\" />");
		}
		else if (score == 3)
		{
			_ = sb.Append("\n<line x1=\"").Append(F(cx - 4.5)).Append("\" y1=\"").Append(F(mouthY + 1))
				.Append("\" x2=\"").Append(F(cx + 4.5)).Append("\" y2=\"").Append(F(mouthY + 1))
				.Append("\" stroke=\"var(--_text)\" stroke-width=\"1.4\" stroke-linecap=\"round\" />");
		}
		else
		{
			// frown arc
			_ = sb.Append("\n<path d=\"M ").Append(F(cx - 5)).Append(' ').Append(F(mouthY + 3))
				.Append(" Q ").Append(F(cx)).Append(' ').Append(F(mouthY - 2))
				.Append(' ').Append(F(cx + 5)).Append(' ').Append(F(mouthY + 3))
				.Append("\" fill=\"none\" stroke=\"var(--_text)\" stroke-width=\"1.4\" stroke-linecap=\"round\" />");
		}
	}

	private static Dictionary<string, string> BuildActorColors(JourneyDiagram diagram)
	{
		var map = new Dictionary<string, string>(StringComparer.Ordinal);
		var i = 0;
		foreach (var section in diagram.Sections)
		{
			foreach (var task in section.Tasks)
			{
				foreach (var actor in task.Actors)
				{
					if (map.ContainsKey(actor))
						continue;
					map[actor] = ActorPalette[i % ActorPalette.Length];
					i++;
				}
			}
		}
		return map;
	}

	private static List<FlatTask> Flatten(JourneyDiagram diagram)
	{
		var list = new List<FlatTask>();
		for (var s = 0; s < diagram.Sections.Count; s++)
		{
			var section = diagram.Sections[s];
			for (var t = 0; t < section.Tasks.Count; t++)
				list.Add(new FlatTask(section.Tasks[t], s, t == 0));
		}
		return list;
	}

	// Rough proportional width (Mermaider chart style — no full text metrics dependency)
	private static double EstimateTextWidth(string text) =>
		Math.Max(text.Length * 7.2, 24);

	private readonly record struct FlatTask(JourneyTask Task, int SectionIndex, bool IsSectionStart);

	private readonly record struct Column(FlatTask Item, double Cx, double BoxWidth, double BoxLeft);

	private static string F(double value) =>
		value.ToString("0.##", CultureInfo.InvariantCulture);
}
