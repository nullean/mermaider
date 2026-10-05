using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class MindmapSvgRenderer
{
	private const double HorizontalGap = 56;
	private const double MaxLabelWidth = 190;
	private const double VerticalGap = 50;
	private const double NodePadX = 16;
	private const double NodePadY = 8;
	private const string NodeFontSize = RenderConstants.FsVar.M;
	private const double NodeFontSizePx = 16;
	private const string RootFontSize = RenderConstants.FsVar.L;
	private const double RootFontSizePx = 18;


	internal static string Render(MindmapDiagram diagram, SvgRenderContext context)
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

	internal static StringBuilder RenderToBuilder(MindmapDiagram diagram, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		var positioned = new List<PositionedMindmapNode>();
		_ = LayoutTree(diagram.Root, 40, 40, 0, positioned, context.Styles.Colors.AutoPalette(), context.Limits, null);

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

		StyleBlock.AppendSvgOpenTag(sb, width, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles.Font, context.Styles.Strict, context.Styles.FontScale, context.Styles.MonoFont);
		_ = sb.Append("\n<defs>\n</defs>\n");

		foreach (var node in positioned)
		{
			if (node.ParentCx is not null)
				AppendLink(sb, node.ParentCx.Value, node.ParentCy!.Value, node.X + (node.W / 2), node.Y + (node.H / 2), VisualLanguage.Border(node.Color));
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

	private static double LayoutTree(MindmapNode node, double x, double y, int depth, List<PositionedMindmapNode> result, string[] palette, ResourceLimits limits, string? branchColor)
	{
		ResourceGuard.CheckRecursionDepth(depth, limits);
		var fontSizePx = depth == 0 ? RootFontSizePx : NodeFontSizePx;
		var weight = depth == 0 ? 700 : 500;
		var label = Wrap(node.Label, fontSizePx, weight);
		var metrics = TextMetrics.MeasureMultiline(label.AsSpan(), fontSizePx, weight);
		var w = metrics.Width + (NodePadX * 2);
		var h = metrics.Height + (NodePadY * 2);
		if (node.Shape == MindmapShape.Circle)
		{
			// the circle has to enclose the text box
			w = h = Math.Sqrt((w * w) + (h * h));
		}

		// Same language as the other diagrams: the root takes the default colour, every branch of the root one colour of its own.
		var color = branchColor ?? palette[0];

		if (node.Children.Count == 0)
		{
			result.Add(new PositionedMindmapNode(x, y, w, h, label, node.Shape, color, depth, null, null));
			return h;
		}

		var childX = x + w + HorizontalGap;
		var childY = y;
		var totalChildHeight = 0.0;

		var childPositions = new List<int>();
		var branch = 0;
		foreach (var child in node.Children)
		{
			var childColor = depth == 0 ? palette[(++branch) % palette.Length] : color;
			var childH = LayoutTree(child, childX, childY, depth + 1, result, palette, limits, childColor);
			childPositions.Add(result.Count - 1); // a node is added after its descendants
			childY += childH + VerticalGap;
			totalChildHeight += childH + VerticalGap;
		}
		totalChildHeight -= VerticalGap;

		var nodeY = y + (totalChildHeight / 2) - (h / 2);
		var nodeCx = x + (w / 2);
		var nodeCy = nodeY + (h / 2);

		result.Add(new PositionedMindmapNode(x, nodeY, w, h, label, node.Shape, color, depth, null, null));

		for (var i = 0; i < childPositions.Count; i++)
		{
			var idx = childPositions[i];
			var child = result[idx];
			result[idx] = child with { ParentCx = nodeCx, ParentCy = nodeCy };
		}

		return Math.Max(totalChildHeight, h);
	}

	// Long labels wrap into lines so a node never grows without bound.
	private static string Wrap(string text, double fontPx, int weight)
	{
		if (TextMetrics.MeasureTextWidth(text, fontPx, weight) <= MaxLabelWidth)
			return text;
		var lines = new List<string>();
		var current = "";
		foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			var candidate = current.Length == 0 ? word : current + " " + word;
			if (current.Length > 0 && TextMetrics.MeasureTextWidth(candidate, fontPx, weight) > MaxLabelWidth)
			{
				lines.Add(current);
				current = word;
			}
			else
			{
				current = candidate;
			}
		}

		if (current.Length > 0)
			lines.Add(current);
		return string.Join('\n', lines);
	}

	private static void AppendLink(StringBuilder sb, double x1, double y1, double x2, double y2, string color)
	{
		var midX = (x1 + x2) / 2;
		_ = sb.Append("\n<path d=\"M ").Append(x1.SvgFormat()).Append(' ').Append(y1.SvgFormat())
			.Append(" C ").Append(midX.SvgFormat()).Append(' ').Append(y1.SvgFormat())
			.Append(' ').Append(midX.SvgFormat()).Append(' ').Append(y2.SvgFormat())
			.Append(' ').Append(x2.SvgFormat()).Append(' ').Append(y2.SvgFormat())
			.Append("\" fill=\"none\" stroke=\"").Append(color)
			.Append("\" stroke-width=\"").Append(RenderConstants.StrokeWidths.Connector.SvgFormat()).Append("\" />");
	}

	private static void AppendNode(StringBuilder sb, PositionedMindmapNode node)
	{
		var cx = node.X + (node.W / 2);
		var cy = node.Y + (node.H / 2);
		var fill = VisualLanguage.Tint(node.Color, node.Depth == 0 ? VisualLanguage.HeaderTint : VisualLanguage.NodeTint);
		var paint = $"fill=\"{fill}\" stroke=\"{VisualLanguage.Border(node.Color)}\" stroke-width=\"{RenderConstants.StrokeWidths.OuterBox.SvgFormat()}\"";
		var fontSize = node.Depth == 0 ? RootFontSize : NodeFontSize;

		switch (node.Shape)
		{
			case MindmapShape.Circle:
				var r = Math.Max(node.W, node.H) / 2;
				_ = sb.Append("\n<circle cx=\"").Append(cx.SvgFormat()).Append("\" cy=\"").Append(cy.SvgFormat())
					.Append("\" r=\"").Append(r.SvgFormat())
					.Append("\" ").Append(paint).Append(" />");
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
					.Append("\" ").Append(paint).Append(" />");
				break;
			case MindmapShape.Square:
				_ = sb.Append("\n<rect x=\"").Append(node.X.SvgFormat()).Append("\" y=\"").Append(node.Y.SvgFormat())
					.Append("\" width=\"").Append(node.W.SvgFormat()).Append("\" height=\"").Append(node.H.SvgFormat())
					.Append("\" ").Append(paint).Append(" />");
				break;
			default:
				var rx = node.Shape == MindmapShape.Cloud ? node.H / 2 : 8;
				_ = sb.Append("\n<rect x=\"").Append(node.X.SvgFormat()).Append("\" y=\"").Append(node.Y.SvgFormat())
					.Append("\" width=\"").Append(node.W.SvgFormat()).Append("\" height=\"").Append(node.H.SvgFormat())
					.Append("\" rx=\"").Append(rx.SvgFormat()).Append("\" ry=\"").Append(rx.SvgFormat())
					.Append("\" ").Append(paint).Append(" />");
				break;
		}

		_ = sb.Append('\n');
		MultilineUtils.AppendMultilineText(
			sb, node.Label, cx, cy,
			node.Depth == 0 ? RootFontSizePx : NodeFontSizePx,
			$"text-anchor=\"middle\" font-size=\"{fontSize}\" font-weight=\"{(node.Depth == 0 ? "700" : "500")}\" fill=\"var(--_text)\"");
	}

}
