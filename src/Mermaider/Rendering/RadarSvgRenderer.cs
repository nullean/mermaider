using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Radar: concentric soft graticule (rings or polygons) with soft spokes, axis names outside the rim, each curve an
/// overlap-safe region (series area + outline) with data points, and a legend to the right.
/// </summary>
internal static class RadarSvgRenderer
{
	private const double Radius = 168;
	private const double LabelPad = 28;
	private const double LegendGap = 32;

	internal static string Render(RadarChart chart, SvgRenderContext context)
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

	internal static StringBuilder RenderToBuilder(RadarChart chart, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var ds = DesignSystem.For(context);

		if (chart.Axes.Count == 0)
		{
			StyleBlock.AppendSvgOpenTag(sb, 200, 100, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
			StyleBlock.AppendStyleBlock(sb, context.Styles);
			ds.Close(sb);
			return sb;
		}

		var n = chart.Axes.Count;
		var hasTitle = chart.Title is { Length: > 0 };

		// room for the axis names left / right of the rim
		var leftW = 0.0;
		var rightW = 0.0;
		for (var i = 0; i < n; i++)
		{
			var cos = Math.Cos(Angle(i, n));
			var w = DesignSystem.MeasureRole(chart.Axes[i].Label, TypeRole.Label, 500);
			if (cos < -0.1)
				leftW = Math.Max(leftW, w + ((Radius + LabelPad) * (1 + cos)));
			else if (cos > 0.1)
				rightW = Math.Max(rightW, w - ((Radius + LabelPad) * (1 - cos)));
			else
			{
				leftW = Math.Max(leftW, (w / 2) - Radius - LabelPad);
				rightW = Math.Max(rightW, (w / 2) - Radius - LabelPad);
			}
		}

		var cx = Math.Ceiling(DesignSystem.ChartPad + Math.Max(0, leftW) + Radius + LabelPad);
		var cy = DesignSystem.ChartTop(hasTitle) + LabelPad + 8 + Radius;
		var rimRight = Math.Ceiling(cx + Radius + LabelPad + Math.Max(0, rightW));

		var showLegend = chart.ShowLegend && chart.Curves.Count > 0;
		var legendX = rimRight + LegendGap;
		var legendW = 0.0;
		if (showLegend)
		{
			foreach (var curve in chart.Curves)
				legendW = Math.Max(legendW, ds.LegendItemWidth(curve.Label));
		}

		var width = (showLegend ? legendX + legendW : rimRight) + DesignSystem.ChartPad;
		var height = cy + Radius + LabelPad + 8 + DesignSystem.ChartPad;

		StyleBlock.AppendSvgOpenTag(sb, width, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles);

		if (hasTitle)
			ds.AppendTitle(sb, DesignSystem.ChartPad, DesignSystem.ChartTitleCy, chart.Title!);

		_ = sb.Append("\n<g class=\"radar-grid\">");
		AppendGraticule(sb, ds, chart, n, cx, cy);
		AppendAxes(sb, ds, chart, n, cx, cy);
		_ = sb.Append("\n</g>");

		for (var ci = 0; ci < chart.Curves.Count; ci++)
			AppendCurve(sb, ds, chart, chart.Curves[ci], ci, n, cx, cy);

		if (showLegend)
		{
			var top = cy - Radius;
			for (var i = 0; i < chart.Curves.Count; i++)
			{
				_ = sb.Append('\n');
				_ = ds.AppendLegendItem(sb, legendX, top + (i * DesignSystem.RowHeight) + (DesignSystem.RowHeight / 2), chart.Curves[i].Label, ds.Series(i).Base);
			}
		}

		ds.Close(sb);
		return sb;
	}

	private static double Angle(int i, int n) => (2 * Math.PI * i / n) - (Math.PI / 2);

	private static void AppendGraticule(StringBuilder sb, DesignSystem ds, RadarChart chart, int n, double cx, double cy)
	{
		var ticks = Math.Max(1, chart.Ticks);
		for (var t = 1; t <= ticks; t++)
		{
			var r = Radius * t / ticks;
			_ = sb.Append("\n  ");
			if (chart.Graticule == RadarGraticule.Circle)
			{
				ds.AppendGridCircle(sb, cx, cy, r);
				continue;
			}

			var d = new StringBuilder(n * 24);
			for (var i = 0; i < n; i++)
			{
				var a = Angle(i, n);
				_ = d.Append(i == 0 ? "M " : " L ").Append((cx + (r * Math.Cos(a))).SvgFormat()).Append(' ').Append((cy + (r * Math.Sin(a))).SvgFormat());
			}

			_ = d.Append(" Z");
			ds.AppendGridPath(sb, d.ToString());
		}
	}

	private static void AppendAxes(StringBuilder sb, DesignSystem ds, RadarChart chart, int n, double cx, double cy)
	{
		for (var i = 0; i < n; i++)
		{
			var angle = Angle(i, n);
			var cos = Math.Cos(angle);
			_ = sb.Append("\n  ");
			ds.AppendGridLine(sb, cx, cy, Math.Round(cx + (Radius * cos), 3), Math.Round(cy + (Radius * Math.Sin(angle)), 3));

			var labelR = Radius + LabelPad;
			var anchor = Math.Abs(cos) < 0.1 ? "middle" : cos > 0 ? "start" : "end";
			_ = sb.Append("\n  ");
			ds.AppendChartText(sb, chart.Axes[i].Label, cx + (labelR * cos), cy + (labelR * Math.Sin(angle)), TypeRole.Label, "var(--_text-sec)", anchor, 500);
		}
	}

	private static void AppendCurve(StringBuilder sb, DesignSystem ds, RadarChart chart, RadarCurve curve, int index, int n, double cx, double cy)
	{
		var range = chart.Max - chart.Min;
		if (range <= 0)
			return;

		var family = ds.Series(index);
		var pts = new (double X, double Y)[n];
		for (var i = 0; i < n; i++)
		{
			var angle = Angle(i, n);
			var val = i < curve.Values.Count ? curve.Values[i] : 0;
			var r = Radius * Math.Clamp((val - chart.Min) / range, 0, 1);
			pts[i] = (Math.Round(cx + (r * Math.Cos(angle)), 3), Math.Round(cy + (r * Math.Sin(angle)), 3));
		}

		_ = sb.Append("\n<g class=\"radar-curve\" data-curve=\"");
		MultilineUtils.AppendEscapedAttr(sb, curve.Id);
		_ = sb.Append("\">\n  <polygon points=\"");
		for (var i = 0; i < n; i++)
		{
			if (i > 0)
				_ = sb.Append(' ');
			_ = sb.Append(pts[i].X.SvgFormat()).Append(',').Append(pts[i].Y.SvgFormat());
		}

		_ = sb.Append('"');
		ds.AppendAreaAttributes(sb, family);
		_ = sb.Append(" stroke-linejoin=\"round\" />");

		foreach (var (x, y) in pts)
		{
			_ = sb.Append("\n  ");
			ds.AppendPoint(sb, x, y, family.Base, ds.PointRadius);
		}

		_ = sb.Append("\n</g>");
	}
}
