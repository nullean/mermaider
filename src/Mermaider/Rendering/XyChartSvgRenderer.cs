using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class XyChartSvgRenderer
{
	private const double DefaultWidth = 700;
	private const double DefaultHeight = 500;
	private const double PadL = 70;
	private const double PadR = 30;
	private const double PadT = 40;
	private const double PadB = 50;
	private const double TitleH = 28;
	private const double LegendH = 28;
	private const string TitleFontSize = RenderConstants.FsVar.L;
	private const string AxisFontSize = RenderConstants.FsVar.S;
	private const string TickFontSize = RenderConstants.FsVar.Xs;

	private static readonly string[] PlotColors =
	[
		"#4e79a7", "#f28e2b", "#e15759", "#76b7b2",
		"#59a14f", "#edc948", "#b07aa1", "#ff9da7",
	];

	internal static string Render(XyChart chart, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = RenderToBuilder(chart, colors, font, transparent, strict, accessibility, diagramType);
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

	internal static StringBuilder RenderToBuilder(XyChart chart, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var hasTitle = chart.Title is { Length: > 0 };
		var namedSeries = chart.Series.Where(s => s.Name is { Length: > 0 }).ToList();
		var hasLegend = namedSeries.Count > 0;

		var top = PadT + (hasTitle ? TitleH : 0);
		var bottom = PadB + (hasLegend ? LegendH : 0);
		var plotW = DefaultWidth - PadL - PadR;
		var plotH = DefaultHeight - top - bottom;
		if (plotW < 40)
			plotW = 40;
		if (plotH < 40)
			plotH = 40;

		var width = DefaultWidth;
		var height = DefaultHeight;
		var plotX = PadL;
		var plotY = top;

		StyleBlock.AppendSvgOpenTag(sb, width, height, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict);
		_ = sb.Append("\n<defs>\n</defs>\n");

		if (hasTitle)
		{
			_ = sb.Append("\n<text x=\"").Append(F(width * 0.5)).Append("\" y=\"24\" text-anchor=\"middle\" font-size=\"")
				.Append(TitleFontSize).Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, chart.Title.AsSpan());
			_ = sb.Append("</text>");
		}

		if (chart.Series.Count == 0)
		{
			_ = sb.Append("\n</svg>");
			return sb;
		}

		var catCount = chart.XCategories?.Count ?? 0;
		var maxPoints = chart.Series.Max(s => s.Values.Count);
		if (catCount == 0)
			catCount = Math.Max(1, maxPoints);

		var (yMin, yMax) = ResolveYRange(chart);
		if (Math.Abs(yMax - yMin) < 1e-12)
		{
			yMin -= 1;
			yMax += 1;
		}

		// Axes
		_ = sb.Append("\n<line x1=\"").Append(F(plotX)).Append("\" y1=\"").Append(F(plotY + plotH))
			.Append("\" x2=\"").Append(F(plotX + plotW)).Append("\" y2=\"").Append(F(plotY + plotH))
			.Append("\" stroke=\"var(--_line)\" stroke-width=\"1.5\" />");
		_ = sb.Append("\n<line x1=\"").Append(F(plotX)).Append("\" y1=\"").Append(F(plotY))
			.Append("\" x2=\"").Append(F(plotX)).Append("\" y2=\"").Append(F(plotY + plotH))
			.Append("\" stroke=\"var(--_line)\" stroke-width=\"1.5\" />");

		// Y ticks
		const int tickCount = 5;
		for (var t = 0; t <= tickCount; t++)
		{
			var frac = t / (double)tickCount;
			var val = yMin + ((yMax - yMin) * (1 - frac)); // top = max
			var y = plotY + (plotH * frac);
			_ = sb.Append("\n<line x1=\"").Append(F(plotX - 4)).Append("\" y1=\"").Append(F(y))
				.Append("\" x2=\"").Append(F(plotX)).Append("\" y2=\"").Append(F(y))
				.Append("\" stroke=\"var(--_line)\" stroke-width=\"1\" />");
			_ = sb.Append("\n<text x=\"").Append(F(plotX - 8)).Append("\" y=\"").Append(F(y))
				.Append("\" text-anchor=\"end\" dy=\"").Append(RenderConstants.TextBaselineShift)
				.Append("\" font-size=\"").Append(TickFontSize).Append("\" fill=\"var(--_text-sec)\">")
				.Append(F(val)).Append("</text>");
		}

		// X categories / indices
		for (var i = 0; i < catCount; i++)
		{
			var x = CategoryX(i, catCount, plotX, plotW, chart.Horizontal);
			var label = chart.XCategories is { Count: > 0 } cats && i < cats.Count
				? cats[i]
				: (i + 1).ToString(CultureInfo.InvariantCulture);
			_ = sb.Append("\n<line x1=\"").Append(F(x)).Append("\" y1=\"").Append(F(plotY + plotH))
				.Append("\" x2=\"").Append(F(x)).Append("\" y2=\"").Append(F(plotY + plotH + 4))
				.Append("\" stroke=\"var(--_line)\" stroke-width=\"1\" />");
			_ = sb.Append("\n<text x=\"").Append(F(x)).Append("\" y=\"").Append(F(plotY + plotH + 16))
				.Append("\" text-anchor=\"middle\" font-size=\"").Append(TickFontSize)
				.Append("\" fill=\"var(--_text-sec)\">");
			MultilineUtils.AppendEscapedXml(sb, label.AsSpan());
			_ = sb.Append("</text>");
		}

		if (chart.YAxisTitle is { Length: > 0 } yt)
		{
			_ = sb.Append("\n<text x=\"16\" y=\"").Append(F(plotY + (plotH * 0.5)))
				.Append("\" text-anchor=\"middle\" transform=\"rotate(-90 16 ")
				.Append(F(plotY + (plotH * 0.5))).Append(")\" font-size=\"").Append(AxisFontSize)
				.Append("\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, yt.AsSpan());
			_ = sb.Append("</text>");
		}

		if (chart.XAxisTitle is { Length: > 0 } xt)
		{
			_ = sb.Append("\n<text x=\"").Append(F(plotX + (plotW * 0.5)))
				.Append("\" y=\"").Append(F(height - (hasLegend ? LegendH + 8 : 12)))
				.Append("\" text-anchor=\"middle\" font-size=\"").Append(AxisFontSize)
				.Append("\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, xt.AsSpan());
			_ = sb.Append("</text>");
		}

		// Draw bars first, then lines (mermaid-ish)
		var colorIdx = 0;
		var barSeries = chart.Series.Where(s => s.Type == XySeriesType.Bar).ToList();
		var barGroupCount = Math.Max(1, barSeries.Count);
		var groupWidth = plotW / catCount;
		var barWidth = Math.Max(2, groupWidth * 0.7 / barGroupCount);

		for (var bi = 0; bi < barSeries.Count; bi++)
		{
			var series = barSeries[bi];
			var color = PlotColors[colorIdx++ % PlotColors.Length];
			for (var i = 0; i < series.Values.Count && i < catCount; i++)
			{
				var v = series.Values[i];
				var cx = CategoryX(i, catCount, plotX, plotW, chart.Horizontal);
				var offset = (bi - ((barGroupCount - 1) * 0.5)) * barWidth;
				var x = cx + offset - (barWidth * 0.5);
				var y0 = ValueToY(0, yMin, yMax, plotY, plotH);
				var y1 = ValueToY(v, yMin, yMax, plotY, plotH);
				var barTop = Math.Min(y0, y1);
				var h = Math.Abs(y1 - y0);
				if (h < 0.5)
					h = 0.5;
				_ = sb.Append("\n<rect x=\"").Append(F(x)).Append("\" y=\"").Append(F(barTop))
					.Append("\" width=\"").Append(F(barWidth)).Append("\" height=\"").Append(F(h))
					.Append("\" fill=\"").Append(color).Append("\" opacity=\"0.9\" />");
			}
		}

		foreach (var series in chart.Series.Where(s => s.Type == XySeriesType.Line))
		{
			var color = PlotColors[colorIdx++ % PlotColors.Length];
			if (series.Values.Count == 0)
				continue;

			_ = sb.Append("\n<polyline fill=\"none\" stroke=\"").Append(color)
				.Append("\" stroke-width=\"2\" points=\"");
			for (var i = 0; i < series.Values.Count && i < catCount; i++)
			{
				if (i > 0)
					_ = sb.Append(' ');
				var x = CategoryX(i, catCount, plotX, plotW, chart.Horizontal);
				var y = ValueToY(series.Values[i], yMin, yMax, plotY, plotH);
				_ = sb.Append(F(x)).Append(',').Append(F(y));
			}
			_ = sb.Append("\" />");

			for (var i = 0; i < series.Values.Count && i < catCount; i++)
			{
				var x = CategoryX(i, catCount, plotX, plotW, chart.Horizontal);
				var y = ValueToY(series.Values[i], yMin, yMax, plotY, plotH);
				_ = sb.Append("\n<circle cx=\"").Append(F(x)).Append("\" cy=\"").Append(F(y))
					.Append("\" r=\"3\" fill=\"").Append(color).Append("\" />");
			}
		}

		if (hasLegend)
		{
			var lx = plotX;
			var ly = height - 14;
			// re-walk colors in same order: bars then lines matching draw order
			colorIdx = 0;
			foreach (var series in chart.Series)
			{
				var color = PlotColors[colorIdx++ % PlotColors.Length];
				if (series.Name is not { Length: > 0 } name)
					continue;
				_ = sb.Append("\n<rect x=\"").Append(F(lx)).Append("\" y=\"").Append(F(ly - 8))
					.Append("\" width=\"12\" height=\"12\" rx=\"2\" fill=\"").Append(color).Append("\" />");
				_ = sb.Append("\n<text x=\"").Append(F(lx + 16)).Append("\" y=\"").Append(F(ly))
					.Append("\" dy=\"").Append(RenderConstants.TextBaselineShift)
					.Append("\" font-size=\"").Append(TickFontSize).Append("\" fill=\"var(--_text)\">");
				MultilineUtils.AppendEscapedXml(sb, name.AsSpan());
				_ = sb.Append("</text>");
				lx += 16 + (name.Length * 7) + 16;
			}
		}

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static (double Min, double Max) ResolveYRange(XyChart chart)
	{
		if (chart.YMin is not null && chart.YMax is not null)
			return (chart.YMin.Value, chart.YMax.Value);

		var min = double.PositiveInfinity;
		var max = double.NegativeInfinity;
		foreach (var s in chart.Series)
		{
			foreach (var v in s.Values)
			{
				if (v < min)
					min = v;
				if (v > max)
					max = v;
			}
		}
		if (double.IsInfinity(min))
		{
			min = 0;
			max = 1;
		}
		// Include 0 baseline for bars when range is positive
		if (chart.Series.Any(s => s.Type == XySeriesType.Bar) && min > 0)
			min = 0;
		if (chart.YMin is not null)
			min = chart.YMin.Value;
		if (chart.YMax is not null)
			max = chart.YMax.Value;
		return (min, max);
	}

	private static double CategoryX(int index, int count, double plotX, double plotW, bool horizontal)
	{
		_ = horizontal; // horizontal orientation reserved; v1 maps categories on X the same
		if (count <= 1)
			return plotX + (plotW * 0.5);
		return plotX + (plotW * ((index + 0.5) / count));
	}

	private static double ValueToY(double value, double yMin, double yMax, double plotY, double plotH)
	{
		var t = (value - yMin) / (yMax - yMin);
		t = Math.Clamp(t, 0, 1);
		return plotY + (plotH * (1 - t));
	}

	private static string F(double value) =>
		value.ToString("0.##", CultureInfo.InvariantCulture);
}
