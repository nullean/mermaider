using System.Globalization;
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
	private const double LabelFontSize = 12;
	private const double ValueFontSize = 10;
	private const double HeaderHeight = 20;

	private static readonly string[] NodeColors =
	[
		"#4e79a7", "#f28e2b", "#e15759", "#76b7b2",
		"#59a14f", "#edc948", "#b07aa1", "#ff9da7",
		"#9c755f", "#bab0ac",
	];

	internal static string Render(TreemapDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
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

	internal static StringBuilder RenderToBuilder(TreemapDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		StyleBlock.AppendSvgOpenTag(sb, ChartWidth, ChartHeight, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict);
		_ = sb.Append("\n<defs>\n</defs>\n");

		var allNodes = diagram.Roots;
		if (allNodes.Count > 0)
		{
			var rects = new List<TreeRect>();
			Squarify(allNodes, 0, 0, ChartWidth, ChartHeight, rects, 0);

			foreach (var rect in rects)
				AppendRect(sb, rect);
		}

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private sealed record TreeRect(double X, double Y, double W, double H, string Label, double Value, string Color, int Depth);

	private static void Squarify(IReadOnlyList<TreemapNode> nodes, double x, double y, double w, double h, List<TreeRect> rects, int depth)
	{
		var total = 0.0;
		foreach (var node in nodes)
			total += node.ComputedValue;

		if (total <= 0 || w <= 0 || h <= 0)
			return;

		var sorted = new List<TreemapNode>(nodes);
		sorted.Sort((a, b) => b.ComputedValue.CompareTo(a.ComputedValue));

		LayoutRow(sorted, x, y, w, h, total, rects, depth);
	}

	private static void LayoutRow(List<TreemapNode> nodes, double x, double y, double w, double h, double total, List<TreeRect> rects, int depth)
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

			var color = NodeColors[rects.Count % NodeColors.Length];
			rects.Add(new TreeRect(cx + Padding, cy + Padding, rw - (Padding * 2), rh - (Padding * 2), node.Label, node.ComputedValue, color, depth));

			if (node.Children.Count > 0)
			{
				var innerY = cy + Padding + HeaderHeight;
				var innerH = rh - (Padding * 2) - HeaderHeight;
				if (innerH > 10)
					Squarify(node.Children, cx + Padding, innerY, rw - (Padding * 2), innerH, rects, depth + 1);
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
		_ = sb.Append("\n<rect x=\"").Append(F(rect.X)).Append("\" y=\"").Append(F(rect.Y))
			.Append("\" width=\"").Append(F(rect.W)).Append("\" height=\"").Append(F(rect.H))
			.Append("\" rx=\"3\" ry=\"3\" fill=\"").Append(rect.Color)
			.Append("\" opacity=\"").Append(opacity)
			.Append("\" stroke=\"var(--bg)\" stroke-width=\"1\" />");

		if (rect.W > 30 && rect.H > 16)
		{
			var textX = rect.X + (rect.W / 2);
			var textY = rect.Y + 12;
			_ = sb.Append("\n<text x=\"").Append(F(textX)).Append("\" y=\"").Append(F(textY))
				.Append("\" text-anchor=\"middle\" font-size=\"").Append(LabelFontSize)
				.Append("\" font-weight=\"600\" fill=\"#fff\">");
			MultilineUtils.AppendEscapedXml(sb, rect.Label.AsSpan());
			_ = sb.Append("</text>");

			if (rect.H > 30)
			{
				_ = sb.Append("\n<text x=\"").Append(F(textX)).Append("\" y=\"").Append(F(textY + 14))
					.Append("\" text-anchor=\"middle\" font-size=\"").Append(ValueFontSize)
					.Append("\" fill=\"rgba(255,255,255,0.8)\">");
				_ = sb.Append(F(rect.Value));
				_ = sb.Append("</text>");
			}
		}
	}

	private static string F(double value) =>
		value.ToString("0.##", CultureInfo.InvariantCulture);
}
