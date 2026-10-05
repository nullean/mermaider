using System.Text;
using Mermaider.Icons;
using Mermaider.Layout;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>Renders a <see cref="PositionedArchitectureDiagram"/> to SVG via pooled StringBuilder.</summary>
internal static class ArchitectureSvgRenderer
{
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
		StyleBlock.AppendStyleBlock(sb, context.Styles);
		var ds = DesignSystem.For(context);

		// Same language as the other diagrams: connected services (and the services sharing a group) share a cluster family;
		// groups are the shared container, nesting follows their geometry.
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
			context.Styles.Colors).WithTint(ds.TintStrength);

		var ordered = diagram.Groups.OrderBy(g => groupTree.Depth[g.Id]).ToList();
		foreach (var group in ordered)
			AppendGroupBody(sb, ds, group, palette.GroupFamily(group.Id), groupTree.Depth[group.Id]);

		var serviceIds = diagram.Services.Select(sv => sv.Id).ToHashSet(StringComparer.Ordinal);
		foreach (var edge in diagram.Edges)
			AppendEdge(sb, ds, edge, context.EdgeRadius, serviceIds);

		foreach (var group in ordered)
			AppendGroupHeader(sb, ds, group, palette.GroupFamily(group.Id));

		foreach (var service in diagram.Services)
			AppendService(sb, ds, service, palette.Family(service.Id));

		foreach (var junction in diagram.Junctions)
			AppendJunction(sb, junction);

		ds.Close(sb);
		return sb;
	}

	private static void AppendGroupBody(StringBuilder sb, DesignSystem ds, PositionedArchitectureGroup group, ColorFamily family, int depth)
	{
		var attrs = new StringBuilder("data-id=\"");
		MultilineUtils.AppendEscapedAttr(attrs, group.Id.AsSpan());
		_ = attrs.Append('"');
		ds.AppendContainerBody(sb, group.X, group.Y, group.Width, group.Height, family, depth, "architecture-group", attrs.ToString());
	}

	private static void AppendGroupHeader(StringBuilder sb, DesignSystem ds, PositionedArchitectureGroup group, ColorFamily family)
	{
		_ = sb.Append("\n<g class=\"architecture-group-title\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, group.Id.AsSpan());
		_ = sb.Append("\">");
		if (group.Icon is { Length: > 0 } icon)
		{
			ds.AppendContainerHeaderWithIcon(sb, group.X, group.Y, group.Title, family,
				(b, cx, cy) => AppendIcon(b, ds, icon, cx, cy, DesignSystem.HeaderIconSize, family.Ink));
		}
		else
		{
			ds.AppendContainerHeader(sb, group.X, group.Y, group.Width, group.Title, family);
		}

		_ = sb.Append("\n</g>");
	}

	/// <summary>Icon centre, from the top of a service card.</summary>
	private const double IconCenterY = 32;

	/// <summary>Icon slot of a service card.</summary>
	private const double ServiceIconSize = 26;

	private static void AppendService(StringBuilder sb, DesignSystem ds, PositionedArchitectureService service, ColorFamily family)
	{
		_ = sb.Append("\n<g class=\"architecture-service\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, service.Id.AsSpan());
		_ = sb.Append("\" data-icon=\"");
		MultilineUtils.AppendEscapedAttr(sb, service.Icon.AsSpan());
		_ = sb.Append("\">\n  ");

		// an icon card on the node recipe: the card is the family, vendor / custom artwork keeps its own colours
		ds.AppendBox(sb, service.X, service.Y, service.Width, service.Height, family, ds.Spec.NodeRadius + 4);
		_ = sb.Append("\n  ");
		var cx = service.X + (service.Width / 2);
		AppendIcon(sb, ds, service.Icon, cx, service.Y + IconCenterY, ServiceIconSize, family.Ink);
		_ = sb.Append("\n  ");

		var lines = DesignSystem.WrapWords(service.Title, service.Width - (2 * ArchitectureLayout.ServiceTextPad),
			DesignSystem.Px(TypeRole.Label), ds.Weight(TypeRole.Label));
		var label = string.Join('\n', lines);
		ds.AppendText(sb, label, cx, service.Y + (lines.Count > 1 ? 70 : 72), TypeRole.Label);
		_ = sb.Append("\n</g>");
	}

	/// <summary>Mermaid's default pictograms, drawn as line glyphs in the family ink (unless a custom icon overrides the name).</summary>
	private static DesignSystem.Glyph? BuiltInGlyph(string iconName)
	{
		var name = string.IsNullOrWhiteSpace(iconName) || !IconRegistry.TryGet(iconName, out _) ? IconRegistry.FallbackName : iconName.ToLowerInvariant();
		DesignSystem.Glyph? glyph = name switch
		{
			"cloud" => DesignSystem.Glyph.Cloud,
			"database" => DesignSystem.Glyph.Database,
			"disk" => DesignSystem.Glyph.Disk,
			"server" => DesignSystem.Glyph.Server,
			"internet" => DesignSystem.Glyph.Internet,
			"generic" => DesignSystem.Glyph.Generic,
			_ => null,
		};
		if (glyph is null)
			return null;
		// a user registration under a default name wins over the built-in glyph
		return IconRegistry.Resolve(name) == BuiltInIcons.Map[name] ? glyph : null;
	}

	/// <summary>
	/// Draws the icon centred at (<paramref name="cx"/>, <paramref name="cy"/>) in a <paramref name="size"/> slot: default
	/// pictograms as family-ink glyphs, vendor glyphs on their own gradient badge, anything else as the registered artwork.
	/// Artwork is embedded as a base64 data URI on an &lt;image&gt;, the one narrow case the SVG sanitizer allows an href
	/// through (see <see cref="SvgSanitizer"/>); the markup was sanitized when it entered the <see cref="IconRegistry"/>.
	/// </summary>
	private static void AppendIcon(StringBuilder sb, DesignSystem ds, string iconName, double cx, double cy, double size, string ink)
	{
		if (BuiltInGlyph(iconName) is { } glyph)
		{
			DesignSystem.AppendGlyph(sb, glyph, cx, cy, ink, size / 22);
			return;
		}

		var inner = size;
		if (IconRegistry.TryGetBadgeGradient(iconName, out var gradient))
		{
			// vendor glyphs are white on transparent: they sit on a small badge in the vendor's own colours
			var fill = ds.DiagonalGradient("vi-" + SanitizeId(iconName.ToLowerInvariant()), gradient.Light, gradient.Dark);
			_ = sb.Append("<rect x=\"").Append(cx - (size / 2)).Append("\" y=\"").Append(cy - (size / 2))
				.Append("\" width=\"").Append(size).Append("\" height=\"").Append(size)
				.Append("\" rx=\"").Append(DesignSystem.Num(size / 4)).Append("\" ry=\"").Append(DesignSystem.Num(size / 4))
				.Append("\" fill=\"").Append(fill).Append("\" />");
			inner = size * 0.7;
		}

		var svg = IconRegistry.Resolve(iconName);
		var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
		_ = sb.Append("<image x=\"").Append(cx - (inner / 2)).Append("\" y=\"").Append(cy - (inner / 2))
			.Append("\" width=\"").Append(inner).Append("\" height=\"").Append(inner)
			.Append("\" href=\"data:image/svg+xml;base64,").Append(base64).Append("\" />");
	}

	/// <summary>Strips anything unsafe for an XML <c>id</c> attribute value.</summary>
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
			.Append("\" r=\"3.5\" fill=\"").Append(DesignSystem.EdgeColor).Append("\" />");
	}

	private static void AppendEdge(StringBuilder sb, DesignSystem ds, PositionedArchitectureEdge edge, double cornerRadius, HashSet<string> serviceIds)
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
		_ = sb.Append("\" fill=\"none\" stroke=\"").Append(DesignSystem.EdgeColor).Append("\" stroke-width=\"").Append(ds.EdgeWidth).Append('"');

		if (edge.SourceArrow)
			_ = sb.Append(" marker-start=\"").Append(ds.Marker(MarkerShape.Arrow, atStart: true)).Append('"');
		if (edge.TargetArrow)
			_ = sb.Append(" marker-end=\"").Append(ds.Marker(MarkerShape.Arrow)).Append('"');

		_ = sb.Append(" />");

		// a plain end on a service is a small port, so the connection reads as attached to the card's side
		if (!edge.SourceArrow && serviceIds.Contains(edge.SourceId))
			AppendPort(sb, ds, edge.Points[0]);
		if (!edge.TargetArrow && serviceIds.Contains(edge.TargetId))
			AppendPort(sb, ds, edge.Points[^1]);
	}

	private static void AppendPort(StringBuilder sb, DesignSystem ds, Point p) =>
		sb.Append("\n<circle class=\"architecture-port\" cx=\"").Append(p.X).Append("\" cy=\"").Append(p.Y)
			.Append("\" r=\"3\" fill=\"var(--bg)\" stroke=\"").Append(DesignSystem.EdgeColor)
			.Append("\" stroke-width=\"").Append(ds.EdgeWidth).Append("\" />");

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
}
