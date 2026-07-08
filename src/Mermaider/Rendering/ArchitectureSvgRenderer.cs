using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class ArchitectureSvgRenderer
{
	private const double Margin = 32;
	private const double GroupPad = 16;
	private const double GroupHeaderH = 28;
	private const double ServiceW = 120;
	private const double ServiceH = 72;
	private const double ServiceGap = 16;
	private const double GroupGap = 36;
	private const double IconSize = 22;
	private const double EmptyGroupMinW = 100;
	private const double EmptyGroupMinH = 64;

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

		// Groups (dashed containers) first so services paint on top
		foreach (var g in layout.Groups)
			AppendGroup(sb, g);

		foreach (var s in layout.Services)
			AppendService(sb, s);

		foreach (var e in layout.Edges)
			AppendEdge(sb, e, layout);

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

		var groupById = diagram.Groups.ToDictionary(g => g.Id, StringComparer.Ordinal);
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
			var (w, h) = PlaceGroupTree(group, cursorX, Margin, groupById, servicesByParent, childGroupsByParent, placedGroups, placedServices, bounds);
			cursorX += w + GroupGap;
			maxBottom = Math.Max(maxBottom, Margin + h);
		}

		// Ungrouped services: place in a row to the right of groups, or starting at margin if none
		if (ungrouped.Count > 0)
		{
			var sx = topLevelGroups.Count > 0 ? cursorX : Margin;
			var sy = Margin;
			var rowBottom = sy;
			foreach (var svc in ungrouped)
			{
				placedServices.Add(new PlacedService(svc.Id, svc.Icon, svc.Label, sx, sy, ServiceW, ServiceH));
				bounds[svc.Id] = (sx, sy, ServiceW, ServiceH);
				rowBottom = Math.Max(rowBottom, sy + ServiceH);
				sx += ServiceW + ServiceGap;
			}
			cursorX = Math.Max(cursorX, sx);
			maxBottom = Math.Max(maxBottom, rowBottom);
		}

		// If nothing placed, still produce a minimal canvas
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

		var width = Math.Max(cursorX - GroupGap + Margin, Margin + EmptyGroupMinW);
		// cursorX already includes trailing gap after last column; clamp if no trailing content
		if (topLevelGroups.Count > 0 || ungrouped.Count > 0)
			width = cursorX - (ungrouped.Count > 0 ? ServiceGap : GroupGap) + Margin;
		var height = maxBottom + Margin;

		return new LayoutResult
		{
			Width = Math.Max(width, 120),
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
		Dictionary<string, ArchitectureGroup> groupById,
		Dictionary<string, List<ArchitectureService>> servicesByParent,
		Dictionary<string, List<ArchitectureGroup>> childGroupsByParent,
		List<PlacedGroup> placedGroups,
		List<PlacedService> placedServices,
		Dictionary<string, (double X, double Y, double W, double H)> bounds)
	{
		_ = groupById;
		var services = servicesByParent.GetValueOrDefault(group.Id, []);
		var childGroups = childGroupsByParent.GetValueOrDefault(group.Id, []);

		var contentX = x + GroupPad;
		var contentY = y + GroupHeaderH + GroupPad;
		var contentRight = contentX;
		var contentBottom = contentY;

		// Stack direct services vertically
		var svcY = contentY;
		foreach (var svc in services)
		{
			placedServices.Add(new PlacedService(svc.Id, svc.Icon, svc.Label, contentX, svcY, ServiceW, ServiceH));
			bounds[svc.Id] = (contentX, svcY, ServiceW, ServiceH);
			contentRight = Math.Max(contentRight, contentX + ServiceW);
			contentBottom = Math.Max(contentBottom, svcY + ServiceH);
			svcY += ServiceH + ServiceGap;
		}

		// Nested groups as further columns inside this group
		var nestedX = services.Count > 0 ? contentX + ServiceW + GroupGap : contentX;
		var nestedY = contentY;
		foreach (var child in childGroups)
		{
			var (cw, ch) = PlaceGroupTree(child, nestedX, nestedY, groupById, servicesByParent, childGroupsByParent, placedGroups, placedServices, bounds);
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

		var labelW = TextMetrics.MeasureTextWidth(group.Label, 13, 600) + 40;
		var w = Math.Max(innerW + (GroupPad * 2), labelW);
		var h = GroupHeaderH + GroupPad + innerH + GroupPad;

		placedGroups.Add(new PlacedGroup(group.Id, group.Icon, group.Label, x, y, w, h));
		bounds[group.Id] = (x, y, w, h);
		return (w, h);
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
			.Append("\" fill=\"var(--_arrow)\" />\n");
		_ = sb.Append("  </marker>\n");
		_ = sb.Append("  <marker id=\"arch-arrow-start\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(s)
			.Append("\" markerHeight=\"").Append(s)
			.Append("\" refX=\"0\" refY=\"").Append(F(hh))
			.Append("\" orient=\"auto\">\n");
		_ = sb.Append("    <polygon points=\"").Append(s).Append(" 0, 0 ").Append(F(hh))
			.Append(", ").Append(s).Append(' ').Append(s)
			.Append("\" fill=\"var(--_arrow)\" />\n");
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
			.Append("\" fill=\"var(--_group-fill)\" stroke=\"var(--_group-stroke)\" stroke-width=\"1.5\" stroke-dasharray=\"6 4\" />\n");

		// Icon glyph + label in header
		var iconCx = g.X + GroupPad + (IconSize / 2);
		var iconCy = g.Y + (GroupHeaderH / 2);
		AppendIconGlyph(sb, g.Icon, iconCx, iconCy, IconSize * 0.85);

		var labelX = g.X + GroupPad + IconSize + 8;
		var labelY = g.Y + (GroupHeaderH / 2);
		_ = sb.Append("  <text x=\"").Append(F(labelX)).Append("\" y=\"").Append(F(labelY))
			.Append("\" text-anchor=\"start\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(RenderConstants.FsVar.S)
			.Append("\" font-weight=\"").Append(RenderConstants.FontWeights.GroupHeader)
			.Append("\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, g.Label.AsSpan());
		_ = sb.Append("</text>\n</g>");
	}

	private static void AppendService(StringBuilder sb, PlacedService s)
	{
		_ = sb.Append("\n<g class=\"arch-service\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, s.Id.AsSpan());
		_ = sb.Append("\">\n");
		_ = sb.Append("  <rect x=\"").Append(F(s.X)).Append("\" y=\"").Append(F(s.Y))
			.Append("\" width=\"").Append(F(s.W)).Append("\" height=\"").Append(F(s.H))
			.Append("\" rx=\"").Append(RenderConstants.Radii.Rectangle)
			.Append("\" fill=\"var(--_node-fill)\" stroke=\"var(--_node-stroke)\" stroke-width=\"1.5\" />\n");

		var iconCx = s.X + (s.W / 2);
		var iconCy = s.Y + 22;
		AppendIconGlyph(sb, s.Icon, iconCx, iconCy, IconSize);

		var labelY = s.Y + s.H - 18;
		_ = sb.Append("  <text x=\"").Append(F(iconCx)).Append("\" y=\"").Append(F(labelY))
			.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(RenderConstants.FsVar.S)
			.Append("\" font-weight=\"").Append(RenderConstants.FontWeights.NodeLabel)
			.Append("\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, s.Label.AsSpan());
		_ = sb.Append("</text>\n</g>");
	}

	private static void AppendIconGlyph(StringBuilder sb, string icon, double cx, double cy, double size)
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
				AppendDatabaseIcon(sb, cx, cy, half);
				break;

			case "disk":
			case "storage":
				_ = sb.Append("  <circle cx=\"").Append(F(cx)).Append("\" cy=\"").Append(F(cy))
					.Append("\" r=\"").Append(F(half * 0.75))
					.Append("\" fill=\"none\" stroke=\"var(--_accent)\" stroke-width=\"1.5\" />\n");
				_ = sb.Append("  <circle cx=\"").Append(F(cx)).Append("\" cy=\"").Append(F(cy))
					.Append("\" r=\"").Append(F(half * 0.22))
					.Append("\" fill=\"var(--_accent)\" />\n");
				break;

			case "server":
				AppendServerIcon(sb, cx, cy, half, size);
				break;

			case "internet":
			case "globe":
				AppendInternetIcon(sb, cx, cy, half);
				break;

			case "cloud":
				AppendCloudIcon(sb, cx, cy, half);
				break;

			default:
				// Letter glyph from first character of icon name
				var letter = key.Length > 0 ? char.ToUpperInvariant(key[0]).ToString() : "?";
				_ = sb.Append("  <circle cx=\"").Append(F(cx)).Append("\" cy=\"").Append(F(cy))
					.Append("\" r=\"").Append(F(half * 0.8))
					.Append("\" fill=\"none\" stroke=\"var(--_accent)\" stroke-width=\"1.5\" />\n");
				_ = sb.Append("  <text x=\"").Append(F(cx)).Append("\" y=\"").Append(F(cy))
					.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
					.Append("\" font-size=\"").Append(RenderConstants.FsVar.S)
					.Append("\" font-weight=\"700\" fill=\"var(--_accent)\">");
				MultilineUtils.AppendEscapedXml(sb, letter.AsSpan());
				_ = sb.Append("</text>\n");
				break;
		}
	}

	private static void AppendDatabaseIcon(StringBuilder sb, double cx, double cy, double half)
	{
		var top = cy - (half * 0.55);
		var bottom = cy + (half * 0.4);
		var left = cx - (half * 0.7);
		var right = cx + (half * 0.7);
		var rx = half * 0.7;
		var ry = half * 0.28;

		_ = sb.Append("  <ellipse cx=\"").Append(F(cx)).Append("\" cy=\"").Append(F(top))
			.Append("\" rx=\"").Append(F(rx)).Append("\" ry=\"").Append(F(ry))
			.Append("\" fill=\"none\" stroke=\"var(--_accent)\" stroke-width=\"1.5\" />\n");
		_ = sb.Append("  <path d=\"M").Append(F(left)).Append(' ').Append(F(top))
			.Append(" L").Append(F(left)).Append(' ').Append(F(bottom))
			.Append(" A").Append(F(rx)).Append(' ').Append(F(ry))
			.Append(" 0 0 0 ").Append(F(right)).Append(' ').Append(F(bottom))
			.Append(" L").Append(F(right)).Append(' ').Append(F(top))
			.Append("\" fill=\"none\" stroke=\"var(--_accent)\" stroke-width=\"1.5\" />\n");
	}

	private static void AppendServerIcon(StringBuilder sb, double cx, double cy, double half, double size)
	{
		var left = cx - (half * 0.7);
		var top = cy - (half * 0.7);
		var box = size * 0.7;
		_ = sb.Append("  <rect x=\"").Append(F(left)).Append("\" y=\"").Append(F(top))
			.Append("\" width=\"").Append(F(box)).Append("\" height=\"").Append(F(box))
			.Append("\" rx=\"2\" fill=\"none\" stroke=\"var(--_accent)\" stroke-width=\"1.5\" />\n");

		var lineLeft = cx - (half * 0.45);
		var lineRight = cx + (half * 0.45);
		var y1 = cy - (half * 0.25);
		var y2 = cy + (half * 0.1);
		_ = sb.Append("  <line x1=\"").Append(F(lineLeft)).Append("\" y1=\"").Append(F(y1))
			.Append("\" x2=\"").Append(F(lineRight)).Append("\" y2=\"").Append(F(y1))
			.Append("\" stroke=\"var(--_accent)\" stroke-width=\"1.25\" />\n");
		_ = sb.Append("  <line x1=\"").Append(F(lineLeft)).Append("\" y1=\"").Append(F(y2))
			.Append("\" x2=\"").Append(F(lineRight)).Append("\" y2=\"").Append(F(y2))
			.Append("\" stroke=\"var(--_accent)\" stroke-width=\"1.25\" />\n");
	}

	private static void AppendInternetIcon(StringBuilder sb, double cx, double cy, double half)
	{
		var r = half * 0.75;
		_ = sb.Append("  <circle cx=\"").Append(F(cx)).Append("\" cy=\"").Append(F(cy))
			.Append("\" r=\"").Append(F(r))
			.Append("\" fill=\"none\" stroke=\"var(--_accent)\" stroke-width=\"1.5\" />\n");
		_ = sb.Append("  <ellipse cx=\"").Append(F(cx)).Append("\" cy=\"").Append(F(cy))
			.Append("\" rx=\"").Append(F(half * 0.35)).Append("\" ry=\"").Append(F(r))
			.Append("\" fill=\"none\" stroke=\"var(--_accent)\" stroke-width=\"1.25\" />\n");
		_ = sb.Append("  <line x1=\"").Append(F(cx - r)).Append("\" y1=\"").Append(F(cy))
			.Append("\" x2=\"").Append(F(cx + r)).Append("\" y2=\"").Append(F(cy))
			.Append("\" stroke=\"var(--_accent)\" stroke-width=\"1.25\" />\n");
	}

	private static void AppendCloudIcon(StringBuilder sb, double cx, double cy, double half)
	{
		var startX = cx - (half * 0.55);
		var startY = cy + (half * 0.25);
		_ = sb.Append("  <path d=\"M").Append(F(startX)).Append(' ').Append(F(startY))
			.Append(" a").Append(F(half * 0.4)).Append(' ').Append(F(half * 0.35))
			.Append(" 0 1 1 ").Append(F(half * 0.15)).Append(' ').Append(F(-(half * 0.45)))
			.Append(" a").Append(F(half * 0.45)).Append(' ').Append(F(half * 0.4))
			.Append(" 0 1 1 ").Append(F(half * 0.7)).Append(" 0")
			.Append(" a").Append(F(half * 0.35)).Append(' ').Append(F(half * 0.3))
			.Append(" 0 1 1 ").Append(F(half * 0.2)).Append(' ').Append(F(half * 0.45))
			.Append(" z\" fill=\"none\" stroke=\"var(--_accent)\" stroke-width=\"1.5\" />\n");
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

		// Simple elbow: mid-point bend based on dominant direction
		string path;
		var horizontalPorts = edge.SourcePort is ArchitecturePort.Left or ArchitecturePort.Right
			|| edge.TargetPort is ArchitecturePort.Left or ArchitecturePort.Right;
		if (horizontalPorts)
		{
			var mx = (x1 + x2) / 2;
			path = $"M{F(x1)} {F(y1)} L{F(mx)} {F(y1)} L{F(mx)} {F(y2)} L{F(x2)} {F(y2)}";
		}
		else
		{
			var my = (y1 + y2) / 2;
			path = $"M{F(x1)} {F(y1)} L{F(x1)} {F(my)} L{F(x2)} {F(my)} L{F(x2)} {F(y2)}";
		}

		_ = sb.Append("\n<path d=\"").Append(path)
			.Append("\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"1.75\"");

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
