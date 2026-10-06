using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Treemap: a squarified layout where every section (a node with children) is a shared container in its series family
/// (header carrying the name and the section total) and every leaf is a tile on that family's value ramp
/// (10–36% of the section colour by value / largest value). A tile shows its name, a big numeral and its share of the
/// total; the largest tile carries the accent dot.
/// </summary>
internal static class TreemapSvgRenderer
{
	private const double ChartWidth = 600;
	private const double ChartHeight = 372;

	/// <summary>Gap between sibling sections.</summary>
	private const double SectionGap = 16;

	/// <summary>Gap between sibling tiles.</summary>
	private const double TileGap = 8;

	/// <summary>Where a section's content starts below its top edge (28 strip + 12 inset), identical for every preset.</summary>
	private const double SectionHeader = DesignSystem.StripHeight + DesignSystem.ContainerInset;

	private const double TilePad = 16;

	/// <summary>Value ramp: lightest and darkest tile mix of the section colour.</summary>
	private const double RampFloor = 10;

	private const double RampSpan = 26;

	internal static string Render(TreemapDiagram diagram, SvgRenderContext context)
	{
		var sb = RenderToBuilder(diagram, context);
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

	internal static StringBuilder RenderToBuilder(TreemapDiagram diagram, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var ds = DesignSystem.For(context);
		var pad = DesignSystem.ChartPad;

		StyleBlock.AppendSvgOpenTag(sb, ChartWidth + (pad * 2), ChartHeight + (pad * 2), context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles);

		var roots = diagram.Roots.Where(r => r.ComputedValue > 0).ToList();
		if (roots.Count > 0)
		{
			var total = roots.Sum(r => r.ComputedValue);
			var leaves = new List<TreemapNode>();
			CollectLeaves(roots, leaves, 0, context.Limits);
			var vmax = leaves.Count == 0 ? 1 : leaves.Max(l => l.ComputedValue);
			var hottest = leaves.Count == 0 ? null : leaves.MaxBy(l => l.ComputedValue);
			var anySection = roots.Any(r => r.Children.Count > 0);

			var state = new RenderState(ds, total, vmax, hottest, context.Limits);
			var rects = Squarify(roots, pad, pad, ChartWidth, ChartHeight);
			for (var i = 0; i < roots.Count; i++)
			{
				// sections take their own series colour; a flat treemap is one family read by intensity
				var family = ds.Series(anySection ? i : 0);
				AppendNode(sb, state, roots[i], rects[i], family, 0, anySection ? SectionGap : TileGap);
			}
		}

		ds.Close(sb);
		return sb;
	}

	private sealed record RenderState(DesignSystem Ds, double Total, double VMax, TreemapNode? Hottest, ResourceLimits Limits);

	private readonly record struct Rect(double X, double Y, double W, double H);

	private static void CollectLeaves(IReadOnlyList<TreemapNode> nodes, List<TreemapNode> leaves, int depth, ResourceLimits limits)
	{
		ResourceGuard.CheckRecursionDepth(depth, limits);
		foreach (var node in nodes)
		{
			if (node.Children.Count == 0)
			{
				if (node.ComputedValue > 0)
					leaves.Add(node);
			}
			else
			{
				CollectLeaves(node.Children, leaves, depth + 1, limits);
			}
		}
	}

	private static void AppendNode(StringBuilder sb, RenderState state, TreemapNode node, Rect slot, ColorFamily family, int depth, double gap)
	{
		ResourceGuard.CheckRecursionDepth(depth, state.Limits);
		var r = new Rect(slot.X + (gap / 2), slot.Y + (gap / 2), slot.W - gap, slot.H - gap);
		if (r.W <= 1 || r.H <= 1)
			return;

		if (node.Children.Count == 0)
		{
			AppendTile(sb, state, node, r, family);
			return;
		}

		AppendSection(sb, state, node, r, family, depth);
	}

	private static void AppendSection(StringBuilder sb, RenderState state, TreemapNode node, Rect r, ColorFamily family, int depth)
	{
		var ds = state.Ds;
		var count = FormatValue(node.ComputedValue);
		ds.AppendContainerBody(sb, r.X, r.Y, r.W, r.H, family, depth, "treemap-section",
			"data-label=\"" + Escape(node.Label) + "\"");
		ds.AppendContainerHeader(sb, r.X, r.Y, r.W, node.Label, family, count);

		var inner = new Rect(
			r.X + DesignSystem.ContainerInset - (TileGap / 2),
			r.Y + SectionHeader - (TileGap / 2),
			r.W - (DesignSystem.ContainerInset * 2) + TileGap,
			r.H - SectionHeader - DesignSystem.ContainerInset + TileGap);
		if (inner.W <= TileGap || inner.H <= TileGap)
			return;

		var children = node.Children.Where(c => c.ComputedValue > 0).ToList();
		var rects = Squarify(children, inner.X, inner.Y, inner.W, inner.H);
		for (var i = 0; i < children.Count; i++)
			AppendNode(sb, state, children[i], rects[i], family, depth + 1, TileGap);
	}

	private static void AppendTile(StringBuilder sb, RenderState state, TreemapNode node, Rect r, ColorFamily family)
	{
		var ds = state.Ds;
		var value = node.ComputedValue;
		var ramp = (RampFloor + (RampSpan * value / state.VMax)) * ds.TintStrength;
		var outline = ds.Spec.ChartMarks == ChartMarkKind.Outline;
		// Blueprint keeps the ramp but at a third of the strength, outlined in the family edge
		var fill = family.Mix(outline ? ramp / 3 : ramp);
		var rr = DesignSystem.Num(Math.Min(ds.TileRadius, Math.Min(r.W, r.H) / 2));

		_ = sb.Append("\n<g class=\"treemap-tile\" data-label=\"").Append(Escape(node.Label)).Append("\" data-value=\"")
			.Append(FormatValue(value)).Append("\">\n  <rect x=\"").Append(r.X.SvgFormat()).Append("\" y=\"").Append(r.Y.SvgFormat())
			.Append("\" width=\"").Append(r.W.SvgFormat()).Append("\" height=\"").Append(r.H.SvgFormat())
			.Append("\" rx=\"").Append(rr).Append("\" ry=\"").Append(rr)
			.Append("\" fill=\"").Append(fill).Append('"');
		if (outline)
			_ = sb.Append(" stroke=\"").Append(family.Edge).Append("\" stroke-width=\"1\"");
		_ = sb.Append(" />");

		var textW = r.W - (TilePad * 2);
		var nameW = DesignSystem.MeasureRole(node.Label, TypeRole.Subheading);
		var showName = r.H >= 28 && nameW <= textW + 8;
		if (showName)
		{
			_ = sb.Append("\n  ");
			ds.AppendChartText(sb, node.Label, r.X + TilePad, r.Y + 24, TypeRole.Subheading, null, anchor: "start");
		}
		else if (r.H >= 20 && r.W >= 24)
		{
			// tiny tile: no room for the name, keep the value readable
			_ = sb.Append("\n  ");
			ds.AppendChartText(sb, FormatValue(value), r.X + (r.W / 2), r.Y + (r.H / 2), TypeRole.Tag, null);
		}

		var share = FormatPercent(value / state.Total * 100);
		var big = r.H >= 120 && r.W >= 96;
		var figureRole = big ? TypeRole.Title : TypeRole.Heading;
		var figureW = DesignSystem.MeasureRole(FormatValue(value), figureRole, 700);
		var shareW = DesignSystem.MeasureRole(share, TypeRole.Tag);
		if (showName && r.H >= 64 && figureW + shareW + 12 <= textW)
		{
			var baseY = r.Y + r.H - 24;
			_ = sb.Append("\n  ");
			ds.AppendFigure(sb, FormatValue(value), r.X + TilePad, baseY - 2, figureRole);
			_ = sb.Append("\n  ");
			ds.AppendChartText(sb, share, r.X + r.W - TilePad, baseY, TypeRole.Tag, "var(--_text)", anchor: "end");
		}

		if (ReferenceEquals(node, state.Hottest) && showName && r.W >= nameW + (TilePad * 2) + 16)
		{
			_ = sb.Append("\n  <circle cx=\"").Append((r.X + r.W - TilePad).SvgFormat()).Append("\" cy=\"").Append((r.Y + 20).SvgFormat())
				.Append("\" r=\"4.5\" fill=\"").Append(ColorFamily.AccentBase).Append("\" />");
		}

		_ = sb.Append("\n</g>");
	}

	// ====================================================================
	// Squarified layout (Bruls, Huizing, van Wijk)
	// ====================================================================

	/// <summary>Rectangles for <paramref name="nodes"/> (in input order) filling the given area, aspect ratios near 1.</summary>
	private static Rect[] Squarify(IReadOnlyList<TreemapNode> nodes, double x, double y, double w, double h)
	{
		var result = new Rect[nodes.Count];
		var total = nodes.Sum(n => n.ComputedValue);
		if (total <= 0 || w <= 0 || h <= 0)
			return result;

		var order = Enumerable.Range(0, nodes.Count).OrderByDescending(i => nodes[i].ComputedValue).ToList();
		var scale = w * h / total;
		var areas = order.Select(i => nodes[i].ComputedValue * scale).ToList();

		var row = new List<int>();
		var k = 0;
		while (k < order.Count)
		{
			var side = Math.Min(w, h);
			if (side <= 0)
				break;
			var candidate = areas[k];
			if (row.Count == 0 || Worst(row.Select(i => areas[i]).Append(candidate), side) <= Worst(row.Select(i => areas[i]), side))
			{
				row.Add(k);
				k++;
				continue;
			}

			LayoutRow(row, order, areas, result, ref x, ref y, ref w, ref h);
			row.Clear();
		}

		if (row.Count > 0)
			LayoutRow(row, order, areas, result, ref x, ref y, ref w, ref h);
		return result;
	}

	private static double Worst(IEnumerable<double> row, double side)
	{
		var list = row.ToList();
		var sum = list.Sum();
		if (sum <= 0)
			return double.MaxValue;
		var max = list.Max();
		var min = list.Min();
		var s2 = side * side;
		var sum2 = sum * sum;
		return Math.Max(s2 * max / sum2, sum2 / (s2 * Math.Max(min, 1e-9)));
	}

	private static void LayoutRow(List<int> row, List<int> order, List<double> areas, Rect[] result, ref double x, ref double y, ref double w, ref double h)
	{
		var sum = row.Sum(i => areas[i]);
		if (w >= h)
		{
			// column along the left edge
			var colW = sum / h;
			var cy = y;
			foreach (var i in row)
			{
				var ih = areas[i] / colW;
				result[order[i]] = new Rect(x, cy, colW, ih);
				cy += ih;
			}

			x += colW;
			w -= colW;
		}
		else
		{
			// row along the top edge
			var rowH = sum / w;
			var cx = x;
			foreach (var i in row)
			{
				var iw = areas[i] / rowH;
				result[order[i]] = new Rect(cx, y, iw, rowH);
				cx += iw;
			}

			y += rowH;
			h -= rowH;
		}
	}

	private static string Escape(string text)
	{
		var sb = new StringBuilder(text.Length + 8);
		MultilineUtils.AppendEscapedAttr(sb, text);
		return sb.ToString();
	}

	private static string FormatValue(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

	private static string FormatPercent(double pct) => Math.Round(pct).ToString("0", CultureInfo.InvariantCulture) + "%";
}
