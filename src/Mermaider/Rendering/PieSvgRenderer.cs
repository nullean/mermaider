using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class PieSvgRenderer
{
	private const double Radius = 140;
	private const double CenterX = 200;
	private const double LegendX = 400;
	private const double LegendSwatchSize = 14;
	private const double LegendRowHeight = 22;
	private const double LegendTextOffset = 22;
	private const string TitleFontSize = RenderConstants.FsVar.L;
	private const string LabelFontSize = RenderConstants.FsVar.S;
	private const string LegendFontSize = RenderConstants.FsVar.S;
	private const double TextPosition = 0.75;


	internal static string Render(PieChart chart, DiagramColors colors, string font, string? monoFont = null, bool transparent = false, StrictStylingOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = RenderToBuilder(chart, colors, font, monoFont, transparent, strict, accessibility, diagramType);
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

	internal static StringBuilder RenderToBuilder(PieChart chart, DiagramColors colors, string font, string? monoFont = null, bool transparent = false, StrictStylingOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		var total = 0.0;
		foreach (var slice in chart.Slices)
			total += slice.Value;

		var hasTitle = chart.Title is { Length: > 0 };
		var titleHeight = hasTitle ? 36.0 : 0.0;
		var centerY = titleHeight + 20 + Radius;
		var chartHeight = centerY + Radius + 30;
		var legendTop = titleHeight + 30;
		var legendHeight = chart.Slices.Count * LegendRowHeight;
		var height = Math.Max(chartHeight, legendTop + legendHeight + 20);
		var width = LegendX + 200;

		StyleBlock.AppendSvgOpenTag(sb, width, height, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict, monoFont: monoFont);
		_ = sb.Append("\n<defs>\n</defs>\n");

		if (hasTitle)
		{
			_ = sb.Append("\n<text x=\"").Append(CenterX)
				.Append("\" y=\"24\" text-anchor=\"middle\" font-size=\"")
				.Append(TitleFontSize).Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, chart.Title.AsSpan());
			_ = sb.Append("</text>");
		}

		if (total <= 0 || chart.Slices.Count == 0)
		{
			_ = sb.Append("\n</svg>");
			return sb;
		}

		var startAngle = 0.0;
		for (var i = 0; i < chart.Slices.Count; i++)
		{
			var slice = chart.Slices[i];
			var fraction = slice.Value / total;
			var sweepAngle = fraction * 2 * Math.PI;
			var color = colors.PaletteAt(i);

			AppendSlice(sb, centerY, startAngle, sweepAngle, color);
			AppendSliceLabel(sb, fraction, centerY, startAngle, sweepAngle, color);

			startAngle += sweepAngle;
		}

		AppendLegend(sb, chart, legendTop, colors);

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static void AppendSlice(StringBuilder sb, double centerY, double startAngle, double sweepAngle, string color)
	{
		if (sweepAngle >= (2 * Math.PI) - 0.0001)
		{
			_ = sb.Append("\n<circle cx=\"").Append(CenterX.SvgFormat()).Append("\" cy=\"").Append(centerY.SvgFormat())
				.Append("\" r=\"").Append(Radius.SvgFormat())
				.Append("\" fill=\"").Append(color)
				.Append("\" stroke=\"var(--bg)\" stroke-width=\"2\" />");
			return;
		}

		var x1 = CenterX + (Radius * Math.Cos(startAngle));
		var y1 = centerY + (Radius * Math.Sin(startAngle));
		var x2 = CenterX + (Radius * Math.Cos(startAngle + sweepAngle));
		var y2 = centerY + (Radius * Math.Sin(startAngle + sweepAngle));
		var largeArc = sweepAngle > Math.PI ? 1 : 0;

		_ = sb.Append("\n<path d=\"M ").Append(CenterX.SvgFormat()).Append(' ').Append(centerY.SvgFormat())
			.Append(" L ").Append(x1.SvgFormat()).Append(' ').Append(y1.SvgFormat())
			.Append(" A ").Append(Radius.SvgFormat()).Append(' ').Append(Radius.SvgFormat())
			.Append(" 0 ").Append(largeArc).Append(" 1 ")
			.Append(x2.SvgFormat()).Append(' ').Append(y2.SvgFormat())
			.Append(" Z\" fill=\"").Append(color)
			.Append("\" stroke=\"var(--bg)\" stroke-width=\"2\" />");
	}

	private static void AppendSliceLabel(
		StringBuilder sb, double fraction,
		double centerY, double startAngle, double sweepAngle, string sliceColor)
	{
		var pct = fraction * 100;
		if (pct < 3)
			return;

		var midAngle = startAngle + (sweepAngle / 2);
		var labelR = Radius * TextPosition;
		var lx = CenterX + (labelR * Math.Cos(midAngle));
		var ly = centerY + (labelR * Math.Sin(midAngle));

		_ = sb.Append("\n<text x=\"").Append(lx.SvgFormat()).Append("\" y=\"").Append(ly.SvgFormat())
			.Append("\" text-anchor=\"middle\" dy=\"0.35em\" font-size=\"")
			.Append(LabelFontSize).Append("\" font-weight=\"600\" fill=\"")
			.Append(ColorUtils.ContrastText(sliceColor)).Append("\">");

		_ = sb.Append(pct.SvgFormat()).Append('%');
		_ = sb.Append("</text>");
	}

	private static void AppendLegend(StringBuilder sb, PieChart chart, double legendTop, DiagramColors colors)
	{
		for (var i = 0; i < chart.Slices.Count; i++)
		{
			var slice = chart.Slices[i];
			var color = colors.PaletteAt(i);
			var y = legendTop + (i * LegendRowHeight);

			_ = sb.Append("\n<rect x=\"").Append(LegendX.SvgFormat()).Append("\" y=\"").Append(y.SvgFormat())
				.Append("\" width=\"").Append(LegendSwatchSize).Append("\" height=\"").Append(LegendSwatchSize)
				.Append("\" rx=\"3\" ry=\"3\" fill=\"").Append(color).Append("\" />");

			_ = sb.Append("\n<text x=\"").Append((LegendX + LegendTextOffset).SvgFormat()).Append("\" y=\"").Append((y + (LegendSwatchSize / 2)).SvgFormat())
				.Append("\" dy=\"0.35em\" font-size=\"").Append(LegendFontSize)
				.Append("\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, slice.Label.AsSpan());

			if (chart.ShowData)
				_ = sb.Append(" (").Append(slice.Value.SvgFormat()).Append(')');

			_ = sb.Append("</text>");
		}
	}

}
