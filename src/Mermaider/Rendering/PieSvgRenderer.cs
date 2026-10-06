using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Pie: solid series slices with page-coloured separators (Quiet), area tint + series outline wireframe (Blueprint) or a
/// donut with a centre figure (Tonal). Percent labels sit outside the pie on leader lines; the legend carries the values.
/// </summary>
internal static class PieSvgRenderer
{
	private const double Radius = 128;

	/// <summary>Donut hole as a share of the radius (Tonal).</summary>
	private const double DonutHole = 0.58;

	private const double LeaderFrom = Radius + 3;
	private const double LeaderTo = Radius + 14;
	private const double LabelAt = Radius + 26;

	/// <summary>Horizontal room kept on each side of the pie for outside labels.</summary>
	private const double LabelRoom = 64;

	private const double LegendGap = 48;
	private const double LegendRow = DesignSystem.RowHeight;
	private const double LabelLineHeight = 16;

	/// <summary>Slices below this share get no outside label (the legend still lists them).</summary>
	private const double MinLabelPercent = 1;

	internal static string Render(PieChart chart, SvgRenderContext context)
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

	internal static StringBuilder RenderToBuilder(PieChart chart, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var ds = DesignSystem.For(context);

		var total = 0.0;
		foreach (var slice in chart.Slices)
			total += slice.Value;

		var hasTitle = chart.Title is { Length: > 0 };
		var top = DesignSystem.ChartTop(hasTitle);
		var cx = DesignSystem.ChartPad + LabelRoom + Radius;
		var cy = top + LabelLineHeight + 8 + Radius;

		// legend: value is the share (and the raw value with showData)
		var legend = new List<(string Label, string Value)>(chart.Slices.Count);
		var legendW = 0.0;
		foreach (var slice in chart.Slices)
		{
			var value = total > 0 ? FormatPercent(slice.Value / total * 100) : "";
			if (chart.ShowData)
				value = FormatValue(slice.Value) + " · " + value;
			legend.Add((slice.Label, value));
			legendW = Math.Max(legendW, ds.LegendItemWidth(slice.Label, value));
		}

		var legendX = cx + Radius + LabelRoom + LegendGap;
		var legendH = chart.Slices.Count * LegendRow;
		var legendTop = Math.Max(top, cy - (legendH / 2));
		var width = legendX + legendW + DesignSystem.ChartPad;
		var height = Math.Max(cy + Radius + LabelLineHeight + 8 + DesignSystem.ChartPad, legendTop + legendH + DesignSystem.ChartPad);

		StyleBlock.AppendSvgOpenTag(sb, width, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles);

		if (hasTitle)
			ds.AppendTitle(sb, DesignSystem.ChartPad, DesignSystem.ChartTitleCy, chart.Title!);

		if (total <= 0 || chart.Slices.Count == 0)
		{
			ds.Close(sb);
			return sb;
		}

		var donut = ds.Spec.ChartMarks == ChartMarkKind.Fat;
		var labels = new List<OutsideLabel>();
		var startAngle = -Math.PI / 2;
		var largest = 0;
		for (var i = 0; i < chart.Slices.Count; i++)
		{
			var fraction = chart.Slices[i].Value / total;
			if (chart.Slices[i].Value > chart.Slices[largest].Value)
				largest = i;
			var sweep = fraction * 2 * Math.PI;
			var family = ds.Series(i);
			_ = sb.Append("\n<g class=\"pie-slice\" data-label=\"");
			MultilineUtils.AppendEscapedAttr(sb, chart.Slices[i].Label);
			_ = sb.Append("\">\n  ");
			AppendSlice(sb, ds, family, cx, cy, startAngle, sweep, donut);
			_ = sb.Append("\n</g>");

			if (fraction * 100 >= MinLabelPercent && chart.Slices.Count > 1)
			{
				var mid = startAngle + (sweep / 2);
				labels.Add(new OutsideLabel(mid, FormatPercent(fraction * 100), Math.Cos(mid) >= 0));
			}

			startAngle += sweep;
		}

		AppendOutsideLabels(sb, ds, labels, cx, cy);

		if (donut)
		{
			var share = chart.Slices[largest].Value / total * 100;
			_ = sb.Append('\n');
			ds.AppendFigure(sb, Math.Round(share).ToString("0", CultureInfo.InvariantCulture) + "%", cx, cy - 8, anchor: "middle");
			_ = sb.Append('\n');
			ds.AppendChartText(sb, chart.Slices[largest].Label, cx, cy + 14, TypeRole.Meta);
		}

		for (var i = 0; i < legend.Count; i++)
		{
			_ = sb.Append('\n');
			_ = ds.AppendLegendItem(sb, legendX, legendTop + (i * LegendRow) + (LegendRow / 2), legend[i].Label, ds.Series(i).Base, legend[i].Value);
		}

		ds.Close(sb);
		return sb;
	}

	private sealed record OutsideLabel(double Angle, string Text, bool Right)
	{
		internal double Y { get; set; }
	}

	private static void AppendSlice(StringBuilder sb, DesignSystem ds, ColorFamily family, double cx, double cy, double start, double sweep, bool donut)
	{
		var full = sweep >= (2 * Math.PI) - 0.0001;
		var inner = donut ? Radius * DonutHole : 0;
		if (full)
		{
			_ = sb.Append("<circle cx=\"").Append(cx.SvgFormat()).Append("\" cy=\"").Append(cy.SvgFormat())
				.Append("\" r=\"").Append((donut ? (Radius + inner) / 2 : Radius).SvgFormat()).Append('"');
			if (donut)
			{
				_ = sb.Append(" fill=\"none\" stroke=\"").Append(family.Base).Append("\" stroke-width=\"")
					.Append((Radius - inner).SvgFormat()).Append("\" />");
				return;
			}

			ds.AppendMarkPaint(sb, family);
			_ = sb.Append(" />");
			return;
		}

		var end = start + sweep;
		var largeArc = sweep > Math.PI ? 1 : 0;
		var (ox1, oy1) = Polar(cx, cy, Radius, start);
		var (ox2, oy2) = Polar(cx, cy, Radius, end);
		_ = sb.Append("<path d=\"");
		if (donut)
		{
			var (ix1, iy1) = Polar(cx, cy, inner, end);
			var (ix2, iy2) = Polar(cx, cy, inner, start);
			_ = sb.Append('M').Append(ox1.SvgFormat()).Append(' ').Append(oy1.SvgFormat())
				.Append(" A ").Append(Radius.SvgFormat()).Append(' ').Append(Radius.SvgFormat()).Append(" 0 ").Append(largeArc).Append(" 1 ")
				.Append(ox2.SvgFormat()).Append(' ').Append(oy2.SvgFormat())
				.Append(" L ").Append(ix1.SvgFormat()).Append(' ').Append(iy1.SvgFormat())
				.Append(" A ").Append(inner.SvgFormat()).Append(' ').Append(inner.SvgFormat()).Append(" 0 ").Append(largeArc).Append(" 0 ")
				.Append(ix2.SvgFormat()).Append(' ').Append(iy2.SvgFormat()).Append(" Z\"");
		}
		else
		{
			_ = sb.Append("M ").Append(cx.SvgFormat()).Append(' ').Append(cy.SvgFormat())
				.Append(" L ").Append(ox1.SvgFormat()).Append(' ').Append(oy1.SvgFormat())
				.Append(" A ").Append(Radius.SvgFormat()).Append(' ').Append(Radius.SvgFormat()).Append(" 0 ").Append(largeArc).Append(" 1 ")
				.Append(ox2.SvgFormat()).Append(' ').Append(oy2.SvgFormat()).Append(" Z\"");
		}

		if (ds.Spec.ChartMarks == ChartMarkKind.Outline)
		{
			ds.AppendMarkPaint(sb, family);
			_ = sb.Append(" stroke-linejoin=\"miter\" />");
			return;
		}

		// solid / donut slices: a radial gradient (lighter towards the centre) when gradients are on
		var fill = ds.Gradient
			? ds.RadialGradient("pie-" + family.Key, cx, cy, Radius, ColorFamily.Mix(family.Base, 72, "var(--bg)"), family.Base, donut ? DonutHole : 0)
			: family.Base;
		if (ds.Spec.OutlineWidth > 0)
		{
			// outlined like every other box: the family stroke at node-outline weight
			_ = sb.Append(" fill=\"").Append(fill).Append("\" stroke=\"").Append(family.Stroke).Append("\" stroke-width=\"")
				.Append(ds.NodeStrokeWidth).Append("\" stroke-linejoin=\"round\" />");
			return;
		}

		// presets without outlines: page-coloured separators between neighbours (rounded on the donut)
		_ = sb.Append(" fill=\"").Append(fill).Append("\" stroke=\"var(--bg)\" stroke-width=\"")
			.Append(donut ? "3" : "2.5").Append("\" stroke-linejoin=\"round\" />");
	}

	/// <summary>Percent labels outside the pie on short leader lines, nudged apart per side so they never overlap.</summary>
	private static void AppendOutsideLabels(StringBuilder sb, DesignSystem ds, List<OutsideLabel> labels, double cx, double cy)
	{
		foreach (var label in labels)
			label.Y = cy + (LabelAt * Math.Sin(label.Angle));

		foreach (var side in new[] { true, false })
		{
			var group = labels.Where(l => l.Right == side).OrderBy(l => l.Y).ToList();
			for (var i = 1; i < group.Count; i++)
			{
				if (group[i].Y - group[i - 1].Y < LabelLineHeight)
					group[i].Y = group[i - 1].Y + LabelLineHeight;
			}

			// if the stack overflowed the bottom, push it back up
			var maxY = cy + LabelAt;
			if (group.Count > 0 && group[^1].Y > maxY)
			{
				group[^1].Y = maxY;
				for (var i = group.Count - 2; i >= 0; i--)
				{
					if (group[i + 1].Y - group[i].Y < LabelLineHeight)
						group[i].Y = group[i + 1].Y - LabelLineHeight;
				}
			}
		}

		foreach (var label in labels)
		{
			var (x1, y1) = Polar(cx, cy, LeaderFrom, label.Angle);
			var (x2, y2) = Polar(cx, cy, LeaderTo, label.Angle);
			var naturalY = cy + (LabelAt * Math.Sin(label.Angle));
			var dy = label.Y - naturalY;
			var nudged = Math.Abs(dy) > 0.5;
			var dir = label.Right ? 1 : -1;
			var textX = cx + (LabelAt * Math.Cos(label.Angle));
			if (nudged)
				textX = label.Right ? Math.Max(textX, x2 + 10) : Math.Min(textX, x2 - 10);

			// a nudged label gets an elbow: leader to the label's row, then a short horizontal run
			_ = sb.Append("\n<path d=\"M ").Append(x1.SvgFormat()).Append(' ').Append(y1.SvgFormat())
				.Append(" L ").Append(x2.SvgFormat()).Append(' ').Append((nudged ? label.Y : y2).SvgFormat());
			if (nudged)
				_ = sb.Append(" H ").Append((textX - (dir * 4)).SvgFormat());
			_ = sb.Append("\" fill=\"none\" stroke=\"").Append(DesignSystem.AxisStroke).Append("\" stroke-width=\"")
				.Append(DesignSystem.AxisWidth.SvgFormat()).Append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\" />");
			_ = sb.Append('\n');
			ds.AppendChartText(sb, label.Text, textX, label.Y, TypeRole.Label, null, anchor: label.Right ? "start" : "end", weight: 600);
		}
	}

	private static (double X, double Y) Polar(double cx, double cy, double r, double angle) =>
		(cx + (r * Math.Cos(angle)), cy + (r * Math.Sin(angle)));

	private static string FormatPercent(double pct) => pct.ToString("0.##", CultureInfo.InvariantCulture) + "%";

	private static string FormatValue(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
