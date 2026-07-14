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

	internal static string Render(C4Diagram diagram, DiagramColors colors, string font, string? monoFont = null, bool transparent = false, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
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

	internal static StringBuilder RenderToBuilder(C4Diagram diagram, DiagramColors colors, string font, string? monoFont = null, bool transparent = false, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var hasTitle = diagram.Title is { Length: > 0 };
		var titleOffset = hasTitle ? TitleHeight : 0;

		var placements = new Dictionary<string, PlacedElement>(StringComparer.Ordinal);
		// Relation anchors include leaf elements + nested deployment-node boxes (not redrawn as leaves).
		var relationAnchors = new Dictionary<string, PlacedElement>(StringComparer.Ordinal);
		var rootBoundaries = new List<PlacedBoundary>();

		var (contentW, contentH) = LayoutNodes(
			diagram.RootNodes,
			diagram.ShapeInRow,
			diagram.BoundaryInRow,
			Margin,
			Margin + titleOffset,
			placements,
			relationAnchors,
			rootBoundaries);

		var width = Math.Max(contentW + Margin, 320);
		var height = Math.Max(contentH + Margin, 200);

		StyleBlock.AppendSvgOpenTag(sb, width, height, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict, monoFont: monoFont);
		AppendDefs(sb);

		if (hasTitle)
		{
			_ = sb.Append("\n<text x=\"").Append((width * 0.5).SvgFormat())
				.Append("\" y=\"28\" text-anchor=\"middle\" font-size=\"")
				.Append(TitleFontSize).Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, diagram.Title.AsSpan());
			_ = sb.Append("</text>");
		}

		foreach (var b in rootBoundaries)
			AppendBoundary(sb, b);

		foreach (var rel in diagram.Relations)
			AppendRelation(sb, rel, relationAnchors);

		foreach (var p in placements.Values)
			AppendElement(sb, p);

		foreach (var rel in diagram.Relations)
			AppendRelationLabel(sb, rel, relationAnchors);

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
		Dictionary<string, PlacedElement> relationAnchors,
		List<PlacedBoundary> outBoundaries)
	{
		// Walk source order so Person → Boundary → System_Ext keeps left-to-right flow.
		var cursorX = originX;
		var cursorY = originY;
		var rowMaxH = 0.0;
		var leafCol = 0;
		var boundaryCol = 0;
		var maxX = originX;
		var maxY = originY;

		void NewRow()
		{
			cursorX = originX;
			cursorY += rowMaxH + GapY;
			rowMaxH = 0;
			leafCol = 0;
			boundaryCol = 0;
		}

		foreach (var n in nodes)
		{
			if (n is C4Element el)
			{
				if (leafCol >= shapeInRow)
					NewRow();

				var placed = new PlacedElement
				{
					Element = el,
					X = cursorX,
					Y = cursorY,
					W = BoxWidth,
					H = BoxHeight,
				};
				placements[el.Alias] = placed;
				relationAnchors[el.Alias] = placed;
				cursorX += BoxWidth + GapX;
				rowMaxH = Math.Max(rowMaxH, BoxHeight);
				leafCol++;
				maxX = Math.Max(maxX, placed.X + placed.W);
				maxY = Math.Max(maxY, placed.Y + placed.H);
				continue;
			}

			if (n is not C4Boundary boundary)
				continue;

			if (boundaryCol >= boundaryInRow)
				NewRow();

			var childPlacements = new Dictionary<string, PlacedElement>(StringComparer.Ordinal);
			var childBoundaries = new List<PlacedBoundary>();
			var innerOriginX = cursorX + BoundaryPad;
			var innerOriginY = cursorY + BoundaryPad + BoundaryHeader;

			var (innerMaxX, innerMaxY) = LayoutNodes(
				boundary.Children,
				shapeInRow,
				boundaryInRow,
				innerOriginX,
				innerOriginY,
				childPlacements,
				relationAnchors,
				childBoundaries);

			foreach (var kv in childPlacements)
				placements[kv.Key] = kv.Value;

			var bw = Math.Max(BoxWidth + (BoundaryPad * 2), innerMaxX - cursorX + BoundaryPad);
			var bh = Math.Max(BoxHeight + (BoundaryPad * 2) + BoundaryHeader, innerMaxY - cursorY + BoundaryPad);

			var pb = new PlacedBoundary
			{
				Boundary = boundary,
				X = cursorX,
				Y = cursorY,
				W = bw,
				H = bh,
			};
			pb.Children.AddRange(childBoundaries);
			outBoundaries.Add(pb);

			// Nested deployment nodes are relation endpoints (outer box), not leaf redraws.
			if (boundary.IsDeploymentNode)
			{
				relationAnchors[boundary.Alias] = new PlacedElement
				{
					Element = new C4Element(
						boundary.Alias,
						C4ElementType.DeploymentNode,
						boundary.Label,
						boundary.Technology,
						Description: null,
						External: false),
					X = cursorX,
					Y = cursorY,
					W = bw,
					H = bh,
				};
			}

			cursorX += bw + GapX;
			rowMaxH = Math.Max(rowMaxH, bh);
			boundaryCol++;
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
		if (b.Boundary.IsDeploymentNode)
		{
			// Solid node chrome (not dashed enterprise boundary style)
			_ = sb.Append("\n<rect x=\"").Append(b.X.SvgFormat()).Append("\" y=\"").Append(b.Y.SvgFormat())
				.Append("\" width=\"").Append(b.W.SvgFormat()).Append("\" height=\"").Append(b.H.SvgFormat())
				.Append("\" rx=\"4\" ry=\"4\" fill=\"").Append(NodeColors.Fill)
				.Append("\" stroke=\"").Append(NodeColors.Stroke).Append("\" stroke-width=\"1.5\" />");
		}
		else
		{
			_ = sb.Append("\n<rect x=\"").Append(b.X.SvgFormat()).Append("\" y=\"").Append(b.Y.SvgFormat())
				.Append("\" width=\"").Append(b.W.SvgFormat()).Append("\" height=\"").Append(b.H.SvgFormat())
				.Append("\" rx=\"4\" ry=\"4\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"1.5\" stroke-dasharray=\"6 4\" />");
		}

		var header = b.Boundary.Label;
		if (b.Boundary.TypeLabel is { Length: > 0 } tl)
			header = $"{header} [{tl}]";
		else if (b.Boundary.IsDeploymentNode && b.Boundary.Technology is { Length: > 0 } techn)
			header = $"{header} [{techn}]";

		var fill = b.Boundary.IsDeploymentNode ? NodeColors.Text : "var(--_text-sec)";
		_ = sb.Append("\n<text x=\"").Append((b.X + 10).SvgFormat()).Append("\" y=\"").Append((b.Y + 16).SvgFormat())
			.Append("\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(LabelFontSize)
			.Append("\" font-weight=\"600\" fill=\"").Append(fill).Append("\">");
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
			_ = sb.Append("\n<circle cx=\"").Append(cx.SvgFormat()).Append("\" cy=\"").Append(headCy.SvgFormat())
				.Append("\" r=\"").Append(headR.SvgFormat())
				.Append("\" fill=\"").Append(fill).Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"1.5\" />");
			_ = sb.Append("\n<path d=\"M ").Append((p.X + 30).SvgFormat()).Append(' ').Append((p.Y + 40).SvgFormat())
				.Append(" Q ").Append(cx.SvgFormat()).Append(' ').Append((p.Y + 32).SvgFormat())
				.Append(' ').Append((p.X + p.W - 30).SvgFormat()).Append(' ').Append((p.Y + 40).SvgFormat())
				.Append(" L ").Append((p.X + p.W - 20).SvgFormat()).Append(' ').Append((p.Y + p.H - 12).SvgFormat())
				.Append(" Q ").Append(cx.SvgFormat()).Append(' ').Append((p.Y + p.H).SvgFormat())
				.Append(' ').Append((p.X + 20).SvgFormat()).Append(' ').Append((p.Y + p.H - 12).SvgFormat())
				.Append(" Z\" fill=\"").Append(fill).Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"1.5\" />");
		}
		else if (isDb)
		{
			var rx = p.W * 0.5;
			var ry = 12.0;
			_ = sb.Append("\n<path d=\"M ").Append(p.X.SvgFormat()).Append(' ').Append((p.Y + ry).SvgFormat())
				.Append(" A ").Append(rx.SvgFormat()).Append(' ').Append(ry.SvgFormat()).Append(" 0 0 1 ")
				.Append((p.X + p.W).SvgFormat()).Append(' ').Append((p.Y + ry).SvgFormat())
				.Append(" L ").Append((p.X + p.W).SvgFormat()).Append(' ').Append((p.Y + p.H - ry).SvgFormat())
				.Append(" A ").Append(rx.SvgFormat()).Append(' ').Append(ry.SvgFormat()).Append(" 0 0 1 ")
				.Append(p.X.SvgFormat()).Append(' ').Append((p.Y + p.H - ry).SvgFormat())
				.Append(" Z\" fill=\"").Append(fill).Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"1.5\" />");
			_ = sb.Append("\n<path d=\"M ").Append(p.X.SvgFormat()).Append(' ').Append((p.Y + ry).SvgFormat())
				.Append(" A ").Append(rx.SvgFormat()).Append(' ').Append(ry.SvgFormat()).Append(" 0 0 0 ")
				.Append((p.X + p.W).SvgFormat()).Append(' ').Append((p.Y + ry).SvgFormat())
				.Append("\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"1.5\" />");
		}
		else
		{
			var rx = isQueue ? 24 : 4;
			_ = sb.Append("\n<rect x=\"").Append(p.X.SvgFormat()).Append("\" y=\"").Append(p.Y.SvgFormat())
				.Append("\" width=\"").Append(p.W.SvgFormat()).Append("\" height=\"").Append(p.H.SvgFormat())
				.Append("\" rx=\"").Append(rx).Append("\" ry=\"").Append(rx).Append("\" fill=\"").Append(fill)
				.Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"1.5\" />");
		}

		var typeLabel = TypeLabel(p.Element);
		var midX = p.X + (p.W * 0.5);
		var textStartY = isPerson ? p.Y + 58 : p.Y + 28;

		_ = sb.Append("\n<text x=\"").Append(midX.SvgFormat()).Append("\" y=\"").Append(textStartY.SvgFormat())
			.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(TypeFontSize)
			.Append("\" fill=\"").Append(text).Append("\" opacity=\"0.85\">");
		MultilineUtils.AppendEscapedXml(sb, typeLabel.AsSpan());
		_ = sb.Append("</text>");

		_ = sb.Append("\n<text x=\"").Append(midX.SvgFormat()).Append("\" y=\"").Append((textStartY + 18).SvgFormat())
			.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(LabelFontSize)
			.Append("\" font-weight=\"700\" fill=\"").Append(text).Append("\">");
		AppendTruncated(sb, p.Element.Label, 28);
		_ = sb.Append("</text>");

		if (p.Element.Technology is { Length: > 0 } techn)
		{
			_ = sb.Append("\n<text x=\"").Append(midX.SvgFormat()).Append("\" y=\"").Append((textStartY + 34).SvgFormat())
				.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
				.Append("\" font-size=\"").Append(TypeFontSize)
				.Append("\" fill=\"").Append(text).Append("\" opacity=\"0.9\">[");
			AppendTruncated(sb, techn, 24);
			_ = sb.Append("]</text>");
		}

		if (p.Element.Description is { Length: > 0 } descr)
		{
			var descY = p.Element.Technology is { Length: > 0 } ? 50 : 36;
			_ = sb.Append("\n<text x=\"").Append(midX.SvgFormat()).Append("\" y=\"").Append((textStartY + descY).SvgFormat())
				.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
				.Append("\" font-size=\"").Append(DescFontSize)
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

		// Self-relation: small loop on the right side of the box (v1).
		if (string.Equals(rel.From, rel.To, StringComparison.Ordinal))
		{
			var sx = from.X + from.W;
			var sy = from.Y + (from.H * 0.35);
			var ex = from.X + from.W;
			var ey = from.Y + (from.H * 0.65);
			var cx = sx + 28;
			_ = sb.Append("\n<path d=\"M ").Append(sx.SvgFormat()).Append(' ').Append(sy.SvgFormat())
				.Append(" C ").Append(cx.SvgFormat()).Append(' ').Append(sy.SvgFormat())
				.Append(' ').Append(cx.SvgFormat()).Append(' ').Append(ey.SvgFormat())
				.Append(' ').Append(ex.SvgFormat()).Append(' ').Append(ey.SvgFormat())
				.Append("\" fill=\"none\" stroke=\"var(--_arrow)\" stroke-width=\"1.5\" marker-end=\"url(#c4-arrow)\" />");
			return;
		}

		var x1 = from.X + (from.W * 0.5);
		var y1 = from.Y + (from.H * 0.5);
		var x2 = to.X + (to.W * 0.5);
		var y2 = to.Y + (to.H * 0.5);

		(x1, y1, x2, y2) = ClipToBoxes(x1, y1, x2, y2, from, to);

		var marker = " marker-end=\"url(#c4-arrow)\"";
		var markerStart = rel.Bidirectional ? " marker-start=\"url(#c4-arrow)\"" : "";

		_ = sb.Append("\n<line x1=\"").Append(x1.SvgFormat()).Append("\" y1=\"").Append(y1.SvgFormat())
			.Append("\" x2=\"").Append(x2.SvgFormat()).Append("\" y2=\"").Append(y2.SvgFormat())
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

		_ = sb.Append("\n<text x=\"").Append(mx.SvgFormat()).Append("\" y=\"").Append(my.SvgFormat())
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

}
