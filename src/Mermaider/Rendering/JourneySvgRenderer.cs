using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// User-journey renderer matching mermaid.js layout (journeyRenderer + svgDraw):
/// fixed-width task columns, section banners, actor dots, timeline arrow at height*4,
/// dashed drop-lines, faces at cy = 300 + (5-score)*30.
/// </summary>
internal static class JourneySvgRenderer
{
	// Defaults from mermaid JourneyDiagramConfig / journeyRenderer
	private const double TaskWidth = 150;
	private const double TaskHeight = 50;
	private const double TaskMargin = 50;
	private const double DiagramMarginX = 50;
	private const double LeftMarginBase = 150;
	private const double SectionY = 50;
	private const double FaceBaseY = 300;
	private const double FaceStepY = 30;
	private const double FaceRadius = 15;
	private const double ActorDotR = 7;
	private const double TitleY = 25;
	private const double TopPad = 8;

	// Face bottom extent pad used for drop-lines and default content height
	private const double MaxFaceY = FaceBaseY + (5 * FaceStepY) + FaceRadius + 20;

	// Actor legend: cy starts 60, +20 per row
	private const double LegendStartY = 60;
	private const double LegendStepY = 20;

	private static readonly string[] ActorColours =
	[
		"#8FBC8F", "#7CFC00", "#00FFFF", "#20B2AA", "#B0E0E6", "#FFFFE0",
	];

	private static readonly string[] SectionFills =
	[
		"#191970", "#8B008B", "#4B0082", "#2F4F4F", "#800000", "#8B4513", "#00008B",
	];

	private const string SectionTextColour = "#ffffff";
	private const string FaceFill = "#FFF8DC";
	private const string FaceStroke = "#999999";
	private const string MouthStroke = "#666666";
	private const string DropLineStroke = "#666666";
	private const string TimelineStroke = "#000000";

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
		var actors = CollectActors(diagram);
		var actorMap = new Dictionary<string, (string Color, int Pos)>(StringComparer.Ordinal);
		for (var i = 0; i < actors.Count; i++)
			actorMap[actors[i]] = (ActorColours[i % ActorColours.Length], i);

		// left margin expands with longest actor name (mermaid measures text; we estimate)
		var legendLabelW = 0.0;
		foreach (var a in actors)
			legendLabelW = Math.Max(legendLabelW, EstimateTextWidth(a));
		var leftMargin = LeftMarginBase + Math.Max(0, legendLabelW - 40);

		var flat = Flatten(diagram);
		var legendBottom = LegendBottom(actors.Count);
		if (flat.Count == 0)
		{
			var emptyW = leftMargin + 200;
			var emptyH = Math.Max(120.0, legendBottom + 16);
			StyleBlock.AppendSvgOpenTag(sb, emptyW, emptyH, colors, transparent, accessibility, diagramType);
			StyleBlock.AppendStyleBlock(sb, font, strict);
			if (hasTitle)
				AppendTitle(sb, diagram.Title!, leftMargin);
			_ = sb.Append("\n</svg>");
			return sb;
		}

		// task.x = i * taskMargin + i * width + leftMargin  (mermaid)
		// column pitch = TaskWidth + TaskMargin = 200
		var pitch = TaskWidth + TaskMargin;
		var lastTaskRight = leftMargin + ((flat.Count - 1) * pitch) + TaskWidth;
		var width = lastTaskRight + DiagramMarginX + 24;
		// Content bottom: faces and dashed lines, or actor legend when many actors
		var height = Math.Max(MaxFaceY + 16, legendBottom + 16);
		// room for title above section row
		var viewTop = hasTitle ? -8.0 : 0;
		var totalHeight = height - viewTop;

		StyleBlock.AppendSvgOpenTag(sb, width, totalHeight, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict);

		// Arrow marker (mermaid arrowhead)
		_ = sb.Append("\n<defs>\n  <marker id=\"journey-arrow\" refX=\"5\" refY=\"2\" markerWidth=\"6\" markerHeight=\"4\" orient=\"auto\">")
			.Append("\n    <path d=\"M 0,0 V 4 L6,2 Z\" fill=\"").Append(TimelineStroke).Append("\" />")
			.Append("\n  </marker>\n</defs>\n");

		// Translate so title can sit above y=0 section area like mermaid viewBox
		_ = sb.Append("\n<g transform=\"translate(0,").Append(F(-viewTop)).Append(")\">");

		if (hasTitle)
			AppendTitle(sb, diagram.Title!, leftMargin);

		AppendActorLegend(sb, actors, actorMap);

		// sectionVHeight = height*2 + diagramMarginY = 110; task.y = 110
		var taskY = (TaskHeight * 2) + 10;
		// timeline at height * 4 = 200
		var timelineY = TaskHeight * 4;

		// Sections
		var sectionNum = 0;
		foreach (var section in diagram.Sections)
		{
			if (section.Tasks.Count == 0)
				continue;

			var firstIdx = flat.FindIndex(t => t.SectionIndex == sectionNum);
			var count = section.Tasks.Count;
			if (firstIdx < 0)
			{
				sectionNum++;
				continue;
			}

			var fill = SectionFills[sectionNum % SectionFills.Length];
			// mermaid: width * taskCount + diagramMarginX * (taskCount - 1)
			// but task spacing uses taskMargin not diagramMarginX for positions.
			// drawSection width = conf.width * taskCount + conf.diagramMarginX * (taskCount-1)
			// That doesn't match x spacing of i*(width+taskMargin). Visual in practice spans tasks.
			// Span from first task.x to last task.x + width:
			var secX = leftMargin + (firstIdx * pitch);
			var secW = (count * TaskWidth) + ((count - 1) * TaskMargin);

			_ = sb.Append("\n<rect x=\"").Append(F(secX)).Append("\" y=\"").Append(F(SectionY))
				.Append("\" width=\"").Append(F(secW)).Append("\" height=\"").Append(F(TaskHeight))
				.Append("\" rx=\"3\" ry=\"3\" fill=\"").Append(fill).Append("\" />");

			if (section.Name is { Length: > 0 })
			{
				// House style: y = box mid-line, dy shifts for optical vertical center
				_ = sb.Append("\n<text x=\"").Append(F(secX + (secW / 2)))
					.Append("\" y=\"").Append(F(SectionY + (TaskHeight / 2)))
					.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
					.Append("\" font-size=\"14\" fill=\"").Append(SectionTextColour).Append("\">");
				MultilineUtils.AppendEscapedXml(sb, section.Name.AsSpan());
				_ = sb.Append("</text>");
			}

			sectionNum++;
		}

		// Tasks + faces + drop lines
		for (var i = 0; i < flat.Count; i++)
		{
			var item = flat[i];
			var task = item.Task;
			var taskX = leftMargin + (i * pitch);
			var center = taskX + (TaskWidth / 2);
			var fill = SectionFills[item.SectionIndex % SectionFills.Length];
			var score = Math.Clamp(task.Score, 1, 5);
			var faceCy = FaceBaseY + ((5 - score) * FaceStepY);

			// dashed line (under rect so only lower part shows) — mermaid draws full line then rect on top
			_ = sb.Append("\n<line x1=\"").Append(F(center)).Append("\" y1=\"").Append(F(taskY))
				.Append("\" x2=\"").Append(F(center)).Append("\" y2=\"").Append(F(MaxFaceY))
				.Append("\" stroke=\"").Append(DropLineStroke)
				.Append("\" stroke-width=\"1\" stroke-dasharray=\"4 2\" />");

			// face
			AppendFace(sb, center, faceCy, score);

			// task box
			_ = sb.Append("\n<rect x=\"").Append(F(taskX)).Append("\" y=\"").Append(F(taskY))
				.Append("\" width=\"").Append(F(TaskWidth)).Append("\" height=\"").Append(F(TaskHeight))
				.Append("\" rx=\"3\" ry=\"3\" fill=\"").Append(fill).Append("\" />");

			// actor dots along top of task (mermaid: xPos = task.x + 14, step 10)
			var dotX = taskX + 14;
			foreach (var person in task.Actors)
			{
				if (!actorMap.TryGetValue(person, out var info))
					continue;
				_ = sb.Append("\n<circle cx=\"").Append(F(dotX)).Append("\" cy=\"").Append(F(taskY))
					.Append("\" r=\"").Append(ActorDotR)
					.Append("\" fill=\"").Append(info.Color)
					.Append("\" stroke=\"#000\" stroke-width=\"1\">")
					.Append("<title>");
				MultilineUtils.AppendEscapedXml(sb, person.AsSpan());
				_ = sb.Append("</title></circle>");
				dotX += 10;
			}

			// task label (white on dark section fill)
			_ = sb.Append("\n<text x=\"").Append(F(center)).Append("\" y=\"").Append(F(taskY + (TaskHeight / 2)))
				.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
				.Append("\" font-size=\"14\" fill=\"").Append(SectionTextColour).Append("\">");
			MultilineUtils.AppendEscapedXml(sb, task.Name.AsSpan());
			_ = sb.Append("</text>");
		}

		// activity line + arrow: anchor to task geometry (not width - leftMargin, which
		// detaches when leftMargin expands for long actor legend labels)
		var lineX1 = leftMargin;
		var lineX2 = lastTaskRight - 4;
		_ = sb.Append("\n<line x1=\"").Append(F(lineX1)).Append("\" y1=\"").Append(F(timelineY))
			.Append("\" x2=\"").Append(F(lineX2)).Append("\" y2=\"").Append(F(timelineY))
			.Append("\" stroke=\"").Append(TimelineStroke)
			.Append("\" stroke-width=\"4\" marker-end=\"url(#journey-arrow)\" />");

		_ = sb.Append("\n</g>\n</svg>");
		return sb;
	}

	private static void AppendTitle(StringBuilder sb, string title, double x)
	{
		// mermaid: x = leftMargin, y = 25, bold, font-size 4ex ≈ 16-18px
		_ = sb.Append("\n<text x=\"").Append(F(x)).Append("\" y=\"").Append(TitleY)
			.Append("\" font-size=\"18\" font-weight=\"bold\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, title.AsSpan());
		_ = sb.Append("</text>");
	}

	private static void AppendActorLegend(
		StringBuilder sb,
		List<string> actors,
		Dictionary<string, (string Color, int Pos)> actorMap)
	{
		// mermaid: cx=20, cy starts 60, r=7, label x=40, y=cy+7, step ~20
		var yPos = LegendStartY;
		foreach (var person in actors)
		{
			var color = actorMap[person].Color;
			_ = sb.Append("\n<circle cx=\"20\" cy=\"").Append(F(yPos))
				.Append("\" r=\"").Append(ActorDotR)
				.Append("\" fill=\"").Append(color).Append("\" stroke=\"#000\" stroke-width=\"1\" />");
			_ = sb.Append("\n<text x=\"40\" y=\"").Append(F(yPos + 5))
				.Append("\" font-size=\"14\" fill=\"#666666\">");
			MultilineUtils.AppendEscapedXml(sb, person.AsSpan());
			_ = sb.Append("</text>");
			yPos += LegendStepY;
		}
	}

	/// <summary>Bottom Y of actor legend (last row circle + pad), or 0 when empty.</summary>
	private static double LegendBottom(int actorCount)
	{
		if (actorCount <= 0)
			return 0;
		// last row cy + radius; text sits at cy+5 with ~14px font
		return LegendStartY + ((actorCount - 1) * LegendStepY) + Math.Max(ActorDotR, 12);
	}

	/// <summary>mermaid svgDraw.drawFace — radius 15, smile/sad/ambivalent by score.</summary>
	private static void AppendFace(StringBuilder sb, double cx, double cy, int score)
	{
		_ = sb.Append("\n<circle class=\"face\" cx=\"").Append(F(cx)).Append("\" cy=\"").Append(F(cy))
			.Append("\" r=\"").Append(FaceRadius)
			.Append("\" fill=\"").Append(FaceFill)
			.Append("\" stroke=\"").Append(FaceStroke)
			.Append("\" stroke-width=\"2\" />");

		// eyes
		var eyeOffset = FaceRadius / 3;
		var eyeY = cy - eyeOffset;
		_ = sb.Append("\n<circle cx=\"").Append(F(cx - eyeOffset)).Append("\" cy=\"").Append(F(eyeY))
			.Append("\" r=\"1.5\" fill=\"").Append(MouthStroke).Append("\" stroke=\"").Append(MouthStroke).Append("\" />");
		_ = sb.Append("\n<circle cx=\"").Append(F(cx + eyeOffset)).Append("\" cy=\"").Append(F(eyeY))
			.Append("\" r=\"1.5\" fill=\"").Append(MouthStroke).Append("\" stroke=\"").Append(MouthStroke).Append("\" />");

		if (score > 3)
		{
			// smile: lower semicircle arc (d3 arc start π/2 end 3π/2)
			// Approximate with cubic: open upward smile
			var r = FaceRadius / 2.1;
			_ = sb.Append("\n<path class=\"mouth\" d=\"M ")
				.Append(F(cx - r)).Append(' ').Append(F(cy + 2))
				.Append(" A ").Append(F(r)).Append(' ').Append(F(r))
				.Append(" 0 0 0 ").Append(F(cx + r)).Append(' ').Append(F(cy + 2))
				.Append("\" fill=\"none\" stroke=\"").Append(MouthStroke)
				.Append("\" stroke-width=\"1.5\" />");
		}
		else if (score < 3)
		{
			// sad: upper arc, translated down
			var r = FaceRadius / 2.1;
			_ = sb.Append("\n<path class=\"mouth\" d=\"M ")
				.Append(F(cx - r)).Append(' ').Append(F(cy + 7))
				.Append(" A ").Append(F(r)).Append(' ').Append(F(r))
				.Append(" 0 0 1 ").Append(F(cx + r)).Append(' ').Append(F(cy + 7))
				.Append("\" fill=\"none\" stroke=\"").Append(MouthStroke)
				.Append("\" stroke-width=\"1.5\" />");
		}
		else
		{
			// ambivalent line
			_ = sb.Append("\n<line class=\"mouth\" x1=\"").Append(F(cx - 5)).Append("\" y1=\"").Append(F(cy + 7))
				.Append("\" x2=\"").Append(F(cx + 5)).Append("\" y2=\"").Append(F(cy + 7))
				.Append("\" stroke=\"").Append(MouthStroke).Append("\" stroke-width=\"1\" />");
		}
	}

	private static List<string> CollectActors(JourneyDiagram diagram)
	{
		var list = new List<string>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var section in diagram.Sections)
		{
			foreach (var task in section.Tasks)
			{
				foreach (var a in task.Actors)
				{
					if (seen.Add(a))
						list.Add(a);
				}
			}
		}
		return list;
	}

	private static List<FlatTask> Flatten(JourneyDiagram diagram)
	{
		var list = new List<FlatTask>();
		for (var s = 0; s < diagram.Sections.Count; s++)
		{
			foreach (var task in diagram.Sections[s].Tasks)
				list.Add(new FlatTask(task, s));
		}
		return list;
	}

	private static double EstimateTextWidth(string text) => text.Length * 7.5;

	private readonly record struct FlatTask(JourneyTask Task, int SectionIndex);

	private static string F(double value) =>
		value.ToString("0.##", CultureInfo.InvariantCulture);
}
