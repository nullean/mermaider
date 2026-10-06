using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// XY chart: soft grid, one axis line along the value baseline, meta ticks, bars in the preset's mark language (gradient
/// solid / outlined area / fat rounded) and line series with points. Colours follow declaration order (series i).
/// </summary>
internal static class XyChartSvgRenderer
{
	private const double DefaultWidth = 760;
	private const double DefaultHeight = 500;
	private const double TickGap = 12;
	private const double CategoryLabelGap = 20;
	private const double AxisTitleBand = 24;
	private const double LegendBand = 32;
	private const int TickCount = 5;

	internal static string Render(XyChart chart, SvgRenderContext context)
	{
		var sb = RenderToBuilder(chart, context);
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

	internal static StringBuilder RenderToBuilder(XyChart chart, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var ds = DesignSystem.For(context);
		var hasTitle = chart.Title is { Length: > 0 };
		var hasLegend = chart.Series.Any(s => s.Name is { Length: > 0 });
		var horizontal = chart.Horizontal;

		var catCount = chart.XCategories?.Count ?? 0;
		var maxPoints = chart.Series.Count == 0 ? 0 : chart.Series.Max(s => s.Values.Count);
		if (catCount == 0)
			catCount = Math.Max(1, maxPoints);

		var (vMin, vMax) = ResolveYRange(chart);
		if (Math.Abs(vMax - vMin) < 1e-12)
		{
			vMin -= 1;
			vMax += 1;
		}

		// left gutter: the labels on the left axis (values when vertical, categories when horizontal)
		var leftLabelW = 0.0;
		if (horizontal)
		{
			for (var i = 0; i < catCount; i++)
				leftLabelW = Math.Max(leftLabelW, DesignSystem.MeasureRole(CategoryLabel(chart, i), TypeRole.Meta, 400));
		}
		else
		{
			for (var t = 0; t <= TickCount; t++)
				leftLabelW = Math.Max(leftLabelW, DesignSystem.MeasureRole(FormatTick(vMin + ((vMax - vMin) * t / TickCount)), TypeRole.Meta, 400));
		}

		var leftTitle = horizontal ? chart.XAxisTitle : chart.YAxisTitle;
		var bottomTitle = horizontal ? chart.YAxisTitle : chart.XAxisTitle;
		var plotX = DesignSystem.ChartPad + (leftTitle is { Length: > 0 } ? AxisTitleBand : 0) + leftLabelW + TickGap;
		plotX = Math.Max(Math.Ceiling(plotX), 100);
		var plotY = DesignSystem.ChartTop(hasTitle) + 4;
		var width = DefaultWidth;
		var height = DefaultHeight;
		var plotW = Math.Max(40, width - DesignSystem.ChartPad - plotX);
		var plotBottom = height - DesignSystem.ChartPad - CategoryLabelGap
			- (bottomTitle is { Length: > 0 } ? AxisTitleBand : 0) - (hasLegend ? LegendBand : 0);
		var plotH = Math.Max(40, plotBottom - plotY);
		plotBottom = plotY + plotH;

		StyleBlock.AppendSvgOpenTag(sb, width, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles);

		if (hasTitle)
			ds.AppendTitle(sb, DesignSystem.ChartPad, DesignSystem.ChartTitleCy, chart.Title!);

		if (chart.Series.Count == 0)
		{
			ds.Close(sb);
			return sb;
		}

		// grid + value ticks
		_ = sb.Append("\n<g class=\"xy-grid\">");
		for (var t = 0; t <= TickCount; t++)
		{
			var frac = t / (double)TickCount;
			var val = vMin + ((vMax - vMin) * frac);
			_ = sb.Append("\n  ");
			if (horizontal)
			{
				var x = plotX + (plotW * frac);
				ds.AppendGridLine(sb, Math.Round(x, 3), plotY, Math.Round(x, 3), plotBottom);
				_ = sb.Append("\n  ");
				ds.AppendChartText(sb, FormatTick(val), x, plotBottom + CategoryLabelGap, TypeRole.Meta);
			}
			else
			{
				var y = plotBottom - (plotH * frac);
				ds.AppendGridLine(sb, plotX, Math.Round(y, 3), plotX + plotW, Math.Round(y, 3));
				_ = sb.Append("\n  ");
				ds.AppendChartText(sb, FormatTick(val), plotX - TickGap, y, TypeRole.Meta, anchor: "end");
			}
		}

		// category labels
		for (var i = 0; i < catCount; i++)
		{
			_ = sb.Append("\n  ");
			if (horizontal)
				ds.AppendChartText(sb, CategoryLabel(chart, i), plotX - TickGap, CategoryPos(i, catCount, plotY, plotH), TypeRole.Meta, anchor: "end");
			else
				ds.AppendChartText(sb, CategoryLabel(chart, i), CategoryPos(i, catCount, plotX, plotW), plotBottom + CategoryLabelGap, TypeRole.Meta);
		}

		_ = sb.Append("\n</g>");

		// axis titles (x-axis = categories, y-axis = values, per source semantics)
		if (leftTitle is { Length: > 0 })
		{
			_ = sb.Append('\n');
			ds.AppendRotatedText(sb, leftTitle, DesignSystem.ChartPad + 6, plotY + (plotH / 2), TypeRole.Caption);
		}

		if (bottomTitle is { Length: > 0 })
		{
			_ = sb.Append('\n');
			ds.AppendChartText(sb, bottomTitle, plotX + (plotW / 2), plotBottom + CategoryLabelGap + AxisTitleBand, TypeRole.Caption);
		}

		var barSeriesIdx = new List<int>();
		for (var si = 0; si < chart.Series.Count; si++)
		{
			if (chart.Series[si].Type == XySeriesType.Bar)
				barSeriesIdx.Add(si);
		}

		var barGroupCount = Math.Max(1, barSeriesIdx.Count);
		var groupSize = (horizontal ? plotH : plotW) / catCount;
		const double barGap = 2;
		var barBand = groupSize * ds.BarSlotRatio;
		var barThickness = Math.Max(2, (barBand - (barGap * (barGroupCount - 1))) / barGroupCount);

		// bars (under lines), colours declaration-indexed
		for (var bi = 0; bi < barSeriesIdx.Count; bi++)
		{
			var si = barSeriesIdx[bi];
			var series = chart.Series[si];
			var family = ds.Series(si);
			var fill = ds.BarFill(family);
			_ = sb.Append("\n<g class=\"xy-bars\" data-series=\"").Append(si.ToString(CultureInfo.InvariantCulture)).Append("\">");
			for (var i = 0; i < series.Values.Count && i < catCount; i++)
			{
				var v = series.Values[i];
				var offset = (bi - ((barGroupCount - 1) * 0.5)) * (barThickness + barGap);
				_ = sb.Append("\n  ");
				if (horizontal)
				{
					var cy = CategoryPos(i, catCount, plotY, plotH) + offset;
					var x0 = ValueToX(Baseline(vMin, vMax), vMin, vMax, plotX, plotW);
					var x1 = ValueToX(v, vMin, vMax, plotX, plotW);
					AppendBar(sb, ds, family, fill, horizontal: true, cy - (barThickness / 2), barThickness, x0, x1);
				}
				else
				{
					var cx = CategoryPos(i, catCount, plotX, plotW) + offset;
					var y0 = ValueToY(Baseline(vMin, vMax), vMin, vMax, plotY, plotH);
					var y1 = ValueToY(v, vMin, vMax, plotY, plotH);
					AppendBar(sb, ds, family, fill, horizontal: false, cx - (barThickness / 2), barThickness, y0, y1);
				}
			}

			_ = sb.Append("\n</g>");
		}

		// the value baseline sits on top of the bars' base
		_ = sb.Append('\n');
		if (horizontal)
		{
			var x0 = ValueToX(Baseline(vMin, vMax), vMin, vMax, plotX, plotW);
			DesignSystem.AppendAxisLine(sb, x0, plotY, x0, plotBottom);
		}
		else
		{
			var y0 = ValueToY(Baseline(vMin, vMax), vMin, vMax, plotY, plotH);
			DesignSystem.AppendAxisLine(sb, plotX, y0, plotX + plotW, y0);
		}

		AppendBoxes(sb, ds, chart, catCount, groupSize, horizontal, vMin, vMax, plotX, plotY, plotW, plotH);

		for (var si = 0; si < chart.Series.Count; si++)
		{
			var series = chart.Series[si];
			if (series.Type != XySeriesType.Line || series.Values.Count == 0)
				continue;
			var color = ds.Series(si).Base;
			var points = new List<(double X, double Y)>();
			for (var i = 0; i < series.Values.Count && i < catCount; i++)
			{
				points.Add(horizontal
					? (ValueToX(series.Values[i], vMin, vMax, plotX, plotW), CategoryPos(i, catCount, plotY, plotH))
					: (CategoryPos(i, catCount, plotX, plotW), ValueToY(series.Values[i], vMin, vMax, plotY, plotH)));
			}

			_ = sb.Append("\n<g class=\"xy-line\" data-series=\"").Append(si.ToString(CultureInfo.InvariantCulture)).Append("\">\n  ");
			AppendLine(sb, ds, points, color);
			foreach (var (x, y) in points)
			{
				_ = sb.Append("\n  ");
				ds.AppendPoint(sb, Math.Round(x, 3), Math.Round(y, 3), color, ds.PointRadius);
			}

			_ = sb.Append("\n</g>");
		}

		if (hasLegend)
		{
			var lx = plotX;
			var ly = height - DesignSystem.ChartPad - (LegendBand / 2) + 8;
			for (var si = 0; si < chart.Series.Count; si++)
			{
				if (chart.Series[si].Name is not { Length: > 0 } name)
					continue;
				_ = sb.Append('\n');
				lx += ds.AppendLegendItem(sb, lx, ly, name, ds.Series(si).Base) + 24;
			}
		}

		ds.Close(sb);
		return sb;
	}

	/// <summary>A bar from <paramref name="from"/> (baseline) to <paramref name="to"/> along the value axis, rounded at the free end.</summary>
	private static void AppendBar(StringBuilder sb, DesignSystem ds, ColorFamily family, string fill, bool horizontal, double cross, double thickness, double from, double to)
	{
		var len = Math.Abs(to - from);
		if (len < 0.5)
		{
			len = 0.5;
			to = from + (to >= from ? 0.5 : -0.5);
		}

		var r = Math.Min(ds.BarRadius, Math.Min(thickness / 2, len));
		if (r <= 0)
		{
			var x = horizontal ? Math.Min(from, to) : cross;
			var y = horizontal ? cross : Math.Min(from, to);
			_ = sb.Append("<rect x=\"").Append(x.SvgFormat()).Append("\" y=\"").Append(y.SvgFormat())
				.Append("\" width=\"").Append((horizontal ? len : thickness).SvgFormat())
				.Append("\" height=\"").Append((horizontal ? thickness : len).SvgFormat()).Append('"');
			ds.AppendMarkPaint(sb, family, fill);
			_ = sb.Append(" />");
			return;
		}

		// path: base edge → up to the tip with two rounded corners → back to base
		var s = Math.Sign(to - from);
		var a = cross;
		var b = cross + thickness;
		string P(double along, double across) => horizontal
			? along.SvgFormat() + " " + across.SvgFormat()
			: across.SvgFormat() + " " + along.SvgFormat();

		_ = sb.Append("<path d=\"M ").Append(P(from, a))
			.Append(" L ").Append(P(to - (s * r), a))
			.Append(" Q ").Append(P(to, a)).Append(' ').Append(P(to, a + r))
			.Append(" L ").Append(P(to, b - r))
			.Append(" Q ").Append(P(to, b)).Append(' ').Append(P(to - (s * r), b))
			.Append(" L ").Append(P(from, b)).Append(" Z\"");
		ds.AppendMarkPaint(sb, family, fill);
		_ = sb.Append(" />");
	}

	/// <summary>A series line: straight polyline, or a smooth Catmull-Rom curve for fat marks.</summary>
	private static void AppendLine(StringBuilder sb, DesignSystem ds, List<(double X, double Y)> pts, string color)
	{
		var sw = ds.SeriesLineWidth.SvgFormat();
		if (!ds.SmoothLines || pts.Count < 3)
		{
			_ = sb.Append("<polyline points=\"");
			for (var i = 0; i < pts.Count; i++)
			{
				if (i > 0)
					_ = sb.Append(' ');
				_ = sb.Append(pts[i].X.SvgFormat()).Append(',').Append(pts[i].Y.SvgFormat());
			}

			_ = sb.Append("\" fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"").Append(sw)
				.Append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\" />");
			return;
		}

		_ = sb.Append("<path d=\"M ").Append(pts[0].X.SvgFormat()).Append(' ').Append(pts[0].Y.SvgFormat());
		for (var i = 0; i < pts.Count - 1; i++)
		{
			var p0 = pts[Math.Max(0, i - 1)];
			var p1 = pts[i];
			var p2 = pts[i + 1];
			var p3 = pts[Math.Min(pts.Count - 1, i + 2)];
			var c1x = p1.X + ((p2.X - p0.X) / 6);
			var c1y = p1.Y + ((p2.Y - p0.Y) / 6);
			var c2x = p2.X - ((p3.X - p1.X) / 6);
			var c2y = p2.Y - ((p3.Y - p1.Y) / 6);
			_ = sb.Append(" C ").Append(c1x.SvgFormat()).Append(' ').Append(c1y.SvgFormat())
				.Append(' ').Append(c2x.SvgFormat()).Append(' ').Append(c2y.SvgFormat())
				.Append(' ').Append(p2.X.SvgFormat()).Append(' ').Append(p2.Y.SvgFormat());
		}

		_ = sb.Append("\" fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"").Append(sw)
			.Append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\" />");
	}

	/// <summary>
	/// Box-and-whisker. Each box series is one category's five-number summary, so the Nth box line sits at the Nth
	/// category; values are sorted first so an unordered summary still reads right. The box is a region (area + outline).
	/// </summary>
	private static void AppendBoxes(StringBuilder sb, DesignSystem ds, XyChart chart, int catCount, double groupSize, bool horizontal,
		double vMin, double vMax, double plotX, double plotY, double plotW, double plotH)
	{
		var boxOrdinal = 0;
		for (var si = 0; si < chart.Series.Count; si++)
		{
			var series = chart.Series[si];
			if (series.Type != XySeriesType.Box)
				continue;
			var category = boxOrdinal++;
			if (series.Values.Count < 5 || category >= catCount)
				continue;

			var five = series.Values.Take(5).OrderBy(v => v).ToArray();
			var family = ds.Series(si);
			var color = family.Base;
			var thickness = Math.Max(4, groupSize * 0.45);
			var center = CategoryPos(category, catCount, horizontal ? plotY : plotX, horizontal ? plotH : plotW);
			var at = new double[5];
			for (var k = 0; k < 5; k++)
			{
				at[k] = horizontal
					? ValueToX(five[k], vMin, vMax, plotX, plotW)
					: ValueToY(five[k], vMin, vMax, plotY, plotH);
			}

			_ = sb.Append("\n<g class=\"xy-box\" data-series=\"").Append(si.ToString(CultureInfo.InvariantCulture)).Append("\">");
			AppendWhisker(sb, color, horizontal, center, at[0], at[1]);
			AppendWhisker(sb, color, horizontal, center, at[3], at[4]);
			AppendCap(sb, color, horizontal, center, at[0], thickness * 0.5);
			AppendCap(sb, color, horizontal, center, at[4], thickness * 0.5);

			var low = Math.Min(at[1], at[3]);
			var span = Math.Max(1, Math.Abs(at[3] - at[1]));
			var boxX = horizontal ? low : center - (thickness * 0.5);
			var boxY = horizontal ? center - (thickness * 0.5) : low;
			var r = DesignSystem.Num(Math.Min(ds.BarRadius / 2, thickness / 4));
			_ = sb.Append("\n  <rect x=\"").Append(boxX.SvgFormat()).Append("\" y=\"").Append(boxY.SvgFormat())
				.Append("\" width=\"").Append((horizontal ? span : thickness).SvgFormat())
				.Append("\" height=\"").Append((horizontal ? thickness : span).SvgFormat())
				.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r).Append('"');
			ds.AppendAreaAttributes(sb, family);
			_ = sb.Append(" />");
			AppendCap(sb, color, horizontal, center, at[2], thickness);
			_ = sb.Append("\n</g>");
		}
	}

	private static string CategoryLabel(XyChart chart, int index)
	{
		if (chart.XCategories is { Count: > 0 } cats && index < cats.Count)
			return cats[index];
		return (index + 1).ToString(CultureInfo.InvariantCulture);
	}

	/// <summary>The value bars grow from: zero when it is in range, else the nearest end of the range.</summary>
	private static double Baseline(double vMin, double vMax) => Math.Max(Math.Min(vMin, vMax), Math.Min(Math.Max(vMin, vMax), 0));

	private static string FormatTick(double v) => Math.Round(v, 2).SvgFormat();

	/// <summary>The line from a quartile out to its extreme, along the value axis.</summary>
	private static void AppendWhisker(StringBuilder sb, string color, bool horizontal, double center, double from, double to)
	{
		var (x1, y1) = horizontal ? (from, center) : (center, from);
		var (x2, y2) = horizontal ? (to, center) : (center, to);
		_ = sb.Append("\n  <line x1=\"").Append(x1.SvgFormat()).Append("\" y1=\"").Append(y1.SvgFormat())
			.Append("\" x2=\"").Append(x2.SvgFormat()).Append("\" y2=\"").Append(y2.SvgFormat())
			.Append("\" stroke=\"").Append(color).Append("\" stroke-width=\"1.5\" />");
	}

	/// <summary>A tick across the category axis: the end of a whisker, or the median across the box.</summary>
	private static void AppendCap(StringBuilder sb, string color, bool horizontal, double center, double at, double width)
	{
		var half = width * 0.5;
		var (x1, y1) = horizontal ? (at, center - half) : (center - half, at);
		var (x2, y2) = horizontal ? (at, center + half) : (center + half, at);
		_ = sb.Append("\n  <line x1=\"").Append(x1.SvgFormat()).Append("\" y1=\"").Append(y1.SvgFormat())
			.Append("\" x2=\"").Append(x2.SvgFormat()).Append("\" y2=\"").Append(y2.SvgFormat())
			.Append("\" stroke=\"").Append(color).Append("\" stroke-width=\"2\" stroke-linecap=\"round\" />");
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
