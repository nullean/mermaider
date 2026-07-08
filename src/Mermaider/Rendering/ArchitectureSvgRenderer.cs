using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class ArchitectureSvgRenderer
{
	private const double Margin = 40;
	private const double GroupPad = 28;
	private const double GroupHeaderH = 36;
	private const double IconTile = 56;
	private const double ServiceW = 100;
	private const double ServiceH = 92; // icon tile + label
	private const double CellGapX = 48;
	private const double CellGapY = 40;
	private const double GroupGap = 48;
	private const double EmptyGroupMinW = 140;
	private const double EmptyGroupMinH = 100;

	// Mermaid-like solid icon chrome (architecture is fixed-style upstream)
	private const string IconFill = "#326ce5";
	private const string IconGlyph = "#ffffff";
	private const string GroupStroke = "#a5b4fc";

	internal static string Render(ArchitectureDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
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

	internal static StringBuilder RenderToBuilder(ArchitectureDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var layout = Layout(diagram);

		StyleBlock.AppendSvgOpenTag(sb, layout.Width, layout.Height, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict);
		AppendDefs(sb);

		foreach (var g in layout.Groups)
			AppendGroup(sb, g);

		// Edges under services so arrowheads don't cover tiles
		foreach (var e in layout.Edges)
			AppendEdge(sb, e, layout);

		foreach (var s in layout.Services)
			AppendService(sb, s);

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private sealed class LayoutResult
	{
		public double Width { get; init; }
		public double Height { get; init; }
		public required List<PlacedGroup> Groups { get; init; }
		public required List<PlacedService> Services { get; init; }
		public required List<ArchitectureEdge> Edges { get; init; }
		public required Dictionary<string, (double X, double Y, double W, double H)> Bounds { get; init; }
	}

	private sealed record PlacedGroup(string Id, string Icon, string Label, double X, double Y, double W, double H);
	private sealed record PlacedService(string Id, string Icon, string Label, double X, double Y, double W, double H);

	private static LayoutResult Layout(ArchitectureDiagram diagram)
	{
		var placedGroups = new List<PlacedGroup>();
		var placedServices = new List<PlacedService>();
		var bounds = new Dictionary<string, (double X, double Y, double W, double H)>(StringComparer.Ordinal);

		var servicesByParent = diagram.Services
			.GroupBy(s => s.ParentId ?? "", StringComparer.Ordinal)
			.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
		var childGroupsByParent = diagram.Groups
			.Where(g => g.ParentId is not null)
			.GroupBy(g => g.ParentId!, StringComparer.Ordinal)
			.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

		var topLevelGroups = diagram.Groups.Where(g => g.ParentId is null).ToList();
		var ungrouped = servicesByParent.GetValueOrDefault("", []);

		var cursorX = Margin;
		var maxBottom = Margin;

		foreach (var group in topLevelGroups)
		{
			var (w, h) = PlaceGroupTree(
				group, cursorX, Margin, diagram.Edges,
				servicesByParent, childGroupsByParent,
				placedGroups, placedServices, bounds);
			cursorX += w + GroupGap;
			maxBottom = Math.Max(maxBottom, Margin + h);
		}

		if (ungrouped.Count > 0)
		{
			var originX = topLevelGroups.Count > 0 ? cursorX : Margin;
			var (uw, uh) = PlaceServiceCluster(
				ungrouped, diagram.Edges, originX, Margin, placedServices, bounds);
			cursorX = Math.Max(cursorX, originX + uw + GroupGap);
			maxBottom = Math.Max(maxBottom, Margin + uh);
		}

		if (placedGroups.Count == 0 && placedServices.Count == 0)
		{
			return new LayoutResult
			{
				Width = 200,
				Height = 100,
				Groups = placedGroups,
				Services = placedServices,
				Edges = diagram.Edges.ToList(),
				Bounds = bounds,
			};
		}

		var width = cursorX - GroupGap + Margin;
		if (width < Margin + 120)
			width = Margin + 120;
		var height = maxBottom + Margin;

		return new LayoutResult
		{
			Width = width,
			Height = Math.Max(height, 80),
			Groups = placedGroups,
			Services = placedServices,
			Edges = diagram.Edges.ToList(),
			Bounds = bounds,
		};
	}

	private static (double W, double H) PlaceGroupTree(
		ArchitectureGroup group,
		double x,
		double y,
		IReadOnlyList<ArchitectureEdge> edges,
		Dictionary<string, List<ArchitectureService>> servicesByParent,
		Dictionary<string, List<ArchitectureGroup>> childGroupsByParent,
		List<PlacedGroup> placedGroups,
		List<PlacedService> placedServices,
		Dictionary<string, (double X, double Y, double W, double H)> bounds)
	{
		var services = servicesByParent.GetValueOrDefault(group.Id, []);
		var childGroups = childGroupsByParent.GetValueOrDefault(group.Id, []);

		var contentX = x + GroupPad;
		var contentY = y + GroupHeaderH + GroupPad;
		var contentRight = contentX;
		var contentBottom = contentY;

		if (services.Count > 0)
		{
			var (cw, ch) = PlaceServiceCluster(services, edges, contentX, contentY, placedServices, bounds);
			contentRight = contentX + cw;
			contentBottom = contentY + ch;
		}

		var nestedX = services.Count > 0 ? contentRight + GroupGap : contentX;
		var nestedY = contentY;
		foreach (var child in childGroups)
		{
			var (cw, ch) = PlaceGroupTree(child, nestedX, nestedY, edges, servicesByParent, childGroupsByParent, placedGroups, placedServices, bounds);
			contentRight = Math.Max(contentRight, nestedX + cw);
			contentBottom = Math.Max(contentBottom, nestedY + ch);
			nestedX += cw + GroupGap;
		}

		double innerW;
		double innerH;
		if (services.Count == 0 && childGroups.Count == 0)
		{
			innerW = EmptyGroupMinW;
			innerH = EmptyGroupMinH;
		}
		else
		{
			innerW = Math.Max(contentRight - contentX, EmptyGroupMinW);
			innerH = Math.Max(contentBottom - contentY, ServiceH);
		}

		var labelW = TextMetrics.MeasureTextWidth(group.Label, 14, 600) + 48;
		var w = Math.Max(innerW + (GroupPad * 2), labelW);
		var h = GroupHeaderH + GroupPad + innerH + GroupPad;

		placedGroups.Add(new PlacedGroup(group.Id, group.Icon, group.Label, x, y, w, h));
		bounds[group.Id] = (x, y, w, h);
		return (w, h);
	}

	/// <summary>
	/// Place services on a grid derived from edge port directions
	/// (R→L = left of, T→B = below, etc.) so layout matches mermaid spatial intent.
	/// </summary>
	private static (double W, double H) PlaceServiceCluster(
		List<ArchitectureService> services,
		IReadOnlyList<ArchitectureEdge> allEdges,
		double originX,
		double originY,
		List<PlacedService> placedServices,
		Dictionary<string, (double X, double Y, double W, double H)> bounds)
	{
		var ids = services.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
		var col = new Dictionary<string, int>(StringComparer.Ordinal);
		var row = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var s in services)
		{
			col[s.Id] = 0;
			row[s.Id] = 0;
		}

		// Local edges only (both ends in this cluster)
		var edges = allEdges
			.Where(e => ids.Contains(e.SourceId) && ids.Contains(e.TargetId))
			.ToList();

		// Relax port constraints into grid coords
		for (var iter = 0; iter < Math.Max(4, services.Count * 2); iter++)
		{
			foreach (var e in edges)
			{
				ApplyPortConstraint(e, col, row);
			}
		}

		// Normalize so min col/row is 0
		if (services.Count > 0)
		{
			var minC = col.Values.Min();
			var minR = row.Values.Min();
			foreach (var s in services)
			{
				col[s.Id] -= minC;
				row[s.Id] -= minR;
			}
		}

		// Resolve collisions: if two share a cell, nudge later one down/right
		var occupied = new Dictionary<(int C, int R), string>();
		foreach (var s in services.OrderBy(s => s.Id, StringComparer.Ordinal))
		{
			var c = col[s.Id];
			var r = row[s.Id];
			while (occupied.ContainsKey((c, r)))
			{
				// Prefer push down, then right
				r++;
				if (r > services.Count)
				{
					r = 0;
					c++;
				}
			}
			col[s.Id] = c;
			row[s.Id] = r;
			occupied[(c, r)] = s.Id;
		}

		var maxC = services.Count == 0 ? 0 : col.Values.Max();
		var maxR = services.Count == 0 ? 0 : row.Values.Max();
		var cellW = ServiceW + CellGapX;
		var cellH = ServiceH + CellGapY;

		foreach (var s in services)
		{
			var x = originX + (col[s.Id] * cellW);
			var y = originY + (row[s.Id] * cellH);
			// Service bounds for edges: the icon tile (not the full label area)
			var tileX = x + ((ServiceW - IconTile) / 2);
			var tileY = y;
			placedServices.Add(new PlacedService(s.Id, s.Icon, s.Label, x, y, ServiceW, ServiceH));
			// Edge attachment uses icon tile so arrows hit the blue squares
			bounds[s.Id] = (tileX, tileY, IconTile, IconTile);
		}

		var w = ((maxC + 1) * cellW) - CellGapX;
		var h = ((maxR + 1) * cellH) - CellGapY;
		if (services.Count == 0)
		{
			w = 0;
			h = 0;
		}
		return (Math.Max(w, 0), Math.Max(h, 0));
	}

	private static void ApplyPortConstraint(
		ArchitectureEdge e,
		Dictionary<string, int> col,
		Dictionary<string, int> row)
	{
		// Source port tells which side of source the edge leaves — implies relative placement.
		// Target port is where it arrives.
		// db:R --> L:server  => db left of server
		// disk:T --> B:server => disk below server
		switch (e.SourcePort)
		{
			case ArchitecturePort.Right when e.TargetPort == ArchitecturePort.Left:
				// source left of target
				if (col[e.SourceId] >= col[e.TargetId])
					col[e.TargetId] = col[e.SourceId] + 1;
				// prefer same row
				row[e.TargetId] = row[e.SourceId];
				break;
			case ArchitecturePort.Left when e.TargetPort == ArchitecturePort.Right:
				if (col[e.TargetId] >= col[e.SourceId])
					col[e.SourceId] = col[e.TargetId] + 1;
				row[e.SourceId] = row[e.TargetId];
				break;
			case ArchitecturePort.Bottom when e.TargetPort == ArchitecturePort.Top:
				// source above target
				if (row[e.SourceId] >= row[e.TargetId])
					row[e.TargetId] = row[e.SourceId] + 1;
				col[e.TargetId] = col[e.SourceId];
				break;
			case ArchitecturePort.Top when e.TargetPort == ArchitecturePort.Bottom:
				// source below target
				if (row[e.TargetId] >= row[e.SourceId])
					row[e.SourceId] = row[e.TargetId] + 1;
				col[e.SourceId] = col[e.TargetId];
				break;
			case ArchitecturePort.Right:
				if (col[e.SourceId] >= col[e.TargetId])
					col[e.TargetId] = col[e.SourceId] + 1;
				break;
			case ArchitecturePort.Left:
				if (col[e.TargetId] >= col[e.SourceId])
					col[e.SourceId] = col[e.TargetId] + 1;
				break;
			case ArchitecturePort.Bottom:
				if (row[e.SourceId] >= row[e.TargetId])
					row[e.TargetId] = row[e.SourceId] + 1;
				break;
			case ArchitecturePort.Top:
				if (row[e.TargetId] >= row[e.SourceId])
					row[e.SourceId] = row[e.TargetId] + 1;
				break;
		}
	}

	private static void AppendDefs(StringBuilder sb)
	{
		var s = RenderConstants.ArrowHead.Size;
		var hh = s / 2.0;
		_ = sb.Append("\n<defs>\n");
		_ = sb.Append("  <marker id=\"arch-arrow\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(s)
			.Append("\" markerHeight=\"").Append(s)
			.Append("\" refX=\"").Append(s)
			.Append("\" refY=\"").Append(F(hh))
			.Append("\" orient=\"auto\">\n");
		_ = sb.Append("    <polygon points=\"0 0, ").Append(s).Append(' ').Append(F(hh))
			.Append(", 0 ").Append(s)
			.Append("\" fill=\"#1f2937\" />\n");
		_ = sb.Append("  </marker>\n");
		_ = sb.Append("  <marker id=\"arch-arrow-start\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(s)
			.Append("\" markerHeight=\"").Append(s)
			.Append("\" refX=\"0\" refY=\"").Append(F(hh))
			.Append("\" orient=\"auto\">\n");
		_ = sb.Append("    <polygon points=\"").Append(s).Append(" 0, 0 ").Append(F(hh))
			.Append(", ").Append(s).Append(' ').Append(s)
			.Append("\" fill=\"#1f2937\" />\n");
		_ = sb.Append("  </marker>\n");
		_ = sb.Append("</defs>\n");
	}

	private static void AppendGroup(StringBuilder sb, PlacedGroup g)
	{
		_ = sb.Append("\n<g class=\"arch-group\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, g.Id.AsSpan());
		_ = sb.Append("\">\n");
		_ = sb.Append("  <rect x=\"").Append(F(g.X)).Append("\" y=\"").Append(F(g.Y))
			.Append("\" width=\"").Append(F(g.W)).Append("\" height=\"").Append(F(g.H))
			.Append("\" rx=\"").Append(RenderConstants.Radii.Group)
			.Append("\" fill=\"none\" stroke=\"").Append(GroupStroke)
			.Append("\" stroke-width=\"1.75\" stroke-dasharray=\"7 5\" />\n");

		// Header chip: blue tile + label (mermaid style)
		var chip = 22.0;
		var chipX = g.X + 14;
		var chipY = g.Y + 10;
		_ = sb.Append("  <rect x=\"").Append(F(chipX)).Append("\" y=\"").Append(F(chipY))
			.Append("\" width=\"").Append(F(chip)).Append("\" height=\"").Append(F(chip))
			.Append("\" rx=\"4\" fill=\"").Append(IconFill).Append("\" />\n");
		AppendIconGlyph(sb, g.Icon, chipX + (chip / 2), chipY + (chip / 2), chip * 0.72, IconGlyph);

		var labelX = chipX + chip + 8;
		var labelY = chipY + (chip / 2);
		_ = sb.Append("  <text x=\"").Append(F(labelX)).Append("\" y=\"").Append(F(labelY))
			.Append("\" text-anchor=\"start\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(RenderConstants.FsVar.S)
			.Append("\" font-weight=\"600\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, g.Label.AsSpan());
		_ = sb.Append("</text>\n</g>");
	}

	private static void AppendService(StringBuilder sb, PlacedService s)
	{
		_ = sb.Append("\n<g class=\"arch-service\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, s.Id.AsSpan());
		_ = sb.Append("\">\n");

		// Blue icon tile centered in service cell
		var tileX = s.X + ((s.W - IconTile) / 2);
		var tileY = s.Y;
		_ = sb.Append("  <rect x=\"").Append(F(tileX)).Append("\" y=\"").Append(F(tileY))
			.Append("\" width=\"").Append(F(IconTile)).Append("\" height=\"").Append(F(IconTile))
			.Append("\" rx=\"6\" fill=\"").Append(IconFill).Append("\" />\n");

		var iconCx = tileX + (IconTile / 2);
		var iconCy = tileY + (IconTile / 2);
		AppendIconGlyph(sb, s.Icon, iconCx, iconCy, IconTile * 0.55, IconGlyph);

		// Label under tile
		var labelY = tileY + IconTile + 16;
		_ = sb.Append("  <text x=\"").Append(F(iconCx)).Append("\" y=\"").Append(F(labelY))
			.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(RenderConstants.FsVar.S)
			.Append("\" font-weight=\"500\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, s.Label.AsSpan());
		_ = sb.Append("</text>\n</g>");
	}

	private static void AppendIconGlyph(StringBuilder sb, string icon, double cx, double cy, double size, string stroke)
	{
		var half = size / 2;
		var key = icon;
		var colon = icon.LastIndexOf(':');
		if (colon >= 0 && colon < icon.Length - 1)
			key = icon[(colon + 1)..];

		var kind = key.ToLowerInvariant();
		switch (kind)
		{
			case "database":
			case "db":
				AppendDatabaseIcon(sb, cx, cy, half, stroke);
				break;
			case "disk":
			case "storage":
				AppendDiskIcon(sb, cx, cy, half, stroke);
				break;
			case "server":
				AppendServerIcon(sb, cx, cy, half, size, stroke);
				break;
			case "internet":
			case "globe":
				AppendInternetIcon(sb, cx, cy, half, stroke);
				break;
			case "cloud":
				AppendCloudIcon(sb, cx, cy, half, stroke);
				break;
			default:
				var letter = key.Length > 0 ? char.ToUpperInvariant(key[0]).ToString() : "?";
				_ = sb.Append("  <text x=\"").Append(F(cx)).Append("\" y=\"").Append(F(cy))
					.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
					.Append("\" font-size=\"").Append(F(size * 0.55))
					.Append("\" font-weight=\"700\" fill=\"").Append(stroke).Append("\">");
				MultilineUtils.AppendEscapedXml(sb, letter.AsSpan());
				_ = sb.Append("</text>\n");
				break;
		}
	}

	private static void AppendDatabaseIcon(StringBuilder sb, double cx, double cy, double half, string stroke)
	{
		var top = cy - (half * 0.45);
		var bottom = cy + (half * 0.4);
		var left = cx - (half * 0.55);
		var right = cx + (half * 0.55);
		var rx = half * 0.55;
		var ry = half * 0.22;

		_ = sb.Append("  <ellipse cx=\"").Append(F(cx)).Append("\" cy=\"").Append(F(top))
			.Append("\" rx=\"").Append(F(rx)).Append("\" ry=\"").Append(F(ry))
			.Append("\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"2\" />\n");
		_ = sb.Append("  <path d=\"M").Append(F(left)).Append(' ').Append(F(top))
			.Append(" L").Append(F(left)).Append(' ').Append(F(bottom))
			.Append(" A").Append(F(rx)).Append(' ').Append(F(ry))
			.Append(" 0 0 0 ").Append(F(right)).Append(' ').Append(F(bottom))
			.Append(" L").Append(F(right)).Append(' ').Append(F(top))
			.Append("\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"2\" />\n");
		// middle ellipse band
		var mid = cy + (half * 0.05);
		_ = sb.Append("  <path d=\"M").Append(F(left)).Append(' ').Append(F(mid))
			.Append(" A").Append(F(rx)).Append(' ').Append(F(ry))
			.Append(" 0 0 0 ").Append(F(right)).Append(' ').Append(F(mid))
			.Append("\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"1.5\" />\n");
	}

	private static void AppendDiskIcon(StringBuilder sb, double cx, double cy, double half, string stroke)
	{
		_ = sb.Append("  <circle cx=\"").Append(F(cx)).Append("\" cy=\"").Append(F(cy))
			.Append("\" r=\"").Append(F(half * 0.7))
			.Append("\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"2\" />\n");
		_ = sb.Append("  <circle cx=\"").Append(F(cx)).Append("\" cy=\"").Append(F(cy))
			.Append("\" r=\"").Append(F(half * 0.18))
			.Append("\" fill=\"").Append(stroke).Append("\" />\n");
		// platter arm
		_ = sb.Append("  <line x1=\"").Append(F(cx + (half * 0.12))).Append("\" y1=\"").Append(F(cy - (half * 0.1)))
			.Append("\" x2=\"").Append(F(cx + (half * 0.45))).Append("\" y2=\"").Append(F(cy - (half * 0.4)))
			.Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"2\" stroke-linecap=\"round\" />\n");
	}

	private static void AppendServerIcon(StringBuilder sb, double cx, double cy, double half, double size, string stroke)
	{
		var left = cx - (half * 0.6);
		var top = cy - (half * 0.55);
		var boxW = size * 0.6;
		var boxH = size * 0.55;
		_ = sb.Append("  <rect x=\"").Append(F(left)).Append("\" y=\"").Append(F(top))
			.Append("\" width=\"").Append(F(boxW)).Append("\" height=\"").Append(F(boxH))
			.Append("\" rx=\"3\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"2\" />\n");

		var lineLeft = cx - (half * 0.4);
		var lineRight = cx + (half * 0.4);
		for (var i = 0; i < 3; i++)
		{
			var ly = top + (boxH * (0.28 + (i * 0.22)));
			_ = sb.Append("  <line x1=\"").Append(F(lineLeft)).Append("\" y1=\"").Append(F(ly))
				.Append("\" x2=\"").Append(F(lineRight)).Append("\" y2=\"").Append(F(ly))
				.Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"1.75\" />\n");
		}
	}

	private static void AppendInternetIcon(StringBuilder sb, double cx, double cy, double half, string stroke)
	{
		var r = half * 0.7;
		_ = sb.Append("  <circle cx=\"").Append(F(cx)).Append("\" cy=\"").Append(F(cy))
			.Append("\" r=\"").Append(F(r))
			.Append("\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"2\" />\n");
		_ = sb.Append("  <ellipse cx=\"").Append(F(cx)).Append("\" cy=\"").Append(F(cy))
			.Append("\" rx=\"").Append(F(half * 0.32)).Append("\" ry=\"").Append(F(r))
			.Append("\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"1.5\" />\n");
		_ = sb.Append("  <line x1=\"").Append(F(cx - r)).Append("\" y1=\"").Append(F(cy))
			.Append("\" x2=\"").Append(F(cx + r)).Append("\" y2=\"").Append(F(cy))
			.Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"1.5\" />\n");
	}

	private static void AppendCloudIcon(StringBuilder sb, double cx, double cy, double half, string stroke)
	{
		var startX = cx - (half * 0.55);
		var startY = cy + (half * 0.2);
		_ = sb.Append("  <path d=\"M").Append(F(startX)).Append(' ').Append(F(startY))
			.Append(" a").Append(F(half * 0.38)).Append(' ').Append(F(half * 0.32))
			.Append(" 0 1 1 ").Append(F(half * 0.12)).Append(' ').Append(F(-(half * 0.4)))
			.Append(" a").Append(F(half * 0.42)).Append(' ').Append(F(half * 0.36))
			.Append(" 0 1 1 ").Append(F(half * 0.65)).Append(" 0")
			.Append(" a").Append(F(half * 0.32)).Append(' ').Append(F(half * 0.28))
			.Append(" 0 1 1 ").Append(F(half * 0.18)).Append(' ').Append(F(half * 0.4))
			.Append(" z\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"2\" />\n");
	}

	private static void AppendEdge(StringBuilder sb, ArchitectureEdge edge, LayoutResult layout)
	{
		if (!layout.Bounds.TryGetValue(edge.SourceId, out var src)
			|| !layout.Bounds.TryGetValue(edge.TargetId, out var dst))
		{
			return;
		}

		var (x1, y1) = PortPoint(src, edge.SourcePort);
		var (x2, y2) = PortPoint(dst, edge.TargetPort);

		// Prefer straight line when ports align; otherwise single elbow
		string path;
		var dx = Math.Abs(x2 - x1);
		var dy = Math.Abs(y2 - y1);
		if (dx < 1.5 || dy < 1.5)
		{
			// Already aligned
			path = $"M{F(x1)} {F(y1)} L{F(x2)} {F(y2)}";
		}
		else if (edge.SourcePort is ArchitecturePort.Left or ArchitecturePort.Right
			|| edge.TargetPort is ArchitecturePort.Left or ArchitecturePort.Right)
		{
			// Horizontal-first elbow
			var mx = (x1 + x2) / 2;
			path = $"M{F(x1)} {F(y1)} L{F(mx)} {F(y1)} L{F(mx)} {F(y2)} L{F(x2)} {F(y2)}";
		}
		else
		{
			// Vertical-first elbow
			var my = (y1 + y2) / 2;
			path = $"M{F(x1)} {F(y1)} L{F(x1)} {F(my)} L{F(x2)} {F(my)} L{F(x2)} {F(y2)}";
		}

		_ = sb.Append("\n<path d=\"").Append(path)
			.Append("\" fill=\"none\" stroke=\"#1f2937\" stroke-width=\"2\"");

		if (edge.ArrowToTarget)
			_ = sb.Append(" marker-end=\"url(#arch-arrow)\"");
		if (edge.ArrowToSource)
			_ = sb.Append(" marker-start=\"url(#arch-arrow-start)\"");

		_ = sb.Append(" />");
	}

	private static (double X, double Y) PortPoint((double X, double Y, double W, double H) b, ArchitecturePort port) =>
		port switch
		{
			ArchitecturePort.Top => (b.X + (b.W / 2), b.Y),
			ArchitecturePort.Bottom => (b.X + (b.W / 2), b.Y + b.H),
			ArchitecturePort.Left => (b.X, b.Y + (b.H / 2)),
			ArchitecturePort.Right => (b.X + b.W, b.Y + (b.H / 2)),
			_ => (b.X + (b.W / 2), b.Y + (b.H / 2)),
		};

	private static string F(double value) =>
		value.ToString("0.##", CultureInfo.InvariantCulture);
}
