using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// C4 diagrams on the shared design system. Element kinds map to families: the system / container / component in scope is
/// the accent, a person is cluster <c>p0</c>, anything external is the neutral; data stores are cylinders and queues are
/// horizontal cylinders in the same family. Boxes are node-recipe cards with the <c>[kind]</c> tag, the name as a heading
/// and the description wrapped to fit (the box grows, nothing is clipped). Boundaries are dashed shared containers,
/// relations are orthogonal edges whose labels sit in the gaps between boxes, never over box text.
/// </summary>
internal static class C4SvgRenderer
{
	private const double BoxWidth = 216;
	private const double MinBoxHeight = 120;
	private const double TextPad = 16;
	private const double GapX = 56;
	private const double GapY = 64;
	private const double BoundarySidePad = 24;
	private const double BoundaryTop = DesignSystem.StripHeight + 20;
	private const double BoundaryBottomPad = 24;
	private const double Margin = 40;

	/// <summary>Title centre and where content starts below a title.</summary>
	private const double TitleCy = 40;

	private const double TitledContentTop = 88;

	/// <summary>Half-height of the cap ellipse of a cylinder (db) and half-width of the caps of a queue.</summary>
	private const double CapRy = 10;

	private const double CapRx = 12;

	/// <summary>Distance of a lane routed over a row from the boxes it clears, and between stacked lanes.</summary>
	private const double LaneOffset = 32;

	private const double LaneStep = 24;

	// text is measured at the heaviest weight any preset uses, so layout never depends on the preset
	private const int HeadingMeasureWeight = 700;
	private const double HeadingLine = 21;
	private const double BodyLine = 18.2;
	private const double MetaLine = 15.6;
	private const double TagLine = 16;
	private const double GlyphRoom = 24;

	private sealed class PlacedElement
	{
		public required C4Element Element { get; init; }
		public double X { get; set; }
		public double Y { get; set; }
		public double W { get; set; } = BoxWidth;
		public double H { get; set; } = MinBoxHeight;
		public double Right => X + W;
		public double Bottom => Y + H;
		public double Cx => X + (W / 2);
		public double Cy => Y + (H / 2);
	}

	private sealed class PlacedBoundary
	{
		public required C4Boundary Boundary { get; init; }
		public double X { get; set; }
		public double Y { get; set; }
		public double W { get; set; }
		public double H { get; set; }
		public int Depth { get; init; }
		public int Ordinal { get; init; }
		public List<PlacedBoundary> Children { get; } = [];
	}

	private sealed record Route(IReadOnlyList<Point> Points, Point LabelAt, bool Self, bool LabelOnVertical = false);

	/// <summary>Wrapped text of one element and the height it needs.</summary>
	private sealed record ElementText(string Kind, IReadOnlyList<string> Name, IReadOnlyList<string> Tech, IReadOnlyList<string> Description, double Height);

	internal static string Render(C4Diagram diagram, SvgRenderContext context)
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

	internal static StringBuilder RenderToBuilder(C4Diagram diagram, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var ds = DesignSystem.For(context);
		var hasTitle = diagram.Title is { Length: > 0 };
		var contentTop = hasTitle ? TitledContentTop : Margin;

		var texts = new Dictionary<string, ElementText>(StringComparer.Ordinal);
		var placements = new Dictionary<string, PlacedElement>(StringComparer.Ordinal);
		// Relation anchors include leaf elements + nested deployment-node boxes (not redrawn as leaves).
		var relationAnchors = new Dictionary<string, PlacedElement>(StringComparer.Ordinal);
		var rootBoundaries = new List<PlacedBoundary>();
		var ordinal = 0;

		var layout = new LayoutState(diagram, texts, placements, relationAnchors);
		_ = layout.LayoutNodes(diagram.RootNodes, Margin, contentTop, rootBoundaries, 0, ref ordinal);

		// lanes routed over a row may need room above the content: shift everything down until they clear the title
		var routes = RouteAll(diagram.Relations, relationAnchors, placements);
		var minY = contentTop;
		foreach (var (_, route) in routes)
		{
			foreach (var pt in route.Points)
				minY = Math.Min(minY, pt.Y);
			minY = Math.Min(minY, route.LabelAt.Y - DesignSystem.PillHeight);
		}

		if (minY < contentTop)
		{
			var shift = contentTop - minY;
			foreach (var p in relationAnchors.Values)
				p.Y += shift;
			foreach (var b in AllBoundaries(rootBoundaries))
				b.Y += shift;
			routes = RouteAll(diagram.Relations, relationAnchors, placements);
		}

		var maxX = Margin + 280;
		var maxY = contentTop + 120;
		foreach (var p in relationAnchors.Values)
		{
			maxX = Math.Max(maxX, p.Right);
			maxY = Math.Max(maxY, p.Bottom);
		}

		foreach (var b in AllBoundaries(rootBoundaries))
		{
			maxX = Math.Max(maxX, b.X + b.W);
			maxY = Math.Max(maxY, b.Y + b.H);
		}

		foreach (var (rel, route) in routes)
		{
			foreach (var pt in route.Points)
			{
				maxX = Math.Max(maxX, pt.X + 16);
				maxY = Math.Max(maxY, pt.Y);
			}

			var labelW = DesignSystem.LabelBoxWidth(LabelWidth(rel));
			maxX = Math.Max(maxX, route.LabelAt.X + (route.Self ? labelW : labelW / 2));
			maxY = Math.Max(maxY, route.LabelAt.Y + DesignSystem.PillHeight);
		}

		if (hasTitle)
			maxX = Math.Max(maxX, Margin + DesignSystem.TitleIndent + TextMetrics.MeasureTextWidth(diagram.Title!, DesignSystem.Px(TypeRole.Title), 700));

		var width = maxX + Margin;
		var height = maxY + Margin;

		StyleBlock.AppendSvgOpenTag(sb, width, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles);

		if (hasTitle)
			ds.AppendTitle(sb, Margin, TitleCy, diagram.Title!);

		foreach (var b in rootBoundaries)
			AppendBoundaryBody(sb, ds, b);

		foreach (var (rel, route) in routes)
			AppendRelation(sb, ds, rel, route, context.EdgeRadius);

		foreach (var b in AllBoundaries(rootBoundaries))
			AppendBoundaryHeader(sb, ds, b);

		foreach (var p in placements.Values)
			AppendElement(sb, ds, p, texts[p.Element.Alias]);

		foreach (var (rel, route) in routes)
			AppendRelationLabel(sb, ds, rel, route);

		ds.Close(sb);
		return sb;
	}

	// ========================================================================
	// Layout
	// ========================================================================

	private sealed class LayoutState(
		C4Diagram diagram,
		Dictionary<string, ElementText> texts,
		Dictionary<string, PlacedElement> placements,
		Dictionary<string, PlacedElement> relationAnchors)
	{
		/// <summary>Places <paramref name="nodes"/> in rows from (<paramref name="originX"/>, <paramref name="originY"/>); returns the extent.</summary>
		internal (double MaxX, double MaxY) LayoutNodes(IReadOnlyList<C4Node> nodes, double originX, double originY, List<PlacedBoundary> outBoundaries, int depth, ref int ordinal)
		{
			// Walk source order so Person → Boundary → System_Ext keeps left-to-right flow.
			var cursorX = originX;
			var cursorY = originY;
			var rowMaxH = 0.0;
			var leafCol = 0;
			var boundaryCol = 0;
			var maxX = originX;
			var maxY = originY;
			var rowLeaves = new List<PlacedElement>();
			C4Node? previous = null;

			void EndRow()
			{
				// boxes in a row share one height, so the row reads as a row
				var leafMax = rowLeaves.Count > 0 ? rowLeaves.Max(l => l.H) : 0;
				foreach (var l in rowLeaves)
					l.H = leafMax;
				rowLeaves.Clear();
			}

			void NewRow()
			{
				EndRow();
				cursorX = originX;
				cursorY += rowMaxH + GapY;
				rowMaxH = 0;
				leafCol = 0;
				boundaryCol = 0;
				previous = null;
			}

			foreach (var n in nodes)
			{
				if (n is C4Element el)
				{
					if (leafCol >= diagram.ShapeInRow)
						NewRow();

					if (previous is not null)
						cursorX += GapBetween(previous, n);

					var text = MeasureElement(el);
					texts[el.Alias] = text;
					var placed = new PlacedElement { Element = el, X = cursorX, Y = cursorY, W = BoxWidth, H = text.Height };
					placements[el.Alias] = placed;
					relationAnchors[el.Alias] = placed;
					rowLeaves.Add(placed);
					cursorX += BoxWidth;
					rowMaxH = Math.Max(rowMaxH, placed.H);
					leafCol++;
					maxX = Math.Max(maxX, placed.Right);
					maxY = Math.Max(maxY, placed.Bottom);
					previous = n;
					continue;
				}

				if (n is not C4Boundary boundary)
					continue;

				if (boundaryCol >= diagram.BoundaryInRow)
					NewRow();

				if (previous is not null)
					cursorX += GapBetween(previous, n);

				var childBoundaries = new List<PlacedBoundary>();
				var myOrdinal = ordinal++;
				var (innerMaxX, innerMaxY) = LayoutNodes(
					boundary.Children,
					cursorX + BoundarySidePad,
					cursorY + BoundaryTop,
					childBoundaries,
					depth + 1,
					ref ordinal);

				var titleW = TextMetrics.MeasureTextWidth(boundary.Label, DesignSystem.Px(TypeRole.Subheading), 700) + 80
					+ (BoundaryTag(boundary) is { } tag ? TextMetrics.MeasureTextWidth(tag, DesignSystem.Px(TypeRole.Tag), 600) + 24 : 0);
				var bw = Math.Max(Math.Max(BoxWidth + (BoundarySidePad * 2), titleW), innerMaxX - cursorX + BoundarySidePad);
				var bh = Math.Max(MinBoxHeight + BoundaryTop + BoundaryBottomPad, innerMaxY - cursorY + BoundaryBottomPad);

				var pb = new PlacedBoundary { Boundary = boundary, X = cursorX, Y = cursorY, W = bw, H = bh, Depth = depth, Ordinal = myOrdinal };
				pb.Children.AddRange(childBoundaries);
				outBoundaries.Add(pb);

				// Nested deployment nodes are relation endpoints (outer box), not leaf redraws.
				if (boundary.IsDeploymentNode)
				{
					relationAnchors[boundary.Alias] = new PlacedElement
					{
						Element = new C4Element(boundary.Alias, C4ElementType.DeploymentNode, boundary.Label, boundary.Technology, Description: null, External: false),
						X = cursorX,
						Y = cursorY,
						W = bw,
						H = bh,
					};
				}

				cursorX += bw;
				rowMaxH = Math.Max(rowMaxH, bh);
				boundaryCol++;
				maxX = Math.Max(maxX, pb.X + pb.W);
				maxY = Math.Max(maxY, pb.Y + pb.H);
				previous = n;
			}

			EndRow();
			return (maxX, maxY);
		}

		/// <summary>The gap before <paramref name="next"/>: wide enough for the widest label of a relation between the two neighbours.</summary>
		private double GapBetween(C4Node prev, C4Node next)
		{
			var a = Aliases(prev);
			var b = Aliases(next);
			var widest = 0.0;
			foreach (var rel in diagram.Relations)
			{
				if ((a.Contains(rel.From) && b.Contains(rel.To)) || (b.Contains(rel.From) && a.Contains(rel.To)))
					widest = Math.Max(widest, DesignSystem.LabelBoxWidth(LabelWidth(rel)));
			}

			return Math.Max(GapX, widest + 32);
		}

		private static HashSet<string> Aliases(C4Node node)
		{
			var set = new HashSet<string>(StringComparer.Ordinal) { node.Alias };
			if (node is C4Boundary b)
			{
				foreach (var c in b.Children)
					set.UnionWith(Aliases(c));
			}

			return set;
		}
	}

	private static string? BoundaryTag(C4Boundary b) =>
		b.TypeLabel is { Length: > 0 } tl ? tl
		: b.IsDeploymentNode && b.Technology is { Length: > 0 } techn ? techn
		: null;

	private static ElementText MeasureElement(C4Element el)
	{
		var inner = BoxWidth - (TextPad * 2);
		if (IsQueue(el.Type))
			inner -= CapRx * 2;
		var name = DesignSystem.WrapWords(el.Label, inner, DesignSystem.Px(TypeRole.Heading), HeadingMeasureWeight);
		var tech = el.Technology is { Length: > 0 } t
			? DesignSystem.WrapWords("[" + t + "]", inner, DesignSystem.Px(TypeRole.Meta), 400)
			: [];
		var description = el.Description is { Length: > 0 } d
			? DesignSystem.WrapWords(MultilineUtils.NormalizeBrTags(d).Replace('\n', ' '), inner, DesignSystem.Px(TypeRole.Body), 400)
			: [];

		var h = 14 + TagLine + 4 + (name.Count * HeadingLine) + (tech.Count * MetaLine) + (description.Count > 0 ? 4 + (description.Count * BodyLine) : 0) + 16;
		if (el.Type == C4ElementType.Person)
			h += GlyphRoom;
		if (IsDb(el.Type))
			h += CapRy * 2;
		return new ElementText(TypeLabel(el), name, tech, description, Math.Max(MinBoxHeight, Math.Ceiling(h)));
	}

	private static IEnumerable<PlacedBoundary> AllBoundaries(IEnumerable<PlacedBoundary> roots)
	{
		foreach (var b in roots)
		{
			yield return b;
			foreach (var c in AllBoundaries(b.Children))
				yield return c;
		}
	}

	// ========================================================================
	// Routing
	// ========================================================================

	private static List<(C4Relation Rel, Route Route)> RouteAll(
		IReadOnlyList<C4Relation> relations, Dictionary<string, PlacedElement> anchors, Dictionary<string, PlacedElement> placements)
	{
		var obstacles = placements.Values.ToList();
		var result = new List<(C4Relation, Route)>();
		var lanes = 0;
		foreach (var rel in relations)
		{
			if (!anchors.TryGetValue(rel.From, out var from) || !anchors.TryGetValue(rel.To, out var to))
				continue;
			result.Add((rel, RouteOne(from, to, obstacles, ref lanes)));
		}

		return result;
	}

	private static Route RouteOne(PlacedElement a, PlacedElement b, List<PlacedElement> obstacles, ref int lanes)
	{
		if (ReferenceEquals(a, b))
		{
			// self relation: a small loop on the right side, its label to the right of the loop
			var sy = a.Y + (a.H * 0.35);
			var ey = a.Y + (a.H * 0.65);
			var lx = a.Right + 24;
			return new Route([new(a.Right, sy), new(lx, sy), new(lx, ey), new(a.Right, ey)], new(lx + 8, (sy + ey) / 2), Self: true);
		}

		bool Blocked(Point p, Point q) => obstacles.Any(o =>
			!ReferenceEquals(o, a) && !ReferenceEquals(o, b) && SegmentHitsBox(p, q, o));

		var yTop = Math.Max(a.Y, b.Y);
		var yBottom = Math.Min(a.Bottom, b.Bottom);
		var xLeft = Math.Max(a.X, b.X);
		var xRight = Math.Min(a.Right, b.Right);
		var horizontallyApart = a.Right <= b.X || b.Right <= a.X;
		var verticallyApart = a.Bottom <= b.Y || b.Bottom <= a.Y;

		// side by side: a straight line through the gap, or a lane over the row when a box is in the way
		if (horizontallyApart && yBottom - yTop > 24)
		{
			var y = (yTop + yBottom) / 2;
			var (x1, x2) = a.Right <= b.X ? (a.Right, b.X) : (a.X, b.Right);
			var p = new Point(x1, y);
			var q = new Point(x2, y);
			if (!Blocked(p, q))
				return new Route([p, q], new((x1 + x2) / 2, y), Self: false);

			var minX = Math.Min(a.Cx, b.Cx);
			var maxX = Math.Max(a.Cx, b.Cx);
			var top = obstacles.Where(o => o.Right > minX && o.X < maxX).Select(o => o.Y).Append(a.Y).Append(b.Y).Min();
			var laneY = top - LaneOffset - (lanes++ * LaneStep);
			return new Route([new(a.Cx, a.Y), new(a.Cx, laneY), new(b.Cx, laneY), new(b.Cx, b.Y)], new((a.Cx + b.Cx) / 2, laneY), Self: false);
		}

		// stacked: a straight vertical line through the gap
		if (verticallyApart && xRight - xLeft > 24)
		{
			var x = (xLeft + xRight) / 2;
			var (y1, y2) = a.Bottom <= b.Y ? (a.Bottom, b.Y) : (a.Y, b.Bottom);
			return new Route([new(x, y1), new(x, y2)], new(x, (y1 + y2) / 2), Self: false, LabelOnVertical: true);
		}

		// diagonal neighbours: a Z through the gap between the rows, label on its middle run
		if (verticallyApart)
		{
			var (y1, y2) = a.Bottom <= b.Y ? (a.Bottom, b.Y) : (a.Y, b.Bottom);
			var midY = (y1 + y2) / 2;
			return new Route([new(a.Cx, y1), new(a.Cx, midY), new(b.Cx, midY), new(b.Cx, y2)], new((a.Cx + b.Cx) / 2, midY), Self: false);
		}

		var (gx1, gx2) = a.Right <= b.X ? (a.Right, b.X) : (a.X, b.Right);
		var midX = (gx1 + gx2) / 2;
		return new Route([new(gx1, a.Cy), new(midX, a.Cy), new(midX, b.Cy), new(gx2, b.Cy)], new(midX, (a.Cy + b.Cy) / 2), Self: false, LabelOnVertical: true);
	}

	private static bool SegmentHitsBox(Point p, Point q, PlacedElement box)
	{
		var minX = Math.Min(p.X, q.X);
		var maxX = Math.Max(p.X, q.X);
		var minY = Math.Min(p.Y, q.Y);
		var maxY = Math.Max(p.Y, q.Y);
		return maxX > box.X && minX < box.Right && maxY > box.Y && minY < box.Bottom;
	}

	// ========================================================================
	// Painting
	// ========================================================================

	/// <summary>Family of a boundary: deployment nodes are neutral, others alternate through the palette after the person.</summary>
	private static ColorFamily BoundaryFamily(DesignSystem ds, PlacedBoundary b) =>
		b.Boundary.IsDeploymentNode ? ds.Neutral : ds.Cluster(1 + b.Ordinal);

	private static void AppendBoundaryBody(StringBuilder sb, DesignSystem ds, PlacedBoundary b)
	{
		var family = BoundaryFamily(ds, b);
		var attrs = new StringBuilder("data-id=\"");
		MultilineUtils.AppendEscapedAttr(attrs, b.Boundary.Alias.AsSpan());
		_ = attrs.Append('"');
		if (b.Boundary.IsDeploymentNode)
			ds.AppendContainerBody(sb, b.X, b.Y, b.W, b.H, family, b.Depth, "c4-deployment-node", attrs.ToString());
		else
			ds.AppendDashedContainerBody(sb, b.X, b.Y, b.W, b.H, family, b.Depth, "c4-boundary", attrs.ToString());

		foreach (var child in b.Children)
			AppendBoundaryBody(sb, ds, child);
	}

	private static void AppendBoundaryHeader(StringBuilder sb, DesignSystem ds, PlacedBoundary b) =>
		ds.AppendContainerHeader(sb, b.X, b.Y, b.W, b.Boundary.Label, BoundaryFamily(ds, b), BoundaryTag(b.Boundary));

	private static bool IsDb(C4ElementType t) => t is C4ElementType.SystemDb or C4ElementType.ContainerDb or C4ElementType.ComponentDb;

	private static bool IsQueue(C4ElementType t) => t is C4ElementType.SystemQueue or C4ElementType.ContainerQueue or C4ElementType.ComponentQueue;

	/// <summary>Kind → family: external = neutral, person = p0, everything in scope = accent.</summary>
	private static ColorFamily FamilyFor(DesignSystem ds, C4Element el) => el.Type switch
	{
		_ when el.External => ds.Neutral,
		C4ElementType.Person => ds.Cluster(0),
		C4ElementType.DeploymentNode => ds.Neutral,
		_ => ds.Accent,
	};

	private static void AppendElement(StringBuilder sb, DesignSystem ds, PlacedElement p, ElementText text)
	{
		var el = p.Element;
		var family = FamilyFor(ds, el);
		var isDb = IsDb(el.Type);
		var isQueue = IsQueue(el.Type);

		_ = sb.Append("\n<g class=\"node c4-element\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, el.Alias.AsSpan());
		_ = sb.Append("\" data-kind=\"").Append(el.Type.ToString().ToLowerInvariant())
			.Append("\" data-external=\"").Append(el.External ? "true" : "false").Append("\">\n  ");

		var fill = ds.NodeFill(family);
		var stroke = ds.NodeStroke(family);
		var sw = ds.NodeStrokeWidth;
		// the cap seam stays visible when the preset has no outline
		var seam = ds.Spec.OutlineWidth > 0 ? stroke : family.Edge;
		var seamW = ds.Spec.OutlineWidth > 0 ? sw : "1.25";
		if (isDb)
		{
			// cylinder: body + front of the top cap
			_ = sb.Append("<path d=\"M").Append(p.X).Append(',').Append(p.Y + CapRy)
				.Append(" A").Append(p.W / 2).Append(',').Append(CapRy).Append(" 0 0 1 ").Append(p.Right).Append(',').Append(p.Y + CapRy)
				.Append(" L").Append(p.Right).Append(',').Append(p.Bottom - CapRy)
				.Append(" A").Append(p.W / 2).Append(',').Append(CapRy).Append(" 0 0 1 ").Append(p.X).Append(',').Append(p.Bottom - CapRy)
				.Append(" Z\" fill=\"").Append(fill).Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"").Append(sw).Append("\" />\n  ");
			_ = sb.Append("<path d=\"M").Append(p.X).Append(',').Append(p.Y + CapRy)
				.Append(" A").Append(p.W / 2).Append(',').Append(CapRy).Append(" 0 0 0 ").Append(p.Right).Append(',').Append(p.Y + CapRy)
				.Append("\" fill=\"none\" stroke=\"").Append(seam).Append("\" stroke-width=\"").Append(seamW).Append("\" />");
		}
		else if (isQueue)
		{
			// horizontal cylinder: body + front of the right cap
			_ = sb.Append("<path d=\"M").Append(p.X + CapRx).Append(',').Append(p.Y)
				.Append(" L").Append(p.Right - CapRx).Append(',').Append(p.Y)
				.Append(" A").Append(CapRx).Append(',').Append(p.H / 2).Append(" 0 0 1 ").Append(p.Right - CapRx).Append(',').Append(p.Bottom)
				.Append(" L").Append(p.X + CapRx).Append(',').Append(p.Bottom)
				.Append(" A").Append(CapRx).Append(',').Append(p.H / 2).Append(" 0 0 1 ").Append(p.X + CapRx).Append(',').Append(p.Y)
				.Append(" Z\" fill=\"").Append(fill).Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"").Append(sw).Append("\" />\n  ");
			_ = sb.Append("<path d=\"M").Append(p.Right - CapRx).Append(',').Append(p.Y)
				.Append(" A").Append(CapRx).Append(',').Append(p.H / 2).Append(" 0 0 0 ").Append(p.Right - CapRx).Append(',').Append(p.Bottom)
				.Append("\" fill=\"none\" stroke=\"").Append(seam).Append("\" stroke-width=\"").Append(seamW).Append("\" />");
		}
		else
		{
			ds.AppendBox(sb, p.X, p.Y, p.W, p.H, family);
		}

		// text column, top aligned: [glyph] [kind] name [tech] description
		var cx = isQueue ? p.Cx - (CapRx / 2) : p.Cx;
		var y = p.Y + 14 + (isDb ? CapRy * 2 : 0);
		if (el.Type == C4ElementType.Person)
		{
			_ = sb.Append("\n  ");
			DesignSystem.AppendGlyph(sb, DesignSystem.Glyph.Person, cx, y + 10, family.Ink);
			y += GlyphRoom;
		}

		// the kind is a tag in the family ink, set in mono so it reads as notation
		_ = sb.Append("\n  ");
		var tagAttrs = (ds.IsMono(TypeRole.Tag) ? "" : "class=\"mono\" ") + ds.TextAttributes(TypeRole.Tag, family.Ink);
		MultilineUtils.AppendMultilineText(sb, text.Kind, cx, y + (TagLine / 2), DesignSystem.Px(TypeRole.Tag), tagAttrs);
		y += TagLine + 4;

		AppendLines(sb, ds, text.Name, cx, ref y, HeadingLine, TypeRole.Heading, null);
		AppendLines(sb, ds, text.Tech, cx, ref y, MetaLine, TypeRole.Meta, null);
		if (text.Description.Count > 0)
		{
			y += 4;
			AppendLines(sb, ds, text.Description, cx, ref y, BodyLine, TypeRole.Body, "var(--_text-sec)");
		}

		_ = sb.Append("\n</g>");
	}

	private static void AppendLines(StringBuilder sb, DesignSystem ds, IReadOnlyList<string> lines, double cx, ref double y, double lineHeight, TypeRole role, string? color)
	{
		foreach (var line in lines)
		{
			_ = sb.Append("\n  ");
			ds.AppendText(sb, line, cx, y + (lineHeight / 2), role, color);
			y += lineHeight;
		}
	}

	private static void AppendRelation(StringBuilder sb, DesignSystem ds, C4Relation rel, Route route, double cornerRadius)
	{
		_ = sb.Append("\n<path class=\"c4-relation\" data-from=\"");
		MultilineUtils.AppendEscapedAttr(sb, rel.From.AsSpan());
		_ = sb.Append("\" data-to=\"");
		MultilineUtils.AppendEscapedAttr(sb, rel.To.AsSpan());
		_ = sb.Append("\" d=\"");
		SvgRenderer.BuildOrthogonalPath(sb, route.Points, route.Self ? Math.Min(6, cornerRadius) : cornerRadius);
		_ = sb.Append("\" fill=\"none\" stroke=\"").Append(DesignSystem.EdgeColor).Append("\" stroke-width=\"").Append(ds.EdgeWidth)
			.Append("\" marker-end=\"").Append(ds.Marker(MarkerShape.Arrow)).Append('"');
		if (rel.Bidirectional)
			_ = sb.Append(" marker-start=\"").Append(ds.Marker(MarkerShape.Arrow, atStart: true)).Append('"');
		_ = sb.Append(" />");
	}

	private static string LabelText(C4Relation rel) =>
		rel.Technology is { Length: > 0 } t ? $"{rel.Label} [{t}]" : rel.Label ?? "";

	private static double LabelWidth(C4Relation rel) =>
		rel.Label is { Length: > 0 } ? TextMetrics.MeasureTextWidth(LabelText(rel), DesignSystem.Px(TypeRole.Caption), 600) : 0;

	private static void AppendRelationLabel(StringBuilder sb, DesignSystem ds, C4Relation rel, Route route)
	{
		if (rel.Label is not { Length: > 0 })
			return;

		var cx = route.Self ? route.LabelAt.X + (DesignSystem.LabelBoxWidth(LabelWidth(rel)) / 2) : route.LabelAt.X;
		// halo labels have no box, so on a horizontal run they sit just above the line instead of on it
		var cy = ds.Spec.Label == LabelKind.Halo && !route.Self && !route.LabelOnVertical
			? route.LabelAt.Y - (DesignSystem.PillHeight / 2)
			: route.LabelAt.Y;
		_ = sb.Append("\n<g class=\"edge-label\" data-from=\"");
		MultilineUtils.AppendEscapedAttr(sb, rel.From.AsSpan());
		_ = sb.Append("\" data-to=\"");
		MultilineUtils.AppendEscapedAttr(sb, rel.To.AsSpan());
		_ = sb.Append("\">\n  ");
		ds.AppendEdgeLabel(sb, cx, cy, LabelText(rel));
		_ = sb.Append("\n</g>");
	}

	private static string TypeLabel(C4Element el)
	{
		var ext = el.External ? "external " : "";
		return el.Type switch
		{
			C4ElementType.Person => $"[{ext}person]",
			C4ElementType.System => $"[{ext}system]",
			C4ElementType.SystemDb => $"[{ext}system db]",
			C4ElementType.SystemQueue => $"[{ext}system queue]",
			C4ElementType.Container => $"[{ext}container]",
			C4ElementType.ContainerDb => $"[{ext}container db]",
			C4ElementType.ContainerQueue => $"[{ext}container queue]",
			C4ElementType.Component => $"[{ext}component]",
			C4ElementType.ComponentDb => $"[{ext}component db]",
			C4ElementType.ComponentQueue => $"[{ext}component queue]",
			C4ElementType.DeploymentNode => "[deployment node]",
			_ => "[element]",
		};
	}
}
