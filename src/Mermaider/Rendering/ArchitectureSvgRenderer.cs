using System.Text;
using Mermaider.Layout;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class ArchitectureSvgRenderer
{
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
		var layout = ArchitectureLayout.Layout(diagram);

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

	private static void AppendDefs(StringBuilder sb)
	{
		var s = RenderConstants.ArrowHead.Size;
		var hh = s / 2.0;
		_ = sb.Append("\n<defs>\n");
		_ = sb.Append("  <marker id=\"arch-arrow\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(s)
			.Append("\" markerHeight=\"").Append(s)
			.Append("\" refX=\"").Append(s)
			.Append("\" refY=\"").Append(SvgFormat.F(hh))
			.Append("\" orient=\"auto\">\n");
		_ = sb.Append("    <polygon points=\"0 0, ").Append(s).Append(' ').Append(SvgFormat.F(hh))
			.Append(", 0 ").Append(s)
			.Append("\" fill=\"var(--_arrow)\" />\n");
		_ = sb.Append("  </marker>\n");
		_ = sb.Append("  <marker id=\"arch-arrow-start\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(s)
			.Append("\" markerHeight=\"").Append(s)
			.Append("\" refX=\"0\" refY=\"").Append(SvgFormat.F(hh))
			.Append("\" orient=\"auto\">\n");
		_ = sb.Append("    <polygon points=\"").Append(s).Append(" 0, 0 ").Append(SvgFormat.F(hh))
			.Append(", ").Append(s).Append(' ').Append(s)
			.Append("\" fill=\"var(--_arrow)\" />\n");
		_ = sb.Append("  </marker>\n");
		_ = sb.Append("</defs>\n");
	}

	private static void AppendGroup(StringBuilder sb, ArchitectureLayout.PlacedGroup g)
	{
		_ = sb.Append("\n<g class=\"arch-group\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, g.Id.AsSpan());
		_ = sb.Append("\">\n");
		_ = sb.Append("  <rect x=\"").Append(SvgFormat.F(g.X)).Append("\" y=\"").Append(SvgFormat.F(g.Y))
			.Append("\" width=\"").Append(SvgFormat.F(g.W)).Append("\" height=\"").Append(SvgFormat.F(g.H))
			.Append("\" rx=\"").Append(RenderConstants.Radii.Group)
			.Append("\" fill=\"none\" stroke=\"").Append(GroupStroke)
			.Append("\" stroke-width=\"1.75\" stroke-dasharray=\"7 5\" />\n");

		// Header chip: blue tile + label (mermaid style)
		var chip = 22.0;
		var chipX = g.X + 14;
		var chipY = g.Y + 10;
		_ = sb.Append("  <rect x=\"").Append(SvgFormat.F(chipX)).Append("\" y=\"").Append(SvgFormat.F(chipY))
			.Append("\" width=\"").Append(SvgFormat.F(chip)).Append("\" height=\"").Append(SvgFormat.F(chip))
			.Append("\" rx=\"4\" fill=\"").Append(IconFill).Append("\" />\n");
		AppendIconGlyph(sb, g.Icon, chipX + (chip / 2), chipY + (chip / 2), chip * 0.72, IconGlyph);

		var labelX = chipX + chip + 8;
		var labelY = chipY + (chip / 2);
		_ = sb.Append("  <text x=\"").Append(SvgFormat.F(labelX)).Append("\" y=\"").Append(SvgFormat.F(labelY))
			.Append("\" text-anchor=\"start\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(RenderConstants.FsVar.S)
			.Append("\" font-weight=\"600\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, g.Label.AsSpan());
		_ = sb.Append("</text>\n</g>");
	}

	private static void AppendService(StringBuilder sb, ArchitectureLayout.PlacedService s)
	{
		_ = sb.Append("\n<g class=\"arch-service\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, s.Id.AsSpan());
		_ = sb.Append("\">\n");

		// Blue icon tile centered in service cell
		var tile = ArchitectureLayout.IconTile;
		var tileX = s.X + ((s.W - tile) / 2);
		var tileY = s.Y;
		_ = sb.Append("  <rect x=\"").Append(SvgFormat.F(tileX)).Append("\" y=\"").Append(SvgFormat.F(tileY))
			.Append("\" width=\"").Append(SvgFormat.F(tile)).Append("\" height=\"").Append(SvgFormat.F(tile))
			.Append("\" rx=\"6\" fill=\"").Append(IconFill).Append("\" />\n");

		var iconCx = tileX + (tile / 2);
		var iconCy = tileY + (tile / 2);
		AppendIconGlyph(sb, s.Icon, iconCx, iconCy, tile * 0.55, IconGlyph);

		// Label under tile
		var labelY = tileY + tile + 16;
		_ = sb.Append("  <text x=\"").Append(SvgFormat.F(iconCx)).Append("\" y=\"").Append(SvgFormat.F(labelY))
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
				_ = sb.Append("  <text x=\"").Append(SvgFormat.F(cx)).Append("\" y=\"").Append(SvgFormat.F(cy))
					.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
					.Append("\" font-size=\"").Append(SvgFormat.F(size * 0.55))
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

		_ = sb.Append("  <ellipse cx=\"").Append(SvgFormat.F(cx)).Append("\" cy=\"").Append(SvgFormat.F(top))
			.Append("\" rx=\"").Append(SvgFormat.F(rx)).Append("\" ry=\"").Append(SvgFormat.F(ry))
			.Append("\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"2\" />\n");
		_ = sb.Append("  <path d=\"M").Append(SvgFormat.F(left)).Append(' ').Append(SvgFormat.F(top))
			.Append(" L").Append(SvgFormat.F(left)).Append(' ').Append(SvgFormat.F(bottom))
			.Append(" A").Append(SvgFormat.F(rx)).Append(' ').Append(SvgFormat.F(ry))
			.Append(" 0 0 0 ").Append(SvgFormat.F(right)).Append(' ').Append(SvgFormat.F(bottom))
			.Append(" L").Append(SvgFormat.F(right)).Append(' ').Append(SvgFormat.F(top))
			.Append("\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"2\" />\n");
		// middle ellipse band
		var mid = cy + (half * 0.05);
		_ = sb.Append("  <path d=\"M").Append(SvgFormat.F(left)).Append(' ').Append(SvgFormat.F(mid))
			.Append(" A").Append(SvgFormat.F(rx)).Append(' ').Append(SvgFormat.F(ry))
			.Append(" 0 0 0 ").Append(SvgFormat.F(right)).Append(' ').Append(SvgFormat.F(mid))
			.Append("\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"1.5\" />\n");
	}

	private static void AppendDiskIcon(StringBuilder sb, double cx, double cy, double half, string stroke)
	{
		_ = sb.Append("  <circle cx=\"").Append(SvgFormat.F(cx)).Append("\" cy=\"").Append(SvgFormat.F(cy))
			.Append("\" r=\"").Append(SvgFormat.F(half * 0.7))
			.Append("\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"2\" />\n");
		_ = sb.Append("  <circle cx=\"").Append(SvgFormat.F(cx)).Append("\" cy=\"").Append(SvgFormat.F(cy))
			.Append("\" r=\"").Append(SvgFormat.F(half * 0.18))
			.Append("\" fill=\"").Append(stroke).Append("\" />\n");
		// platter arm
		_ = sb.Append("  <line x1=\"").Append(SvgFormat.F(cx + (half * 0.12))).Append("\" y1=\"").Append(SvgFormat.F(cy - (half * 0.1)))
			.Append("\" x2=\"").Append(SvgFormat.F(cx + (half * 0.45))).Append("\" y2=\"").Append(SvgFormat.F(cy - (half * 0.4)))
			.Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"2\" stroke-linecap=\"round\" />\n");
	}

	private static void AppendServerIcon(StringBuilder sb, double cx, double cy, double half, double size, string stroke)
	{
		var left = cx - (half * 0.6);
		var top = cy - (half * 0.55);
		var boxW = size * 0.6;
		var boxH = size * 0.55;
		_ = sb.Append("  <rect x=\"").Append(SvgFormat.F(left)).Append("\" y=\"").Append(SvgFormat.F(top))
			.Append("\" width=\"").Append(SvgFormat.F(boxW)).Append("\" height=\"").Append(SvgFormat.F(boxH))
			.Append("\" rx=\"3\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"2\" />\n");

		var lineLeft = cx - (half * 0.4);
		var lineRight = cx + (half * 0.4);
		for (var i = 0; i < 3; i++)
		{
			var ly = top + (boxH * (0.28 + (i * 0.22)));
			_ = sb.Append("  <line x1=\"").Append(SvgFormat.F(lineLeft)).Append("\" y1=\"").Append(SvgFormat.F(ly))
				.Append("\" x2=\"").Append(SvgFormat.F(lineRight)).Append("\" y2=\"").Append(SvgFormat.F(ly))
				.Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"1.75\" />\n");
		}
	}

	private static void AppendInternetIcon(StringBuilder sb, double cx, double cy, double half, string stroke)
	{
		var r = half * 0.7;
		_ = sb.Append("  <circle cx=\"").Append(SvgFormat.F(cx)).Append("\" cy=\"").Append(SvgFormat.F(cy))
			.Append("\" r=\"").Append(SvgFormat.F(r))
			.Append("\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"2\" />\n");
		_ = sb.Append("  <ellipse cx=\"").Append(SvgFormat.F(cx)).Append("\" cy=\"").Append(SvgFormat.F(cy))
			.Append("\" rx=\"").Append(SvgFormat.F(half * 0.32)).Append("\" ry=\"").Append(SvgFormat.F(r))
			.Append("\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"1.5\" />\n");
		_ = sb.Append("  <line x1=\"").Append(SvgFormat.F(cx - r)).Append("\" y1=\"").Append(SvgFormat.F(cy))
			.Append("\" x2=\"").Append(SvgFormat.F(cx + r)).Append("\" y2=\"").Append(SvgFormat.F(cy))
			.Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"1.5\" />\n");
	}

	private static void AppendCloudIcon(StringBuilder sb, double cx, double cy, double half, string stroke)
	{
		var startX = cx - (half * 0.55);
		var startY = cy + (half * 0.2);
		_ = sb.Append("  <path d=\"M").Append(SvgFormat.F(startX)).Append(' ').Append(SvgFormat.F(startY))
			.Append(" a").Append(SvgFormat.F(half * 0.38)).Append(' ').Append(SvgFormat.F(half * 0.32))
			.Append(" 0 1 1 ").Append(SvgFormat.F(half * 0.12)).Append(' ').Append(SvgFormat.F(-(half * 0.4)))
			.Append(" a").Append(SvgFormat.F(half * 0.42)).Append(' ').Append(SvgFormat.F(half * 0.36))
			.Append(" 0 1 1 ").Append(SvgFormat.F(half * 0.65)).Append(" 0")
			.Append(" a").Append(SvgFormat.F(half * 0.32)).Append(' ').Append(SvgFormat.F(half * 0.28))
			.Append(" 0 1 1 ").Append(SvgFormat.F(half * 0.18)).Append(' ').Append(SvgFormat.F(half * 0.4))
			.Append(" z\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"2\" />\n");
	}

	private static void AppendEdge(StringBuilder sb, ArchitectureEdge edge, ArchitectureLayout.Result layout)
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
			path = $"M{SvgFormat.F(x1)} {SvgFormat.F(y1)} L{SvgFormat.F(x2)} {SvgFormat.F(y2)}";
		}
		else if (edge.SourcePort is ArchitecturePort.Left or ArchitecturePort.Right
			|| edge.TargetPort is ArchitecturePort.Left or ArchitecturePort.Right)
		{
			// Horizontal-first elbow
			var mx = (x1 + x2) / 2;
			path = $"M{SvgFormat.F(x1)} {SvgFormat.F(y1)} L{SvgFormat.F(mx)} {SvgFormat.F(y1)} L{SvgFormat.F(mx)} {SvgFormat.F(y2)} L{SvgFormat.F(x2)} {SvgFormat.F(y2)}";
		}
		else
		{
			// Vertical-first elbow
			var my = (y1 + y2) / 2;
			path = $"M{SvgFormat.F(x1)} {SvgFormat.F(y1)} L{SvgFormat.F(x1)} {SvgFormat.F(my)} L{SvgFormat.F(x2)} {SvgFormat.F(my)} L{SvgFormat.F(x2)} {SvgFormat.F(y2)}";
		}

		_ = sb.Append("\n<path d=\"").Append(path)
			.Append("\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"2\"");

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

}
