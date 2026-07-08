using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class C4SvgRenderer
{
	private const double BoxWidth = 200;
	private const double BoxHeight = 120;
	private const double GapX = 40;
	private const double GapY = 50;
	private const double BoundaryPad = 28;
	private const double BoundaryHeader = 22;
	private const double Margin = 40;
	private const double TitleHeight = 36;
	private const string TitleFontSize = RenderConstants.FsVar.L;
	private const string LabelFontSize = RenderConstants.FsVar.S;
	private const string TypeFontSize = RenderConstants.FsVar.Xs;
	private const string DescFontSize = RenderConstants.FsVar.Xs;

	// C4 palette (mermaid-compatible fixed style)
	private static readonly (string Fill, string Stroke, string Text) PersonColors = ("#08427B", "#052E56", "#FFFFFF");
	private static readonly (string Fill, string Stroke, string Text) PersonExtColors = ("#999999", "#8A8A8A", "#FFFFFF");
	private static readonly (string Fill, string Stroke, string Text) SystemColors = ("#1168BD", "#0B4884", "#FFFFFF");
	private static readonly (string Fill, string Stroke, string Text) SystemExtColors = ("#999999", "#8A8A8A", "#FFFFFF");
	private static readonly (string Fill, string Stroke, string Text) ContainerColors = ("#438DD5", "#3C7FC0", "#FFFFFF");
	private static readonly (string Fill, string Stroke, string Text) ContainerExtColors = ("#B3B3B3", "#A6A6A6", "#FFFFFF");
	private static readonly (string Fill, string Stroke, string Text) ComponentColors = ("#85BBF0", "#78A8D8", "#000000");
	private static readonly (string Fill, string Stroke, string Text) ComponentExtColors = ("#CCCCCC", "#B8B8B8", "#000000");
	private static readonly (string Fill, string Stroke, string Text) NodeColors = ("#FFFFFF", "#666666", "#000000");

	private sealed class PlacedElement
	{
		public required C4Element Element { get; init; }
		public double X { get; set; }
		public double Y { get; set; }
		public double W { get; set; } = BoxWidth;
		public double H { get; set; } = BoxHeight;
	}

	private sealed class PlacedBoundary
	{
		public required C4Boundary Boundary { get; init; }
		public double X { get; set; }
		public double Y { get; set; }
		public double W { get; set; }
		public double H { get; set; }
		public List<PlacedBoundary> Children { get; } = [];
	}

	internal static string Render(C4Diagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
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

	internal static StringBuilder RenderToBuilder(C4Diagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var hasTitle = diagram.Title is { Length: > 0 };
		var titleOffset = hasTitle ? TitleHeight : 0;

		var placements = new Dictionary<string, PlacedElement>(StringComparer.Ordinal);
		var rootBoundaries = new List<PlacedBoundary>();

		var (contentW, contentH) = LayoutNodes(
			diagram.RootNodes,
			diagram.ShapeInRow,
			diagram.BoundaryInRow,
			Margin,
			Margin + titleOffset,
			placements,
			rootBoundaries);

		var width = Math.Max(contentW + Margin, 320);
		var height = Math.Max(contentH + Margin, 200);

		StyleBlock.AppendSvgOpenTag(sb, width, height, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict);
		AppendDefs(sb);

		if (hasTitle)
		{
			_ = sb.Append("\n<text x=\"").Append(F(width * 0.5))
				.Append("\" y=\"28\" text-anchor=\"middle\" font-size=\"")
				.Append(TitleFontSize).Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, diagram.Title.AsSpan());
			_ = sb.Append("</text>");
		}

		foreach (var b in rootBoundaries)
			AppendBoundary(sb, b);

		foreach (var rel in diagram.Relations)
			AppendRelation(sb, rel, placements);

		foreach (var p in placements.Values)
			AppendElement(sb, p);

		foreach (var rel in diagram.Relations)
			AppendRelationLabel(sb, rel, placements);

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static (double MaxX, double MaxY) LayoutNodes(
		IReadOnlyList<C4Node> nodes,
		int shapeInRow,
		int boundaryInRow,
		double originX,
		double originY,
		Dictionary<string, PlacedElement> placements,
		List<PlacedBoundary> outBoundaries)
	{
		var cursorX = originX;
		var cursorY = originY;
		var rowMaxH = 0.0;
		var col = 0;
		var maxX = originX;
		var maxY = originY;

		var leaves = new List<C4Element>();
		var boundaries = new List<C4Boundary>();
		foreach (var n in nodes)
		{
			if (n is C4Element e)
				leaves.Add(e);
			else if (n is C4Boundary b)
				boundaries.Add(b);
		}

		for (var i = 0; i < leaves.Count; i++)
		{
			if (col >= shapeInRow)
			{
				cursorX = originX;
				cursorY += rowMaxH + GapY;
				rowMaxH = 0;
				col = 0;
			}

			var el = leaves[i];
			var placed = new PlacedElement
			{
				Element = el,
				X = cursorX,
				Y = cursorY,
				W = BoxWidth,
				H = BoxHeight,
			};
			placements[el.Alias] = placed;
			cursorX += BoxWidth + GapX;
			rowMaxH = Math.Max(rowMaxH, BoxHeight);
			col++;
			maxX = Math.Max(maxX, placed.X + placed.W);
			maxY = Math.Max(maxY, placed.Y + placed.H);
		}

		if (leaves.Count > 0 && boundaries.Count > 0)
		{
			cursorX = originX;
			cursorY += rowMaxH + GapY;
			rowMaxH = 0;
			col = 0;
		}

		for (var i = 0; i < boundaries.Count; i++)
		{
			if (col >= boundaryInRow)
			{
				cursorX = originX;
				cursorY += rowMaxH + GapY;
				rowMaxH = 0;
				col = 0;
			}

			var childPlacements = new Dictionary<string, PlacedElement>(StringComparer.Ordinal);
			var childBoundaries = new List<PlacedBoundary>();
			var innerOriginX = cursorX + BoundaryPad;
			var innerOriginY = cursorY + BoundaryPad + BoundaryHeader;

			var (innerMaxX, innerMaxY) = LayoutNodes(
				boundaries[i].Children,
				shapeInRow,
				boundaryInRow,
				innerOriginX,
				innerOriginY,
				childPlacements,
				childBoundaries);

			foreach (var kv in childPlacements)
				placements[kv.Key] = kv.Value;

			var bw = Math.Max(BoxWidth + (BoundaryPad * 2), innerMaxX - cursorX + BoundaryPad);
			var bh = Math.Max(BoxHeight + (BoundaryPad * 2) + BoundaryHeader, innerMaxY - cursorY + BoundaryPad);

			var pb = new PlacedBoundary
			{
				Boundary = boundaries[i],
				X = cursorX,
				Y = cursorY,
				W = bw,
				H = bh,
			};
			pb.Children.AddRange(childBoundaries);
			outBoundaries.Add(pb);

			cursorX += bw + GapX;
			rowMaxH = Math.Max(rowMaxH, bh);
			col++;
			maxX = Math.Max(maxX, pb.X + pb.W);
			maxY = Math.Max(maxY, pb.Y + pb.H);
		}

		return (maxX, maxY);
	}

	private static void AppendDefs(StringBuilder sb)
	{
		_ = sb.Append("\n<defs>\n");
		_ = sb.Append("  <marker id=\"c4-arrow\" markerUnits=\"userSpaceOnUse\" markerWidth=\"10\" markerHeight=\"8\" refX=\"9\" refY=\"4\" orient=\"auto-start-reverse\">\n");
		_ = sb.Append("    <polygon points=\"0 0, 10 4, 0 8\" fill=\"var(--_arrow)\" />\n");
		_ = sb.Append("  </marker>\n");
		_ = sb.Append("</defs>\n");
	}

	private static void AppendBoundary(StringBuilder sb, PlacedBoundary b)
	{
		_ = sb.Append("\n<rect x=\"").Append(F(b.X)).Append("\" y=\"").Append(F(b.Y))
			.Append("\" width=\"").Append(F(b.W)).Append("\" height=\"").Append(F(b.H))
			.Append("\" rx=\"4\" ry=\"4\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"1.5\" stroke-dasharray=\"6 4\" />");

		var header = b.Boundary.Label;
		if (b.Boundary.TypeLabel is { Length: > 0 } tl)
			header = $"{header} [{tl}]";

		_ = sb.Append("\n<text x=\"").Append(F(b.X + 10)).Append("\" y=\"").Append(F(b.Y + 16))
			.Append("\" font-size=\"").Append(LabelFontSize)
			.Append("\" font-weight=\"600\" fill=\"var(--_text-sec)\">");
		MultilineUtils.AppendEscapedXml(sb, header.AsSpan());
		_ = sb.Append("</text>");

		foreach (var child in b.Children)
			AppendBoundary(sb, child);
	}

	private static void AppendElement(StringBuilder sb, PlacedElement p)
	{
		var (fill, stroke, text) = ColorsFor(p.Element);
		var isPerson = p.Element.Type == C4ElementType.Person;
		var isDb = p.Element.Type is C4ElementType.SystemDb or C4ElementType.ContainerDb or C4ElementType.ComponentDb;
		var isQueue = p.Element.Type is C4ElementType.SystemQueue or C4ElementType.ContainerQueue or C4ElementType.ComponentQueue;

		if (isPerson)
		{
			var cx = p.X + (p.W * 0.5);
			var headR = 14.0;
			var headCy = p.Y + 22;
			_ = sb.Append("\n<circle cx=\"").Append(F(cx)).Append("\" cy=\"").Append(F(headCy))
				.Append("\" r=\"").Append(F(headR))
				.Append("\" fill=\"").Append(fill).Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"1.5\" />");
			_ = sb.Append("\n<path d=\"M ").Append(F(p.X + 30)).Append(' ').Append(F(p.Y + 40))
				.Append(" Q ").Append(F(cx)).Append(' ').Append(F(p.Y + 32))
				.Append(' ').Append(F(p.X + p.W - 30)).Append(' ').Append(F(p.Y + 40))
				.Append(" L ").Append(F(p.X + p.W - 20)).Append(' ').Append(F(p.Y + p.H - 12))
				.Append(" Q ").Append(F(cx)).Append(' ').Append(F(p.Y + p.H))
				.Append(' ').Append(F(p.X + 20)).Append(' ').Append(F(p.Y + p.H - 12))
				.Append(" Z\" fill=\"").Append(fill).Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"1.5\" />");
		}
		else if (isDb)
		{
			var rx = p.W * 0.5;
			var ry = 12.0;
			_ = sb.Append("\n<path d=\"M ").Append(F(p.X)).Append(' ').Append(F(p.Y + ry))
				.Append(" A ").Append(F(rx)).Append(' ').Append(F(ry)).Append(" 0 0 1 ")
				.Append(F(p.X + p.W)).Append(' ').Append(F(p.Y + ry))
				.Append(" L ").Append(F(p.X + p.W)).Append(' ').Append(F(p.Y + p.H - ry))
				.Append(" A ").Append(F(rx)).Append(' ').Append(F(ry)).Append(" 0 0 1 ")
				.Append(F(p.X)).Append(' ').Append(F(p.Y + p.H - ry))
				.Append(" Z\" fill=\"").Append(fill).Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"1.5\" />");
			_ = sb.Append("\n<path d=\"M ").Append(F(p.X)).Append(' ').Append(F(p.Y + ry))
				.Append(" A ").Append(F(rx)).Append(' ').Append(F(ry)).Append(" 0 0 0 ")
				.Append(F(p.X + p.W)).Append(' ').Append(F(p.Y + ry))
				.Append("\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"1.5\" />");
		}
		else
		{
			var rx = isQueue ? 24 : 4;
			_ = sb.Append("\n<rect x=\"").Append(F(p.X)).Append("\" y=\"").Append(F(p.Y))
				.Append("\" width=\"").Append(F(p.W)).Append("\" height=\"").Append(F(p.H))
				.Append("\" rx=\"").Append(rx).Append("\" ry=\"").Append(rx).Append("\" fill=\"").Append(fill)
				.Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"1.5\" />");
		}

		var typeLabel = TypeLabel(p.Element);
		var midX = p.X + (p.W * 0.5);
		var textStartY = isPerson ? p.Y + 58 : p.Y + 28;

		_ = sb.Append("\n<text x=\"").Append(F(midX)).Append("\" y=\"").Append(F(textStartY))
			.Append("\" text-anchor=\"middle\" font-size=\"").Append(TypeFontSize)
			.Append("\" fill=\"").Append(text).Append("\" opacity=\"0.85\">");
		MultilineUtils.AppendEscapedXml(sb, typeLabel.AsSpan());
		_ = sb.Append("</text>");

		_ = sb.Append("\n<text x=\"").Append(F(midX)).Append("\" y=\"").Append(F(textStartY + 18))
			.Append("\" text-anchor=\"middle\" font-size=\"").Append(LabelFontSize)
			.Append("\" font-weight=\"700\" fill=\"").Append(text).Append("\">");
		AppendTruncated(sb, p.Element.Label, 28);
		_ = sb.Append("</text>");

		if (p.Element.Technology is { Length: > 0 } techn)
		{
			_ = sb.Append("\n<text x=\"").Append(F(midX)).Append("\" y=\"").Append(F(textStartY + 34))
				.Append("\" text-anchor=\"middle\" font-size=\"").Append(TypeFontSize)
				.Append("\" fill=\"").Append(text).Append("\" opacity=\"0.9\">[");
			AppendTruncated(sb, techn, 24);
			_ = sb.Append("]</text>");
		}

		if (p.Element.Description is { Length: > 0 } descr)
		{
			var dy = p.Element.Technology is { Length: > 0 } ? 50 : 36;
			_ = sb.Append("\n<text x=\"").Append(F(midX)).Append("\" y=\"").Append(F(textStartY + dy))
				.Append("\" text-anchor=\"middle\" font-size=\"").Append(DescFontSize)
				.Append("\" fill=\"").Append(text).Append("\" opacity=\"0.8\">");
			var cleaned = descr.Replace("<br/>", " ", StringComparison.OrdinalIgnoreCase)
				.Replace("<br>", " ", StringComparison.OrdinalIgnoreCase);
			AppendTruncated(sb, cleaned, 32);
			_ = sb.Append("</text>");
		}
	}

	private static void AppendRelation(StringBuilder sb, C4Relation rel, Dictionary<string, PlacedElement> placements)
	{
		if (!placements.TryGetValue(rel.From, out var from) || !placements.TryGetValue(rel.To, out var to))
			return;

		var x1 = from.X + (from.W * 0.5);
		var y1 = from.Y + (from.H * 0.5);
		var x2 = to.X + (to.W * 0.5);
		var y2 = to.Y + (to.H * 0.5);

		(x1, y1, x2, y2) = ClipToBoxes(x1, y1, x2, y2, from, to);

		var marker = " marker-end=\"url(#c4-arrow)\"";
		var markerStart = rel.Bidirectional ? " marker-start=\"url(#c4-arrow)\"" : "";

		_ = sb.Append("\n<line x1=\"").Append(F(x1)).Append("\" y1=\"").Append(F(y1))
			.Append("\" x2=\"").Append(F(x2)).Append("\" y2=\"").Append(F(y2))
			.Append("\" stroke=\"var(--_arrow)\" stroke-width=\"1.5\"")
			.Append(marker).Append(markerStart).Append(" />");
	}

	private static void AppendRelationLabel(StringBuilder sb, C4Relation rel, Dictionary<string, PlacedElement> placements)
	{
		if (rel.Label is not { Length: > 0 })
			return;
		if (!placements.TryGetValue(rel.From, out var from) || !placements.TryGetValue(rel.To, out var to))
			return;

		var fromCx = from.X + (from.W * 0.5);
		var fromCy = from.Y + (from.H * 0.5);
		var toCx = to.X + (to.W * 0.5);
		var toCy = to.Y + (to.H * 0.5);
		var mx = (fromCx + toCx) * 0.5;
		var my = ((fromCy + toCy) * 0.5) - 6;

		var text = rel.Label;
		if (rel.Technology is { Length: > 0 } t)
			text = $"{text} [{t}]";

		_ = sb.Append("\n<text x=\"").Append(F(mx)).Append("\" y=\"").Append(F(my))
			.Append("\" text-anchor=\"middle\" font-size=\"").Append(TypeFontSize)
			.Append("\" fill=\"var(--_text)\" font-weight=\"500\">");
		MultilineUtils.AppendEscapedXml(sb, text.AsSpan());
		_ = sb.Append("</text>");
	}

	private static (double x1, double y1, double x2, double y2) ClipToBoxes(
		double x1, double y1, double x2, double y2, PlacedElement from, PlacedElement to)
	{
		(x1, y1) = IntersectBoxEdge(x1, y1, x2, y2, from);
		(x2, y2) = IntersectBoxEdge(x2, y2, x1, y1, to);
		return (x1, y1, x2, y2);
	}

	private static (double x, double y) IntersectBoxEdge(double cx, double cy, double tx, double ty, PlacedElement box)
	{
		var dx = tx - cx;
		var dy = ty - cy;
		if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
			return (cx, cy);

		var hw = box.W * 0.5;
		var hh = box.H * 0.5;
		var scaleX = Math.Abs(dx) < 0.001 ? double.MaxValue : hw / Math.Abs(dx);
		var scaleY = Math.Abs(dy) < 0.001 ? double.MaxValue : hh / Math.Abs(dy);
		var scale = Math.Min(scaleX, scaleY);
		return (cx + (dx * scale), cy + (dy * scale));
	}

	private static (string Fill, string Stroke, string Text) ColorsFor(C4Element el) => el.Type switch
	{
		C4ElementType.Person when el.External => PersonExtColors,
		C4ElementType.Person => PersonColors,
		C4ElementType.System or C4ElementType.SystemDb or C4ElementType.SystemQueue when el.External => SystemExtColors,
		C4ElementType.System or C4ElementType.SystemDb or C4ElementType.SystemQueue => SystemColors,
		C4ElementType.Container or C4ElementType.ContainerDb or C4ElementType.ContainerQueue when el.External => ContainerExtColors,
		C4ElementType.Container or C4ElementType.ContainerDb or C4ElementType.ContainerQueue => ContainerColors,
		C4ElementType.Component or C4ElementType.ComponentDb or C4ElementType.ComponentQueue when el.External => ComponentExtColors,
		C4ElementType.Component or C4ElementType.ComponentDb or C4ElementType.ComponentQueue => ComponentColors,
		C4ElementType.DeploymentNode => NodeColors,
		_ => SystemColors,
	};

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

	private static void AppendTruncated(StringBuilder sb, string text, int maxChars)
	{
		if (text.Length <= maxChars)
		{
			MultilineUtils.AppendEscapedXml(sb, text.AsSpan());
			return;
		}
		MultilineUtils.AppendEscapedXml(sb, text.AsSpan(0, maxChars - 1));
		_ = sb.Append('…');
	}

	private static string F(double value) =>
		value.ToString("0.##", CultureInfo.InvariantCulture);
}
