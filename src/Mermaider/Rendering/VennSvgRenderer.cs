using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Venn: every set is an overlap-safe region (series area + outline). Set names are headings pushed outward into the
/// part of the circle no other set covers; intersection labels wrap to two lines and sit in their own lens, pushed away
/// from the centre of the arrangement so neighbouring pair labels never collide.
/// </summary>
internal static class VennSvgRenderer
{
	private const double Radius = 130;

	/// <summary>Centre distance of the two-set arrangement, as a share of the radius.</summary>
	private const double TwoSetOffset = 0.55;

	/// <summary>Arrangement radius (centroid → set centre) for three or more sets, as a share of the radius.</summary>
	private const double RingOffset = 0.666;

	/// <summary>How far a set name is pushed outward from its centre, as a share of the radius.</summary>
	private const double SetLabelPush = 0.55;

	/// <summary>How far a pair label is pushed away from the centroid, as a share of the radius.</summary>
	private const double PairLabelPush = 0.2;

	internal static string Render(VennDiagram diagram, SvgRenderContext context)
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

	internal static StringBuilder RenderToBuilder(VennDiagram diagram, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var ds = DesignSystem.For(context);
		var n = diagram.Sets.Count;
		var positions = ComputePositions(n);

		// translate the arrangement so its bounding box starts at the padding
		var minX = positions.Count == 0 ? 0 : positions.Min(p => p.X) - Radius;
		var minY = positions.Count == 0 ? 0 : positions.Min(p => p.Y) - Radius;
		var maxX = positions.Count == 0 ? 200 : positions.Max(p => p.X) + Radius;
		var maxY = positions.Count == 0 ? 100 : positions.Max(p => p.Y) + Radius;
		var dx = DesignSystem.ChartPad - minX;
		var dy = DesignSystem.ChartPad - minY;
		for (var i = 0; i < positions.Count; i++)
			positions[i] = (positions[i].X + dx, positions[i].Y + dy);

		var width = maxX - minX + (DesignSystem.ChartPad * 2);
		var height = maxY - minY + (DesignSystem.ChartPad * 2);

		StyleBlock.AppendSvgOpenTag(sb, width, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles);

		if (n == 0)
		{
			ds.Close(sb);
			return sb;
		}

		var centroid = (X: positions.Average(p => p.X), Y: positions.Average(p => p.Y));
		var setPositions = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);

		for (var i = 0; i < n; i++)
		{
			var set = diagram.Sets[i];
			var (px, py) = positions[i];
			setPositions[set.Id] = (px, py);
			_ = sb.Append("\n<g class=\"venn-set\" data-set=\"");
			MultilineUtils.AppendEscapedAttr(sb, set.Id);
			_ = sb.Append("\">\n  <circle cx=\"").Append(px.SvgFormat()).Append("\" cy=\"").Append(py.SvgFormat())
				.Append("\" r=\"").Append(Radius.SvgFormat()).Append('"');
			ds.AppendAreaAttributes(sb, ds.Series(i));
			_ = sb.Append(" />\n</g>");
		}

		// set names after every region so no circle paints over a name
		for (var i = 0; i < n; i++)
		{
			var (px, py) = positions[i];
			var (ux, uy) = Outward(px, py, centroid, n == 1 ? 0 : SetLabelPush * Radius);
			_ = sb.Append('\n');
			ds.AppendChartText(sb, Wrap(diagram.Sets[i].Label, Radius * 0.9, TypeRole.Heading), ux, uy, TypeRole.Heading);
		}

		foreach (var union in diagram.Unions)
		{
			if (union.Label is not { Length: > 0 })
				continue;

			var members = union.SetIds.Where(setPositions.ContainsKey).Select(id => setPositions[id]).ToList();
			if (members.Count < 2)
				continue;

			var mx = members.Average(p => p.X);
			var my = members.Average(p => p.Y);
			// a partial intersection lens sits on the far side of its pair, away from the full overlap in the middle
			if (members.Count < n)
				(mx, my) = Outward(mx, my, centroid, PairLabelPush * Radius);

			_ = sb.Append('\n');
			ds.AppendChartText(sb, Wrap(union.Label, Radius * 0.55, TypeRole.Label), mx, my, TypeRole.Label);
		}

		ds.Close(sb);
		return sb;
	}

	/// <summary>(<paramref name="x"/>, <paramref name="y"/>) moved <paramref name="distance"/> further away from <paramref name="from"/>.</summary>
	private static (double X, double Y) Outward(double x, double y, (double X, double Y) from, double distance)
	{
		var vx = x - from.X;
		var vy = y - from.Y;
		var len = Math.Sqrt((vx * vx) + (vy * vy));
		return len < 1e-6 ? (x, y) : (x + (vx / len * distance), y + (vy / len * distance));
	}

	/// <summary>Breaks a label wider than <paramref name="maxWidth"/> into two lines at the space nearest its middle.</summary>
	private static string Wrap(string label, double maxWidth, TypeRole role)
	{
		if (label.Contains('\n', StringComparison.Ordinal) || DesignSystem.MeasureRole(label, role) <= maxWidth)
			return label;

		var mid = label.Length / 2;
		var best = -1;
		for (var i = 0; i < label.Length; i++)
		{
			if (label[i] == ' ' && (best < 0 || Math.Abs(i - mid) < Math.Abs(best - mid)))
				best = i;
		}

		return best <= 0 ? label : label[..best] + "\n" + label[(best + 1)..];
	}

	private static List<(double X, double Y)> ComputePositions(int n)
	{
		var positions = new List<(double X, double Y)>(n);
		if (n == 0)
			return positions;

		if (n == 1)
		{
			positions.Add((0, 0));
		}
		else if (n == 2)
		{
			var offset = Radius * TwoSetOffset;
			positions.Add((-offset, 0));
			positions.Add((offset, 0));
		}
		else
		{
			var ring = Radius * (n == 3 ? RingOffset : 0.75);
			for (var i = 0; i < n; i++)
			{
				var angle = (2 * Math.PI * i / n) - (Math.PI / 2);
				positions.Add((ring * Math.Cos(angle), ring * Math.Sin(angle)));
			}
		}

		return positions;
	}
}
