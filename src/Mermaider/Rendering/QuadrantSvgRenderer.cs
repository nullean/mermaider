using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class QuadrantSvgRenderer
{
	private const double ChartSize = 400;
	private const double Padding = 60;
	private const double TitleHeight = 32;
	private const double AxisLabelPad = 8;
	private const double PointRadius = 6;
	private const double PointLabelFontSize = 11;
	private const double QuadrantLabelFontSize = 14;
	private const double AxisLabelFontSize = 12;

	private static readonly string[] QuadrantFills =
	[
		"color-mix(in srgb, var(--accent, var(--fg)) 12%, var(--bg))",
		"color-mix(in srgb, var(--accent, var(--fg)) 8%, var(--bg))",
		"color-mix(in srgb, var(--accent, var(--fg)) 4%, var(--bg))",
		"color-mix(in srgb, var(--accent, var(--fg)) 6%, var(--bg))",
	];

	internal static string Render(QuadrantChart chart, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
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

	internal static StringBuilder RenderToBuilder(QuadrantChart chart, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		var hasTitle = chart.Title is { Length: > 0 };
		var titleOffset = hasTitle ? TitleHeight : 0;
		var hasPoints = chart.Points.Count > 0;

		var axisBottomPad = (chart.XAxisLeft ?? chart.XAxisRight) is not null ? 28.0 : 0;
		var axisLeftPad = (chart.YAxisBottom ?? chart.YAxisTop) is not null ? 20.0 : 0;

		var totalWidth = Padding + axisLeftPad + ChartSize + Padding;
		var totalHeight = titleOffset + Padding + ChartSize + axisBottomPad + Padding;

		var chartLeft = Padding + axisLeftPad;
		var chartTop = titleOffset + Padding;
		var half = ChartSize / 2;

		StyleBlock.AppendSvgOpenTag(sb, totalWidth, totalHeight, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict);
		_ = sb.Append("\n<defs>\n</defs>\n");

		if (hasTitle)
		{
			_ = sb.Append("\n<text x=\"").Append(F(chartLeft + half))
				.Append("\" y=\"").Append(F(titleOffset))
				.Append("\" text-anchor=\"middle\" font-size=\"16\" font-weight=\"700\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, chart.Title!.AsSpan());
			_ = sb.Append("</text>");
		}

		AppendQuadrant(sb, chartLeft, chartTop, half, half, QuadrantFills[1], chart.Quadrant2, hasPoints);
		AppendQuadrant(sb, chartLeft + half, chartTop, half, half, QuadrantFills[0], chart.Quadrant1, hasPoints);
		AppendQuadrant(sb, chartLeft, chartTop + half, half, half, QuadrantFills[2], chart.Quadrant3, hasPoints);
		AppendQuadrant(sb, chartLeft + half, chartTop + half, half, half, QuadrantFills[3], chart.Quadrant4, hasPoints);

		_ = sb.Append("\n<rect x=\"").Append(F(chartLeft)).Append("\" y=\"").Append(F(chartTop))
			.Append("\" width=\"").Append(F(ChartSize)).Append("\" height=\"").Append(F(ChartSize))
			.Append("\" fill=\"none\" stroke=\"var(--_node-stroke)\" stroke-width=\"1.5\" />");

		_ = sb.Append("\n<line x1=\"").Append(F(chartLeft + half)).Append("\" y1=\"").Append(F(chartTop))
			.Append("\" x2=\"").Append(F(chartLeft + half)).Append("\" y2=\"").Append(F(chartTop + ChartSize))
			.Append("\" stroke=\"var(--_node-stroke)\" stroke-width=\"1\" stroke-dasharray=\"4 3\" />");
		_ = sb.Append("\n<line x1=\"").Append(F(chartLeft)).Append("\" y1=\"").Append(F(chartTop + half))
			.Append("\" x2=\"").Append(F(chartLeft + ChartSize)).Append("\" y2=\"").Append(F(chartTop + half))
			.Append("\" stroke=\"var(--_node-stroke)\" stroke-width=\"1\" stroke-dasharray=\"4 3\" />");

		AppendAxisLabels(sb, chart, chartLeft, chartTop, hasPoints, axisBottomPad);

		foreach (var point in chart.Points)
			AppendPoint(sb, point, chartLeft, chartTop);

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static void AppendQuadrant(StringBuilder sb, double x, double y, double w, double h, string fill, string? label, bool hasPoints)
	{
		_ = sb.Append("\n<rect x=\"").Append(F(x)).Append("\" y=\"").Append(F(y))
			.Append("\" width=\"").Append(F(w)).Append("\" height=\"").Append(F(h))
			.Append("\" fill=\"").Append(fill).Append("\" />");

		if (label is { Length: > 0 })
		{
			var textY = hasPoints ? y + 20 : y + (h / 2);
			_ = sb.Append("\n<text x=\"").Append(F(x + (w / 2))).Append("\" y=\"").Append(F(textY))
				.Append("\" text-anchor=\"middle\" dy=\"0.35em\" font-size=\"")
				.Append(QuadrantLabelFontSize).Append("\" font-weight=\"600\" fill=\"var(--_text-sec)\">");
			MultilineUtils.AppendEscapedXml(sb, label.AsSpan());
			_ = sb.Append("</text>");
		}
	}

	private static void AppendAxisLabels(StringBuilder sb, QuadrantChart chart, double chartLeft, double chartTop, bool hasPoints, double axisBottomPad)
	{
		var bottom = chartTop + ChartSize;
		var half = ChartSize / 2;

		if (hasPoints && axisBottomPad > 0)
		{
			if (chart.XAxisLeft is { Length: > 0 })
			{
				_ = sb.Append("\n<text x=\"").Append(F(chartLeft))
					.Append("\" y=\"").Append(F(bottom + AxisLabelPad + 16))
					.Append("\" text-anchor=\"start\" font-size=\"").Append(AxisLabelFontSize)
					.Append("\" fill=\"var(--_text-sec)\">");
				MultilineUtils.AppendEscapedXml(sb, chart.XAxisLeft.AsSpan());
				_ = sb.Append("</text>");
			}
			if (chart.XAxisRight is { Length: > 0 })
			{
				_ = sb.Append("\n<text x=\"").Append(F(chartLeft + ChartSize))
					.Append("\" y=\"").Append(F(bottom + AxisLabelPad + 16))
					.Append("\" text-anchor=\"end\" font-size=\"").Append(AxisLabelFontSize)
					.Append("\" fill=\"var(--_text-sec)\">");
				MultilineUtils.AppendEscapedXml(sb, chart.XAxisRight.AsSpan());
				_ = sb.Append("</text>");
			}
		}
		else if (axisBottomPad > 0)
		{
			if (chart.XAxisLeft is { Length: > 0 })
			{
				_ = sb.Append("\n<text x=\"").Append(F(chartLeft + (half / 2)))
					.Append("\" y=\"").Append(F(bottom + AxisLabelPad + 16))
					.Append("\" text-anchor=\"middle\" font-size=\"").Append(AxisLabelFontSize)
					.Append("\" fill=\"var(--_text-sec)\">");
				MultilineUtils.AppendEscapedXml(sb, chart.XAxisLeft.AsSpan());
				_ = sb.Append("</text>");
			}
			if (chart.XAxisRight is { Length: > 0 })
			{
				_ = sb.Append("\n<text x=\"").Append(F(chartLeft + half + (half / 2)))
					.Append("\" y=\"").Append(F(bottom + AxisLabelPad + 16))
					.Append("\" text-anchor=\"middle\" font-size=\"").Append(AxisLabelFontSize)
					.Append("\" fill=\"var(--_text-sec)\">");
				MultilineUtils.AppendEscapedXml(sb, chart.XAxisRight.AsSpan());
				_ = sb.Append("</text>");
			}
		}

		if (chart.YAxisBottom is { Length: > 0 })
		{
			var yPos = hasPoints ? bottom : chartTop + half + (half / 2);
			_ = sb.Append("\n<text x=\"").Append(F(chartLeft - AxisLabelPad))
				.Append("\" y=\"").Append(F(yPos))
				.Append("\" text-anchor=\"end\" font-size=\"").Append(AxisLabelFontSize)
				.Append("\" fill=\"var(--_text-sec)\" transform=\"rotate(-90, ")
				.Append(F(chartLeft - AxisLabelPad)).Append(", ").Append(F(yPos)).Append(")\">");
			MultilineUtils.AppendEscapedXml(sb, chart.YAxisBottom.AsSpan());
			_ = sb.Append("</text>");
		}

		if (chart.YAxisTop is { Length: > 0 })
		{
			var yPos = hasPoints ? chartTop : chartTop + (half / 2);
			_ = sb.Append("\n<text x=\"").Append(F(chartLeft - AxisLabelPad))
				.Append("\" y=\"").Append(F(yPos))
				.Append("\" text-anchor=\"end\" font-size=\"").Append(AxisLabelFontSize)
				.Append("\" fill=\"var(--_text-sec)\" transform=\"rotate(-90, ")
				.Append(F(chartLeft - AxisLabelPad)).Append(", ").Append(F(yPos)).Append(")\">");
			MultilineUtils.AppendEscapedXml(sb, chart.YAxisTop.AsSpan());
			_ = sb.Append("</text>");
		}
	}

	private static void AppendPoint(StringBuilder sb, QuadrantPoint point, double chartLeft, double chartTop)
	{
		var px = chartLeft + (point.X * ChartSize);
		var py = chartTop + ((1 - point.Y) * ChartSize);

		_ = sb.Append("\n<circle cx=\"").Append(F(px)).Append("\" cy=\"").Append(F(py))
			.Append("\" r=\"").Append(PointRadius)
			.Append("\" fill=\"var(--_arrow)\" stroke=\"var(--bg)\" stroke-width=\"1.5\" />");

		_ = sb.Append("\n<text x=\"").Append(F(px)).Append("\" y=\"").Append(F(py + PointRadius + 12))
			.Append("\" text-anchor=\"middle\" font-size=\"").Append(PointLabelFontSize)
			.Append("\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, point.Label.AsSpan());
		_ = sb.Append("</text>");
	}

	private static string F(double value) =>
		value.ToString("0.##", CultureInfo.InvariantCulture);
}
