using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class TreemapSvgRenderer
{
	private const double ChartWidth = 600;
	private const double ChartHeight = 400;
	private const double Padding = 2;
	private const string LabelFontSize = RenderConstants.FsVar.S;
	private const string ValueFontSize = RenderConstants.FsVar.Xs;
	private const double HeaderHeight = 20;


	internal static string Render(TreemapDiagram diagram, SvgRenderContext context)
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

	internal static StringBuilder RenderToBuilder(TreemapDiagram diagram, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		StyleBlock.AppendSvgOpenTag(sb, ChartWidth, ChartHeight, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles.Font, context.Styles.Strict, context.Styles.FontScale, context.Styles.MonoFont);
		_ = sb.Append("\n<defs>\n</defs>\n");

		var allNodes = diagram.Roots;
		if (allNodes.Count > 0)
		{
			var rects = new List<TreeRect>();
			Squarify(allNodes, 0, 0, ChartWidth, ChartHeight, rects, 0, context.Styles.Colors);

			foreach (var rect in rects)
				AppendRect(sb, rect);
		}

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private sealed record TreeRect(double X, double Y, double W, double H, string Label, double Value, string Color, int Depth);

	private static void Squarify(IReadOnlyList<TreemapNode> nodes, double x, double y, double w, double h, List<TreeRect> rects, int depth, DiagramColors colors)
	{
		var total = 0.0;
		foreach (var node in nodes)
			total += node.ComputedValue;

		if (total <= 0 || w <= 0 || h <= 0)
			return;

		var sorted = new List<TreemapNode>(nodes);
		sorted.Sort((a, b) => b.ComputedValue.CompareTo(a.ComputedValue));

		LayoutRow(sorted, x, y, w, h, total, rects, depth, colors);
	}

	private static void LayoutRow(List<TreemapNode> nodes, double x, double y, double w, double h, double total, List<TreeRect> rects, int depth, DiagramColors colors)
	{
		if (nodes.Count == 0 || total <= 0)
			return;

		var isWide = w >= h;
		var cx = x;
		var cy = y;

		foreach (var node in nodes)
		{
			var fraction = node.ComputedValue / total;
			double rw, rh;

			if (isWide)
			{
				rw = w * fraction;
				rh = h;
			}
			else
			{
				rw = w;
				rh = h * fraction;
			}

			var color = colors.PaletteAt(rects.Count);
			rects.Add(new TreeRect(cx + Padding, cy + Padding, rw - (Padding * 2), rh - (Padding * 2), node.Label, node.ComputedValue, color, depth));

			if (node.Children.Count > 0)
			{
				var innerY = cy + Padding + HeaderHeight;
				var innerH = rh - (Padding * 2) - HeaderHeight;
				if (innerH > 10)
					Squarify(node.Children, cx + Padding, innerY, rw - (Padding * 2), innerH, rects, depth + 1, colors);
			}

			if (isWide)
				cx += rw;
			else
				cy += rh;
		}
	}

	private static void AppendRect(StringBuilder sb, TreeRect rect)
	{
		if (rect.W <= 0 || rect.H <= 0)
			return;

		var opacity = rect.Depth == 0 ? "0.8" : "0.6";
		_ = sb.Append("\n<rect x=\"").Append(rect.X.SvgFormat()).Append("\" y=\"").Append(rect.Y.SvgFormat())
			.Append("\" width=\"").Append(rect.W.SvgFormat()).Append("\" height=\"").Append(rect.H.SvgFormat())
			.Append("\" rx=\"3\" ry=\"3\" fill=\"").Append(rect.Color)
			.Append("\" opacity=\"").Append(opacity)
			.Append("\" stroke=\"var(--bg)\" stroke-width=\"1\" />");

		if (rect.W > 30 && rect.H > 16)
		{
			var textX = rect.X + (rect.W / 2);
			var textY = rect.Y + 12;
			_ = sb.Append("\n<text x=\"").Append(textX.SvgFormat()).Append("\" y=\"").Append(textY.SvgFormat())
				.Append("\" text-anchor=\"middle\" font-size=\"").Append(LabelFontSize)
				.Append("\" font-weight=\"600\" fill=\"").Append(ColorUtils.ContrastText(rect.Color)).Append("\">");
			MultilineUtils.AppendEscapedXml(sb, rect.Label.AsSpan());
			_ = sb.Append("</text>");

			if (rect.H > 30)
			{
				_ = sb.Append("\n<text x=\"").Append(textX.SvgFormat()).Append("\" y=\"").Append((textY + 14).SvgFormat())
					.Append("\" text-anchor=\"middle\" font-size=\"").Append(ValueFontSize)
					.Append("\" fill=\"").Append(ColorUtils.ContrastText(rect.Color)).Append("\" opacity=\"0.8\">");
				_ = sb.Append(rect.Value.SvgFormat());
				_ = sb.Append("</text>");
			}
		}
	}

}
