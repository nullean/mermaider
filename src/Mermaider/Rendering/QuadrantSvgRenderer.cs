using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Quadrant chart: four panels separated by a small gap. Quadrant 1 (top right, "invest") is the accent cell; the other
/// three are neutral panels (Quiet), dashed wireframes (Blueprint) or soft series blocks (Tonal). Quadrant names are
/// eyebrow text in the panel's corner; points are accent markers with haloed labels.
/// </summary>
internal static class QuadrantSvgRenderer
{
	private const double Cell = 238;
	private const double Gap = 4;
	private const double ChartSize = (Cell * 2) + Gap;
	private const double AxisBand = 22;
	private const double PointRadius = 5.5;
	private const double PointHalo = 11;
	private const double LabelOffset = 16;

	internal static string Render(QuadrantChart chart, SvgRenderContext context)
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

	internal static StringBuilder RenderToBuilder(QuadrantChart chart, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var ds = DesignSystem.For(context);

		var hasTitle = chart.Title is { Length: > 0 };
		var hasXAxis = chart.XAxisLeft is { Length: > 0 } || chart.XAxisRight is { Length: > 0 };
		var hasYAxis = chart.YAxisBottom is { Length: > 0 } || chart.YAxisTop is { Length: > 0 };

		var chartLeft = DesignSystem.ChartPad + (hasYAxis ? AxisBand + 16 : 0);
		var chartTop = DesignSystem.ChartTop(hasTitle) - (hasTitle ? 4 : 0);

		var totalWidth = chartLeft + ChartSize + DesignSystem.ChartPad;
		var totalHeight = chartTop + ChartSize + (hasXAxis ? 18 + AxisBand : 0) + DesignSystem.ChartPad;

		StyleBlock.AppendSvgOpenTag(sb, totalWidth, totalHeight, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles);

		if (hasTitle)
			ds.AppendTitle(sb, DesignSystem.ChartPad, DesignSystem.ChartTitleCy, chart.Title!);

		var right = chartLeft + Cell + Gap;
		var lower = chartTop + Cell + Gap;
		AppendQuadrant(sb, ds, chartLeft, chartTop, chart.Quadrant2, 2, ds.Series(0));
		AppendQuadrant(sb, ds, right, chartTop, chart.Quadrant1, 1, ds.Accent);
		AppendQuadrant(sb, ds, chartLeft, lower, chart.Quadrant3, 3, ds.Series(1));
		AppendQuadrant(sb, ds, right, lower, chart.Quadrant4, 4, ds.Series(2));

		AppendAxisLabels(sb, ds, chart, chartLeft, chartTop);

		foreach (var point in chart.Points)
			AppendPoint(sb, ds, point, chartLeft, chartTop);

		ds.Close(sb);
		return sb;
	}

	private static void AppendQuadrant(StringBuilder sb, DesignSystem ds, double x, double y, string? label, int quadrant, ColorFamily tonalFamily)
	{
		var invest = quadrant == 1;
		var r = DesignSystem.Num(ds.PanelRadius);
		string fill, stroke, strokeWidth;
		var dashed = false;
		switch (ds.Spec.ChartMarks)
		{
			case ChartMarkKind.Outline:
				fill = "none";
				stroke = invest ? ds.Accent.Edge : DesignSystem.GridStroke;
				strokeWidth = "1";
				dashed = true;
				break;
			case ChartMarkKind.Fat:
				fill = invest ? ds.Accent.Soft : tonalFamily.Tint();
				stroke = "none";
				strokeWidth = "0";
				break;
			default:
				fill = invest ? ds.Accent.Tint() : "var(--_group-fill)";
				stroke = invest ? ds.Accent.Edge : DesignSystem.GridStroke;
				strokeWidth = invest ? "1.25" : "1";
				break;
		}

		_ = sb.Append("\n<g class=\"quadrant\" data-quadrant=\"").Append(quadrant).Append('"');
		if (label is { Length: > 0 })
		{
			// the eyebrow renders in caps; keep the author's text as data
			_ = sb.Append(" data-label=\"");
			MultilineUtils.AppendEscapedAttr(sb, label);
			_ = sb.Append('"');
		}

		_ = sb.Append(">\n  <rect x=\"").Append(x.SvgFormat())
			.Append("\" y=\"").Append(y.SvgFormat()).Append("\" width=\"").Append(Cell.SvgFormat()).Append("\" height=\"").Append(Cell.SvgFormat())
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"").Append(fill).Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"").Append(strokeWidth).Append('"');
		if (dashed)
			_ = sb.Append(" stroke-dasharray=\"2 3\"");
		_ = sb.Append(" />");

		if (label is { Length: > 0 })
		{
			_ = sb.Append("\n  ");
			ds.AppendChartText(sb, label.ToUpperInvariant(), x + 16, y + 22, TypeRole.Eyebrow, invest ? ds.Accent.Ink : null, anchor: "start");
		}

		_ = sb.Append("\n</g>");
	}

	private static void AppendAxisLabels(StringBuilder sb, DesignSystem ds, QuadrantChart chart, double chartLeft, double chartTop)
	{
		var bottom = chartTop + ChartSize;
		if (chart.XAxisLeft is { Length: > 0 } || chart.XAxisRight is { Length: > 0 })
		{
			_ = sb.Append('\n');
			ds.AppendGridLine(sb, chartLeft, bottom + 18, chartLeft + ChartSize, bottom + 18);
			if (chart.XAxisLeft is { Length: > 0 } xl)
			{
				_ = sb.Append('\n');
				ds.AppendChartText(sb, xl, chartLeft, bottom + 18 + 18, TypeRole.Caption, anchor: "start");
			}

			if (chart.XAxisRight is { Length: > 0 } xr)
			{
				_ = sb.Append('\n');
				ds.AppendChartText(sb, xr, chartLeft + ChartSize, bottom + 18 + 18, TypeRole.Caption, anchor: "end");
			}
		}

		var axisX = chartLeft - 22;
		if (chart.YAxisTop is { Length: > 0 } yt)
		{
			_ = sb.Append('\n');
			ds.AppendRotatedText(sb, yt, axisX, chartTop, TypeRole.Caption, anchor: "end");
		}

		if (chart.YAxisBottom is { Length: > 0 } yb)
		{
			_ = sb.Append('\n');
			ds.AppendRotatedText(sb, yb, axisX, bottom, TypeRole.Caption, anchor: "start");
		}
	}

	private static void AppendPoint(StringBuilder sb, DesignSystem ds, QuadrantPoint point, double chartLeft, double chartTop)
	{
		var px = Math.Round(chartLeft + (Math.Clamp(point.X, 0, 1) * ChartSize), 3);
		var py = Math.Round(chartTop + ((1 - Math.Clamp(point.Y, 0, 1)) * ChartSize), 3);

		_ = sb.Append("\n<g class=\"quadrant-point\">\n  <circle cx=\"").Append(px.SvgFormat()).Append("\" cy=\"").Append(py.SvgFormat())
			.Append("\" r=\"").Append(PointHalo.SvgFormat()).Append("\" fill=\"").Append(ds.Accent.Soft).Append("\" fill-opacity=\"0.7\" />\n  ");
		ds.AppendPoint(sb, px, py, ColorFamily.AccentBase, PointRadius);

		// label right of the point, flipped left when it would run off the chart
		var w = DesignSystem.MeasureRole(point.Label, TypeRole.Label);
		var flip = px + LabelOffset + w > chartLeft + ChartSize;
		_ = sb.Append("\n  ");
		ds.AppendHaloText(sb, point.Label, flip ? px - LabelOffset : px + LabelOffset, py, TypeRole.Label, anchor: flip ? "end" : "start");
		_ = sb.Append("\n</g>");
	}
}
