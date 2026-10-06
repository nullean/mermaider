using System.Text;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Parts shared by the time and tree diagrams (gantt, gitgraph, packet, treeview): state swatches with an accent
/// ring, the accent "look here" ring, the neutral plot panel and the rounded-orthogonal connector.
/// </summary>
internal sealed partial class DesignSystem
{
	/// <summary>Width of the accent ring around an active / highlighted mark.</summary>
	internal const double RingWidth = 1.5;

	/// <summary>Gap between a mark and its accent ring.</summary>
	internal const double RingGap = 3;

	/// <summary>Corner radius of a bar (gantt task, packet cell) of height <paramref name="height"/>: pills in Tonal, square in Blueprint.</summary>
	internal double TaskBarRadius(double height) => Math.Min(height / 2, Spec.NodeRadius * 0.75);

	/// <summary>An accent ring (no fill) drawn <see cref="RingGap"/> outside the rectangle (x, y, w, h) with corner radius <paramref name="r"/>.</summary>
	internal static void AppendAccentRing(StringBuilder sb, double x, double y, double w, double h, double r)
	{
		var rr = r > 0 ? r + RingGap - 1 : 0;
		_ = sb.Append("<rect x=\"").Append(Num(x - RingGap)).Append("\" y=\"").Append(Num(y - RingGap))
			.Append("\" width=\"").Append(Num(w + (RingGap * 2))).Append("\" height=\"").Append(Num(h + (RingGap * 2)))
			.Append("\" rx=\"").Append(Num(rr)).Append("\" ry=\"").Append(Num(rr))
			.Append("\" fill=\"none\" stroke=\"").Append(ColorFamily.AccentBase)
			.Append("\" stroke-width=\"").Append(Num(RingWidth)).Append("\" />");
	}

	/// <summary>A rectangle with explicit fill / stroke (state bars, cells); <paramref name="stroke"/> <c>none</c> draws no outline.</summary>
	internal static void AppendRect(StringBuilder sb, double x, double y, double w, double h, double r, string fill, string stroke, double strokeWidth = 1.25)
	{
		_ = sb.Append("<rect x=\"").Append(Num(x)).Append("\" y=\"").Append(Num(y))
			.Append("\" width=\"").Append(Num(w)).Append("\" height=\"").Append(Num(h))
			.Append("\" rx=\"").Append(Num(r)).Append("\" ry=\"").Append(Num(r))
			.Append("\" fill=\"").Append(fill).Append('"');
		if (stroke != "none")
			_ = sb.Append(" stroke=\"").Append(stroke).Append("\" stroke-width=\"").Append(Num(strokeWidth)).Append('"');
		_ = sb.Append(" />");
	}

	/// <summary>
	/// A neutral plot / content panel (gantt plot area): <c>--_group-fill</c> with the container radius; the Tab preset draws
	/// a soft hairline outline on the page instead of a fill.
	/// </summary>
	internal void AppendPanel(StringBuilder sb, double x, double y, double w, double h)
	{
		var r = Num(Spec.ContainerRadius);
		var tab = Spec.Container == ContainerKind.Tab;
		_ = sb.Append("<rect class=\"plot-panel\" x=\"").Append(Num(x)).Append("\" y=\"").Append(Num(y))
			.Append("\" width=\"").Append(Num(w)).Append("\" height=\"").Append(Num(h))
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"").Append(tab ? "none" : "var(--_group-fill)")
			.Append("\" stroke=\"").Append(tab ? "var(--_line-soft)" : "none").Append("\" stroke-width=\"1\" />");
	}

	/// <summary>
	/// A state legend row (gantt: done / active / critical / planned): a 12px swatch with its own fill and outline, an
	/// optional accent ring, and a caption. Returns the row's width.
	/// </summary>
	internal double AppendStateLegendItem(StringBuilder sb, double x, double cy, string label, string fill, string stroke, bool ring)
	{
		const double s = 12;
		var r = Math.Min(4, TaskBarRadius(s));
		AppendRect(sb, x, cy - (s / 2), s, s, r, fill, stroke);
		if (ring)
		{
			_ = sb.Append(' ');
			AppendAccentRing(sb, x, cy - (s / 2), s, s, r);
		}

		_ = sb.Append(' ');
		var labelX = x + s + 8;
		AppendText(sb, label, labelX, cy, TypeRole.Caption, null, anchor: "start");
		return s + 8 + StateLegendLabelWidth(label);
	}

	/// <summary>Measured width of a legend caption.</summary>
	internal static double StateLegendLabelWidth(string label) => XsWidth(label, 500);

	/// <summary>
	/// Width of xs-tier text (caption, tag, meta, eyebrow) that is safe in every preset: the wider of the proportional
	/// measure and the mono estimate, since Blueprint sets this tier in mono. Layout stays preset-independent.
	/// </summary>
	internal static double XsWidth(string text, int weight, double letterSpacingPerChar = 0) =>
		Math.Max(TextMetrics.MeasureTextWidth(text, Px(TypeRole.Caption), weight),
			TextMetrics.EstimateMonoTextWidth(text, Px(TypeRole.Caption))) + (text.Length * letterSpacingPerChar);
}
