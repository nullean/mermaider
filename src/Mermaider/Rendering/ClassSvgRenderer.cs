using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;
using Microsoft.Extensions.ObjectPool;

namespace Mermaider.Rendering;

internal static class ClassSvgRenderer
{
	private static readonly ObjectPool<StringBuilder> StringBuilderPool =
		new DefaultObjectPoolProvider().CreateStringBuilderPool(initialCapacity: 4096, maximumRetainedCapacity: 64 * 1024);

	private static readonly int MemberFontSize = RenderConstants.FontSizes.Member;
	private static readonly int MemberFontWeight = RenderConstants.FontWeights.Member;
	private static readonly int AnnotationFontSize = RenderConstants.FontSizes.Annotation;
	private static readonly int AnnotationFontWeight = RenderConstants.FontWeights.Annotation;

	internal static string Render(PositionedClassDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null)
	{
		var sb = StringBuilderPool.Get();
		try
		{
			StyleBlock.AppendSvgOpenTag(sb, diagram.Width, diagram.Height, colors, transparent);
			StyleBlock.AppendStyleBlock(sb, font, strict);
			AppendMarkerDefs(sb);

			foreach (var rel in diagram.Relationships)
				AppendRelationship(sb, rel);

			foreach (var cls in diagram.Classes)
				AppendClassBox(sb, cls);

			foreach (var rel in diagram.Relationships)
				AppendRelationshipLabels(sb, rel);

			_ = sb.Append("\n</svg>");
			return sb.ToString();
		}
		finally
		{
			_ = sb.Clear();
			StringBuilderPool.Return(sb);
		}
	}

	private static void AppendMarkerDefs(StringBuilder sb)
	{
		var s = RenderConstants.ArrowHead.Size;
		var w = s;
		var h = s;
		var hw = w / 2.0;
		var hh = h / 2.0;

		_ = sb.Append("\n<defs>\n");

		_ = sb.Append("  <marker id=\"cls-inherit\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"").Append(w)
			.Append("\" refY=\"").Append(hh)
			.Append("\" orient=\"auto-start-reverse\">\n");
		_ = sb.Append("    <polygon points=\"0 0, ").Append(w).Append(' ').Append(hh)
			.Append(", 0 ").Append(h)
			.Append("\" fill=\"var(--bg)\" stroke=\"var(--_arrow)\" stroke-width=\"1.5\" />\n");
		_ = sb.Append("  </marker>\n");

		_ = sb.Append("  <marker id=\"cls-composition\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"0\" refY=\"").Append(hh)
			.Append("\" orient=\"auto-start-reverse\">\n");
		_ = sb.Append("    <polygon points=\"").Append(hw).Append(" 0, ").Append(w).Append(' ').Append(hh)
			.Append(", ").Append(hw).Append(' ').Append(h).Append(", 0 ").Append(hh)
			.Append("\" fill=\"var(--_arrow)\" stroke=\"var(--_arrow)\" stroke-width=\"1\" />\n");
		_ = sb.Append("  </marker>\n");

		_ = sb.Append("  <marker id=\"cls-aggregation\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"0\" refY=\"").Append(hh)
			.Append("\" orient=\"auto-start-reverse\">\n");
		_ = sb.Append("    <polygon points=\"").Append(hw).Append(" 0, ").Append(w).Append(' ').Append(hh)
			.Append(", ").Append(hw).Append(' ').Append(h).Append(", 0 ").Append(hh)
			.Append("\" fill=\"var(--bg)\" stroke=\"var(--_arrow)\" stroke-width=\"1.5\" />\n");
		_ = sb.Append("  </marker>\n");

		_ = sb.Append("  <marker id=\"cls-arrow\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"").Append(w)
			.Append("\" refY=\"").Append(hh)
			.Append("\" orient=\"auto-start-reverse\">\n");
		_ = sb.Append("    <polyline points=\"0 0, ").Append(w).Append(' ').Append(hh)
			.Append(", 0 ").Append(h)
			.Append("\" fill=\"none\" stroke=\"var(--_arrow)\" stroke-width=\"1.5\" />\n");
		_ = sb.Append("  </marker>\n");

		_ = sb.Append("</defs>\n");
	}

	private static void AppendClassBox(StringBuilder sb, PositionedClassNode cls)
	{
		var (x, y, width, height) = (cls.X, cls.Y, cls.Width, cls.Height);
		var headerHeight = cls.HeaderHeight;
		var attrHeight = cls.AttrHeight;

		_ = sb.Append("\n<g class=\"class-node\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, cls.Id.AsSpan());
		_ = sb.Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, cls.Label.AsSpan());
		_ = sb.Append('"');
		if (cls.Annotation != null)
		{
			_ = sb.Append(" data-annotation=\"");
			MultilineUtils.AppendEscapedAttr(sb, cls.Annotation.AsSpan());
			_ = sb.Append('"');
		}
		_ = sb.Append(">\n");

		var r = RenderConstants.Radii.Rectangle;
		_ = sb.Append("  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
			.Append("\" width=\"").Append(width).Append("\" height=\"").Append(height)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"var(--_node-fill)\" stroke=\"var(--_node-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n");

		_ = sb.Append("  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
			.Append("\" width=\"").Append(width).Append("\" height=\"").Append(headerHeight)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"var(--_group-hdr)\" stroke=\"var(--_node-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n");

		var nameY = y + (headerHeight / 2);
		if (cls.Annotation != null)
		{
			var annotY = y + 12;
			_ = sb.Append("  <text x=\"").Append(x + (width / 2)).Append("\" y=\"").Append(annotY)
				.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
				.Append("\" font-size=\"").Append(AnnotationFontSize)
				.Append("\" font-weight=\"").Append(AnnotationFontWeight)
				.Append("\" font-style=\"italic\" fill=\"var(--_text-muted)\">&lt;&lt;");
			MultilineUtils.AppendEscapedXml(sb, cls.Annotation.AsSpan());
			_ = sb.Append("&gt;&gt;</text>\n");
			nameY = y + (headerHeight / 2) + 6;
		}

		_ = sb.Append("  ");
		MultilineUtils.AppendMultilineText(
			sb, cls.Label, x + (width / 2), nameY,
			RenderConstants.FontSizes.NodeLabel,
			$"text-anchor=\"middle\" font-size=\"{RenderConstants.FontSizes.NodeLabel}\" font-weight=\"700\" fill=\"var(--_text)\"");
		_ = sb.Append('\n');

		var attrTop = y + headerHeight;
		_ = sb.Append("  <line x1=\"").Append(x).Append("\" y1=\"").Append(attrTop)
			.Append("\" x2=\"").Append(x + width).Append("\" y2=\"").Append(attrTop)
			.Append("\" stroke=\"var(--_node-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.InnerBox).Append("\" />\n");

		const double memberRowH = 20;
		const double boxPadX = 8;
		foreach (var (member, i) in cls.Attributes.Select((m, i) => (m, i)))
		{
			var memberY = attrTop + 4 + (i * memberRowH) + (memberRowH / 2);
			_ = sb.Append("  ");
			AppendMember(sb, member, x + boxPadX, memberY);
			_ = sb.Append('\n');
		}

		var methodTop = attrTop + attrHeight;
		_ = sb.Append("  <line x1=\"").Append(x).Append("\" y1=\"").Append(methodTop)
			.Append("\" x2=\"").Append(x + width).Append("\" y2=\"").Append(methodTop)
			.Append("\" stroke=\"var(--_node-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.InnerBox).Append("\" />\n");

		foreach (var (member, i) in cls.Methods.Select((m, i) => (m, i)))
		{
			var memberY = methodTop + 4 + (i * memberRowH) + (memberRowH / 2);
			_ = sb.Append("  ");
			AppendMember(sb, member, x + boxPadX, memberY);
			_ = sb.Append('\n');
		}

		_ = sb.Append("</g>");
	}

	private static void AppendMember(StringBuilder sb, ClassMember member, double x, double y)
	{
		var fontStyle = member.IsAbstract ? " font-style=\"italic\"" : "";
		var decoration = member.IsStatic ? " text-decoration=\"underline\"" : "";

		_ = sb.Append("<text class=\"mono\" x=\"").Append(x).Append("\" y=\"").Append(y)
			.Append("\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(MemberFontSize)
			.Append("\" font-weight=\"").Append(MemberFontWeight).Append('"')
			.Append(fontStyle).Append(decoration).Append('>');

		if (member.Visibility != ClassVisibility.None)
		{
			var vis = member.Visibility switch
			{
				ClassVisibility.Public => "+",
				ClassVisibility.Private => "-",
				ClassVisibility.Protected => "#",
				ClassVisibility.Package => "~",
				_ => "",
			};
			_ = sb.Append("<tspan fill=\"var(--_text-faint)\">").Append(vis).Append(" </tspan>");
		}

		var displayName = member.IsMethod ? $"{member.Name}({member.Params ?? ""})" : member.Name;
		_ = sb.Append("<tspan fill=\"var(--_text-sec)\">");
		MultilineUtils.AppendEscapedXml(sb, displayName.AsSpan());
		_ = sb.Append("</tspan>");

		if (member.Type != null)
		{
			_ = sb.Append("<tspan fill=\"var(--_text-faint)\">: </tspan>");
			_ = sb.Append("<tspan fill=\"var(--_text-muted)\">");
			MultilineUtils.AppendEscapedXml(sb, member.Type.AsSpan());
			_ = sb.Append("</tspan>");
		}

		_ = sb.Append("</text>");
	}

	private static void AppendRelationship(StringBuilder sb, PositionedClassRelationship rel)
	{
		if (rel.Points.Count < 2)
			return;

		var isDashed = rel.Type is ClassRelationType.Dependency or ClassRelationType.Realization;
		var dashArray = isDashed ? " stroke-dasharray=\"6 4\"" : "";
		var markers = GetMarkers(rel.Type, rel.MarkerAt);

		_ = sb.Append("\n<path class=\"class-relationship\" data-from=\"");
		MultilineUtils.AppendEscapedAttr(sb, rel.From.AsSpan());
		_ = sb.Append("\" data-to=\"");
		MultilineUtils.AppendEscapedAttr(sb, rel.To.AsSpan());
		_ = sb.Append("\" data-type=\"").Append(rel.Type.ToString().ToLowerInvariant());
		_ = sb.Append("\" data-marker-at=\"").Append(rel.MarkerAt == ClassMarkerAt.From ? "from" : "to").Append('"');
		if (rel.Label != null)
		{
			_ = sb.Append(" data-label=\"");
			MultilineUtils.AppendEscapedAttr(sb, rel.Label.AsSpan());
			_ = sb.Append('"');
		}
		_ = sb.Append(" d=\"");
		SvgRenderer.BuildRoundedPath(sb, rel.Points, 6);
		_ = sb.Append("\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.Connector).Append('"').Append(dashArray).Append(markers).Append(" />");
	}

	private static string GetMarkers(ClassRelationType type, ClassMarkerAt markerAt)
	{
		var markerId = type switch
		{
			ClassRelationType.Inheritance or ClassRelationType.Realization => "cls-inherit",
			ClassRelationType.Composition => "cls-composition",
			ClassRelationType.Aggregation => "cls-aggregation",
			ClassRelationType.Association or ClassRelationType.Dependency => "cls-arrow",
			_ => null,
		};
		if (markerId == null)
			return "";

		return markerAt == ClassMarkerAt.From
			? $" marker-start=\"url(#{markerId})\""
			: $" marker-end=\"url(#{markerId})\"";
	}

	private static void AppendRelationshipLabels(StringBuilder sb, PositionedClassRelationship rel)
	{
		if (rel.Label == null && rel.FromCardinality == null && rel.ToCardinality == null)
			return;
		if (rel.Points.Count < 2)
			return;

		if (rel.Label != null)
		{
			var pos = rel.LabelPosition ?? Midpoint(rel.Points);
			_ = sb.Append('\n');
			MultilineUtils.AppendMultilineText(
				sb, rel.Label, pos.X, pos.Y - 8,
				RenderConstants.FontSizes.EdgeLabel,
				$"font-size=\"{RenderConstants.FontSizes.EdgeLabel}\" text-anchor=\"middle\" font-weight=\"{RenderConstants.FontWeights.EdgeLabel}\" fill=\"var(--_text-muted)\"");
		}

		if (rel.FromCardinality != null)
		{
			var p = rel.Points[0];
			var next = rel.Points[1];
			var offset = CardinalityOffset(p, next);
			_ = sb.Append('\n');
			MultilineUtils.AppendMultilineText(
				sb, rel.FromCardinality, p.X + offset.X, p.Y + offset.Y,
				RenderConstants.FontSizes.EdgeLabel,
				$"font-size=\"{RenderConstants.FontSizes.EdgeLabel}\" text-anchor=\"middle\" font-weight=\"{RenderConstants.FontWeights.EdgeLabel}\" fill=\"var(--_text-muted)\"");
		}

		if (rel.ToCardinality != null)
		{
			var p = rel.Points[^1];
			var prev = rel.Points[^2];
			var offset = CardinalityOffset(p, prev);
			_ = sb.Append('\n');
			MultilineUtils.AppendMultilineText(
				sb, rel.ToCardinality, p.X + offset.X, p.Y + offset.Y,
				RenderConstants.FontSizes.EdgeLabel,
				$"font-size=\"{RenderConstants.FontSizes.EdgeLabel}\" text-anchor=\"middle\" font-weight=\"{RenderConstants.FontWeights.EdgeLabel}\" fill=\"var(--_text-muted)\"");
		}
	}

	private static Point Midpoint(IReadOnlyList<Point> points)
	{
		if (points.Count == 0)
			return new Point(0, 0);
		var mid = points.Count / 2;
		return points[mid];
	}

	private static (double X, double Y) CardinalityOffset(Point from, Point to)
	{
		var dx = to.X - from.X;
		var dy = to.Y - from.Y;
		if (Math.Abs(dx) > Math.Abs(dy))
			return (dx > 0 ? 14 : -14, -10);
		return (-14, dy > 0 ? 14 : -14);
	}
}
