using System.Globalization;
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
	private const double NodeFontSize = 13;
	private const double RootFontSize = 16;

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
		var fontSize = depth == 0 ? RootFontSize : NodeFontSize;
		var textWidth = TextMetrics.MeasureTextWidth(node.Label, fontSize, 600);
		var w = textWidth + (NodePadX * 2);
		var h = fontSize + (NodePadY * 2);
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
		_ = sb.Append("\n<path d=\"M ").Append(F(x1)).Append(' ').Append(F(y1))
			.Append(" C ").Append(F(midX)).Append(' ').Append(F(y1))
			.Append(' ').Append(F(midX)).Append(' ').Append(F(y2))
			.Append(' ').Append(F(x2)).Append(' ').Append(F(y2))
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
				_ = sb.Append("\n<circle cx=\"").Append(F(cx)).Append("\" cy=\"").Append(F(cy))
					.Append("\" r=\"").Append(F(r))
					.Append("\" fill=\"").Append(node.Color)
					.Append("\" opacity=\"").Append(opacity).Append("\" />");
				break;
			case MindmapShape.Hexagon:
				var hx = node.W / 2;
				var hy = node.H / 2;
				var inset = hy * 0.6;
				_ = sb.Append("\n<polygon points=\"")
					.Append(F(node.X + inset)).Append(',').Append(F(node.Y)).Append(' ')
					.Append(F(node.X + node.W - inset)).Append(',').Append(F(node.Y)).Append(' ')
					.Append(F(node.X + node.W)).Append(',').Append(F(cy)).Append(' ')
					.Append(F(node.X + node.W - inset)).Append(',').Append(F(node.Y + node.H)).Append(' ')
					.Append(F(node.X + inset)).Append(',').Append(F(node.Y + node.H)).Append(' ')
					.Append(F(node.X)).Append(',').Append(F(cy))
					.Append("\" fill=\"").Append(node.Color)
					.Append("\" opacity=\"").Append(opacity).Append("\" />");
				break;
			case MindmapShape.Square:
				_ = sb.Append("\n<rect x=\"").Append(F(node.X)).Append("\" y=\"").Append(F(node.Y))
					.Append("\" width=\"").Append(F(node.W)).Append("\" height=\"").Append(F(node.H))
					.Append("\" fill=\"").Append(node.Color)
					.Append("\" opacity=\"").Append(opacity).Append("\" />");
				break;
			default:
				var rx = node.Shape == MindmapShape.Cloud ? node.H / 2 : 8;
				_ = sb.Append("\n<rect x=\"").Append(F(node.X)).Append("\" y=\"").Append(F(node.Y))
					.Append("\" width=\"").Append(F(node.W)).Append("\" height=\"").Append(F(node.H))
					.Append("\" rx=\"").Append(F(rx)).Append("\" ry=\"").Append(F(rx))
					.Append("\" fill=\"").Append(node.Color)
					.Append("\" opacity=\"").Append(opacity).Append("\" />");
				break;
		}

		_ = sb.Append("\n<text x=\"").Append(F(cx)).Append("\" y=\"").Append(F(cy))
			.Append("\" text-anchor=\"middle\" dy=\"0.35em\" font-size=\"").Append(fontSize)
			.Append("\" font-weight=\"").Append(node.Depth == 0 ? "700" : "500")
			.Append("\" fill=\"#fff\">");
		MultilineUtils.AppendEscapedXml(sb, node.Label.AsSpan());
		_ = sb.Append("</text>");
	}

	private static string F(double value) =>
		value.ToString("0.##", CultureInfo.InvariantCulture);
}
