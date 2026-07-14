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
	private const double PadLHorizontal = 90;
	private const double PadR = 30;
	private const double PadT = 40;
	private const double PadB = 50;
	private const double TitleH = 28;
	private const double LegendH = 28;
	private const string TitleFontSize = RenderConstants.FsVar.L;
	private const string AxisFontSize = RenderConstants.FsVar.S;
	private const string TickFontSize = RenderConstants.FsVar.Xs;


	internal static string Render(XyChart chart, DiagramColors colors, string font, string? monoFont = null, bool transparent = false, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
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

	internal static StringBuilder RenderToBuilder(XyChart chart, DiagramColors colors, string font, string? monoFont = null, bool transparent = false, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var hasTitle = chart.Title is { Length: > 0 };
		var hasLegend = chart.Series.Any(s => s.Name is { Length: > 0 });
		var horizontal = chart.Horizontal;

		var top = PadT + (hasTitle ? TitleH : 0);
		var bottom = PadB + (hasLegend ? LegendH : 0);
		var left = horizontal ? PadLHorizontal : PadL;
		var plotW = DefaultWidth - left - PadR;
		var plotH = DefaultHeight - top - bottom;
		if (plotW < 40)
			plotW = 40;
		if (plotH < 40)
			plotH = 40;

		var width = DefaultWidth;
		var height = DefaultHeight;
		var plotX = left;
		var plotY = top;

		StyleBlock.AppendSvgOpenTag(sb, width, height, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict, monoFont: monoFont);
		_ = sb.Append("\n<defs>\n</defs>\n");

		if (hasTitle)
		{
			_ = sb.Append("\n<text x=\"").Append((width * 0.5).SvgFormat()).Append("\" y=\"24\" text-anchor=\"middle\" font-size=\"")
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

		var (vMin, vMax) = ResolveYRange(chart);
		if (Math.Abs(vMax - vMin) < 1e-12)
		{
			vMin -= 1;
			vMax += 1;
		}

		// Axes: horizontal puts values on X (bottom) and categories on Y (left).
		_ = sb.Append("\n<line x1=\"").Append(plotX.SvgFormat()).Append("\" y1=\"").Append((plotY + plotH).SvgFormat())
			.Append("\" x2=\"").Append((plotX + plotW).SvgFormat()).Append("\" y2=\"").Append((plotY + plotH).SvgFormat())
			.Append("\" stroke=\"var(--_line)\" stroke-width=\"1.5\" />");
		_ = sb.Append("\n<line x1=\"").Append(plotX.SvgFormat()).Append("\" y1=\"").Append(plotY.SvgFormat())
			.Append("\" x2=\"").Append(plotX.SvgFormat()).Append("\" y2=\"").Append((plotY + plotH).SvgFormat())
			.Append("\" stroke=\"var(--_line)\" stroke-width=\"1.5\" />");

		const int tickCount = 5;
		if (horizontal)
		{
			// Value ticks along bottom (X)
			for (var t = 0; t <= tickCount; t++)
			{
				var frac = t / (double)tickCount;
				var val = vMin + ((vMax - vMin) * frac);
				var x = plotX + (plotW * frac);
				_ = sb.Append("\n<line x1=\"").Append(x.SvgFormat()).Append("\" y1=\"").Append((plotY + plotH).SvgFormat())
					.Append("\" x2=\"").Append(x.SvgFormat()).Append("\" y2=\"").Append((plotY + plotH + 4).SvgFormat())
					.Append("\" stroke=\"var(--_line)\" stroke-width=\"1\" />");
				_ = sb.Append("\n<text x=\"").Append(x.SvgFormat()).Append("\" y=\"").Append((plotY + plotH + 16).SvgFormat())
					.Append("\" text-anchor=\"middle\" font-size=\"").Append(TickFontSize)
					.Append("\" fill=\"var(--_text-sec)\">").Append(val.SvgFormat()).Append("</text>");
			}

			// Category labels along left (Y)
			for (var i = 0; i < catCount; i++)
			{
				var y = CategoryPos(i, catCount, plotY, plotH);
				var label = CategoryLabel(chart, i);
				_ = sb.Append("\n<line x1=\"").Append((plotX - 4).SvgFormat()).Append("\" y1=\"").Append(y.SvgFormat())
					.Append("\" x2=\"").Append(plotX.SvgFormat()).Append("\" y2=\"").Append(y.SvgFormat())
					.Append("\" stroke=\"var(--_line)\" stroke-width=\"1\" />");
				_ = sb.Append("\n<text x=\"").Append((plotX - 8).SvgFormat()).Append("\" y=\"").Append(y.SvgFormat())
					.Append("\" text-anchor=\"end\" dy=\"").Append(RenderConstants.TextBaselineShift)
					.Append("\" font-size=\"").Append(TickFontSize).Append("\" fill=\"var(--_text-sec)\">");
				MultilineUtils.AppendEscapedXml(sb, label.AsSpan());
				_ = sb.Append("</text>");
			}
		}
		else
		{
			// Value ticks along left (Y)
			for (var t = 0; t <= tickCount; t++)
			{
				var frac = t / (double)tickCount;
				var val = vMin + ((vMax - vMin) * (1 - frac)); // top = max
				var y = plotY + (plotH * frac);
				_ = sb.Append("\n<line x1=\"").Append((plotX - 4).SvgFormat()).Append("\" y1=\"").Append(y.SvgFormat())
					.Append("\" x2=\"").Append(plotX.SvgFormat()).Append("\" y2=\"").Append(y.SvgFormat())
					.Append("\" stroke=\"var(--_line)\" stroke-width=\"1\" />");
				_ = sb.Append("\n<text x=\"").Append((plotX - 8).SvgFormat()).Append("\" y=\"").Append(y.SvgFormat())
					.Append("\" text-anchor=\"end\" dy=\"").Append(RenderConstants.TextBaselineShift)
					.Append("\" font-size=\"").Append(TickFontSize).Append("\" fill=\"var(--_text-sec)\">")
					.Append(val.SvgFormat()).Append("</text>");
			}

			// Category labels along bottom (X)
			for (var i = 0; i < catCount; i++)
			{
				var x = CategoryPos(i, catCount, plotX, plotW);
				var label = CategoryLabel(chart, i);
				_ = sb.Append("\n<line x1=\"").Append(x.SvgFormat()).Append("\" y1=\"").Append((plotY + plotH).SvgFormat())
					.Append("\" x2=\"").Append(x.SvgFormat()).Append("\" y2=\"").Append((plotY + plotH + 4).SvgFormat())
					.Append("\" stroke=\"var(--_line)\" stroke-width=\"1\" />");
				_ = sb.Append("\n<text x=\"").Append(x.SvgFormat()).Append("\" y=\"").Append((plotY + plotH + 16).SvgFormat())
					.Append("\" text-anchor=\"middle\" font-size=\"").Append(TickFontSize)
					.Append("\" fill=\"var(--_text-sec)\">");
				MultilineUtils.AppendEscapedXml(sb, label.AsSpan());
				_ = sb.Append("</text>");
			}
		}

		// Axis titles: x-axis = categories, y-axis = values (source semantics).
		// Horizontal places category title on the left, value title on the bottom.
		var bottomTitleY = height - (hasLegend ? LegendH + 8 : 12);
		var midY = plotY + (plotH * 0.5);
		var midX = plotX + (plotW * 0.5);
		if (chart.XAxisTitle is { Length: > 0 } xt)
			AppendAxisTitle(sb, xt, leftSide: horizontal, midX, midY, bottomTitleY);
		if (chart.YAxisTitle is { Length: > 0 } yt)
			AppendAxisTitle(sb, yt, leftSide: !horizontal, midX, midY, bottomTitleY);

		// Stable series colors in declaration order (shared by draw + legend).
		var seriesColors = new string[chart.Series.Count];
		for (var si = 0; si < chart.Series.Count; si++)
			seriesColors[si] = CategoricalPalette.At(si);

		var barSeriesIdx = new List<int>();
		for (var si = 0; si < chart.Series.Count; si++)
		{
			if (chart.Series[si].Type == XySeriesType.Bar)
				barSeriesIdx.Add(si);
		}
		var barGroupCount = Math.Max(1, barSeriesIdx.Count);
		// Group size is along the category axis (X vertical / Y horizontal).
		var groupSize = (horizontal ? plotH : plotW) / catCount;
		var barThickness = Math.Max(2, groupSize * 0.7 / barGroupCount);

		// Bars first (under lines), but colors stay declaration-indexed.
		for (var bi = 0; bi < barSeriesIdx.Count; bi++)
		{
			var si = barSeriesIdx[bi];
			var series = chart.Series[si];
			var color = seriesColors[si];
			for (var i = 0; i < series.Values.Count && i < catCount; i++)
			{
				var v = series.Values[i];
				var offset = (bi - ((barGroupCount - 1) * 0.5)) * barThickness;
				if (horizontal)
				{
					var cy = CategoryPos(i, catCount, plotY, plotH) + offset;
					var y = cy - (barThickness * 0.5);
					var x0 = ValueToX(0, vMin, vMax, plotX, plotW);
					var x1 = ValueToX(v, vMin, vMax, plotX, plotW);
					var barLeft = Math.Min(x0, x1);
					var w = Math.Abs(x1 - x0);
					if (w < 0.5)
						w = 0.5;
					_ = sb.Append("\n<rect x=\"").Append(barLeft.SvgFormat()).Append("\" y=\"").Append(y.SvgFormat())
						.Append("\" width=\"").Append(w.SvgFormat()).Append("\" height=\"").Append(barThickness.SvgFormat())
						.Append("\" fill=\"").Append(color).Append("\" opacity=\"0.9\" />");
				}
				else
				{
					var cx = CategoryPos(i, catCount, plotX, plotW) + offset;
					var x = cx - (barThickness * 0.5);
					var y0 = ValueToY(0, vMin, vMax, plotY, plotH);
					var y1 = ValueToY(v, vMin, vMax, plotY, plotH);
					var barTop = Math.Min(y0, y1);
					var h = Math.Abs(y1 - y0);
					if (h < 0.5)
						h = 0.5;
					_ = sb.Append("\n<rect x=\"").Append(x.SvgFormat()).Append("\" y=\"").Append(barTop.SvgFormat())
						.Append("\" width=\"").Append(barThickness.SvgFormat()).Append("\" height=\"").Append(h.SvgFormat())
						.Append("\" fill=\"").Append(color).Append("\" opacity=\"0.9\" />");
				}
			}
		}

		for (var si = 0; si < chart.Series.Count; si++)
		{
			var series = chart.Series[si];
			if (series.Type != XySeriesType.Line || series.Values.Count == 0)
				continue;
			var color = seriesColors[si];

			_ = sb.Append("\n<polyline fill=\"none\" stroke=\"").Append(color)
				.Append("\" stroke-width=\"2\" points=\"");
			for (var i = 0; i < series.Values.Count && i < catCount; i++)
			{
				if (i > 0)
					_ = sb.Append(' ');
				if (horizontal)
				{
					var x = ValueToX(series.Values[i], vMin, vMax, plotX, plotW);
					var y = CategoryPos(i, catCount, plotY, plotH);
					_ = sb.Append(x.SvgFormat()).Append(',').Append(y.SvgFormat());
				}
				else
				{
					var x = CategoryPos(i, catCount, plotX, plotW);
					var y = ValueToY(series.Values[i], vMin, vMax, plotY, plotH);
					_ = sb.Append(x.SvgFormat()).Append(',').Append(y.SvgFormat());
				}
			}
			_ = sb.Append("\" />");

			for (var i = 0; i < series.Values.Count && i < catCount; i++)
			{
				double x, y;
				if (horizontal)
				{
					x = ValueToX(series.Values[i], vMin, vMax, plotX, plotW);
					y = CategoryPos(i, catCount, plotY, plotH);
				}
				else
				{
					x = CategoryPos(i, catCount, plotX, plotW);
					y = ValueToY(series.Values[i], vMin, vMax, plotY, plotH);
				}
				_ = sb.Append("\n<circle cx=\"").Append(x.SvgFormat()).Append("\" cy=\"").Append(y.SvgFormat())
					.Append("\" r=\"3\" fill=\"").Append(color).Append("\" />");
			}
		}

		if (hasLegend)
		{
			var lx = plotX;
			var ly = height - 14;
			for (var si = 0; si < chart.Series.Count; si++)
			{
				if (chart.Series[si].Name is not { Length: > 0 } name)
					continue;
				var color = seriesColors[si];
				_ = sb.Append("\n<rect x=\"").Append(lx.SvgFormat()).Append("\" y=\"").Append((ly - 8).SvgFormat())
					.Append("\" width=\"12\" height=\"12\" rx=\"2\" fill=\"").Append(color).Append("\" />");
				_ = sb.Append("\n<text x=\"").Append((lx + 16).SvgFormat()).Append("\" y=\"").Append(ly.SvgFormat())
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

	private static void AppendAxisTitle(StringBuilder sb, string title, bool leftSide, double midX, double midY, double bottomTitleY)
	{
		if (leftSide)
		{
			_ = sb.Append("\n<text x=\"16\" y=\"").Append(midY.SvgFormat())
				.Append("\" text-anchor=\"middle\" transform=\"rotate(-90 16 ")
				.Append(midY.SvgFormat()).Append(")\" font-size=\"").Append(AxisFontSize)
				.Append("\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, title.AsSpan());
			_ = sb.Append("</text>");
			return;
		}

		_ = sb.Append("\n<text x=\"").Append(midX.SvgFormat())
			.Append("\" y=\"").Append(bottomTitleY.SvgFormat())
			.Append("\" text-anchor=\"middle\" font-size=\"").Append(AxisFontSize)
			.Append("\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, title.AsSpan());
		_ = sb.Append("</text>");
	}

	private static string CategoryLabel(XyChart chart, int index)
	{
		if (chart.XCategories is { Count: > 0 } cats && index < cats.Count)
			return cats[index];
		return (index + 1).ToString(CultureInfo.InvariantCulture);
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
		// Include 0 baseline for bars (positive or all-negative ranges)
		if (chart.Series.Any(s => s.Type == XySeriesType.Bar))
		{
			if (min > 0)
				min = 0;
			if (max < 0)
				max = 0;
		}
		if (chart.YMin is not null)
			min = chart.YMin.Value;
		if (chart.YMax is not null)
			max = chart.YMax.Value;
		return (min, max);
	}

	/// <summary>Category center along the category axis (X when vertical, Y when horizontal).</summary>
	private static double CategoryPos(int index, int count, double plotOrigin, double plotSize)
	{
		if (count <= 1)
			return plotOrigin + (plotSize * 0.5);
		return plotOrigin + (plotSize * ((index + 0.5) / count));
	}

	private static double ValueToY(double value, double yMin, double yMax, double plotY, double plotH)
	{
		var t = (value - yMin) / (yMax - yMin);
		t = Math.Clamp(t, 0, 1);
		return plotY + (plotH * (1 - t));
	}

	private static double ValueToX(double value, double vMin, double vMax, double plotX, double plotW)
	{
		var t = (value - vMin) / (vMax - vMin);
		t = Math.Clamp(t, 0, 1);
		return plotX + (plotW * t);
	}

}
