using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class MindmapSvgRenderer
{
	private const double HorizontalGap = 180;
	private const double VerticalGap = 50;
	private const double NodePadX = 16;
	private const double NodePadY = 8;
	private const string NodeFontSize = RenderConstants.FsVar.M;
	private const double NodeFontSizePx = 13;
	private const string RootFontSize = RenderConstants.FsVar.L;
	private const double RootFontSizePx = 16;

	private static readonly string[] NodeColors =
	[
		"#4e79a7", "#f28e2b", "#e15759", "#76b7b2",
		"#59a14f", "#edc948", "#b07aa1", "#ff9da7",
	];

	internal static string Render(MindmapDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
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

	internal static StringBuilder RenderToBuilder(MindmapDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		var positioned = new List<PositionedMindmapNode>();
		_ = LayoutTree(diagram.Root, 40, 40, 0, positioned);

		var maxX = 0.0;
		var maxY = 0.0;
		foreach (var node in positioned)
		{
			var right = node.X + node.W;
			var bottom = node.Y + node.H;
			if (right > maxX)
				maxX = right;
			if (bottom > maxY)
				maxY = bottom;
		}

		var width = maxX + 40;
		var height = maxY + 40;

		StyleBlock.AppendSvgOpenTag(sb, width, height, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict);
		_ = sb.Append("\n<defs>\n</defs>\n");

		foreach (var node in positioned)
		{
			if (node.ParentCx is not null)
				AppendLink(sb, node.ParentCx.Value, node.ParentCy!.Value, node.X + (node.W / 2), node.Y + (node.H / 2), node.Color);
		}

		foreach (var node in positioned)
			AppendNode(sb, node);

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private sealed record PositionedMindmapNode(
		double X, double Y, double W, double H,
		string Label, MindmapShape Shape, string Color,
		int Depth, double? ParentCx, double? ParentCy);

	private static double LayoutTree(MindmapNode node, double x, double y, int depth, List<PositionedMindmapNode> result)
	{
		var fontSizePx = depth == 0 ? RootFontSizePx : NodeFontSizePx;
		var textWidth = TextMetrics.MeasureTextWidth(node.Label, fontSizePx, 600);
		var w = textWidth + (NodePadX * 2);
		var h = fontSizePx + (NodePadY * 2);
		var color = NodeColors[depth % NodeColors.Length];

		if (node.Children.Count == 0)
		{
			result.Add(new PositionedMindmapNode(x, y, w, h, node.Label, node.Shape, color, depth, null, null));
			return h;
		}

		var childX = x + HorizontalGap;
		var childY = y;
		var totalChildHeight = 0.0;

		var childPositions = new List<int>();
		foreach (var child in node.Children)
		{
			childPositions.Add(result.Count);
			var childH = LayoutTree(child, childX, childY, depth + 1, result);
			childY += childH + VerticalGap;
			totalChildHeight += childH + VerticalGap;
		}
		totalChildHeight -= VerticalGap;

		var nodeY = y + (totalChildHeight / 2) - (h / 2);
		var nodeCx = x + (w / 2);
		var nodeCy = nodeY + (h / 2);

		result.Add(new PositionedMindmapNode(x, nodeY, w, h, node.Label, node.Shape, color, depth, null, null));

		for (var i = 0; i < childPositions.Count; i++)
		{
			var idx = childPositions[i];
			var child = result[idx];
			result[idx] = child with { ParentCx = nodeCx, ParentCy = nodeCy };
		}

		return Math.Max(totalChildHeight, h);
	}

	private static void AppendLink(StringBuilder sb, double x1, double y1, double x2, double y2, string color)
	{
		var midX = (x1 + x2) / 2;
		_ = sb.Append("\n<path d=\"M ").Append(x1.SvgFormat()).Append(' ').Append(y1.SvgFormat())
			.Append(" C ").Append(midX.SvgFormat()).Append(' ').Append(y1.SvgFormat())
			.Append(' ').Append(midX.SvgFormat()).Append(' ').Append(y2.SvgFormat())
			.Append(' ').Append(x2.SvgFormat()).Append(' ').Append(y2.SvgFormat())
			.Append("\" fill=\"none\" stroke=\"").Append(color)
			.Append("\" stroke-width=\"2\" opacity=\"0.5\" />");
	}

	private static void AppendNode(StringBuilder sb, PositionedMindmapNode node)
	{
		var cx = node.X + (node.W / 2);
		var cy = node.Y + (node.H / 2);
		var opacity = node.Depth == 0 ? "0.9" : "0.7";
		var fontSize = node.Depth == 0 ? RootFontSize : NodeFontSize;

		switch (node.Shape)
		{
			case MindmapShape.Circle:
				var r = Math.Max(node.W, node.H) / 2;
				_ = sb.Append("\n<circle cx=\"").Append(cx.SvgFormat()).Append("\" cy=\"").Append(cy.SvgFormat())
					.Append("\" r=\"").Append(r.SvgFormat())
					.Append("\" fill=\"").Append(node.Color)
					.Append("\" opacity=\"").Append(opacity).Append("\" />");
				break;
			case MindmapShape.Hexagon:
				var hx = node.W / 2;
				var hy = node.H / 2;
				var inset = hy * 0.6;
				_ = sb.Append("\n<polygon points=\"")
					.Append((node.X + inset).SvgFormat()).Append(',').Append(node.Y.SvgFormat()).Append(' ')
					.Append((node.X + node.W - inset).SvgFormat()).Append(',').Append(node.Y.SvgFormat()).Append(' ')
					.Append((node.X + node.W).SvgFormat()).Append(',').Append(cy.SvgFormat()).Append(' ')
					.Append((node.X + node.W - inset).SvgFormat()).Append(',').Append((node.Y + node.H).SvgFormat()).Append(' ')
					.Append((node.X + inset).SvgFormat()).Append(',').Append((node.Y + node.H).SvgFormat()).Append(' ')
					.Append(node.X.SvgFormat()).Append(',').Append(cy.SvgFormat())
					.Append("\" fill=\"").Append(node.Color)
					.Append("\" opacity=\"").Append(opacity).Append("\" />");
				break;
			case MindmapShape.Square:
				_ = sb.Append("\n<rect x=\"").Append(node.X.SvgFormat()).Append("\" y=\"").Append(node.Y.SvgFormat())
					.Append("\" width=\"").Append(node.W.SvgFormat()).Append("\" height=\"").Append(node.H.SvgFormat())
					.Append("\" fill=\"").Append(node.Color)
					.Append("\" opacity=\"").Append(opacity).Append("\" />");
				break;
			default:
				var rx = node.Shape == MindmapShape.Cloud ? node.H / 2 : 8;
				_ = sb.Append("\n<rect x=\"").Append(node.X.SvgFormat()).Append("\" y=\"").Append(node.Y.SvgFormat())
					.Append("\" width=\"").Append(node.W.SvgFormat()).Append("\" height=\"").Append(node.H.SvgFormat())
					.Append("\" rx=\"").Append(rx.SvgFormat()).Append("\" ry=\"").Append(rx.SvgFormat())
					.Append("\" fill=\"").Append(node.Color)
					.Append("\" opacity=\"").Append(opacity).Append("\" />");
				break;
		}

		_ = sb.Append("\n<text x=\"").Append(cx.SvgFormat()).Append("\" y=\"").Append(cy.SvgFormat())
			.Append("\" text-anchor=\"middle\" dy=\"0.35em\" font-size=\"").Append(fontSize)
			.Append("\" font-weight=\"").Append(node.Depth == 0 ? "700" : "500")
			.Append("\" fill=\"#fff\">");
		MultilineUtils.AppendEscapedXml(sb, node.Label.AsSpan());
		_ = sb.Append("</text>");
	}

}
