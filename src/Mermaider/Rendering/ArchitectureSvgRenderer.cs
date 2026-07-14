using System.Text;
using Mermaider.Icons;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>Renders a <see cref="PositionedArchitectureDiagram"/> to SVG via pooled StringBuilder.</summary>
internal static class ArchitectureSvgRenderer
{
	private static readonly string GroupTitleAttrs = RenderConstants.TextAttrs.GroupHeaderFill + "var(--_text-sec)\"";
	private static readonly string ServiceLabelAttrs = RenderConstants.TextAttrs.NodeLabelCenterFill + "var(--_text)\"";

	internal static string Render(PositionedArchitectureDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
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

	internal static StringBuilder RenderToBuilder(PositionedArchitectureDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		StyleBlock.AppendSvgOpenTag(sb, diagram.Width, diagram.Height, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict);
		AppendMarkerDefs(sb);

		foreach (var group in diagram.Groups)
			AppendGroup(sb, group);

		foreach (var edge in diagram.Edges)
			AppendEdge(sb, edge);

		foreach (var service in diagram.Services)
			AppendService(sb, service);

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
			.Append("\" fill=\"var(--_arrow)\" />\n");
		_ = sb.Append("  </marker>\n");

		_ = sb.Append("  <marker id=\"arch-arrow-start\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(s)
			.Append("\" markerHeight=\"").Append(s)
			.Append("\" refX=\"0\" refY=\"").Append(h)
			.Append("\" orient=\"auto\">\n");
		_ = sb.Append("    <polygon points=\"").Append(s).Append(" 0, 0 ").Append(h)
			.Append(", ").Append(s).Append(' ').Append(s)
			.Append("\" fill=\"var(--_arrow)\" />\n");
		_ = sb.Append("  </marker>\n");
		_ = sb.Append("</defs>\n");
	}

	private const double GroupIconSize = 20;
	private const double GroupIconInset = 12;

	private static void AppendGroup(StringBuilder sb, PositionedArchitectureGroup group)
	{
		var r = RenderConstants.Radii.Group;
		_ = sb.Append("\n<g class=\"architecture-group\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, group.Id.AsSpan());
		_ = sb.Append("\">\n");

		// Transparent, dashed boundary — matches how Mermaid itself draws architecture groups:
		// a boundary you can see through, not a filled card. The group is still legible from its
		// border, title, and icon badge alone.
		_ = sb.Append("  <rect x=\"").Append(group.X).Append("\" y=\"").Append(group.Y)
			.Append("\" width=\"").Append(group.Width).Append("\" height=\"").Append(group.Height)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"none\" stroke=\"var(--_accent-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" stroke-dasharray=\"6 4\" />\n  ");

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
			GroupTitleAttrs);

		_ = sb.Append("\n</g>");
	}

	private static void AppendService(StringBuilder sb, PositionedArchitectureService service)
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
				.Append("\" fill=\"var(--_node-fill)\" stroke=\"var(--_node-stroke)\" stroke-width=\"")
				.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n");
		}

		var iconSize = Math.Min(service.Width, service.Height) * 0.55;
		AppendIcon(
			sb, service.Icon,
			service.X + ((service.Width - iconSize) / 2),
			service.Y + ((service.Height - iconSize) / 2),
			iconSize, iconSize);

		_ = sb.Append("  ");
		MultilineUtils.AppendMultilineText(
			sb, service.Title,
			service.X + (service.Width / 2), service.Y + service.Height + 16,
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
			.Append("\" r=\"3\" fill=\"var(--_node-stroke)\" />");
	}

	private static void AppendEdge(StringBuilder sb, PositionedArchitectureEdge edge)
	{
		if (edge.Points.Count < 2)
			return;

		_ = sb.Append("\n<path class=\"architecture-edge\" data-source=\"");
		MultilineUtils.AppendEscapedAttr(sb, edge.SourceId.AsSpan());
		_ = sb.Append("\" data-target=\"");
		MultilineUtils.AppendEscapedAttr(sb, edge.TargetId.AsSpan());
		_ = sb.Append("\" d=\"");
		SvgRenderer.BuildRoundedPath(sb, edge.Points, 6);
		_ = sb.Append("\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.Connector).Append('"');

		if (edge.SourceArrow)
			_ = sb.Append(" marker-start=\"url(#arch-arrow-start)\"");
		if (edge.TargetArrow)
			_ = sb.Append(" marker-end=\"url(#arch-arrow-end)\"");

		_ = sb.Append(" />");
	}
}
