using System.Text;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Chart parts on top of <c>DesignSystem.Charts</c>: the shared chart frame (padding, title band), haloed and rotated
/// text, grid paths, the per-preset bar / ribbon / point knobs and the big figure used by donut centres and treemap tiles.
/// Geometry here is preset-independent; only paint varies.
/// </summary>
internal sealed partial class DesignSystem
{
	/// <summary>Outer padding of every chart.</summary>
	internal const double ChartPad = 40;

	/// <summary>Vertical centre of a chart title.</summary>
	internal const double ChartTitleCy = 40;

	/// <summary>Where chart content starts when the chart has a title (title band + gap).</summary>
	internal const double ChartTitledTop = 88;

	/// <summary>Top of the chart content for a titled / untitled chart.</summary>
	internal static double ChartTop(bool hasTitle) => hasTitle ? ChartTitledTop : ChartPad;

	/// <summary>Opacity of sankey ribbons: .4 solid, .22 outline, .55 fat.</summary>
	internal double RibbonOpacity => Spec.ChartMarks switch
	{
		ChartMarkKind.Outline => 0.22,
		ChartMarkKind.Fat => 0.55,
		_ => 0.4,
	};

	/// <summary>Share of a category slot a bar group fills: fat bars are wide, outline bars narrow.</summary>
	internal double BarSlotRatio => Spec.ChartMarks switch
	{
		ChartMarkKind.Outline => 0.5,
		ChartMarkKind.Fat => 0.74,
		_ => 0.58,
	};

	/// <summary>Corner radius at the free end of a bar.</summary>
	internal double BarRadius => Spec.ChartMarks switch
	{
		ChartMarkKind.Outline => 0,
		ChartMarkKind.Fat => 12,
		_ => 5,
	};

	/// <summary>Outline width of an outline-preset mark (bar, slice, node bar).</summary>
	internal const double MarkStrokeWidth = 1.25;

	/// <summary>Series lines are smoothed (Tonal) instead of straight polylines.</summary>
	internal bool SmoothLines => Spec.ChartMarks == ChartMarkKind.Fat;

	/// <summary>Data point radius per preset.</summary>
	internal double PointRadius => Spec.ChartMarks == ChartMarkKind.Fat ? 4.5 : 3.5;

	/// <summary>Radius of a chart panel (quadrant cell, treemap tile) derived from the preset's container / node radius.</summary>
	internal double PanelRadius => Math.Max(2, Spec.ContainerRadius - 2);

	internal double TileRadius => Math.Max(2, Spec.NodeRadius - 2);

	/// <summary><see cref="AppendText"/> with coordinates rounded to 3 decimals (chart geometry is trigonometry-heavy).</summary>
	internal void AppendChartText(StringBuilder sb, string text, double x, double cy, TypeRole role, string? color = null, string anchor = "middle", int? weight = null) =>
		AppendText(sb, text, Math.Round(x, 3), Math.Round(cy, 3), role, color, anchor, weight);

	/// <summary>Text with a page-background halo so it stays legible over marks, ribbons and grid.</summary>
	internal void AppendHaloText(StringBuilder sb, string text, double x, double cy, TypeRole role, string? color = null, string anchor = "middle", int? weight = null) =>
		MultilineUtils.AppendMultilineText(sb, text, Math.Round(x, 3), Math.Round(cy, 3), Px(role),
			TextAttributes(role, color, anchor, weight) + HaloAttributes);

	internal const string HaloAttributes = " stroke=\"var(--bg)\" stroke-width=\"4\" stroke-linejoin=\"round\" paint-order=\"stroke\"";

	/// <summary>Text rotated -90° around (<paramref name="x"/>, <paramref name="cy"/>) (vertical axis titles).</summary>
	internal void AppendRotatedText(StringBuilder sb, string text, double x, double cy, TypeRole role, string? color = null, string anchor = "middle")
	{
		_ = sb.Append("<g transform=\"translate(").Append(Num(x)).Append(' ').Append(Num(cy)).Append(") rotate(-90)\">");
		AppendText(sb, text, 0, 0, role, color, anchor);
		_ = sb.Append("</g>");
	}

	/// <summary>
	/// A big figure (treemap value, donut centre): the title tier at 700, or 500 mono under Blueprint (where the xs tier is
	/// mono, figures read as instrument readouts).
	/// </summary>
	internal void AppendFigure(StringBuilder sb, string text, double x, double cy, TypeRole role = TypeRole.Title, string anchor = "start")
	{
		var mono = Spec.MonoXs;
		var attrs = TextAttributes(role, null, anchor, mono ? 500 : 700);
		if (mono)
			attrs = "class=\"mono\" " + attrs;
		MultilineUtils.AppendMultilineText(sb, text, Math.Round(x, 3), Math.Round(cy, 3), Px(role), attrs);
	}

	/// <summary>A grid path (graticule ring / polygon) in the grid stroke; dotted under Blueprint.</summary>
	internal void AppendGridPath(StringBuilder sb, string d)
	{
		_ = sb.Append("<path d=\"").Append(d).Append("\" fill=\"none\" stroke=\"").Append(GridStroke)
			.Append("\" stroke-width=\"").Append(Num(GridWidth)).Append("\" stroke-linejoin=\"round\"");
		if (Spec.Style == Models.DiagramStyle.Blueprint)
			_ = sb.Append(" stroke-dasharray=\"").Append(DotArray).Append("\" stroke-linecap=\"round\"");
		_ = sb.Append(" />");
	}

	/// <summary>A grid circle (radar graticule).</summary>
	internal void AppendGridCircle(StringBuilder sb, double cx, double cy, double r)
	{
		_ = sb.Append("<circle cx=\"").Append(Num(cx)).Append("\" cy=\"").Append(Num(cy)).Append("\" r=\"").Append(Num(r))
			.Append("\" fill=\"none\" stroke=\"").Append(GridStroke).Append("\" stroke-width=\"").Append(Num(GridWidth)).Append('"');
		if (Spec.Style == Models.DiagramStyle.Blueprint)
			_ = sb.Append(" stroke-dasharray=\"").Append(DotArray).Append("\" stroke-linecap=\"round\"");
		_ = sb.Append(" />");
	}

	/// <summary>Fill / stroke attributes of a solid mark (bar, slice, node bar) in a series family, per the preset.</summary>
	internal void AppendMarkPaint(StringBuilder sb, ColorFamily series, string? fill = null)
	{
		_ = sb.Append(" fill=\"").Append(fill ?? MarkFill(series)).Append('"');
		if (Spec.ChartMarks == ChartMarkKind.Outline)
			_ = sb.Append(" stroke=\"").Append(series.Base).Append("\" stroke-width=\"").Append(Num(MarkStrokeWidth)).Append('"');
	}

	/// <summary>Width of a legend row (for laying out legends before drawing them).</summary>
	internal double LegendItemWidth(string label, string? value = null)
	{
		var width = SwatchSize + 8 + TextMetrics.MeasureTextWidth(label, Px(TypeRole.Label), Weight(TypeRole.Label));
		if (value is not null)
			width += 8 + TextMetrics.MeasureTextWidth(value, Px(TypeRole.Meta), 400);
		return width;
	}

	/// <summary>Measured width of a text in a role (layout-time, preset-independent weights are close enough).</summary>
	internal static double MeasureRole(string text, TypeRole role, int weight = 600) =>
		TextMetrics.MeasureMultiline(text.AsSpan(), Px(role), weight).Width;
}
