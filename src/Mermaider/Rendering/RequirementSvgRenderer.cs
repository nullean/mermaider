using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class RequirementSvgRenderer
{
	private const double Margin = 24;
	private const double GapX = 80;
	private const double GapY = 36;
	private const double BoxPadX = 14;
	private const double BoxPadY = 10;
	private const double LineHeight = 16;
	private const double TitleFontPx = 16;
	private const double BodyFontPx = 12;
	private const double HeaderFontPx = 13;
	private const double MinBoxW = 160;
	private const double MaxBoxW = 280;

	internal static string Render(
		RequirementDiagram diagram, DiagramColors colors, string font, bool transparent,
		StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
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

	internal static StringBuilder RenderToBuilder(
		RequirementDiagram diagram, DiagramColors colors, string font, bool transparent,
		StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		var boxes = BuildBoxes(diagram);
		if (boxes.Count == 0)
		{
			StyleBlock.AppendSvgOpenTag(sb, 200, 100, colors, transparent, accessibility, diagramType);
			StyleBlock.AppendStyleBlock(sb, font, strict);
			_ = sb.Append("\n</svg>");
			return sb;
		}

		LayoutBoxes(diagram.Direction, boxes);

		var hasTitle = diagram.Title is { Length: > 0 };
		var titleOffset = hasTitle ? 36.0 : 0;

		var maxX = 0.0;
		var maxY = 0.0;
		foreach (var box in boxes.Values)
		{
			maxX = Math.Max(maxX, box.X + box.W);
			maxY = Math.Max(maxY, box.Y + box.H);
		}

		var width = maxX + Margin;
		var height = maxY + Margin + titleOffset;

		// Shift all boxes down for title
		if (titleOffset > 0)
		{
			foreach (var key in boxes.Keys.ToArray())
			{
				var b = boxes[key];
				boxes[key] = b with { Y = b.Y + titleOffset };
			}
		}

		StyleBlock.AppendSvgOpenTag(sb, width, height, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict);
		AppendMarkerDefs(sb);

		if (hasTitle)
		{
			_ = sb.Append("\n<text x=\"").Append(SvgFormat.F(width / 2))
				.Append("\" y=\"24\" text-anchor=\"middle\" font-size=\"")
				.Append(RenderConstants.FsVar.L).Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, diagram.Title.AsSpan());
			_ = sb.Append("</text>");
		}

		foreach (var rel in diagram.Relations)
			AppendRelation(sb, rel, boxes);

		foreach (var box in boxes.Values)
			AppendBox(sb, box);

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private sealed record Box(
		string Name,
		bool IsRequirement,
		string KindLabel,
		IReadOnlyList<string> NameLines,
		IReadOnlyList<string> Lines,
		double X,
		double Y,
		double W,
		double H);

	private static Dictionary<string, Box> BuildBoxes(RequirementDiagram diagram)
	{
		var boxes = new Dictionary<string, Box>(StringComparer.Ordinal);
		var contentW = MaxBoxW - (BoxPadX * 2);

		foreach (var req in diagram.Requirements)
		{
			// First-wins: skip duplicate names (including later elements of the same name)
			if (boxes.ContainsKey(req.Name))
				continue;

			var lines = new List<string>();
			if (req.Id is { Length: > 0 })
				lines.Add($"Id: {req.Id}");
			if (req.Text is { Length: > 0 })
				lines.Add($"Text: {req.Text}");
			if (req.Risk != RequirementRisk.Unspecified)
				lines.Add($"Risk: {FormatRisk(req.Risk)}");
			if (req.VerifyMethod != RequirementVerifyMethod.Unspecified)
				lines.Add($"Verification: {FormatVerify(req.VerifyMethod)}");

			var kindLabel = FormatKind(req.Kind);
			var nameLines = WrapText(req.Name, contentW, TitleFontPx, 700);
			var wrappedLines = WrapLines(lines, contentW, BodyFontPx, 400);
			var (w, h) = MeasureBox(kindLabel, nameLines, wrappedLines);
			boxes[req.Name] = new Box(req.Name, IsRequirement: true, kindLabel, nameLines, wrappedLines, 0, 0, w, h);
		}

		foreach (var elem in diagram.Elements)
		{
			// First-wins: do not overwrite a requirement (or earlier element) of the same name
			if (boxes.ContainsKey(elem.Name))
				continue;

			var lines = new List<string>();
			if (elem.Type is { Length: > 0 })
				lines.Add($"Type: {elem.Type}");
			if (elem.DocRef is { Length: > 0 })
				lines.Add($"Doc ref: {elem.DocRef}");

			const string kindLabel = "Element";
			var nameLines = WrapText(elem.Name, contentW, TitleFontPx, 700);
			var wrappedLines = WrapLines(lines, contentW, BodyFontPx, 400);
			var (w, h) = MeasureBox(kindLabel, nameLines, wrappedLines);
			boxes[elem.Name] = new Box(elem.Name, IsRequirement: false, kindLabel, nameLines, wrappedLines, 0, 0, w, h);
		}

		return boxes;
	}

	private static (double W, double H) MeasureBox(
		string kindLabel, IReadOnlyList<string> nameLines, IReadOnlyList<string> lines)
	{
		var maxTextW = TextMetrics.MeasureTextWidth(kindLabel, HeaderFontPx, 600);
		foreach (var line in nameLines)
			maxTextW = Math.Max(maxTextW, TextMetrics.MeasureTextWidth(line, TitleFontPx, 700));
		foreach (var line in lines)
			maxTextW = Math.Max(maxTextW, TextMetrics.MeasureTextWidth(line, BodyFontPx, 400));

		var w = Math.Clamp(maxTextW + (BoxPadX * 2), MinBoxW, MaxBoxW);
		// kind + name line(s) + property lines
		var lineCount = 1 + nameLines.Count + lines.Count;
		var h = (BoxPadY * 2) + (lineCount * LineHeight) + 6;
		return (w, h);
	}

	private static List<string> WrapLines(IReadOnlyList<string> lines, double maxWidth, double fontSize, int fontWeight)
	{
		var result = new List<string>();
		foreach (var line in lines)
			result.AddRange(WrapText(line, maxWidth, fontSize, fontWeight));
		return result;
	}

	private static List<string> WrapText(string text, double maxWidth, double fontSize, int fontWeight)
	{
		if (text.Length == 0)
			return [""];

		if (TextMetrics.MeasureTextWidth(text, fontSize, fontWeight) <= maxWidth)
			return [text];

		var result = new List<string>();
		var words = text.Split(' ');
		var current = "";
		foreach (var word in words)
		{
			var candidate = current.Length == 0 ? word : current + " " + word;
			var candidateW = TextMetrics.MeasureTextWidth(candidate, fontSize, fontWeight);
			if (candidateW > maxWidth && current.Length > 0)
			{
				result.Add(current);
				// Hard-break an overlong single word so it never paints past the box
				if (TextMetrics.MeasureTextWidth(word, fontSize, fontWeight) > maxWidth)
				{
					result.AddRange(HardBreak(word, maxWidth, fontSize, fontWeight));
					current = "";
				}
				else
				{
					current = word;
				}
			}
			else
			{
				current = candidate;
			}
		}

		if (current.Length > 0)
		{
			if (TextMetrics.MeasureTextWidth(current, fontSize, fontWeight) > maxWidth)
				result.AddRange(HardBreak(current, maxWidth, fontSize, fontWeight));
			else
				result.Add(current);
		}

		return result.Count > 0 ? result : [text];
	}

	private static List<string> HardBreak(string word, double maxWidth, double fontSize, int fontWeight)
	{
		var result = new List<string>();
		var start = 0;
		while (start < word.Length)
		{
			var end = start + 1;
			while (end < word.Length &&
				TextMetrics.MeasureTextWidth(word.AsSpan(start, end - start + 1), fontSize, fontWeight) <= maxWidth)
			{
				end++;
			}

			result.Add(word[start..end]);
			start = end;
		}

		return result;
	}

	private static void LayoutBoxes(Direction direction, Dictionary<string, Box> boxes)
	{
		var elements = boxes.Values.Where(b => !b.IsRequirement).ToList();
		var requirements = boxes.Values.Where(b => b.IsRequirement).ToList();

		// Two-column / two-row layout depending on direction
		var isHorizontal = direction is Direction.LR or Direction.RL;
		var flip = direction is Direction.BT or Direction.RL;

		if (isHorizontal)
		{
			// Elements left (or right if RL), requirements opposite
			var left = flip ? requirements : elements;
			var right = flip ? elements : requirements;

			// If one side empty, put everything in a single column
			if (left.Count == 0)
			{
				left = right;
				right = [];
			}
			else if (right.Count == 0)
			{
				// keep left only
			}

			PlaceColumn(left, Margin, Margin, vertical: true);
			var leftMaxW = left.Count > 0 ? left.Max(b => boxes[b.Name].W) : 0;
			var rightX = Margin + leftMaxW + (right.Count > 0 ? GapX : 0);
			PlaceColumn(right, rightX, Margin, vertical: true);

			// Write positions back
			foreach (var b in left.Concat(right))
				boxes[b.Name] = b;
		}
		else
		{
			// Elements top (or bottom if BT), requirements opposite
			var top = flip ? requirements : elements;
			var bottom = flip ? elements : requirements;

			if (top.Count == 0)
			{
				top = bottom;
				bottom = [];
			}

			PlaceColumn(top, Margin, Margin, vertical: false);
			var topMaxH = top.Count > 0 ? top.Max(b => boxes[b.Name].H) : 0;
			var bottomY = Margin + topMaxH + (bottom.Count > 0 ? GapY : 0);
			PlaceColumn(bottom, Margin, bottomY, vertical: false);

			foreach (var b in top.Concat(bottom))
				boxes[b.Name] = b;
		}

		void PlaceColumn(List<Box> group, double startX, double startY, bool vertical)
		{
			var x = startX;
			var y = startY;
			for (var i = 0; i < group.Count; i++)
			{
				var b = group[i];
				var placed = b with { X = x, Y = y };
				group[i] = placed;
				boxes[b.Name] = placed;

				if (vertical)
					y += placed.H + GapY;
				else
					x += placed.W + GapX;
			}
		}
	}

	private static void AppendMarkerDefs(StringBuilder sb)
	{
		var s = RenderConstants.ArrowHead.Size;
		var w = s;
		var h = s;
		var hh = h / 2.0;

		_ = sb.Append("\n<defs>\n");
		_ = sb.Append("  <marker id=\"req-arrow\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"").Append(w)
			.Append("\" refY=\"").Append(hh)
			.Append("\" orient=\"auto\">\n");
		_ = sb.Append("    <polygon points=\"0 0, ").Append(w).Append(' ').Append(hh)
			.Append(", 0 ").Append(h)
			.Append("\" fill=\"var(--_arrow)\" stroke=\"var(--_arrow)\" stroke-width=\"0.75\" stroke-linejoin=\"round\" />\n");
		_ = sb.Append("  </marker>\n");
		_ = sb.Append("</defs>\n");
	}

	private static void AppendBox(StringBuilder sb, Box box)
	{
		var fill = box.IsRequirement ? "var(--_accent-fill)" : "var(--_node-fill)";
		var stroke = box.IsRequirement ? "var(--_accent-stroke)" : "var(--_node-stroke)";

		_ = sb.Append("\n<g class=\"req-node\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, box.Name.AsSpan());
		_ = sb.Append("\">");

		_ = sb.Append("\n  <rect x=\"").Append(SvgFormat.F(box.X)).Append("\" y=\"").Append(SvgFormat.F(box.Y))
			.Append("\" width=\"").Append(SvgFormat.F(box.W)).Append("\" height=\"").Append(SvgFormat.F(box.H))
			.Append("\" rx=\"").Append(RenderConstants.Radii.Rectangle)
			.Append("\" ry=\"").Append(RenderConstants.Radii.Rectangle)
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(SvgFormat.F(RenderConstants.StrokeWidths.OuterBox)).Append("\" />");

		var cx = box.X + (box.W / 2);
		var y = box.Y + BoxPadY + (LineHeight / 2);

		// Kind label (muted)
		_ = sb.Append("\n  <text x=\"").Append(SvgFormat.F(cx)).Append("\" y=\"").Append(SvgFormat.F(y))
			.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(RenderConstants.FsVar.S)
			.Append("\" font-weight=\"600\" fill=\"var(--_text-sec)\">");
		MultilineUtils.AppendEscapedXml(sb, box.KindLabel.AsSpan());
		_ = sb.Append("</text>");
		y += LineHeight;

		// Name (possibly multi-line after wrap)
		foreach (var nameLine in box.NameLines)
		{
			_ = sb.Append("\n  <text x=\"").Append(SvgFormat.F(cx)).Append("\" y=\"").Append(SvgFormat.F(y))
				.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
				.Append("\" font-size=\"").Append(RenderConstants.FsVar.M)
				.Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, nameLine.AsSpan());
			_ = sb.Append("</text>");
			y += LineHeight;
		}

		y += 2;

		foreach (var line in box.Lines)
		{
			_ = sb.Append("\n  <text x=\"").Append(SvgFormat.F(cx)).Append("\" y=\"").Append(SvgFormat.F(y))
				.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
				.Append("\" font-size=\"").Append(RenderConstants.FsVar.S)
				.Append("\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, line.AsSpan());
			_ = sb.Append("</text>");
			y += LineHeight;
		}

		_ = sb.Append("\n</g>");
	}

	private static void AppendRelation(StringBuilder sb, RequirementRelation rel, Dictionary<string, Box> boxes)
	{
		if (!boxes.TryGetValue(rel.Source, out var src) || !boxes.TryGetValue(rel.Target, out var dst))
			return;

		var (x1, y1, x2, y2) = EdgePoints(src, dst);
		var label = FormatRelation(rel.Type);
		var mx = (x1 + x2) / 2;
		var my = (y1 + y2) / 2;

		_ = sb.Append("\n<line x1=\"").Append(SvgFormat.F(x1)).Append("\" y1=\"").Append(SvgFormat.F(y1))
			.Append("\" x2=\"").Append(SvgFormat.F(x2)).Append("\" y2=\"").Append(SvgFormat.F(y2))
			.Append("\" stroke=\"var(--_line)\" stroke-width=\"")
			.Append(SvgFormat.F(RenderConstants.StrokeWidths.Connector))
			.Append("\" marker-end=\"url(#req-arrow)\" />");

		var labelW = TextMetrics.MeasureTextWidth(label, BodyFontPx, 400) + 12;
		var labelH = 18.0;
		_ = sb.Append("\n<rect x=\"").Append(SvgFormat.F(mx - (labelW / 2))).Append("\" y=\"").Append(SvgFormat.F(my - (labelH / 2)))
			.Append("\" width=\"").Append(SvgFormat.F(labelW)).Append("\" height=\"").Append(SvgFormat.F(labelH))
			.Append("\" rx=\"4\" ry=\"4\" fill=\"var(--bg)\" stroke=\"var(--_line)\" stroke-width=\"0.75\" />");

		_ = sb.Append("\n<text x=\"").Append(SvgFormat.F(mx)).Append("\" y=\"").Append(SvgFormat.F(my))
			.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(RenderConstants.FsVar.S)
			.Append("\" fill=\"var(--_text-muted)\">");
		MultilineUtils.AppendEscapedXml(sb, label.AsSpan());
		_ = sb.Append("</text>");
	}

	private static (double X1, double Y1, double X2, double Y2) EdgePoints(Box src, Box dst)
	{
		var scx = src.X + (src.W / 2);
		var scy = src.Y + (src.H / 2);
		var dcx = dst.X + (dst.W / 2);
		var dcy = dst.Y + (dst.H / 2);

		var dx = dcx - scx;
		var dy = dcy - scy;

		// Prefer side attachment along dominant axis
		double x1, y1, x2, y2;
		if (Math.Abs(dx) >= Math.Abs(dy))
		{
			// Horizontal: attach left/right sides
			if (dx >= 0)
			{
				x1 = src.X + src.W;
				y1 = scy;
				x2 = dst.X;
				y2 = dcy;
			}
			else
			{
				x1 = src.X;
				y1 = scy;
				x2 = dst.X + dst.W;
				y2 = dcy;
			}
		}
		else
		{
			// Vertical: attach top/bottom
			if (dy >= 0)
			{
				x1 = scx;
				y1 = src.Y + src.H;
				x2 = dcx;
				y2 = dst.Y;
			}
			else
			{
				x1 = scx;
				y1 = src.Y;
				x2 = dcx;
				y2 = dst.Y + dst.H;
			}
		}

		return (x1, y1, x2, y2);
	}

	private static string FormatKind(RequirementKind kind) => kind switch
	{
		RequirementKind.FunctionalRequirement => "Functional Requirement",
		RequirementKind.InterfaceRequirement => "Interface Requirement",
		RequirementKind.PerformanceRequirement => "Performance Requirement",
		RequirementKind.PhysicalRequirement => "Physical Requirement",
		RequirementKind.DesignConstraint => "Design Constraint",
		_ => "Requirement",
	};

	private static string FormatRisk(RequirementRisk risk) => risk switch
	{
		RequirementRisk.Low => "Low",
		RequirementRisk.Medium => "Medium",
		RequirementRisk.High => "High",
		_ => "",
	};

	private static string FormatVerify(RequirementVerifyMethod m) => m switch
	{
		RequirementVerifyMethod.Analysis => "Analysis",
		RequirementVerifyMethod.Demonstration => "Demonstration",
		RequirementVerifyMethod.Inspection => "Inspection",
		RequirementVerifyMethod.Test => "Test",
		_ => "",
	};

	private static string FormatRelation(RequirementRelationType t) => t switch
	{
		RequirementRelationType.Contains => "contains",
		RequirementRelationType.Copies => "copies",
		RequirementRelationType.Derives => "derives",
		RequirementRelationType.Satisfies => "satisfies",
		RequirementRelationType.Verifies => "verifies",
		RequirementRelationType.Refines => "refines",
		RequirementRelationType.Traces => "traces",
		_ => t.ToString().ToLowerInvariant(),
	};

}
