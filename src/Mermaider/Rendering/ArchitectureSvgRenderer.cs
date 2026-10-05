using System.Text;
using Mermaider.Icons;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>Renders a <see cref="PositionedArchitectureDiagram"/> to SVG via pooled StringBuilder.</summary>
internal static class ArchitectureSvgRenderer
{
	private static readonly string ServiceLabelAttrs = RenderConstants.TextAttrs.NodeLabelCenterFill + "var(--_text)\"";

	internal static string Render(PositionedArchitectureDiagram diagram, SvgRenderContext context)
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

	internal static StringBuilder RenderToBuilder(PositionedArchitectureDiagram diagram, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		StyleBlock.AppendSvgOpenTag(sb, diagram.Width, diagram.Height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles.Font, context.Styles.Strict, context.Styles.FontScale, context.Styles.MonoFont);
		AppendMarkerDefs(sb);

		// Same language as the other diagrams: connected services (and the services sharing a group) share a cluster colour;
		// groups are tinted boxes whose nesting follows their geometry, titles in the border colour.
		var groupTree = BuildGroupTree(diagram.Groups);
		// junctions are members too, so services joined through one share a colour
		var members = diagram.Services.Select(sv => new ClusterBox(sv.Id, sv.X, sv.Y, sv.Width, sv.Height))
			.Concat(diagram.Junctions.Select(j => new ClusterBox(j.Id, j.X, j.Y, 12, 12)))
			.ToList();
		var memberIds = members.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
		var palette = ClusterPalette.Build(
			members,
			diagram.Edges.Where(e => memberIds.Contains(e.SourceId) && memberIds.Contains(e.TargetId)).Select(e => (e.SourceId, e.TargetId)),
			groupTree.Roots,
			context.Styles.Colors);

		foreach (var group in diagram.Groups.OrderBy(g => groupTree.Depth[g.Id]))
			AppendGroup(sb, group, palette, groupTree.Depth[group.Id]);

		foreach (var edge in diagram.Edges)
			AppendEdge(sb, edge, context.EdgeRadius);

		foreach (var service in diagram.Services)
			AppendService(sb, service, palette, diagram.Edges);

		foreach (var junction in diagram.Junctions)
			AppendJunction(sb, junction);

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static void AppendMarkerDefs(StringBuilder sb)
	{
		var s = RenderConstants.ArrowHead.Size;
		var h = s / 2.0;

		_ = sb.Append("\n<defs>\n");
		_ = sb.Append("  <marker id=\"arch-arrow-end\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(s)
			.Append("\" markerHeight=\"").Append(s)
			.Append("\" refX=\"").Append(s)
			.Append("\" refY=\"").Append(h)
			.Append("\" orient=\"auto\">\n");
		_ = sb.Append("    <polygon points=\"0 0, ").Append(s).Append(' ').Append(h)
			.Append(", 0 ").Append(s)
			.Append("\" fill=\"var(--_line)\" />\n");
		_ = sb.Append("  </marker>\n");

		_ = sb.Append("  <marker id=\"arch-arrow-start\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(s)
			.Append("\" markerHeight=\"").Append(s)
			.Append("\" refX=\"0\" refY=\"").Append(h)
			.Append("\" orient=\"auto\">\n");
		_ = sb.Append("    <polygon points=\"").Append(s).Append(" 0, 0 ").Append(h)
			.Append(", ").Append(s).Append(' ').Append(s)
			.Append("\" fill=\"var(--_line)\" />\n");
		_ = sb.Append("  </marker>\n");
		_ = sb.Append("</defs>\n");
	}

	private const double GroupIconSize = 20;
	private const double GroupIconInset = 12;

	private static void AppendGroup(StringBuilder sb, PositionedArchitectureGroup group, ClusterPalette palette, int depth)
	{
		var r = RenderConstants.Radii.Group;
		_ = sb.Append("\n<g class=\"architecture-group\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, group.Id.AsSpan());
		_ = sb.Append("\">\n");

		var stroke = palette.GroupStroke(group.Id);
		_ = sb.Append("  <rect x=\"").Append(group.X).Append("\" y=\"").Append(group.Y)
			.Append("\" width=\"").Append(group.Width).Append("\" height=\"").Append(group.Height)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"").Append(palette.GroupFill(group.Id, depth)).Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n  ");

		var titleX = group.X + RenderConstants.GroupHeaderContentPad + 8;
		var titleY = group.Y + 20;

		if (group.Icon is { Length: > 0 } icon)
		{
			var iconX = group.X + GroupIconInset;
			var iconY = group.Y + GroupIconInset;

			// Vendor icons are glyph-only (white on transparent) — without a badge behind them
			// they'd be nearly invisible against the light group card. Give them the same small
			// gradient badge treatment as service boxes; default-pack icons (already colored,
			// no gradient entry) render as before, directly on the card.
			if (IconRegistry.TryGetBadgeGradient(icon, out var gradient))
			{
				var gradientId = $"arch-grad-{SanitizeId(group.Id)}";
				_ = sb.Append("  <defs><linearGradient id=\"").Append(gradientId)
					.Append("\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"")
					.Append(gradient.Light).Append("\"/><stop offset=\"1\" stop-color=\"")
					.Append(gradient.Dark).Append("\"/></linearGradient></defs>\n  ");
				_ = sb.Append("<rect x=\"").Append(iconX).Append("\" y=\"").Append(iconY)
					.Append("\" width=\"").Append(GroupIconSize).Append("\" height=\"").Append(GroupIconSize)
					.Append("\" rx=\"4\" ry=\"4\" fill=\"url(#").Append(gradientId).Append(")\" />\n  ");
			}

			AppendIcon(sb, icon, iconX, iconY, GroupIconSize, GroupIconSize);
			titleX = iconX + GroupIconSize + 8;
			titleY = iconY + (GroupIconSize / 2);
		}

		MultilineUtils.AppendMultilineText(
			sb, group.Title,
			titleX, titleY,
			RenderConstants.FontSizes.GroupHeader,
			RenderConstants.TextAttrs.GroupHeaderFill + stroke + "\"");

		_ = sb.Append("\n</g>");
	}

	private static void AppendService(StringBuilder sb, PositionedArchitectureService service, ClusterPalette palette, IReadOnlyList<PositionedArchitectureEdge> edges)
	{
		var r = RenderConstants.Radii.Rounded;
		_ = sb.Append("\n<g class=\"architecture-service\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, service.Id.AsSpan());
		_ = sb.Append("\" data-icon=\"");
		MultilineUtils.AppendEscapedAttr(sb, service.Icon.AsSpan());
		_ = sb.Append("\">\n");

		// Built-in vendor icons (aws:*, azure:*, gcp:*, elastic:*) paint the whole box with a
		// gradient — not just the icon glyph — mirroring how those vendors present their own
		// service icons. Default-pack icons and custom-registered icons render inside the plain
		// themed node box (fill/stroke unchanged), same as before.
		if (IconRegistry.TryGetBadgeGradient(service.Icon, out var gradient))
		{
			var gradientId = $"arch-grad-{SanitizeId(service.Id)}";
			_ = sb.Append("  <defs><linearGradient id=\"").Append(gradientId)
				.Append("\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"")
				.Append(gradient.Light).Append("\"/><stop offset=\"1\" stop-color=\"")
				.Append(gradient.Dark).Append("\"/></linearGradient></defs>\n");

			_ = sb.Append("  <rect x=\"").Append(service.X).Append("\" y=\"").Append(service.Y)
				.Append("\" width=\"").Append(service.Width).Append("\" height=\"").Append(service.Height)
				.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
				.Append("\" fill=\"url(#").Append(gradientId).Append(")\" />\n");
		}
		else
		{
			_ = sb.Append("  <rect x=\"").Append(service.X).Append("\" y=\"").Append(service.Y)
				.Append("\" width=\"").Append(service.Width).Append("\" height=\"").Append(service.Height)
				.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
				.Append("\" fill=\"").Append(palette.NodeFill(service.Id)).Append("\" stroke=\"").Append(palette.NodeStroke(service.Id))
				.Append("\" stroke-width=\"").Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n");
		}

		var iconSize = Math.Min(service.Width, service.Height) * 0.55;
		AppendIcon(
			sb, service.Icon,
			service.X + ((service.Width - iconSize) / 2),
			service.Y + ((service.Height - iconSize) / 2),
			iconSize, iconSize);

		// a line passing through the title gets a background so the text stays readable
		var labelMetrics = TextMetrics.MeasureMultiline(service.Title.AsSpan(), RenderConstants.FontSizes.NodeLabel, RenderConstants.FontWeights.NodeLabel);
		var labelCx = service.X + (service.Width / 2);
		var labelCy = service.Y + service.Height + 16;
		if (edges.Any(e => CrossesBox(e.Points, labelCx - (labelMetrics.Width / 2) - 3, labelCy - (labelMetrics.Height / 2), labelMetrics.Width + 6, labelMetrics.Height)))
		{
			_ = sb.Append("  <rect x=\"").Append(labelCx - (labelMetrics.Width / 2) - 3).Append("\" y=\"").Append(labelCy - (labelMetrics.Height / 2))
				.Append("\" width=\"").Append(labelMetrics.Width + 6).Append("\" height=\"").Append(labelMetrics.Height)
				.Append("\" rx=\"4\" fill=\"var(--bg)\" />\n");
		}

		_ = sb.Append("  ");
		MultilineUtils.AppendMultilineText(
			sb, service.Title,
			labelCx, labelCy,
			RenderConstants.FontSizes.NodeLabel,
			ServiceLabelAttrs);
		_ = sb.Append('\n');

		_ = sb.Append("</g>");
	}

	/// <summary>
	/// Embeds the resolved icon as a base64 data URI on an &lt;image&gt;. This is the one
	/// narrow case the SVG sanitizer allows an href through (see <see cref="SvgSanitizer"/>) —
	/// the icon markup itself was already validated/sanitized when it entered the
	/// <see cref="IconRegistry"/>, so the payload is guaranteed clean.
	/// </summary>
	private static void AppendIcon(StringBuilder sb, string iconName, double x, double y, double width, double height)
	{
		var svg = IconRegistry.Resolve(iconName);
		var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
		_ = sb.Append("  <image x=\"").Append(x).Append("\" y=\"").Append(y)
			.Append("\" width=\"").Append(width).Append("\" height=\"").Append(height)
			.Append("\" href=\"data:image/svg+xml;base64,").Append(base64).Append("\" />\n");
	}

	/// <summary>Strips anything unsafe for an XML <c>id</c> attribute value, keeping gradient ids collision-free per service.</summary>
	private static string SanitizeId(string id)
	{
		var buffer = new char[id.Length];
		for (var i = 0; i < id.Length; i++)
		{
			var c = id[i];
			buffer[i] = char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_';
		}
		return new string(buffer);
	}

	private static void AppendJunction(StringBuilder sb, PositionedArchitectureJunction junction)
	{
		_ = sb.Append("\n<circle class=\"architecture-junction\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, junction.Id.AsSpan());
		_ = sb.Append("\" cx=\"").Append(junction.X + 6).Append("\" cy=\"").Append(junction.Y + 6)
			.Append("\" r=\"3\" fill=\"var(--_line)\" />");
	}

	private static void AppendEdge(StringBuilder sb, PositionedArchitectureEdge edge, double cornerRadius)
	{
		if (edge.Points.Count < 2)
			return;

		_ = sb.Append("\n<path class=\"architecture-edge\" data-source=\"");
		MultilineUtils.AppendEscapedAttr(sb, edge.SourceId.AsSpan());
		_ = sb.Append("\" data-target=\"");
		MultilineUtils.AppendEscapedAttr(sb, edge.TargetId.AsSpan());
		_ = sb.Append("\" d=\"");
		if (SvgRenderer.IsOrthogonal(edge.Points))
			SvgRenderer.BuildOrthogonalPath(sb, edge.Points, cornerRadius);
		else
			SvgRenderer.BuildRoundedPath(sb, edge.Points, cornerRadius);
		_ = sb.Append("\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.Connector).Append('"');

		if (edge.SourceArrow)
			_ = sb.Append(" marker-start=\"url(#arch-arrow-start)\"");
		if (edge.TargetArrow)
			_ = sb.Append(" marker-end=\"url(#arch-arrow-end)\"");

		_ = sb.Append(" />");
	}

	private sealed record GroupTree(IReadOnlyList<ClusterGroup> Roots, Dictionary<string, int> Depth);

	// Architecture groups arrive flat; their nesting is the geometric containment (smallest enclosing group is the parent).
	private static GroupTree BuildGroupTree(IReadOnlyList<PositionedArchitectureGroup> groups)
	{
		static bool Contains(PositionedArchitectureGroup outer, PositionedArchitectureGroup inner) =>
			outer.X <= inner.X + 0.5 && outer.Y <= inner.Y + 0.5
			&& outer.X + outer.Width >= inner.X + inner.Width - 0.5 && outer.Y + outer.Height >= inner.Y + inner.Height - 0.5
			&& outer.Width * outer.Height > inner.Width * inner.Height;

		var parent = new Dictionary<string, string?>(StringComparer.Ordinal);
		foreach (var g in groups)
		{
			parent[g.Id] = groups.Where(o => o.Id != g.Id && Contains(o, g)).OrderBy(o => o.Width * o.Height).FirstOrDefault()?.Id;
		}

		var depth = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var g in groups)
		{
			var d = 0;
			for (var p = parent[g.Id]; p is not null; p = parent[p])
				d++;
			depth[g.Id] = d;
		}

		ClusterGroup Build(PositionedArchitectureGroup g) =>
			new(g.Id, g.X, g.Y, g.Width, g.Height, groups.Where(c => parent[c.Id] == g.Id).Select(Build).ToList());

		var roots = groups.Where(g => parent[g.Id] is null).Select(Build).ToList();
		return new GroupTree(roots, depth);
	}

	private static bool CrossesBox(IReadOnlyList<Point> points, double x, double y, double w, double h)
	{
		for (var i = 1; i < points.Count; i++)
		{
			var minX = Math.Min(points[i - 1].X, points[i].X);
			var maxX = Math.Max(points[i - 1].X, points[i].X);
			var minY = Math.Min(points[i - 1].Y, points[i].Y);
			var maxY = Math.Max(points[i - 1].Y, points[i].Y);
			if (maxX >= x && minX <= x + w && maxY >= y && minY <= y + h)
				return true;
		}

		return false;
	}
}
