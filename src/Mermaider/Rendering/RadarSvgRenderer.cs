using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class RadarSvgRenderer
{
	private const double Radius = 160;
	private const double CenterX = 220;
	private const double LabelPad = 20;
	private const double LegendSwatchSize = 12;
	private const double LegendRowHeight = 20;
	private const string TitleFontSize = RenderConstants.FsVar.L;
	private const string AxisLabelFontSize = RenderConstants.FsVar.S;
	private const string LegendFontSize = RenderConstants.FsVar.S;
	private const double CurveOpacity = 0.25;


	internal static string Render(RadarChart chart, DiagramColors colors, string font, string? monoFont = null, bool transparent = false, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
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

	internal static StringBuilder RenderToBuilder(RadarChart chart, DiagramColors colors, string font, string? monoFont = null, bool transparent = false, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		if (chart.Axes.Count == 0)
		{
			StyleBlock.AppendSvgOpenTag(sb, 200, 100, colors, transparent, accessibility, diagramType);
			StyleBlock.AppendStyleBlock(sb, font, strict, monoFont: monoFont);
			_ = sb.Append("\n</svg>");
			return sb;
		}

		var hasTitle = chart.Title is { Length: > 0 };
		var titleOffset = hasTitle ? 36.0 : 0;
		var centerY = titleOffset + 20 + Radius;
		var legendWidth = chart.ShowLegend && chart.Curves.Count > 0 ? 160.0 : 0;
		var width = CenterX + Radius + LabelPad + 60 + legendWidth;
		var height = centerY + Radius + LabelPad + 30;

		StyleBlock.AppendSvgOpenTag(sb, width, height, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict, monoFont: monoFont);
		_ = sb.Append("\n<defs>\n</defs>\n");

		if (hasTitle)
		{
			_ = sb.Append("\n<text x=\"").Append(CenterX.SvgFormat())
				.Append("\" y=\"28\" text-anchor=\"middle\" font-size=\"")
				.Append(TitleFontSize).Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, chart.Title.AsSpan());
			_ = sb.Append("</text>");
		}

		var n = chart.Axes.Count;
		AppendGraticule(sb, chart, n, centerY);
		AppendAxisLines(sb, chart, n, centerY);

		for (var ci = 0; ci < chart.Curves.Count; ci++)
		{
			var curve = chart.Curves[ci];
			var color = CategoricalPalette.At(ci);
			AppendCurve(sb, chart, curve, n, centerY, color);
		}

		if (chart.ShowLegend && chart.Curves.Count > 0)
			AppendLegend(sb, chart, titleOffset + 30);

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static void AppendGraticule(StringBuilder sb, RadarChart chart, int n, double centerY)
	{
		for (var t = 1; t <= chart.Ticks; t++)
		{
			var r = Radius * t / chart.Ticks;

			if (chart.Graticule == RadarGraticule.Circle)
			{
				_ = sb.Append("\n<circle cx=\"").Append(CenterX.SvgFormat()).Append("\" cy=\"").Append(centerY.SvgFormat())
					.Append("\" r=\"").Append(r.SvgFormat())
					.Append("\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"0.5\" opacity=\"0.5\" />");
			}
			else
			{
				_ = sb.Append("\n<polygon points=\"");
				for (var i = 0; i < n; i++)
				{
					var angle = (2 * Math.PI * i / n) - (Math.PI / 2);
					if (i > 0)
						_ = sb.Append(' ');
					_ = sb.Append((CenterX + (r * Math.Cos(angle))).SvgFormat()).Append(',').Append((centerY + (r * Math.Sin(angle))).SvgFormat());
				}
				_ = sb.Append("\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"0.5\" opacity=\"0.5\" />");
			}
		}
	}

	private static void AppendAxisLines(StringBuilder sb, RadarChart chart, int n, double centerY)
	{
		for (var i = 0; i < n; i++)
		{
			var angle = (2 * Math.PI * i / n) - (Math.PI / 2);
			var tipX = CenterX + (Radius * Math.Cos(angle));
			var tipY = centerY + (Radius * Math.Sin(angle));

			_ = sb.Append("\n<line x1=\"").Append(CenterX.SvgFormat()).Append("\" y1=\"").Append(centerY.SvgFormat())
				.Append("\" x2=\"").Append(tipX.SvgFormat()).Append("\" y2=\"").Append(tipY.SvgFormat())
				.Append("\" stroke=\"var(--_line)\" stroke-width=\"0.5\" opacity=\"0.5\" />");

			var labelR = Radius + LabelPad;
			var lx = CenterX + (labelR * Math.Cos(angle));
			var ly = centerY + (labelR * Math.Sin(angle));
			var anchor = Math.Abs(Math.Cos(angle)) < 0.1 ? "middle"
				: Math.Cos(angle) > 0 ? "start"
				: "end";

			_ = sb.Append("\n<text x=\"").Append(lx.SvgFormat()).Append("\" y=\"").Append(ly.SvgFormat())
				.Append("\" text-anchor=\"").Append(anchor)
				.Append("\" dy=\"0.35em\" font-size=\"").Append(AxisLabelFontSize)
				.Append("\" fill=\"var(--_text-sec)\">");
			MultilineUtils.AppendEscapedXml(sb, chart.Axes[i].Label.AsSpan());
			_ = sb.Append("</text>");
		}
	}

	private static void AppendCurve(StringBuilder sb, RadarChart chart, RadarCurve curve, int n, double centerY, string color)
	{
		var range = chart.Max - chart.Min;
		if (range <= 0)
			return;

		_ = sb.Append("\n<polygon points=\"");
		for (var i = 0; i < n; i++)
		{
			var angle = (2 * Math.PI * i / n) - (Math.PI / 2);
			var val = i < curve.Values.Count ? curve.Values[i] : 0;
			var normalized = Math.Clamp((val - chart.Min) / range, 0, 1);
			var r = Radius * normalized;

			if (i > 0)
				_ = sb.Append(' ');
			_ = sb.Append((CenterX + (r * Math.Cos(angle))).SvgFormat()).Append(',').Append((centerY + (r * Math.Sin(angle))).SvgFormat());
		}
		_ = sb.Append("\" fill=\"").Append(color).Append("\" fill-opacity=\"").Append(CurveOpacity.SvgFormat())
			.Append("\" stroke=\"").Append(color).Append("\" stroke-width=\"2\" />");

		for (var i = 0; i < n; i++)
		{
			var angle = (2 * Math.PI * i / n) - (Math.PI / 2);
			var val = i < curve.Values.Count ? curve.Values[i] : 0;
			var normalized = Math.Clamp((val - chart.Min) / range, 0, 1);
			var r = Radius * normalized;
			var px = CenterX + (r * Math.Cos(angle));
			var py = centerY + (r * Math.Sin(angle));

			_ = sb.Append("\n<circle cx=\"").Append(px.SvgFormat()).Append("\" cy=\"").Append(py.SvgFormat())
				.Append("\" r=\"3\" fill=\"").Append(color).Append("\" />");
		}
	}

	private static void AppendLegend(StringBuilder sb, RadarChart chart, double legendTop)
	{
		var legendX = CenterX + Radius + LabelPad + 50;
		for (var i = 0; i < chart.Curves.Count; i++)
		{
			var curve = chart.Curves[i];
			var color = CategoricalPalette.At(i);
			var y = legendTop + (i * LegendRowHeight);

			_ = sb.Append("\n<rect x=\"").Append(legendX.SvgFormat()).Append("\" y=\"").Append(y.SvgFormat())
				.Append("\" width=\"").Append(LegendSwatchSize).Append("\" height=\"").Append(LegendSwatchSize)
				.Append("\" rx=\"2\" ry=\"2\" fill=\"").Append(color).Append("\" />");

			_ = sb.Append("\n<text x=\"").Append((legendX + LegendSwatchSize + 6).SvgFormat()).Append("\" y=\"").Append((y + (LegendSwatchSize / 2)).SvgFormat())
				.Append("\" dy=\"0.35em\" font-size=\"").Append(LegendFontSize)
				.Append("\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, curve.Label.AsSpan());
			_ = sb.Append("</text>");
		}
	}

}
