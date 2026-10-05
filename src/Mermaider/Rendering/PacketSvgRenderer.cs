using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Packet diagram on the design system: each field is a cell on the node recipe (<see cref="DesignSystem.AppendBox"/>) in
/// alternating cluster families, the field name in the label role, start / end bit numbers as meta text above the cell.
/// </summary>
internal static class PacketSvgRenderer
{
	private const int BitsPerRow = 32;
	// Max rows from PacketDiagram.MaxBitIndex (bits 0..4095 → 128 rows of 32).
	private const int MaxRows = (PacketDiagram.MaxBitIndex / BitsPerRow) + 1;
	private const double Pad = 24;
	private const double TitleCy = Pad + 14;
	private const double TitleSpace = 48;
	private const double BitWidth = 30;
	private const double CellHeight = 40;
	private const double CellGap = 2;
	private const double BitLabelBand = 18;
	private const double RowGap = 10;

	private readonly record struct Segment(int Start, int End, string Label, int ColorIndex);

	internal static string Render(PacketDiagram diagram, SvgRenderContext context)
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

	internal static StringBuilder RenderToBuilder(PacketDiagram diagram, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var ds = DesignSystem.For(context);

		var hasTitle = diagram.Title is { Length: > 0 };
		var top = hasTitle ? Pad + TitleSpace : Pad;

		var rows = BuildRows(diagram.Fields);
		var rowCount = Math.Max(rows.Count, 1);
		var rowPitch = BitLabelBand + CellHeight + RowGap;

		var width = (Pad * 2) + (BitsPerRow * BitWidth);
		if (hasTitle)
			width = Math.Max(width, (Pad * 2) + DesignSystem.TitleIndent + TextMetrics.MeasureTextWidth(diagram.Title!, DesignSystem.Px(TypeRole.Title), 700));
		var height = top + (rowPitch * rowCount) - RowGap + Pad;

		StyleBlock.AppendSvgOpenTag(sb, width, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles);

		if (hasTitle)
			ds.AppendTitle(sb, Pad, TitleCy, diagram.Title!);

		for (var row = 0; row < rows.Count; row++)
		{
			var cellY = top + (row * rowPitch) + BitLabelBand;
			foreach (var seg in rows[row])
				AppendSegment(sb, ds, seg, cellY);
		}

		ds.Close(sb);
		return sb;
	}

	private static List<List<Segment>> BuildRows(IReadOnlyList<PacketField> fields)
	{
		var rows = new List<List<Segment>>();
		if (fields.Count == 0)
			return rows;

		for (var i = 0; i < fields.Count; i++)
		{
			var field = fields[i];
			var start = field.Start;
			var end = field.End;
			// Guard: skip inverted ranges or bits past the hard cap (parser should already reject).
			if (end < start || start < 0 || start > PacketDiagram.MaxBitIndex || end > PacketDiagram.MaxBitIndex)
				continue;

			// Split field across 32-bit row boundaries (mermaid parity).
			while (start <= end)
			{
				var rowIndex = start / BitsPerRow;
				if (rowIndex >= MaxRows)
					break;

				while (rows.Count <= rowIndex)
					rows.Add([]);

				var rowEndBit = ((rowIndex + 1) * BitsPerRow) - 1;
				var segEnd = Math.Min(end, rowEndBit);
				rows[rowIndex].Add(new Segment(start, segEnd, field.Label, i));

				// Avoid int overflow when segEnd == int.MaxValue (defense in depth).
				if (segEnd >= end || segEnd == int.MaxValue)
					break;
				start = segEnd + 1;
			}
		}

		return rows;
	}

	private static void AppendSegment(StringBuilder sb, DesignSystem ds, Segment seg, double cellY)
	{
		var col = seg.Start % BitsPerRow;
		var bitCount = seg.End - seg.Start + 1;
		var x = Pad + (col * BitWidth) + (CellGap / 2);
		var w = Math.Max(1, (bitCount * BitWidth) - CellGap);
		var family = ds.Cluster(seg.ColorIndex);

		_ = sb.Append("\n<g class=\"node packet-field\" data-start=\"").Append(seg.Start.ToString(CultureInfo.InvariantCulture))
			.Append("\" data-end=\"").Append(seg.End.ToString(CultureInfo.InvariantCulture)).Append("\">\n  ");
		ds.AppendBox(sb, x, cellY, w, CellHeight, family, Math.Min(ds.Spec.NodeRadius, 10));
		_ = sb.Append("\n  ");
		ds.AppendText(sb, seg.Label, x + (w / 2), cellY + (CellHeight / 2), TypeRole.Label);

		// bit numbers above the cell: start left, end right (one centred number for a single bit)
		var bitY = cellY - (BitLabelBand / 2);
		var isSingle = seg.Start == seg.End;
		_ = sb.Append("\n  ");
		ds.AppendText(sb, seg.Start.ToString(CultureInfo.InvariantCulture), isSingle ? x + (w / 2) : x + 2, bitY, TypeRole.Meta,
			anchor: isSingle ? "middle" : "start");
		if (!isSingle)
		{
			_ = sb.Append("\n  ");
			ds.AppendText(sb, seg.End.ToString(CultureInfo.InvariantCulture), x + w - 2, bitY, TypeRole.Meta, anchor: "end");
		}

		_ = sb.Append("\n</g>");
	}
}
