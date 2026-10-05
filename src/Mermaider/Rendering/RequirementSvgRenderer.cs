using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;
using Sugiyama;

namespace Mermaider.Rendering;

internal static class RequirementSvgRenderer
{
	private const double Margin = 24;
	private const double GapX = 80;
	private const double GapY = 36;
	private const double BoxPadX = 14;
	private const double BoxPadY = 10;
	private const double LineHeight = 20;
	private const double TitleFontPx = 16;
	private const double BodyFontPx = 14;
	private const double HeaderFontPx = 14;
	private const double MinBoxW = 160;
	private const double MaxBoxW = 340;
	private const double ValueGap = 14;

	internal static string Render(
		RequirementDiagram diagram, SvgRenderContext context)
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

	internal static StringBuilder RenderToBuilder(
		RequirementDiagram diagram, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		var boxes = BuildBoxes(diagram);
		if (boxes.Count == 0)
		{
			StyleBlock.AppendSvgOpenTag(sb, 200, 100, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
			StyleBlock.AppendStyleBlock(sb, context.Styles.Font, context.Styles.Strict, context.Styles.FontScale, context.Styles.MonoFont);
			_ = sb.Append("\n</svg>");
			return sb;
		}

		var routes = LayoutBoxes(diagram, boxes, out var layoutW, out var layoutH);

		var hasTitle = diagram.Title is { Length: > 0 };
		var titleOffset = hasTitle ? 36.0 : 0;

		var width = layoutW;
		var height = layoutH + titleOffset;

		// Shift all boxes down for title
		if (titleOffset > 0)
		{
			foreach (var key in boxes.Keys.ToArray())
			{
				var b = boxes[key];
				boxes[key] = b with { Y = b.Y + titleOffset };
			}

			foreach (var route in routes)
			{
				for (var i = 0; i < route.Points.Count; i++)
					route.Points[i] = new Point(route.Points[i].X, route.Points[i].Y + titleOffset);
			}
		}

		StyleBlock.AppendSvgOpenTag(sb, width, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles.Font, context.Styles.Strict, context.Styles.FontScale, context.Styles.MonoFont);
		AppendMarkerDefs(sb);

		if (hasTitle)
		{
			_ = sb.Append("\n<text x=\"").Append((width / 2).SvgFormat())
				.Append("\" y=\"24\" text-anchor=\"middle\" font-size=\"")
				.Append(RenderConstants.FsVar.L).Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, diagram.Title.AsSpan());
			_ = sb.Append("</text>");
		}

		// Same language as ER and class diagrams: connected boxes share a cluster colour; risk reads through the role colours.
		var palette = ClusterPalette.Build(
			boxes.Values.Select(b => new ClusterBox(b.Name, b.X, b.Y, b.W, b.H)).ToList(),
			diagram.Relations.Select(r => (r.Source, r.Target)),
			[],
			context.Styles.Colors);

		foreach (var route in routes)
			AppendRelation(sb, route, context.EdgeRadius);

		foreach (var box in boxes.Values)
			AppendBox(sb, box, palette, context.Styles.Colors);

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
		// browsers lay text out a little wider than TextMetrics, so wrap a bit early
		var contentW = (MaxBoxW - (BoxPadX * 2)) * 0.93;

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
			var wrappedLines = WrapRows(lines, contentW);
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
			var wrappedLines = WrapRows(lines, contentW);
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

		var labelColW = LabelColumnWidth(lines);
		var tableW = 0.0;
		foreach (var line in lines)
		{
			_ = IsRowStart(line, out _, out var value);
			tableW = Math.Max(tableW, labelColW + ValueGap + TextMetrics.MeasureTextWidth(value, BodyFontPx, 400));
		}

		maxTextW = Math.Max(maxTextW, tableW);
		var w = Math.Clamp(maxTextW + (BoxPadX * 2) + (lines.Count > 0 ? 12 : 0), MinBoxW, MaxBoxW);
		var h = HeaderHeight(nameLines.Count) + (lines.Count > 0 ? (lines.Count * LineHeight) + BoxPadY : 0);
		return (w, h);
	}

	// tinted header: the kind line and the (possibly wrapped) name
	private static double HeaderHeight(int nameLineCount) => BoxPadY + ((1 + nameLineCount) * LineHeight) + 6;

	private static double LabelColumnWidth(IReadOnlyList<string> lines)
	{
		var w = 0.0;
		foreach (var line in lines)
		{
			if (IsRowStart(line, out var l, out _))
				w = Math.Max(w, TextMetrics.MeasureTextWidth(l, BodyFontPx, 600));
		}

		return w;
	}

	// Rows are "Label: value"; long values wrap inside the value column, continuation lines carry no label.
	private static List<string> WrapRows(IReadOnlyList<string> rows, double contentW)
	{
		var labelColW = 0.0;
		foreach (var row in rows)
		{
			var idx = row.IndexOf(": ", StringComparison.Ordinal);
			if (idx > 0)
				labelColW = Math.Max(labelColW, TextMetrics.MeasureTextWidth(row.AsSpan(0, idx), BodyFontPx, 600));
		}

		var valueW = Math.Max(40, contentW - labelColW - ValueGap);
		var result = new List<string>();
		foreach (var row in rows)
		{
			var idx = row.IndexOf(": ", StringComparison.Ordinal);
			var label = row[..idx];
			var wrapped = WrapText(row[(idx + 2)..], valueW, BodyFontPx, 400);
			result.Add($"{label}: {wrapped[0]}");
			result.AddRange(wrapped.Skip(1));
		}

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

	private sealed record Route(RequirementRelation Relation, List<Point> Points, Point? LabelPosition);

	// The shared layered engine: relations flow from the source to the target, direction as declared.
	private static List<Route> LayoutBoxes(RequirementDiagram diagram, Dictionary<string, Box> boxes, out double width, out double height)
	{
		var nodes = boxes.Values.Select(b => new LayoutNode(b.Name, b.W, b.H)).ToList();
		var relations = diagram.Relations.Where(r => boxes.ContainsKey(r.Source) && boxes.ContainsKey(r.Target)).ToList();
		var edges = new List<LayoutEdge>(relations.Count);
		foreach (var rel in relations)
		{
			var metrics = TextMetrics.MeasureMultiline(FormatRelation(rel.Type).AsSpan(), RenderConstants.FontSizes.EdgeLabel, RenderConstants.FontWeights.EdgeLabel);
			edges.Add(new LayoutEdge(rel.Source, rel.Target, metrics.Width + 8, metrics.Height + 6));
		}

		var direction = diagram.Direction switch
		{
			Direction.LR => LayoutDirection.LR,
			Direction.RL => LayoutDirection.RL,
			Direction.BT => LayoutDirection.BT,
			_ => LayoutDirection.TD,
		};
		var result = HierarchicalLayout.Compute(new LayoutGraph(direction, nodes, edges, []), new LayoutOptions
		{
			Padding = Margin,
			NodeSpacing = GapY,
			LayerSpacing = GapX,
			NaturalBackEdgeRouting = true,
		});

		foreach (var n in result.Nodes)
			boxes[n.Id] = boxes[n.Id] with { X = n.X, Y = n.Y };

		var routes = new List<Route>(relations.Count);
		foreach (var e in result.Edges)
		{
			if (e.OriginalIndex < 0 || e.OriginalIndex >= relations.Count)
				continue;
			var label = e.LabelPosition is { } lp ? new Point(lp.X, lp.Y) : (Point?)null;
			routes.Add(new Route(relations[e.OriginalIndex], e.Points.Select(p => new Point(p.X, p.Y)).ToList(), label));
		}

		width = result.Width;
		height = result.Height;
		return routes;
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
			.Append("\" fill=\"var(--_line)\" stroke=\"var(--_line)\" stroke-width=\"0.75\" stroke-linejoin=\"round\" />\n");
		_ = sb.Append("  </marker>\n");
		_ = sb.Append("</defs>\n");
	}

	private static readonly string[] PropertyLabels = ["Id", "Text", "Risk", "Verification", "Type", "Doc ref"];

	private static bool IsRowStart(string line, out string label, out string value)
	{
		foreach (var l in PropertyLabels)
		{
			if (line.StartsWith(l + ": ", StringComparison.Ordinal))
			{
				label = l;
				value = line[(l.Length + 2)..];
				return true;
			}
		}

		label = "";
		value = line;
		return false;
	}

	// An ER-style table: tinted header (kind in the border colour, name in full text), then label | value rows.
	private static void AppendBox(StringBuilder sb, Box box, ClusterPalette palette, DiagramColors colors)
	{
		var border = palette.NodeStroke(box.Name);
		var headerFill = palette.HeaderFill(box.Name);
		var r = RenderConstants.Radii.Rectangle;
		var headerH = HeaderHeight(box.NameLines.Count);
		var x = box.X;
		var y0 = box.Y;

		_ = sb.Append("\n<g class=\"req-node\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, box.Name.AsSpan());
		_ = sb.Append("\">");

		if (box.Lines.Count == 0)
		{
			// no properties: the whole box is the header
			_ = sb.Append("\n  <rect x=\"").Append(x.SvgFormat()).Append("\" y=\"").Append(y0.SvgFormat())
				.Append("\" width=\"").Append(box.W.SvgFormat()).Append("\" height=\"").Append(box.H.SvgFormat())
				.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
				.Append("\" fill=\"").Append(palette.NodeFill(box.Name)).Append("\" stroke=\"").Append(border)
				.Append("\" stroke-width=\"").Append(RenderConstants.StrokeWidths.OuterBox.SvgFormat()).Append("\" />");
		}
		else
		{
			_ = sb.Append("\n  <rect x=\"").Append(x.SvgFormat()).Append("\" y=\"").Append(y0.SvgFormat())
				.Append("\" width=\"").Append(box.W.SvgFormat()).Append("\" height=\"").Append(box.H.SvgFormat())
				.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r).Append("\" fill=\"var(--bg)\" />\n");
			VisualLanguage.AppendHeaderPath(sb, x, y0, box.W, headerH, r, headerFill);
		}

		var cx = box.X + (box.W / 2);
		var y = box.Y + BoxPadY + (LineHeight / 2);

		// Kind label, in the cluster's border colour like a class modifier
		_ = sb.Append("\n  <text x=\"").Append(cx.SvgFormat()).Append("\" y=\"").Append(y.SvgFormat())
			.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(RenderConstants.FsVar.S)
			.Append("\" font-weight=\"600\" fill=\"").Append(border).Append("\">");
		MultilineUtils.AppendEscapedXml(sb, box.KindLabel.AsSpan());
		_ = sb.Append("</text>");
		y += LineHeight;

		// Name (possibly multi-line after wrap)
		foreach (var nameLine in box.NameLines)
		{
			_ = sb.Append("\n  <text x=\"").Append(cx.SvgFormat()).Append("\" y=\"").Append(y.SvgFormat())
				.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
				.Append("\" font-size=\"").Append(RenderConstants.FsVar.M)
				.Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, nameLine.AsSpan());
			_ = sb.Append("</text>");
			y += LineHeight;
		}

		if (box.Lines.Count > 0)
		{
			var bodyTop = box.Y + headerH;
			_ = sb.Append("\n  <line x1=\"").Append(x.SvgFormat()).Append("\" y1=\"").Append(bodyTop.SvgFormat())
				.Append("\" x2=\"").Append((x + box.W).SvgFormat()).Append("\" y2=\"").Append(bodyTop.SvgFormat())
				.Append("\" stroke=\"").Append(border).Append("\" stroke-width=\"")
				.Append(RenderConstants.StrokeWidths.OuterBox.SvgFormat()).Append("\" />");

			var labelColW = LabelColumnWidth(box.Lines);

			var valueX = x + BoxPadX + labelColW + ValueGap;
			var rowY = bodyTop + (LineHeight / 2) + 3;
			for (var i = 0; i < box.Lines.Count; i++)
			{
				var isStart = IsRowStart(box.Lines[i], out var label, out var value);
				if (isStart && i > 0)
				{
					var sepY = rowY - (LineHeight / 2) - 0.5;
					_ = sb.Append("\n  <line x1=\"").Append(x.SvgFormat()).Append("\" y1=\"").Append(sepY.SvgFormat())
						.Append("\" x2=\"").Append((x + box.W).SvgFormat()).Append("\" y2=\"").Append(sepY.SvgFormat())
						.Append("\" stroke=\"").Append(border).Append("\" stroke-width=\"1\" opacity=\"0.35\" />");
				}

				if (isStart)
				{
					_ = sb.Append("\n  <text x=\"").Append((x + BoxPadX).SvgFormat()).Append("\" y=\"").Append(rowY.SvgFormat())
						.Append("\" dy=\"").Append(RenderConstants.TextBaselineShift)
						.Append("\" font-size=\"").Append(RenderConstants.FsVar.S)
						.Append("\" font-weight=\"600\" fill=\"var(--_text-sec)\">");
					MultilineUtils.AppendEscapedXml(sb, label.AsSpan());
					_ = sb.Append("</text>");
				}

				// Risk reads through the role colours: low = success, medium = warning, high = failure
				var valueFill = "var(--_text)";
				if (isStart && label == "Risk")
				{
					var role = value switch { "Low" => ColorRole.Success, "Medium" => ColorRole.Warning, "High" => ColorRole.Failure, _ => ColorRole.Info };
					valueFill = VisualLanguage.Border(colors.RoleColor(role));
				}

				_ = sb.Append("\n  <text x=\"").Append(valueX.SvgFormat()).Append("\" y=\"").Append(rowY.SvgFormat())
					.Append("\" dy=\"").Append(RenderConstants.TextBaselineShift)
					.Append("\" font-size=\"").Append(RenderConstants.FsVar.S)
					.Append("\" fill=\"").Append(valueFill).Append("\">");
				MultilineUtils.AppendEscapedXml(sb, value.AsSpan());
				_ = sb.Append("</text>");
				rowY += LineHeight;
			}

			var divX = valueX - 7;
			_ = sb.Append("\n  <line x1=\"").Append(divX.SvgFormat()).Append("\" y1=\"").Append(bodyTop.SvgFormat())
				.Append("\" x2=\"").Append(divX.SvgFormat()).Append("\" y2=\"").Append((box.Y + box.H).SvgFormat())
				.Append("\" stroke=\"").Append(border).Append("\" stroke-width=\"1\" opacity=\"0.35\" />");

			_ = sb.Append("\n  <rect x=\"").Append(x.SvgFormat()).Append("\" y=\"").Append(y0.SvgFormat())
				.Append("\" width=\"").Append(box.W.SvgFormat()).Append("\" height=\"").Append(box.H.SvgFormat())
				.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
				.Append("\" fill=\"none\" stroke=\"").Append(border).Append("\" stroke-width=\"")
				.Append(RenderConstants.StrokeWidths.OuterBox.SvgFormat()).Append("\" />");
		}

		_ = sb.Append("\n</g>");
	}

	private static void AppendRelation(StringBuilder sb, Route route, double cornerRadius)
	{
		if (route.Points.Count < 2)
			return;

		var label = FormatRelation(route.Relation.Type);
		var pos = route.LabelPosition ?? VisualLanguage.PathMidpoint(route.Points);

		_ = sb.Append("\n<path class=\"req-relation\" d=\"");
		if (SvgRenderer.IsOrthogonal(route.Points))
			SvgRenderer.BuildOrthogonalPath(sb, route.Points, cornerRadius);
		else
			SvgRenderer.BuildRoundedPath(sb, route.Points, cornerRadius);
		_ = sb.Append("\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.Connector.SvgFormat())
			.Append("\" marker-end=\"url(#req-arrow)\" />");

		var metrics = TextMetrics.MeasureMultiline(label.AsSpan(), RenderConstants.FontSizes.EdgeLabel, RenderConstants.FontWeights.EdgeLabel);
		_ = sb.Append('\n');
		VisualLanguage.AppendLabelPill(sb, pos.X, pos.Y, metrics.Width, metrics.Height);
		_ = sb.Append("\n<text x=\"").Append(pos.X.SvgFormat()).Append("\" y=\"").Append(pos.Y.SvgFormat())
			.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(RenderConstants.FsVar.S)
			.Append("\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, label.AsSpan());
		_ = sb.Append("</text>");
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
