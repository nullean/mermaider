using System.Text;
using Mermaid.Models;
using Mermaid.Text;
using Mermaid.Theming;
using Microsoft.Extensions.ObjectPool;

namespace Mermaid.Rendering;

internal static class ClassSvgRenderer
{
	private static readonly ObjectPool<StringBuilder> s_sbPool =
		new DefaultObjectPoolProvider().CreateStringBuilderPool(initialCapacity: 4096, maximumRetainedCapacity: 64 * 1024);

	private const int MemberFontSize = 11;
	private const int MemberFontWeight = 400;
	private const int AnnotationFontSize = 10;
	private const int AnnotationFontWeight = 500;

	internal static string Render(PositionedClassDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null)
	{
		var sb = s_sbPool.Get();
		try
		{
			StyleBlock.AppendSvgOpenTag(sb, diagram.Width, diagram.Height, colors, transparent);
			StyleBlock.AppendStyleBlock(sb, font, strict: strict);
			AppendMarkerDefs(sb);

			foreach (var rel in diagram.Relationships)
				AppendRelationship(sb, rel);

			foreach (var cls in diagram.Classes)
				AppendClassBox(sb, cls);

			foreach (var rel in diagram.Relationships)
				AppendRelationshipLabels(sb, rel);

			sb.Append("\n</svg>");
			return sb.ToString();
		}
		finally
		{
			sb.Clear();
			s_sbPool.Return(sb);
		}
	}

	private static void AppendMarkerDefs(StringBuilder sb)
	{
		var s = RenderConstants.ArrowHead.Size;
		var w = s;
		var h = s;
		var hw = w / 2.0;
		var hh = h / 2.0;

		sb.Append("\n<defs>\n");

		sb.Append("  <marker id=\"cls-inherit\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"").Append(w)
			.Append("\" refY=\"").Append(hh)
			.Append("\" orient=\"auto-start-reverse\">\n");
		sb.Append("    <polygon points=\"0 0, ").Append(w).Append(' ').Append(hh)
			.Append(", 0 ").Append(h)
			.Append("\" fill=\"var(--bg)\" stroke=\"var(--_arrow)\" stroke-width=\"1.5\" />\n");
		sb.Append("  </marker>\n");

		sb.Append("  <marker id=\"cls-composition\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"0\" refY=\"").Append(hh)
			.Append("\" orient=\"auto-start-reverse\">\n");
		sb.Append("    <polygon points=\"").Append(hw).Append(" 0, ").Append(w).Append(' ').Append(hh)
			.Append(", ").Append(hw).Append(' ').Append(h).Append(", 0 ").Append(hh)
			.Append("\" fill=\"var(--_arrow)\" stroke=\"var(--_arrow)\" stroke-width=\"1\" />\n");
		sb.Append("  </marker>\n");

		sb.Append("  <marker id=\"cls-aggregation\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"0\" refY=\"").Append(hh)
			.Append("\" orient=\"auto-start-reverse\">\n");
		sb.Append("    <polygon points=\"").Append(hw).Append(" 0, ").Append(w).Append(' ').Append(hh)
			.Append(", ").Append(hw).Append(' ').Append(h).Append(", 0 ").Append(hh)
			.Append("\" fill=\"var(--bg)\" stroke=\"var(--_arrow)\" stroke-width=\"1.5\" />\n");
		sb.Append("  </marker>\n");

		sb.Append("  <marker id=\"cls-arrow\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"").Append(w)
			.Append("\" refY=\"").Append(hh)
			.Append("\" orient=\"auto-start-reverse\">\n");
		sb.Append("    <polyline points=\"0 0, ").Append(w).Append(' ').Append(hh)
			.Append(", 0 ").Append(h)
			.Append("\" fill=\"none\" stroke=\"var(--_arrow)\" stroke-width=\"1.5\" />\n");
		sb.Append("  </marker>\n");

		sb.Append("</defs>\n");
	}

	private static void AppendClassBox(StringBuilder sb, PositionedClassNode cls)
	{
		var (x, y, width, height) = (cls.X, cls.Y, cls.Width, cls.Height);
		var headerHeight = cls.HeaderHeight;
		var attrHeight = cls.AttrHeight;

		sb.Append("\n<g class=\"class-node\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, cls.Id.AsSpan());
		sb.Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, cls.Label.AsSpan());
		sb.Append('"');
		if (cls.Annotation != null)
		{
			sb.Append(" data-annotation=\"");
			MultilineUtils.AppendEscapedAttr(sb, cls.Annotation.AsSpan());
			sb.Append('"');
		}
		sb.Append(">\n");

		var r = RenderConstants.Radii.Rectangle;
		sb.Append("  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
			.Append("\" width=\"").Append(width).Append("\" height=\"").Append(height)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"var(--_node-fill)\" stroke=\"var(--_node-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n");

		sb.Append("  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
			.Append("\" width=\"").Append(width).Append("\" height=\"").Append(headerHeight)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"var(--_group-hdr)\" stroke=\"var(--_node-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n");

		var nameY = y + headerHeight / 2;
		if (cls.Annotation != null)
		{
			var annotY = y + 12;
			sb.Append("  <text x=\"").Append(x + width / 2).Append("\" y=\"").Append(annotY)
				.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
				.Append("\" font-size=\"").Append(AnnotationFontSize)
				.Append("\" font-weight=\"").Append(AnnotationFontWeight)
				.Append("\" font-style=\"italic\" fill=\"var(--_text-muted)\">&lt;&lt;");
			MultilineUtils.AppendEscapedXml(sb, cls.Annotation.AsSpan());
			sb.Append("&gt;&gt;</text>\n");
			nameY = y + headerHeight / 2 + 6;
		}

		sb.Append("  ");
		MultilineUtils.AppendMultilineText(
			sb, cls.Label, x + width / 2, nameY,
			RenderConstants.FontSizes.NodeLabel,
			$"text-anchor=\"middle\" font-size=\"{RenderConstants.FontSizes.NodeLabel}\" font-weight=\"700\" fill=\"var(--_text)\"");
		sb.Append('\n');

		var attrTop = y + headerHeight;
		sb.Append("  <line x1=\"").Append(x).Append("\" y1=\"").Append(attrTop)
			.Append("\" x2=\"").Append(x + width).Append("\" y2=\"").Append(attrTop)
			.Append("\" stroke=\"var(--_node-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.InnerBox).Append("\" />\n");

		const double memberRowH = 20;
		const double boxPadX = 8;
		foreach (var (member, i) in cls.Attributes.Select((m, i) => (m, i)))
		{
			var memberY = attrTop + 4 + i * memberRowH + memberRowH / 2;
			sb.Append("  ");
			AppendMember(sb, member, x + boxPadX, memberY);
			sb.Append('\n');
		}

		var methodTop = attrTop + attrHeight;
		sb.Append("  <line x1=\"").Append(x).Append("\" y1=\"").Append(methodTop)
			.Append("\" x2=\"").Append(x + width).Append("\" y2=\"").Append(methodTop)
			.Append("\" stroke=\"var(--_node-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.InnerBox).Append("\" />\n");

		foreach (var (member, i) in cls.Methods.Select((m, i) => (m, i)))
		{
			var memberY = methodTop + 4 + i * memberRowH + memberRowH / 2;
			sb.Append("  ");
			AppendMember(sb, member, x + boxPadX, memberY);
			sb.Append('\n');
		}

		sb.Append("</g>");
	}

	private static void AppendMember(StringBuilder sb, ClassMember member, double x, double y)
	{
		var fontStyle = member.IsAbstract ? " font-style=\"italic\"" : "";
		var decoration = member.IsStatic ? " text-decoration=\"underline\"" : "";

		sb.Append("<text x=\"").Append(x).Append("\" y=\"").Append(y)
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
			sb.Append("<tspan fill=\"var(--_text-faint)\">").Append(vis).Append(" </tspan>");
		}

		var displayName = member.IsMethod ? $"{member.Name}({member.Params ?? ""})" : member.Name;
		sb.Append("<tspan fill=\"var(--_text-sec)\">");
		MultilineUtils.AppendEscapedXml(sb, displayName.AsSpan());
		sb.Append("</tspan>");

		if (member.Type != null)
		{
			sb.Append("<tspan fill=\"var(--_text-faint)\">: </tspan>");
			sb.Append("<tspan fill=\"var(--_text-muted)\">");
			MultilineUtils.AppendEscapedXml(sb, member.Type.AsSpan());
			sb.Append("</tspan>");
		}

		sb.Append("</text>");
	}

	private static void AppendRelationship(StringBuilder sb, PositionedClassRelationship rel)
	{
		if (rel.Points.Count < 2) return;

		var isDashed = rel.Type is ClassRelationType.Dependency or ClassRelationType.Realization;
		var dashArray = isDashed ? " stroke-dasharray=\"6 4\"" : "";
		var markers = GetMarkers(rel.Type, rel.MarkerAt);

		sb.Append("\n<polyline class=\"class-relationship\" data-from=\"");
		MultilineUtils.AppendEscapedAttr(sb, rel.From.AsSpan());
		sb.Append("\" data-to=\"");
		MultilineUtils.AppendEscapedAttr(sb, rel.To.AsSpan());
		sb.Append("\" data-type=\"").Append(rel.Type.ToString().ToLowerInvariant());
		sb.Append("\" data-marker-at=\"").Append(rel.MarkerAt == ClassMarkerAt.From ? "from" : "to").Append('"');
		if (rel.Label != null)
		{
			sb.Append(" data-label=\"");
			MultilineUtils.AppendEscapedAttr(sb, rel.Label.AsSpan());
			sb.Append('"');
		}
		sb.Append(" points=\"");
		for (var i = 0; i < rel.Points.Count; i++)
		{
			if (i > 0) sb.Append(' ');
			sb.Append(rel.Points[i].X).Append(',').Append(rel.Points[i].Y);
		}
		sb.Append("\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"")
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
		if (markerId == null) return "";

		return markerAt == ClassMarkerAt.From
			? $" marker-start=\"url(#{markerId})\""
			: $" marker-end=\"url(#{markerId})\"";
	}

	private static void AppendRelationshipLabels(StringBuilder sb, PositionedClassRelationship rel)
	{
		if (rel.Label == null && rel.FromCardinality == null && rel.ToCardinality == null) return;
		if (rel.Points.Count < 2) return;

		if (rel.Label != null)
		{
			var pos = rel.LabelPosition ?? Midpoint(rel.Points);
			sb.Append('\n');
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
			sb.Append('\n');
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
			sb.Append('\n');
			MultilineUtils.AppendMultilineText(
				sb, rel.ToCardinality, p.X + offset.X, p.Y + offset.Y,
				RenderConstants.FontSizes.EdgeLabel,
				$"font-size=\"{RenderConstants.FontSizes.EdgeLabel}\" text-anchor=\"middle\" font-weight=\"{RenderConstants.FontWeights.EdgeLabel}\" fill=\"var(--_text-muted)\"");
		}
	}

	private static Point Midpoint(IReadOnlyList<Point> points)
	{
		if (points.Count == 0) return new Point(0, 0);
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
