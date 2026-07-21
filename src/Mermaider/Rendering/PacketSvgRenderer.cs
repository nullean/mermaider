using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class PacketSvgRenderer
{
	private const int BitsPerRow = 32;
	// Max rows from PacketDiagram.MaxBitIndex (bits 0..4095 → 128 rows of 32).
	private const int MaxRows = (PacketDiagram.MaxBitIndex / BitsPerRow) + 1;
	private const double BitWidth = 32;
	private const double RowHeight = 32;
	private const double PaddingX = 5;
	private const double PaddingY = 5;
	private const double BitLabelPad = 10;
	private const double TitleHeight = 36;
	private const string TitleFontSize = RenderConstants.FsVar.L;
	private const string LabelFontSize = RenderConstants.FsVar.S;
	private const string BitFontSize = RenderConstants.FsVar.Xs;

	// Block fills are light tints of the categorical palette mixed against the theme background,
	// keeping a pastel look while following the theme and the shared palette.
	private static string BlockFill(int index, DiagramColors colors) =>
		$"color-mix(in srgb, {colors.PaletteAt(index)} 18%, var(--bg))";

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

		var hasTitle = diagram.Title is { Length: > 0 };
		var titleOffset = hasTitle ? TitleHeight : 0.0;
		var showBits = true;
		var rowPadY = PaddingY + (showBits ? BitLabelPad : 0);

		var rows = BuildRows(diagram.Fields);
		var rowCount = Math.Max(rows.Count, 1);

		var width = (BitsPerRow * BitWidth) + 2;
		var totalRowHeight = RowHeight + rowPadY;
		var height = titleOffset + (totalRowHeight * rowCount) + PaddingY + 8;

		StyleBlock.AppendSvgOpenTag(sb, width, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles.Font, context.Styles.Strict, context.Styles.FontScale, context.Styles.MonoFont);
		_ = sb.Append("\n<defs>\n</defs>\n");

		if (hasTitle)
		{
			_ = sb.Append("\n<text x=\"").Append((width / 2).SvgFormat())
				.Append("\" y=\"24\" text-anchor=\"middle\" font-size=\"")
				.Append(TitleFontSize).Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, diagram.Title.AsSpan());
			_ = sb.Append("</text>");
		}

		for (var row = 0; row < rows.Count; row++)
		{
			var wordY = titleOffset + (row * totalRowHeight) + rowPadY;
			foreach (var seg in rows[row])
				AppendSegment(sb, seg, wordY, showBits, context.Styles.Colors);
		}

		_ = sb.Append("\n</svg>");
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

	private static void AppendSegment(StringBuilder sb, Segment seg, double wordY, bool showBits, DiagramColors colors)
	{
		var col = seg.Start % BitsPerRow;
		var bitCount = seg.End - seg.Start + 1;
		var blockX = (col * BitWidth) + 1;
		var width = (bitCount * BitWidth) - PaddingX;
		if (width < 1)
			width = 1;

		var fill = BlockFill(seg.ColorIndex, colors);

		_ = sb.Append("\n<rect x=\"").Append(blockX.SvgFormat())
			.Append("\" y=\"").Append(wordY.SvgFormat())
			.Append("\" width=\"").Append(width.SvgFormat())
			.Append("\" height=\"").Append(RowHeight.SvgFormat())
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"var(--_line)\" stroke-width=\"1\" />");

		// Label centered in block
		_ = sb.Append("\n<text x=\"").Append((blockX + (width / 2)).SvgFormat())
			.Append("\" y=\"").Append((wordY + (RowHeight / 2)).SvgFormat())
			.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(LabelFontSize)
			.Append("\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, seg.Label.AsSpan());
		_ = sb.Append("</text>");

		if (!showBits)
			return;

		var isSingle = seg.Start == seg.End;
		var bitY = wordY - 2;

		// Start bit number
		_ = sb.Append("\n<text x=\"").Append((blockX + (isSingle ? width / 2 : 0)).SvgFormat())
			.Append("\" y=\"").Append(bitY.SvgFormat())
			.Append("\" text-anchor=\"").Append(isSingle ? "middle" : "start")
			.Append("\" font-size=\"").Append(BitFontSize)
			.Append("\" fill=\"var(--_text)\">")
			.Append(seg.Start.ToString(CultureInfo.InvariantCulture))
			.Append("</text>");

		if (!isSingle)
		{
			_ = sb.Append("\n<text x=\"").Append((blockX + width).SvgFormat())
				.Append("\" y=\"").Append(bitY.SvgFormat())
				.Append("\" text-anchor=\"end\" font-size=\"").Append(BitFontSize)
				.Append("\" fill=\"var(--_text)\">")
				.Append(seg.End.ToString(CultureInfo.InvariantCulture))
				.Append("</text>");
		}
	}

}
