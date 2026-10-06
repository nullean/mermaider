using System.Text;
using Mermaider.Icons;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Tree view on the design system: branches are rounded orthogonal connectors in <c>--_line</c> (preset bend radius);
/// each top-level branch is a cluster family that colours its folder / file glyphs; level 0 is a heading, level 1 a
/// subheading in family ink, deeper levels body text (folders at label weight). Highlighted entries sit on an accent
/// tint; descriptions are meta text.
/// </summary>
internal static class TreeViewSvgRenderer
{
	private const double Pad = 24;
	private const double RowHeight = 28;
	private const double IndentStep = 24;
	private const double IconSize = 16;
	private const double IconGap = 8;
	private const double DescGap = 12;
	private const double ConnectorWidth = 1;
	private const double HighlightPadX = 6;
	private const double HighlightHeight = 24;

	internal static string Render(TreeViewDiagram diagram, SvgRenderContext context)
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

	internal static StringBuilder RenderToBuilder(TreeViewDiagram diagram, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var ds = DesignSystem.For(context);

		var flatRows = new List<FlatRow>();
		var singleRoot = diagram.Roots.Count == 1;
		var familyCounter = 0;
		foreach (var root in diagram.Roots)
			FlattenTree(root, 0, -1, -1, singleRoot, ref familyCounter, flatRows, context.Limits);

		if (flatRows.Count == 0)
		{
			StyleBlock.AppendSvgOpenTag(sb, 200, 60, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
			StyleBlock.AppendStyleBlock(sb, context.Styles);
			ds.Close(sb);
			return sb;
		}

		var maxWidth = MeasureMaxWidth(flatRows);
		var totalWidth = Pad + maxWidth + Pad + HighlightPadX;
		var totalHeight = Pad + (flatRows.Count * RowHeight) + Pad;

		StyleBlock.AppendSvgOpenTag(sb, totalWidth, totalHeight, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles);

		// connectors first (under everything else)
		AppendConnectors(sb, flatRows, context.EdgeRadius);

		for (var i = 0; i < flatRows.Count; i++)
		{
			var row = flatRows[i];
			var y = Pad + (i * RowHeight);
			var x = Pad + (row.Depth * IndentStep);
			AppendRow(sb, ds, row, x, y);
		}

		ds.Close(sb);
		return sb;
	}

	/// <summary>
	/// Flattens the tree in display order. Family assignment: with a single root, the root takes family 0 and each folder
	/// directly under it starts a new family; with several roots, each root folder starts one. Everything else inherits.
	/// </summary>
	private static void FlattenTree(TreeViewNode node, int depth, int parentIndex, int inheritedFamily, bool singleRoot, ref int counter, List<FlatRow> rows, ResourceLimits limits)
	{
		ResourceGuard.CheckRecursionDepth(depth, limits);
		var startsBranch = node.IsDirectory && (depth == 0 || (singleRoot && depth == 1));
		var family = startsBranch ? counter++ : inheritedFamily;
		var index = rows.Count;
		rows.Add(new FlatRow(node, depth, parentIndex, family));
		foreach (var child in node.Children)
			FlattenTree(child, depth + 1, index, family, singleRoot, ref counter, rows, limits);
	}

	private static TypeRole RoleOf(FlatRow row) => row.Depth switch
	{
		0 when row.Node.IsDirectory => TypeRole.Heading,
		1 when row.Node.IsDirectory => TypeRole.Subheading,
		_ when row.Node.IsDirectory => TypeRole.Label,
		_ => TypeRole.Body,
	};

	/// <summary>
	/// Weight used to measure a row's label: at least the heaviest any preset draws it with, so layout is preset-independent.
	/// Body text is measured one step heavier because the metrics run short on dotted file names (descriptions collided).
	/// </summary>
	private static int MeasureWeight(FlatRow row) => RoleOf(row) == TypeRole.Heading ? 700 : 600;

	private static double LabelWidth(FlatRow row) =>
		TextMetrics.MeasureTextWidth(row.Node.Label, DesignSystem.Px(RoleOf(row)), MeasureWeight(row));

	private static double MeasureMaxWidth(List<FlatRow> rows)
	{
		var max = 0.0;
		foreach (var row in rows)
		{
			var w = (row.Depth * IndentStep) + IconSize + IconGap + LabelWidth(row);
			if (row.Node.Description is { Length: > 0 } desc)
				w += DescGap + DesignSystem.XsWidth(desc, 400);
			max = Math.Max(max, w);
		}

		return max;
	}

	private static void AppendConnectors(StringBuilder sb, List<FlatRow> rows, double radius)
	{
		_ = sb.Append("\n<g class=\"treeview-branches\" fill=\"none\" stroke=\"").Append(DesignSystem.EdgeColor)
			.Append("\" stroke-width=\"").Append(DesignSystem.Num(ConnectorWidth)).Append("\" stroke-linecap=\"round\">");
		for (var i = 0; i < rows.Count; i++)
		{
			var row = rows[i];
			if (row.ParentIndex < 0)
				continue;

			var rowY = Pad + (i * RowHeight) + (RowHeight / 2);
			var parentY = Pad + (row.ParentIndex * RowHeight) + (RowHeight / 2);
			var connX = Pad + ((row.Depth - 1) * IndentStep) + (IconSize / 2);
			var nodeX = Pad + (row.Depth * IndentStep) - 3;

			// vertical from under the parent's glyph, then a rounded elbow into this row
			Point[] points = [new(connX, parentY + (IconSize / 2) + 2), new(connX, rowY), new(nodeX, rowY)];
			_ = sb.Append("\n  <path d=\"");
			SvgRenderer.BuildOrthogonalPath(sb, points, Math.Min(radius, 6));
			_ = sb.Append("\" />");
		}

		_ = sb.Append("\n</g>");
	}

	private static void AppendRow(StringBuilder sb, DesignSystem ds, FlatRow row, double x, double y)
	{
		var node = row.Node;
		var midY = y + (RowHeight / 2);
		var family = row.Family >= 0 ? ds.Cluster(row.Family) : ds.Neutral;
		var role = RoleOf(row);
		var hasHighlight = node.CssClass is "highlight";
		var labelW = LabelWidth(row);
		var textX = x + IconSize + IconGap;

		// Group wrapper with optional CSS class. CssClass is parser-constrained to [\w-], but
		// escape it anyway so this attribute can't become a breakout vector if that ever changes.
		_ = node.CssClass is { Length: > 0 }
			? sb.Append("\n<g class=\"treeview-node ").Append(MultilineUtils.EscapeAttr(node.CssClass)).Append("\" data-depth=\"")
			: sb.Append("\n<g class=\"treeview-node\" data-depth=\"");
		_ = sb.Append(row.Depth.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\">");

		if (hasHighlight)
		{
			// the accent "look here" tint behind the entry
			var totalW = IconSize + IconGap + labelW;
			if (node.Description is { Length: > 0 } descM)
				totalW += DescGap + DesignSystem.XsWidth(descM, 400);
			var r = Math.Min(ds.Spec.NodeRadius, 8);
			_ = sb.Append("\n  ");
			DesignSystem.AppendRect(sb, x - HighlightPadX, midY - (HighlightHeight / 2), totalW + (HighlightPadX * 2), HighlightHeight, r,
				ds.Accent.Tint(2), ds.Spec.OutlineWidth > 0 ? ds.Accent.Edge : "none", 1);
		}

		var iconName = ResolveIconName(node);
		if (iconName is not null)
		{
			_ = sb.Append("\n  ");
			if (node.Icon is null)
				AppendGlyph(sb, ds, node.IsDirectory, family, x, midY - (IconSize / 2));
			else
				AppendIcon(sb, iconName, x, midY - (IconSize / 2));
		}

		_ = sb.Append("\n  ");
		var color = role == TypeRole.Subheading ? family.Ink : null;
		ds.AppendText(sb, node.Label, textX, midY, role, color, anchor: "start", weight: role == TypeRole.Label ? 600 : null);

		if (node.Description is { Length: > 0 } desc)
		{
			_ = sb.Append("\n  ");
			ds.AppendText(sb, desc, textX + labelW + DescGap, midY, TypeRole.Meta, anchor: "start");
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

	/// <summary>The default folder / file glyph drawn inline in the row's family (band fill, ink outline).</summary>
	private static void AppendGlyph(StringBuilder sb, DesignSystem ds, bool folder, ColorFamily family, double x, double y)
	{
		static string N(double v) => DesignSystem.Num(v);
		var fill = ds.Spec.Fill == FillKind.Knockout ? "var(--bg)" : folder ? family.Band : "var(--bg)";
		_ = folder
			? sb.Append("<path class=\"treeview-icon\" d=\"M").Append(N(x + 1)).Append(',').Append(N(y + 3.5))
				.Append(" H").Append(N(x + 6)).Append(" L").Append(N(x + 7.5)).Append(',').Append(N(y + 5))
				.Append(" H").Append(N(x + 15)).Append(" V").Append(N(y + 13.5)).Append(" H").Append(N(x + 1)).Append(" Z")
				.Append("\" fill=\"").Append(fill).Append("\" stroke=\"").Append(family.Ink)
				.Append("\" stroke-width=\"1.1\" stroke-linejoin=\"round\" />")
			: sb.Append("<path class=\"treeview-icon\" d=\"M").Append(N(x + 3)).Append(',').Append(N(y + 1.5))
				.Append(" H").Append(N(x + 9.5)).Append(" L").Append(N(x + 13)).Append(',').Append(N(y + 5))
				.Append(" V").Append(N(y + 14.5)).Append(" H").Append(N(x + 3)).Append(" Z M").Append(N(x + 9.5)).Append(',').Append(N(y + 1.5))
				.Append(" V").Append(N(y + 5)).Append(" H").Append(N(x + 13))
				.Append("\" fill=\"").Append(fill).Append("\" stroke=\"").Append(family.Ink)
				.Append("\" stroke-width=\"1.1\" stroke-linejoin=\"round\" />");
	}

	private static void AppendIcon(StringBuilder sb, string iconName, double x, double y)
	{
		if (!IconRegistry.TryGet(iconName, out var svg))
			svg = IconRegistry.Resolve(null);

		var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
		_ = sb.Append("<image x=\"").Append(x.SvgFormat())
			.Append("\" y=\"").Append(y.SvgFormat())
			.Append("\" width=\"").Append(IconSize.SvgFormat())
			.Append("\" height=\"").Append(IconSize.SvgFormat())
			.Append("\" href=\"data:image/svg+xml;base64,").Append(base64).Append("\" />");
	}

	private sealed record FlatRow(TreeViewNode Node, int Depth, int ParentIndex, int Family);
}
