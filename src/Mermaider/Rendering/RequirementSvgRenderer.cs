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
	private const double HeaderPadY = 8;
	private const double StereotypeLine = 16;
	private const double NameLine = 20;

	/// <summary>Extra height per wrapped continuation line of a property value.</summary>
	private const double ContinuationLine = 18;

	private const double MinBoxW = 160;
	private const double MaxBoxW = 340;

	/// <summary>Space between the title row and the content (title centre sits at <see cref="Margin"/> + 10).</summary>
	private const double TitleOffset = 44;

	private static double NamePx => DesignSystem.Px(TypeRole.Heading);
	private static double KeyPx => DesignSystem.Px(TypeRole.Meta);
	private static double ValuePx => DesignSystem.Px(TypeRole.Body);

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
		var ds = DesignSystem.For(context);

		var boxes = BuildBoxes(diagram);
		if (boxes.Count == 0)
		{
			StyleBlock.AppendSvgOpenTag(sb, 200, 100, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
			StyleBlock.AppendStyleBlock(sb, context.Styles);
			ds.Close(sb);
			return sb;
		}

		var routes = LayoutBoxes(diagram, boxes, out var layoutW, out var layoutH);

		var hasTitle = diagram.Title is { Length: > 0 };
		var titleOffset = hasTitle ? TitleOffset : 0;

		var width = hasTitle
			? Math.Max(layoutW, Margin + DesignSystem.TitleIndent + TextMetrics.MeasureTextWidth(diagram.Title!, DesignSystem.Px(TypeRole.Title), 700) + Margin)
			: layoutW;
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
				if (route.LabelPosition is { } lp)
					route.LabelPosition = lp with { Y = lp.Y + titleOffset };
			}
		}

		StyleBlock.AppendSvgOpenTag(sb, width, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles);

		if (hasTitle)
			ds.AppendTitle(sb, Margin, Margin + 10, diagram.Title!);

		// Same language as ER and class diagrams: connected boxes share a cluster family; risk reads as a role chip.
		var palette = ClusterPalette.Build(
			boxes.Values.Select(b => new ClusterBox(b.Name, b.X, b.Y, b.W, b.H)).ToList(),
			diagram.Relations.Select(r => (r.Source, r.Target)),
			[],
			context.Styles.Colors).WithTint(ds.TintStrength);

		foreach (var route in routes)
			AppendRelation(sb, ds, route, context.EdgeRadius);

		foreach (var box in boxes.Values)
			AppendBox(sb, ds, box, palette.Family(box.Name));

		foreach (var route in routes)
			AppendRelationLabel(sb, ds, route);

		ds.Close(sb);
		return sb;
	}

	/// <summary>One property row: a key and its (possibly wrapped) value lines.</summary>
	private sealed record Row(string Key, IReadOnlyList<string> Lines, RequirementRisk Risk = RequirementRisk.Unspecified)
	{
		internal double Height => DesignSystem.RowHeight + ((Lines.Count - 1) * ContinuationLine);
	}

	private sealed record Box(
		string Name,
		bool IsRequirement,
		string Stereotype,
		IReadOnlyList<string> NameLines,
		IReadOnlyList<Row> Rows,
		double X,
		double Y,
		double W,
		double H);

	private static Dictionary<string, Box> BuildBoxes(RequirementDiagram diagram)
	{
		var boxes = new Dictionary<string, Box>(StringComparer.Ordinal);
		// browsers lay text out a little wider than TextMetrics, so wrap a bit early
		var contentW = (MaxBoxW - (EntityGrid.Pad * 2) - EntityGrid.Slack) * 0.9;

		foreach (var req in diagram.Requirements)
		{
			// First-wins: skip duplicate names (including later elements of the same name)
			if (boxes.ContainsKey(req.Name))
				continue;

			var props = new List<(string Key, string Value, RequirementRisk Risk)>();
			if (req.Id is { Length: > 0 })
				props.Add(("Id", req.Id, RequirementRisk.Unspecified));
			if (req.Text is { Length: > 0 })
				props.Add(("Text", req.Text, RequirementRisk.Unspecified));
			if (req.Risk != RequirementRisk.Unspecified)
				props.Add(("Risk", FormatRisk(req.Risk), req.Risk));
			if (req.VerifyMethod != RequirementVerifyMethod.Unspecified)
				props.Add(("Verification", FormatVerify(req.VerifyMethod), RequirementRisk.Unspecified));

			boxes[req.Name] = MakeBox(req.Name, true, "«" + FormatKind(req.Kind) + "»", props, contentW);
		}

		foreach (var elem in diagram.Elements)
		{
			// First-wins: do not overwrite a requirement (or earlier element) of the same name
			if (boxes.ContainsKey(elem.Name))
				continue;

			var props = new List<(string Key, string Value, RequirementRisk Risk)>();
			if (elem.Type is { Length: > 0 })
				props.Add(("Type", elem.Type, RequirementRisk.Unspecified));
			if (elem.DocRef is { Length: > 0 })
				props.Add(("Doc ref", elem.DocRef, RequirementRisk.Unspecified));

			boxes[elem.Name] = MakeBox(elem.Name, false, "«Element»", props, contentW);
		}

		return boxes;
	}

	private static Box MakeBox(string name, bool isRequirement, string stereotype, List<(string Key, string Value, RequirementRisk Risk)> props, double contentW)
	{
		var nameLines = WrapText(name, contentW, NamePx, 700);
		var keyColW = props.Count == 0 ? 0 : props.Max(p => KeyWidth(p.Key));
		var valueW = Math.Max(40, contentW - keyColW - EntityGrid.ColumnGap);
		var rows = props.Select(p => new Row(p.Key, p.Risk != RequirementRisk.Unspecified ? [p.Value] : WrapText(p.Value, valueW, ValuePx, 400), p.Risk)).ToList();

		var headerW = Math.Max(EntityGrid.StereotypeWidth(stereotype), (nameLines.Max(l => TextMetrics.MeasureTextWidth(l, NamePx, 700)) * ErSvgRenderer.TextWidthCorrection) + (EntityGrid.Pad * 2));
		var tableW = 0.0;
		foreach (var row in rows)
		{
			var lineW = row.Risk != RequirementRisk.Unspecified
				? TextMetrics.MeasureTextWidth(row.Lines[0], DesignSystem.Px(TypeRole.Tag), 600) + 25
				: row.Lines.Max(l => TextMetrics.MeasureTextWidth(l, ValuePx, 400)) * ErSvgRenderer.TextWidthCorrection;
			tableW = Math.Max(tableW, EntityGrid.Pad + keyColW + EntityGrid.ColumnGap + lineW + EntityGrid.Pad + EntityGrid.Slack);
		}

		var w = Math.Clamp(Math.Max(headerW, tableW), MinBoxW, MaxBoxW);
		var h = HeaderHeight(nameLines.Count) + rows.Sum(r => r.Height);
		if (rows.Count == 0)
			h += HeaderPadY;
		return new Box(name, isRequirement, stereotype, nameLines, rows, 0, 0, w, h);
	}

	// header band: the «stereotype» line and the (possibly wrapped) name
	private static double HeaderHeight(int nameLineCount) => HeaderPadY + StereotypeLine + (nameLineCount * NameLine) + HeaderPadY;

	// the key column is mono in presets with mono xs text: measure both so layout never depends on the style
	private static double KeyWidth(string key) =>
		Math.Max(TextMetrics.MeasureTextWidth(key, KeyPx, 600), TextMetrics.EstimateMonoTextWidth(key, KeyPx) + 2);

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

	private sealed class Route(RequirementRelation relation, List<Point> points, Point? labelPosition)
	{
		internal RequirementRelation Relation { get; } = relation;
		internal List<Point> Points { get; } = points;
		internal Point? LabelPosition { get; set; } = labelPosition;
	}

	// The shared layered engine: relations flow from the source to the target, direction as declared.
	private static List<Route> LayoutBoxes(RequirementDiagram diagram, Dictionary<string, Box> boxes, out double width, out double height)
	{
		var nodes = boxes.Values.Select(b => new LayoutNode(b.Name, b.W, b.H)).ToList();
		var relations = diagram.Relations.Where(r => boxes.ContainsKey(r.Source) && boxes.ContainsKey(r.Target)).ToList();
		var edges = new List<LayoutEdge>(relations.Count);
		foreach (var rel in relations)
		{
			var metrics = TextMetrics.MeasureMultiline(FormatRelation(rel.Type).AsSpan(), DesignSystem.Px(TypeRole.Caption), 600);
			edges.Add(new LayoutEdge(rel.Source, rel.Target, DesignSystem.LabelBoxWidth(metrics.Width) + 8, DesignSystem.PillHeight + 6));
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

	private static void AppendBox(StringBuilder sb, DesignSystem ds, Box box, ColorFamily family)
	{
		var (x, y, w, h) = (box.X, box.Y, box.W, box.H);
		var headerH = HeaderHeight(box.NameLines.Count);

		_ = sb.Append("\n<g class=\"req-node entity\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, box.Name.AsSpan());
		_ = sb.Append("\" data-kind=\"").Append(box.IsRequirement ? "requirement" : "element").Append("\">\n  ");

		if (box.Rows.Count == 0)
		{
			ds.AppendHeaderOnlyEntity(sb, x, y, w, h, string.Join('\n', box.NameLines), family, box.Stereotype);
			_ = sb.Append("\n</g>");
			return;
		}

		ds.AppendEntityFrame(sb, x, y, w, h, headerH, family);

		var rowTop = y + headerH;
		for (var i = 0; i < box.Rows.Count; i++)
		{
			ds.AppendZebraRow(sb, x, rowTop, w, box.Rows[i].Height, i, i == box.Rows.Count - 1);
			if (i > 0)
				ds.AppendEntityRowDivider(sb, x, w, rowTop);
			rowTop += box.Rows[i].Height;
		}

		_ = sb.Append("\n  ");
		ds.AppendEntityName(sb, x, y, w, headerH, string.Join('\n', box.NameLines), family, box.Stereotype);

		// key | value on the shared column grid; risk is a role chip (dot + word), never colour alone
		var keyColW = box.Rows.Count == 0 ? 0 : box.Rows.Max(r => KeyWidth(r.Key));
		var valueX = x + EntityGrid.Pad + keyColW + EntityGrid.ColumnGap;
		rowTop = y + headerH;
		foreach (var row in box.Rows)
		{
			var cy = rowTop + (DesignSystem.RowHeight / 2);
			_ = sb.Append("\n  ");
			ds.AppendText(sb, row.Key, x + EntityGrid.Pad, cy, TypeRole.Meta, anchor: "start", weight: 600);
			_ = sb.Append("\n  ");
			if (row.Risk != RequirementRisk.Unspecified)
			{
				_ = ds.AppendRoleChip(sb, valueX, cy, row.Lines[0], ds.Role(RiskRole(row.Risk)));
			}
			else
			{
				for (var l = 0; l < row.Lines.Count; l++)
				{
					if (l > 0)
						_ = sb.Append("\n  ");
					ds.AppendText(sb, row.Lines[l], valueX, cy + (l * ContinuationLine), TypeRole.Body, anchor: "start");
				}
			}

			rowTop += row.Height;
		}

		_ = sb.Append("\n</g>");
	}

	private static ColorRole RiskRole(RequirementRisk risk) => risk switch
	{
		RequirementRisk.Low => ColorRole.Success,
		RequirementRisk.Medium => ColorRole.Warning,
		_ => ColorRole.Failure,
	};

	/// <summary>Dash language: solid = structure (contains), dashed = dependency (satisfies / verifies / refines), dotted = derived / trace / copy.</summary>
	private static string? DashOf(RequirementRelationType type) => type switch
	{
		RequirementRelationType.Satisfies or RequirementRelationType.Verifies or RequirementRelationType.Refines => DesignSystem.DashArray,
		RequirementRelationType.Derives or RequirementRelationType.Traces or RequirementRelationType.Copies => DesignSystem.DotArray,
		_ => null,
	};

	private static void AppendRelation(StringBuilder sb, DesignSystem ds, Route route, double cornerRadius)
	{
		if (route.Points.Count < 2)
			return;

		var type = route.Relation.Type;
		var contains = type == RequirementRelationType.Contains;
		_ = sb.Append("\n<path class=\"req-relation\" data-type=\"").Append(FormatRelation(type)).Append("\" d=\"");
		if (SvgRenderer.IsOrthogonal(route.Points))
			SvgRenderer.BuildOrthogonalPath(sb, route.Points, cornerRadius);
		else
			SvgRenderer.BuildRoundedPath(sb, route.Points, cornerRadius);
		_ = sb.Append("\" fill=\"none\" stroke=\"").Append(DesignSystem.EdgeColor).Append("\" stroke-width=\"").Append(ds.EdgeWidth).Append('"');
		if (DashOf(type) is { } dash)
		{
			_ = sb.Append(" stroke-dasharray=\"").Append(dash).Append('"');
			if (dash == DesignSystem.DotArray)
				_ = sb.Append(" stroke-linecap=\"round\"");
		}

		if (!contains)
			_ = sb.Append(" marker-end=\"").Append(ds.Marker(MarkerShape.Open)).Append('"');
		_ = sb.Append(" />");

		if (contains)
			AppendContainment(sb, ds, route.Points[^1], route.Points[^2]);
	}

	// ⊕ at the contained end: a page-filled circle with a cross, in the marker colour
	private static void AppendContainment(StringBuilder sb, DesignSystem ds, Point end, Point toward)
	{
		const double r = 6;
		var dx = toward.X - end.X;
		var dy = toward.Y - end.Y;
		var len = Math.Sqrt((dx * dx) + (dy * dy));
		if (len < 0.01)
			return;
		var (ux, uy) = (dx / len, dy / len);
		var (cx, cy) = (end.X + (ux * r), end.Y + (uy * r));
		var color = ds.OwnMarkerColor;
		const double arm = 3.5;
		_ = sb.Append("\n<circle cx=\"").Append(cx).Append("\" cy=\"").Append(cy).Append("\" r=\"").Append(r)
			.Append("\" fill=\"var(--bg)\" stroke=\"").Append(color).Append("\" stroke-width=\"").Append(ds.EdgeWidth).Append("\" />");
		_ = sb.Append("\n<path d=\"M").Append(cx - arm).Append(',').Append(cy).Append(" L").Append(cx + arm).Append(',').Append(cy)
			.Append(" M").Append(cx).Append(',').Append(cy - arm).Append(" L").Append(cx).Append(',').Append(cy + arm)
			.Append("\" fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"").Append(ds.EdgeWidth).Append("\" stroke-linecap=\"round\" />");
	}

	private static void AppendRelationLabel(StringBuilder sb, DesignSystem ds, Route route)
	{
		if (route.Points.Count < 2)
			return;

		var label = FormatRelation(route.Relation.Type);
		var pos = route.LabelPosition ?? VisualLanguage.PathMidpoint(route.Points);
		_ = sb.Append("\n<g class=\"edge-label\" data-label=\"").Append(label).Append("\">\n  ");
		ds.AppendEdgeLabel(sb, pos.X, pos.Y, label);
		_ = sb.Append("\n</g>");
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
