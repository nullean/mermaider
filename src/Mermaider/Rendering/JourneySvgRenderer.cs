using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// User-journey renderer: sections are the shared container (families p1, p2 … in document order), tasks are nodes in their
/// section family, each task's score is a face (in the section family) on the accent sentiment curve below, and the actors
/// legend sits in the title row in the next palette families.
/// </summary>
internal static class JourneySvgRenderer
{
	private const double MinTaskWidth = 168;
	private const double MaxTaskWidth = 220;
	private const double MinTaskHeight = 44;
	private const double TaskGap = 24;
	private const double SectionPad = 12;
	private const double SectionGap = 24;
	private const double SectionBottomPad = 24;
	private const double FacesBelow = 44;
	private const double FaceStep = 44;
	private const double ActorDotR = 6;
	private const double LegendRowH = 22;
	private const double LegendItemGap = 20;

	private readonly record struct Placed(JourneyTask Task, int Section, double X);

	internal static string Render(JourneyDiagram diagram, SvgRenderContext context)
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

	internal static StringBuilder RenderToBuilder(JourneyDiagram diagram, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var ds = DesignSystem.For(context);
		var margin = DesignSystem.BoardMargin;

		var hasTitle = diagram.Title is { Length: > 0 };
		var titleW = hasTitle ? DesignSystem.TitleIndent + TextMetrics.MeasureTextWidth(diagram.Title!, DesignSystem.Px(TypeRole.Title), 700) : 0;
		var actors = CollectActors(diagram);
		var sectionCount = diagram.Sections.Count;
		// actors continue after the sections in the palette, so a dot never repeats a section's family
		var actorFamily = new Dictionary<string, ColorFamily>(StringComparer.Ordinal);
		for (var i = 0; i < actors.Count; i++)
			actorFamily[actors[i]] = ds.Cluster(sectionCount + 1 + i);

		// task geometry: one size for every task, wide enough for the longest name up to a cap, then wrapped
		var allTasks = diagram.Sections.SelectMany(s => s.Tasks).ToList();
		var labelPx = DesignSystem.Px(TypeRole.Label);
		var labelWeight = DesignSystem.MeasureWeight(TypeRole.Label);
		var widest = allTasks.Count == 0 ? 0 : allTasks.Max(t => TextMetrics.MeasureTextWidth(t.Name, labelPx, labelWeight));
		var taskW = Math.Clamp(widest + 40, MinTaskWidth, MaxTaskWidth);
		var lines = allTasks.Select(t => DesignSystem.Wrap(t.Name, taskW - 24, TypeRole.Label)).ToList();
		var taskH = Math.Max(MinTaskHeight, DesignSystem.TextHeight(lines.Count == 0 ? 1 : lines.Max(l => l.Count), TypeRole.Label) + 20);

		// horizontal placement
		var placed = new List<Placed>(allTasks.Count);
		var sections = new List<(double X, double W, int Index)>();
		var x = margin;
		for (var s = 0; s < diagram.Sections.Count; s++)
		{
			var section = diagram.Sections[s];
			if (section.Tasks.Count == 0)
				continue;
			var sx = x;
			x += SectionPad;
			foreach (var task in section.Tasks)
			{
				placed.Add(new Placed(task, s, x));
				x += taskW + TaskGap;
			}

			x = x - TaskGap + SectionPad;
			sections.Add((sx, x - sx, s));
			x += SectionGap;
		}

		var contentRight = placed.Count == 0 ? margin + 200 : x - SectionGap;

		// legend in the title row, right-aligned, wrapping into more rows when it does not fit
		var legendItems = actors.Select(a => (Name: a, W: DesignSystem.SwatchSize + 8 + TextMetrics.MeasureTextWidth(a, labelPx, labelWeight))).ToList();
		var available = Math.Max(160, contentRight - margin - (hasTitle ? titleW + 32 : 0));
		var legendRows = new List<List<(string Name, double W)>>();
		foreach (var item in legendItems)
		{
			if (legendRows.Count == 0 || legendRows[^1].Sum(i => i.W + LegendItemGap) + item.W > available)
				legendRows.Add([]);
			legendRows[^1].Add(item);
		}

		var legendWidest = legendRows.Count == 0 ? 0 : legendRows.Max(r => r.Sum(i => i.W) + (LegendItemGap * (r.Count - 1)));
		var width = Math.Max(contentRight, margin + (hasTitle ? titleW + 32 : 0) + legendWidest) + margin;
		var headerRows = Math.Max(hasTitle ? 1 : 0, legendRows.Count);
		var top = headerRows == 0 ? margin : DesignSystem.BoardTitleCy + ((headerRows - 1) * LegendRowH) + (DesignSystem.BoardTitledTop - DesignSystem.BoardTitleCy);

		var taskY = top + DesignSystem.BoardHeaderSpace;
		var bandBottom = taskY + taskH + SectionBottomPad;
		var faceTop = bandBottom + FacesBelow;
		var faceBottom = faceTop + (4 * FaceStep);
		var dropBottom = faceBottom + DesignSystem.FaceRadius + 12;
		var height = placed.Count == 0 ? top + margin : dropBottom + margin;

		StyleBlock.AppendSvgOpenTag(sb, width, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles);

		if (hasTitle)
			ds.AppendTitle(sb, margin, DesignSystem.BoardTitleCy, diagram.Title!);

		AppendLegend(sb, ds, legendRows, width - margin, actorFamily);

		if (placed.Count == 0)
		{
			ds.Close(sb);
			return sb;
		}

		foreach (var (sx, sw, index) in sections)
		{
			var data = new StringBuilder("data-section=\"").Append(index.ToString(CultureInfo.InvariantCulture)).Append('"');
			ds.AppendContainerBody(sb, sx, top, sw, bandBottom - top, SectionFamily(ds, index), 0, "subgraph", data.ToString());
		}

		foreach (var (sx, sw, index) in sections)
		{
			if (diagram.Sections[index].Name is { Length: > 0 } name)
				ds.AppendContainerHeader(sb, sx, top, sw, name, SectionFamily(ds, index));
		}

		// drop-lines from each task down through its face
		foreach (var p in placed)
		{
			var cx = p.X + (taskW / 2);
			_ = sb.Append("\n<line class=\"drop-line\" x1=\"").Append(cx.SvgFormat()).Append("\" y1=\"").Append((taskY + taskH).SvgFormat())
				.Append("\" x2=\"").Append(cx.SvgFormat()).Append("\" y2=\"").Append(dropBottom.SvgFormat())
				.Append("\" stroke=\"var(--_line-soft)\" stroke-width=\"1.25\" stroke-dasharray=\"").Append(DesignSystem.DashArray).Append("\" />");
		}

		// tasks: nodes in the section family, actor dots on their top edge
		for (var i = 0; i < placed.Count; i++)
		{
			var p = placed[i];
			var family = SectionFamily(ds, p.Section);
			_ = sb.Append("\n<g class=\"node journey-task\" data-label=\"");
			MultilineUtils.AppendEscapedAttr(sb, p.Task.Name.AsSpan());
			_ = sb.Append("\" data-score=\"").Append(Score(p.Task).ToString(CultureInfo.InvariantCulture)).Append("\">\n  ");
			ds.AppendBox(sb, p.X, taskY, taskW, taskH, family);
			_ = sb.Append("\n  ");
			ds.AppendText(sb, string.Join('\n', lines[i]), p.X + (taskW / 2), taskY + (taskH / 2), TypeRole.Label);
			_ = sb.Append("\n</g>");

			var dotX = p.X + taskW - 14;
			foreach (var person in p.Task.Actors)
			{
				if (!actorFamily.TryGetValue(person, out var af))
					continue;
				_ = sb.Append("\n<circle class=\"actor-dot\" cx=\"").Append(dotX.SvgFormat()).Append("\" cy=\"").Append(taskY.SvgFormat())
					.Append("\" r=\"").Append(ActorDotR.SvgFormat()).Append("\" fill=\"").Append(af.Base)
					.Append("\" stroke=\"var(--bg)\" stroke-width=\"2\"><title>");
				MultilineUtils.AppendEscapedXml(sb, person.AsSpan());
				_ = sb.Append("</title></circle>");
				dotX -= 14;
			}
		}

		// the sentiment curve is the accent story line; the faces sit on it in their section family
		var curve = placed.Select(p => new Point(p.X + (taskW / 2), FaceY(faceTop, Score(p.Task)))).ToList();
		ds.AppendStoryLine(sb, curve, "journey-line");
		for (var i = 0; i < placed.Count; i++)
			ds.AppendFace(sb, curve[i].X, curve[i].Y, Score(placed[i].Task), SectionFamily(ds, placed[i].Section));

		ds.Close(sb);
		return sb;
	}

	private static ColorFamily SectionFamily(DesignSystem ds, int section) => ds.Cluster(section + 1);

	private static int Score(JourneyTask task) => Math.Clamp(task.Score, 1, 5);

	private static double FaceY(double faceTop, int score) => faceTop + ((5 - score) * FaceStep);

	private static void AppendLegend(StringBuilder sb, DesignSystem ds, List<List<(string Name, double W)>> rows, double right, Dictionary<string, ColorFamily> families)
	{
		if (rows.Count == 0)
			return;
		_ = sb.Append("\n<g class=\"legend\">");
		for (var r = 0; r < rows.Count; r++)
		{
			var row = rows[r];
			var rowW = row.Sum(i => i.W) + (LegendItemGap * (row.Count - 1));
			var lx = right - rowW;
			var cy = DesignSystem.BoardTitleCy + (r * LegendRowH);
			foreach (var (name, w) in row)
			{
				_ = sb.Append("\n  ");
				_ = ds.AppendLegendItem(sb, lx, cy, name, families[name].Base);
				lx += w + LegendItemGap;
			}
		}

		_ = sb.Append("\n</g>");
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
}
