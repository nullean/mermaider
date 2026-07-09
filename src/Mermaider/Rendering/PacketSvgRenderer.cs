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

	// Fixed light fills for packet blocks (not theme-derived).
	private static readonly string[] BlockFills =
	[
		"#e8f0fe", "#fef3e8", "#f0e8fe", "#e8fef0",
		"#fee8e8", "#fefee8", "#e8fefe", "#f5e8fe",
	];

	private readonly record struct Segment(int Start, int End, string Label, int ColorIndex);

	internal static string Render(PacketDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = RenderToBuilder(diagram, colors, font, transparent, strict, accessibility, diagramType);
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

	internal static StringBuilder RenderToBuilder(PacketDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
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

		StyleBlock.AppendSvgOpenTag(sb, width, height, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict);
		_ = sb.Append("\n<defs>\n</defs>\n");

		if (hasTitle)
		{
			_ = sb.Append("\n<text x=\"").Append(SvgFormat.F(width / 2))
				.Append("\" y=\"24\" text-anchor=\"middle\" font-size=\"")
				.Append(TitleFontSize).Append("\" font-weight=\"700\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, diagram.Title.AsSpan());
			_ = sb.Append("</text>");
		}

		for (var row = 0; row < rows.Count; row++)
		{
			var wordY = titleOffset + (row * totalRowHeight) + rowPadY;
			foreach (var seg in rows[row])
				AppendSegment(sb, seg, wordY, showBits);
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

	private static void AppendSegment(StringBuilder sb, Segment seg, double wordY, bool showBits)
	{
		var col = seg.Start % BitsPerRow;
		var bitCount = seg.End - seg.Start + 1;
		var blockX = (col * BitWidth) + 1;
		var width = (bitCount * BitWidth) - PaddingX;
		if (width < 1)
			width = 1;

		var fill = BlockFills[seg.ColorIndex % BlockFills.Length];

		_ = sb.Append("\n<rect x=\"").Append(SvgFormat.F(blockX))
			.Append("\" y=\"").Append(SvgFormat.F(wordY))
			.Append("\" width=\"").Append(SvgFormat.F(width))
			.Append("\" height=\"").Append(SvgFormat.F(RowHeight))
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"var(--_line)\" stroke-width=\"1\" />");

		// Label centered in block
		_ = sb.Append("\n<text x=\"").Append(SvgFormat.F(blockX + (width / 2)))
			.Append("\" y=\"").Append(SvgFormat.F(wordY + (RowHeight / 2)))
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
		_ = sb.Append("\n<text x=\"").Append(SvgFormat.F(blockX + (isSingle ? width / 2 : 0)))
			.Append("\" y=\"").Append(SvgFormat.F(bitY))
			.Append("\" text-anchor=\"").Append(isSingle ? "middle" : "start")
			.Append("\" font-size=\"").Append(BitFontSize)
			.Append("\" fill=\"var(--_text)\">")
			.Append(seg.Start.ToString(CultureInfo.InvariantCulture))
			.Append("</text>");

		if (!isSingle)
		{
			_ = sb.Append("\n<text x=\"").Append(SvgFormat.F(blockX + width))
				.Append("\" y=\"").Append(SvgFormat.F(bitY))
				.Append("\" text-anchor=\"end\" font-size=\"").Append(BitFontSize)
				.Append("\" fill=\"var(--_text)\">")
				.Append(seg.End.ToString(CultureInfo.InvariantCulture))
				.Append("</text>");
		}
	}

}
