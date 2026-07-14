using System.Text;
using Mermaider.Icons;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class TreeViewSvgRenderer
{
	private const double Pad = 24;
	private const double RowHeight = 28;
	private const double IndentStep = 20;
	private const double IconSize = 16;
	private const double IconGap = 6;
	private const double DescGap = 12;
	private const double ConnectorLineWidth = 1;
	private const double HighlightPadX = 6;
	private const double HighlightPadY = 2;
	private const double HighlightRadius = 4;

	private const double LabelFontSizePx = 14;
	private const double DescFontSizePx = 12;
	private const string LabelFontSize = RenderConstants.FsVar.S;
	private const string DescFontSize = RenderConstants.FsVar.Xs;

	internal static string Render(TreeViewDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
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

	internal static StringBuilder RenderToBuilder(TreeViewDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		var flatRows = new List<FlatRow>();
		foreach (var root in diagram.Roots)
			FlattenTree(root, 0, flatRows);

		if (flatRows.Count == 0)
		{
			StyleBlock.AppendSvgOpenTag(sb, 200, 60, colors, transparent, accessibility, diagramType);
			StyleBlock.AppendStyleBlock(sb, font, strict);
			_ = sb.Append("\n</svg>");
			return sb;
		}

		var maxWidth = MeasureMaxWidth(flatRows);
		var totalWidth = Pad + maxWidth + Pad;
		var totalHeight = Pad + (flatRows.Count * RowHeight) + Pad;

		StyleBlock.AppendSvgOpenTag(sb, totalWidth, totalHeight, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict);
		_ = sb.Append("\n<defs>\n</defs>\n");

		// Render connector lines first (under everything else)
		AppendConnectorLines(sb, flatRows);

		// Render rows (highlight bg, icon, label, description)
		for (var i = 0; i < flatRows.Count; i++)
		{
			var row = flatRows[i];
			var y = Pad + (i * RowHeight);
			var x = Pad + (row.Depth * IndentStep);
			AppendRow(sb, row, x, y);
		}

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static void FlattenTree(TreeViewNode node, int depth, List<FlatRow> rows)
	{
		rows.Add(new FlatRow(node, depth));
		foreach (var child in node.Children)
			FlattenTree(child, depth + 1, rows);
	}

	private static double MeasureMaxWidth(List<FlatRow> rows)
	{
		var max = 0.0;
		foreach (var row in rows)
		{
			var x = row.Depth * IndentStep;
			var w = x + IconSize + IconGap;
			w += TextMetrics.MeasureTextWidth(row.Node.Label, LabelFontSizePx, row.Node.IsDirectory ? 700 : 400);
			if (row.Node.Description is { Length: > 0 } desc)
				w += DescGap + TextMetrics.MeasureTextWidth(desc, DescFontSizePx, 400);
			if (w > max)
				max = w;
		}
		return max;
	}

	private static void AppendConnectorLines(StringBuilder sb, List<FlatRow> rows)
	{
		for (var i = 0; i < rows.Count; i++)
		{
			var row = rows[i];
			if (row.Depth == 0)
				continue;

			var rowY = Pad + (i * RowHeight) + (RowHeight / 2);
			var connX = Pad + ((row.Depth - 1) * IndentStep) + (IconSize / 2);
			var nodeX = Pad + (row.Depth * IndentStep);

			// Horizontal connector from parent's vertical line to this node
			_ = sb.Append("\n<line x1=\"").Append(connX.SvgFormat())
				.Append("\" y1=\"").Append(rowY.SvgFormat())
				.Append("\" x2=\"").Append((nodeX - 2).SvgFormat())
				.Append("\" y2=\"").Append(rowY.SvgFormat())
				.Append("\" stroke=\"var(--_line)\" stroke-width=\"")
				.Append(ConnectorLineWidth.SvgFormat()).Append("\" />");

			// Vertical connector from parent row to this row
			var parentIndex = FindParentIndex(rows, i);
			if (parentIndex >= 0)
			{
				var parentY = Pad + (parentIndex * RowHeight) + (RowHeight / 2);
				_ = sb.Append("\n<line x1=\"").Append(connX.SvgFormat())
					.Append("\" y1=\"").Append(parentY.SvgFormat())
					.Append("\" x2=\"").Append(connX.SvgFormat())
					.Append("\" y2=\"").Append(rowY.SvgFormat())
					.Append("\" stroke=\"var(--_line)\" stroke-width=\"")
					.Append(ConnectorLineWidth.SvgFormat()).Append("\" />");
			}
		}
	}

	private static int FindParentIndex(List<FlatRow> rows, int childIndex)
	{
		var childDepth = rows[childIndex].Depth;
		for (var i = childIndex - 1; i >= 0; i--)
		{
			if (rows[i].Depth == childDepth - 1)
				return i;
		}
		return -1;
	}

	private static void AppendRow(StringBuilder sb, FlatRow row, double x, double y)
	{
		var node = row.Node;
		var midY = y + (RowHeight / 2);
		var hasHighlight = node.CssClass is "highlight";
		var hasCustomClass = node.CssClass is { Length: > 0 };

		// Highlight background rect
		if (hasHighlight)
		{
			var labelW = TextMetrics.MeasureTextWidth(node.Label, LabelFontSizePx, node.IsDirectory ? 700 : 400);
			var totalRowW = IconSize + IconGap + labelW;
			if (node.Description is { Length: > 0 } descM)
				totalRowW += DescGap + TextMetrics.MeasureTextWidth(descM, DescFontSizePx, 400);

			_ = sb.Append("\n<rect x=\"").Append((x - HighlightPadX).SvgFormat())
				.Append("\" y=\"").Append((midY - (LabelFontSizePx / 2) - HighlightPadY - 1).SvgFormat())
				.Append("\" width=\"").Append((totalRowW + (HighlightPadX * 2)).SvgFormat())
				.Append("\" height=\"").Append((LabelFontSizePx + (HighlightPadY * 2) + 2).SvgFormat())
				.Append("\" rx=\"").Append(HighlightRadius.SvgFormat())
				.Append("\" ry=\"").Append(HighlightRadius.SvgFormat())
				.Append("\" fill=\"var(--_accent-fill)\" stroke=\"var(--_accent-stroke)\" stroke-width=\"1\" />");
		}

		// Group wrapper with optional CSS class
		_ = hasCustomClass
			? sb.Append("\n<g class=\"treeview-node ").Append(node.CssClass).Append("\">")
			: sb.Append("\n<g class=\"treeview-node\">");

		// Icon
		var iconX = x;
		var iconY = midY - (IconSize / 2);
		var iconName = ResolveIconName(node);
		if (iconName is not null)
		{
			AppendIcon(sb, iconName, iconX, iconY);
		}

		// Label text
		var textX = x + IconSize + IconGap;
		var fontWeight = node.IsDirectory ? 700 : 400;
		_ = sb.Append("\n<text x=\"").Append(textX.SvgFormat())
			.Append("\" y=\"").Append(midY.SvgFormat())
			.Append("\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(LabelFontSize)
			.Append("\" font-weight=\"").Append(fontWeight)
			.Append("\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, node.Label.AsSpan());
		_ = sb.Append("</text>");

		// Description text (italic, muted)
		if (node.Description is { Length: > 0 } desc)
		{
			var labelW = TextMetrics.MeasureTextWidth(node.Label, LabelFontSizePx, fontWeight);
			var descX = textX + labelW + DescGap;
			_ = sb.Append("\n<text x=\"").Append(descX.SvgFormat())
				.Append("\" y=\"").Append(midY.SvgFormat())
				.Append("\" dy=\"").Append(RenderConstants.TextBaselineShift)
				.Append("\" font-size=\"").Append(DescFontSize)
				.Append("\" font-weight=\"400\" font-style=\"italic\" fill=\"var(--_text-muted)\">");
			MultilineUtils.AppendEscapedXml(sb, desc.AsSpan());
			_ = sb.Append("</text>");
		}

		_ = sb.Append("\n</g>");
	}

	private static string? ResolveIconName(TreeViewNode node)
	{
		// Explicit icon override
		if (node.Icon is not null)
		{
			if (node.Icon.Length == 0)
				return null; // icon suppressed
			return node.Icon;
		}

		// Default: folder for directories, file for files
		return node.IsDirectory ? "folder" : "file";
	}

	private static void AppendIcon(StringBuilder sb, string iconName, double x, double y)
	{
		if (!IconRegistry.TryGet(iconName, out var svg))
			svg = IconRegistry.Resolve(null);

		var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
		_ = sb.Append("\n<image x=\"").Append(x.SvgFormat())
			.Append("\" y=\"").Append(y.SvgFormat())
			.Append("\" width=\"").Append(IconSize.SvgFormat())
			.Append("\" height=\"").Append(IconSize.SvgFormat())
			.Append("\" href=\"data:image/svg+xml;base64,").Append(base64).Append("\" />");
	}

	private sealed record FlatRow(TreeViewNode Node, int Depth);
}
