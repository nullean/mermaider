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

	// Actor and section colors are drawn from the single shared palette so they follow themes.
	// SectionTextColour uses ContrastText so labels stay legible on any palette color.
	private const string FaceFill = "var(--_node-fill)";
	private const string FaceStroke = "var(--_node-stroke)";
	private const string MouthStroke = "var(--_text-muted)";
	private const string DropLineStroke = "var(--_line)";
	private const string TimelineStroke = "var(--_text)";

	internal static string Render(JourneyDiagram diagram, DiagramColors colors, string font, string? monoFont = null, bool transparent = false, StrictStylingOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
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

	internal static StringBuilder RenderToBuilder(JourneyDiagram diagram, DiagramColors colors, string font, string? monoFont = null, bool transparent = false, StrictStylingOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		var hasTitle = diagram.Title is { Length: > 0 };
		var actors = CollectActors(diagram);
		var actorMap = new Dictionary<string, (string Color, int Pos)>(StringComparer.Ordinal);
		for (var i = 0; i < actors.Count; i++)
			actorMap[actors[i]] = (colors.PaletteAt(i), i);

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
			StyleBlock.AppendStyleBlock(sb, font, strict, monoFont: monoFont);
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
		StyleBlock.AppendStyleBlock(sb, font, strict, monoFont: monoFont);

		// Arrow marker (mermaid arrowhead)
		_ = sb.Append("\n<defs>\n  <marker id=\"journey-arrow\" refX=\"5\" refY=\"2\" markerWidth=\"6\" markerHeight=\"4\" orient=\"auto\">")
			.Append("\n    <path d=\"M 0,0 V 4 L6,2 Z\" fill=\"").Append(TimelineStroke).Append("\" />")
			.Append("\n  </marker>\n</defs>\n");

		// Translate so title can sit above y=0 section area like mermaid viewBox
		_ = sb.Append("\n<g transform=\"translate(0,").Append((-viewTop).SvgFormat()).Append(")\">");

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

			var fill = colors.PaletteAt(sectionNum);
			// mermaid: width * taskCount + diagramMarginX * (taskCount - 1)
			// but task spacing uses taskMargin not diagramMarginX for positions.
			// drawSection width = conf.width * taskCount + conf.diagramMarginX * (taskCount-1)
			// That doesn't match x spacing of i*(width+taskMargin). Visual in practice spans tasks.
			// Span from first task.x to last task.x + width:
			var secX = leftMargin + (firstIdx * pitch);
			var secW = (count * TaskWidth) + ((count - 1) * TaskMargin);

			_ = sb.Append("\n<rect x=\"").Append(secX.SvgFormat()).Append("\" y=\"").Append(SectionY.SvgFormat())
				.Append("\" width=\"").Append(secW.SvgFormat()).Append("\" height=\"").Append(TaskHeight.SvgFormat())
				.Append("\" rx=\"3\" ry=\"3\" fill=\"").Append(fill).Append("\" />");

			if (section.Name is { Length: > 0 })
			{
				// House style: y = box mid-line, dy shifts for optical vertical center
				_ = sb.Append("\n<text x=\"").Append((secX + (secW / 2)).SvgFormat())
					.Append("\" y=\"").Append((SectionY + (TaskHeight / 2)).SvgFormat())
					.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
					.Append("\" font-size=\"14\" fill=\"").Append(ColorUtils.ContrastText(fill)).Append("\">");
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
			var fill = colors.PaletteAt(item.SectionIndex);
			var score = Math.Clamp(task.Score, 1, 5);
			var faceCy = FaceBaseY + ((5 - score) * FaceStepY);

			// dashed line (under rect so only lower part shows) — mermaid draws full line then rect on top
			_ = sb.Append("\n<line x1=\"").Append(center.SvgFormat()).Append("\" y1=\"").Append(taskY.SvgFormat())
				.Append("\" x2=\"").Append(center.SvgFormat()).Append("\" y2=\"").Append(MaxFaceY.SvgFormat())
				.Append("\" stroke=\"").Append(DropLineStroke)
				.Append("\" stroke-width=\"1\" stroke-dasharray=\"4 2\" />");

			// face
			AppendFace(sb, center, faceCy, score);

			// task box
			_ = sb.Append("\n<rect x=\"").Append(taskX.SvgFormat()).Append("\" y=\"").Append(taskY.SvgFormat())
				.Append("\" width=\"").Append(TaskWidth.SvgFormat()).Append("\" height=\"").Append(TaskHeight.SvgFormat())
				.Append("\" rx=\"3\" ry=\"3\" fill=\"").Append(fill).Append("\" />");

			// actor dots along top of task (mermaid: xPos = task.x + 14, step 10)
			var dotX = taskX + 14;
			foreach (var person in task.Actors)
			{
				if (!actorMap.TryGetValue(person, out var info))
					continue;
				_ = sb.Append("\n<circle cx=\"").Append(dotX.SvgFormat()).Append("\" cy=\"").Append(taskY.SvgFormat())
					.Append("\" r=\"").Append(ActorDotR)
					.Append("\" fill=\"").Append(info.Color)
					.Append("\" stroke=\"var(--_node-stroke)\" stroke-width=\"1\">")
					.Append("<title>");
				MultilineUtils.AppendEscapedXml(sb, person.AsSpan());
				_ = sb.Append("</title></circle>");
				dotX += 10;
			}

			// task label — color chosen for contrast against the section fill
			_ = sb.Append("\n<text x=\"").Append(center.SvgFormat()).Append("\" y=\"").Append((taskY + (TaskHeight / 2)).SvgFormat())
				.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
				.Append("\" font-size=\"14\" fill=\"").Append(ColorUtils.ContrastText(fill)).Append("\">");
			MultilineUtils.AppendEscapedXml(sb, task.Name.AsSpan());
			_ = sb.Append("</text>");
		}

		// activity line + arrow: anchor to task geometry (not width - leftMargin, which
		// detaches when leftMargin expands for long actor legend labels)
		var lineX1 = leftMargin;
		var lineX2 = lastTaskRight - 4;
		_ = sb.Append("\n<line x1=\"").Append(lineX1.SvgFormat()).Append("\" y1=\"").Append(timelineY.SvgFormat())
			.Append("\" x2=\"").Append(lineX2.SvgFormat()).Append("\" y2=\"").Append(timelineY.SvgFormat())
			.Append("\" stroke=\"").Append(TimelineStroke)
			.Append("\" stroke-width=\"4\" marker-end=\"url(#journey-arrow)\" />");

		_ = sb.Append("\n</g>\n</svg>");
		return sb;
	}

	private static void AppendTitle(StringBuilder sb, string title, double x)
	{
		// mermaid: x = leftMargin, y = 25, bold, font-size 4ex ≈ 16-18px
		_ = sb.Append("\n<text x=\"").Append(x.SvgFormat()).Append("\" y=\"").Append(TitleY)
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
			_ = sb.Append("\n<circle cx=\"20\" cy=\"").Append(yPos.SvgFormat())
				.Append("\" r=\"").Append(ActorDotR)
				.Append("\" fill=\"").Append(color).Append("\" stroke=\"var(--_node-stroke)\" stroke-width=\"1\" />");
			_ = sb.Append("\n<text x=\"40\" y=\"").Append((yPos + 5).SvgFormat())
				.Append("\" font-size=\"14\" fill=\"var(--_text-muted)\">");
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
		_ = sb.Append("\n<circle class=\"face\" cx=\"").Append(cx.SvgFormat()).Append("\" cy=\"").Append(cy.SvgFormat())
			.Append("\" r=\"").Append(FaceRadius)
			.Append("\" fill=\"").Append(FaceFill)
			.Append("\" stroke=\"").Append(FaceStroke)
			.Append("\" stroke-width=\"2\" />");

		// eyes
		var eyeOffset = FaceRadius / 3;
		var eyeY = cy - eyeOffset;
		_ = sb.Append("\n<circle cx=\"").Append((cx - eyeOffset).SvgFormat()).Append("\" cy=\"").Append(eyeY.SvgFormat())
			.Append("\" r=\"1.5\" fill=\"").Append(MouthStroke).Append("\" stroke=\"").Append(MouthStroke).Append("\" />");
		_ = sb.Append("\n<circle cx=\"").Append((cx + eyeOffset).SvgFormat()).Append("\" cy=\"").Append(eyeY.SvgFormat())
			.Append("\" r=\"1.5\" fill=\"").Append(MouthStroke).Append("\" stroke=\"").Append(MouthStroke).Append("\" />");

		if (score > 3)
		{
			// smile: lower semicircle arc (d3 arc start π/2 end 3π/2)
			// Approximate with cubic: open upward smile
			var r = FaceRadius / 2.1;
			_ = sb.Append("\n<path class=\"mouth\" d=\"M ")
				.Append((cx - r).SvgFormat()).Append(' ').Append((cy + 2).SvgFormat())
				.Append(" A ").Append(r.SvgFormat()).Append(' ').Append(r.SvgFormat())
				.Append(" 0 0 0 ").Append((cx + r).SvgFormat()).Append(' ').Append((cy + 2).SvgFormat())
				.Append("\" fill=\"none\" stroke=\"").Append(MouthStroke)
				.Append("\" stroke-width=\"1.5\" />");
		}
		else if (score < 3)
		{
			// sad: upper arc, translated down
			var r = FaceRadius / 2.1;
			_ = sb.Append("\n<path class=\"mouth\" d=\"M ")
				.Append((cx - r).SvgFormat()).Append(' ').Append((cy + 7).SvgFormat())
				.Append(" A ").Append(r.SvgFormat()).Append(' ').Append(r.SvgFormat())
				.Append(" 0 0 1 ").Append((cx + r).SvgFormat()).Append(' ').Append((cy + 7).SvgFormat())
				.Append("\" fill=\"none\" stroke=\"").Append(MouthStroke)
				.Append("\" stroke-width=\"1.5\" />");
		}
		else
		{
			// ambivalent line
			_ = sb.Append("\n<line class=\"mouth\" x1=\"").Append((cx - 5).SvgFormat()).Append("\" y1=\"").Append((cy + 7).SvgFormat())
				.Append("\" x2=\"").Append((cx + 5).SvgFormat()).Append("\" y2=\"").Append((cy + 7).SvgFormat())
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

}
