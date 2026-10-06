using System.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Chart parts: one palette rule for every chart. Solid mark = series base; region = series area (20%) with a series
/// outline; grid = <c>--_line-soft</c>; axes = <c>--_line</c>; ticks and units = meta text; labels sit outside marks.
/// </summary>
internal sealed partial class DesignSystem
{
	internal const string GridStroke = "var(--_line-soft)";
	internal const string AxisStroke = "var(--_line)";
	internal const double AxisWidth = 1.25;
	internal const double GridWidth = 1;

	/// <summary>Size of a legend swatch.</summary>
	internal const double SwatchSize = 10;

	/// <summary>Fill of a solid mark (slice, bar, bubble) in a series family, per the preset's chart marks.</summary>
	internal string MarkFill(ColorFamily series) => Spec.ChartMarks == ChartMarkKind.Outline
		? series.Mix(ColorFamily.AreaOpacity * 100)
		: series.Base;

	/// <summary>Outline of a mark: none for solid marks, the series colour for outline marks.</summary>
	internal string MarkStroke(ColorFamily series) => Spec.ChartMarks == ChartMarkKind.Outline ? series.Base : "none";

	/// <summary>Width of a series line (xy line, radar outline).</summary>
	internal double SeriesLineWidth => Spec.ChartMarks switch
	{
		ChartMarkKind.Fat => 3,
		ChartMarkKind.Outline => 1.5,
		_ => 2,
	};

	/// <summary>Appends an overlap-safe region (radar polygon, venn set): series colour at 20% with a series outline.</summary>
	internal void AppendAreaAttributes(StringBuilder sb, ColorFamily series) =>
		sb.Append(" fill=\"").Append(series.Base).Append("\" fill-opacity=\"").Append(Num(ColorFamily.AreaOpacity))
			.Append("\" stroke=\"").Append(series.Base).Append("\" stroke-width=\"").Append(Num(SeriesLineWidth)).Append('"');

	/// <summary>A data point marker: dot (Quiet), square (Blueprint) or disc (Tonal) in the series colour, page-coloured ring.</summary>
	internal void AppendPoint(StringBuilder sb, double cx, double cy, string color, double r = 3.5)
	{
		if (Spec.Terminal == TerminalKind.Square)
		{
			_ = sb.Append("<rect x=\"").Append(cx - r).Append("\" y=\"").Append(cy - r)
				.Append("\" width=\"").Append(r * 2).Append("\" height=\"").Append(r * 2)
				.Append("\" fill=\"var(--bg)\" stroke=\"").Append(color).Append("\" stroke-width=\"1.5\" />");
			return;
		}

		_ = sb.Append("<circle cx=\"").Append(cx).Append("\" cy=\"").Append(cy).Append("\" r=\"").Append(r)
			.Append("\" fill=\"").Append(color).Append("\" stroke=\"var(--bg)\" stroke-width=\"1.5\" />");
	}

	/// <summary>A legend row: swatch, label and optional value (meta). Returns the row's width.</summary>
	internal double AppendLegendItem(StringBuilder sb, double x, double cy, string label, string color, string? value = null)
	{
		var s = SwatchSize;
		var rr = Spec.Terminal switch
		{
			TerminalKind.Square => 0,
			TerminalKind.Disc => s / 2,
			_ => 3,
		};
		_ = sb.Append("<rect x=\"").Append(x).Append("\" y=\"").Append(cy - (s / 2))
			.Append("\" width=\"").Append(s).Append("\" height=\"").Append(s)
			.Append("\" rx=\"").Append(rr).Append("\" ry=\"").Append(rr)
			.Append("\" fill=\"").Append(color);
		// outline marks: the swatch is the series at area opacity with a series outline (fill-opacity, so any colour value
		// — a hex or a family stage that is itself a color-mix — works without nesting color-mix)
		_ = Spec.ChartMarks == ChartMarkKind.Outline
			? sb.Append("\" fill-opacity=\"").Append(Num(ColorFamily.AreaOpacity)).Append("\" stroke=\"").Append(color)
			: sb.Append("\" stroke=\"none");
		_ = sb.Append("\" />");
		_ = sb.Append("\n  ");
		var labelX = x + s + 8;
		AppendText(sb, label, labelX, cy, TypeRole.Label, null, anchor: "start");
		var width = s + 8 + Text.TextMetrics.MeasureTextWidth(label, Px(TypeRole.Label), Weight(TypeRole.Label));
		if (value is not null)
		{
			_ = sb.Append("\n  ");
			AppendText(sb, value, x + width + 8, cy, TypeRole.Meta, null, anchor: "start");
			width += 8 + Text.TextMetrics.MeasureTextWidth(value, Px(TypeRole.Meta), 400);
		}

		return width;
	}

	/// <summary>A gridline.</summary>
	internal void AppendGridLine(StringBuilder sb, double x1, double y1, double x2, double y2)
	{
		_ = sb.Append("<line x1=\"").Append(x1).Append("\" y1=\"").Append(y1)
			.Append("\" x2=\"").Append(x2).Append("\" y2=\"").Append(y2)
			.Append("\" stroke=\"").Append(GridStroke).Append("\" stroke-width=\"").Append(GridWidth).Append('"');
		if (Spec.Style == Models.DiagramStyle.Blueprint)
			_ = sb.Append(" stroke-dasharray=\"").Append(DotArray).Append("\" stroke-linecap=\"round\"");
		_ = sb.Append(" />");
	}

	/// <summary>An axis line.</summary>
	internal static void AppendAxisLine(StringBuilder sb, double x1, double y1, double x2, double y2) =>
		sb.Append("<line x1=\"").Append(x1).Append("\" y1=\"").Append(y1)
			.Append("\" x2=\"").Append(x2).Append("\" y2=\"").Append(y2)
			.Append("\" stroke=\"").Append(AxisStroke).Append("\" stroke-width=\"").Append(AxisWidth).Append("\" />");
}
