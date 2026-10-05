using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class ClassSvgRenderer
{
	private static readonly string ClassHeaderAttrs =
		RenderConstants.TextAttrs.NodeLabelBoldCenterFill + "var(--_text)\"";

	private static readonly string PillLabelAttrs =
		RenderConstants.TextAttrs.ClassRelLabelFill + "var(--_text)\"";

	private static readonly string MemberFontSize = RenderConstants.FsVar.S;
	private static readonly int MemberFontWeight = RenderConstants.FontWeights.Member;
	private static readonly string AnnotationFontSize = RenderConstants.FsVar.Xs;
	private static readonly int AnnotationFontWeight = RenderConstants.FontWeights.Annotation;

	internal static string Render(PositionedClassDiagram diagram, SvgRenderContext context)
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

	internal static StringBuilder RenderToBuilder(PositionedClassDiagram diagram, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		StyleBlock.AppendSvgOpenTag(sb, diagram.Width, diagram.Height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles.Font, context.Styles.Strict, context.Styles.FontScale, context.Styles.MonoFont);
		AppendMarkerDefs(sb);

		// Same language as flowcharts and ER: one palette colour per connected cluster of classes, namespaces alternate.
		var palette = ClusterPalette.Build(
			diagram.Classes.Select(c => new ClusterBox(c.Id, c.X, c.Y, c.Width, c.Height)).ToList(),
			diagram.Relationships.Select(r => (r.From, r.To)),
			diagram.Namespaces.Select(n => new ClusterGroup(n.Name, n.X, n.Y, n.Width, n.Height, [])).ToList(),
			context.Styles.Colors);

		foreach (var ns in diagram.Namespaces)
			AppendNamespaceBox(sb, ns, palette);

		foreach (var rel in diagram.Relationships)
			AppendRelationship(sb, rel, context.EdgeRadius);

		foreach (var cls in diagram.Classes)
		{
			if (cls.IsLollipopTarget)
				AppendLollipopNode(sb, cls);
			else
				AppendClassBox(sb, cls, palette);
		}

		foreach (var rel in diagram.Relationships)
			AppendRelationshipLabels(sb, rel);

		foreach (var note in diagram.Notes)
			VisualLanguage.AppendNote(sb, note);

		_ = sb.Append("\n</svg>");
		return sb;
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
			.Append("\" fill=\"var(--bg)\" stroke=\"var(--_line)\" stroke-width=\"2\" />\n");
		_ = sb.Append("  </marker>\n");

		_ = sb.Append("  <marker id=\"cls-composition\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"").Append(w).Append("\" refY=\"").Append(hh)
			.Append("\" orient=\"auto-start-reverse\">\n");
		_ = sb.Append("    <polygon points=\"").Append(hw).Append(" 0, ").Append(w).Append(' ').Append(hh)
			.Append(", ").Append(hw).Append(' ').Append(h).Append(", 0 ").Append(hh)
			.Append("\" fill=\"var(--_line)\" stroke=\"var(--_line)\" stroke-width=\"1\" />\n");
		_ = sb.Append("  </marker>\n");

		_ = sb.Append("  <marker id=\"cls-aggregation\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"").Append(w).Append("\" refY=\"").Append(hh)
			.Append("\" orient=\"auto-start-reverse\">\n");
		_ = sb.Append("    <polygon points=\"").Append(hw).Append(" 0, ").Append(w).Append(' ').Append(hh)
			.Append(", ").Append(hw).Append(' ').Append(h).Append(", 0 ").Append(hh)
			.Append("\" fill=\"var(--bg)\" stroke=\"var(--_line)\" stroke-width=\"2\" />\n");
		_ = sb.Append("  </marker>\n");

		_ = sb.Append("  <marker id=\"cls-arrow\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"").Append(w)
			.Append("\" refY=\"").Append(hh)
			.Append("\" orient=\"auto-start-reverse\">\n");
		_ = sb.Append("    <polyline points=\"0 0, ").Append(w).Append(' ').Append(hh)
			.Append(", 0 ").Append(h)
			.Append("\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"2\" />\n");
		_ = sb.Append("  </marker>\n");

		_ = sb.Append("  <marker id=\"cls-lollipop\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"").Append(hw)
			.Append("\" refY=\"").Append(hh)
			.Append("\" orient=\"auto-start-reverse\">\n");
		_ = sb.Append("    <circle cx=\"").Append(hw).Append("\" cy=\"").Append(hh)
			.Append("\" r=\"").Append(hh - 1)
			.Append("\" fill=\"var(--bg)\" stroke=\"var(--_line)\" stroke-width=\"2\" />\n");
		_ = sb.Append("  </marker>\n");

		_ = sb.Append("</defs>\n");
	}

	private const double LollipopRadius = 8.0;

	private static void AppendLollipopNode(StringBuilder sb, PositionedClassNode cls)
	{
		var cx = cls.X + (cls.Width / 2);
		var cy = cls.Y + (cls.Height / 2);
		_ = sb.Append("\n<g class=\"class-node lollipop\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, cls.Id.AsSpan());
		_ = sb.Append("\">\n");
		_ = sb.Append("  <circle cx=\"").Append(cx).Append("\" cy=\"").Append(cy)
			.Append("\" r=\"").Append(LollipopRadius)
			.Append("\" fill=\"var(--bg)\" stroke=\"var(--_line)\" stroke-width=\"").Append(RenderConstants.StrokeWidths.Connector).Append("\" />\n");
		_ = sb.Append("  <text x=\"").Append(cx).Append("\" y=\"").Append(cy + LollipopRadius + 14)
			.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(RenderConstants.FontSizes.NodeLabel)
			.Append("\" font-weight=\"").Append(RenderConstants.FontWeights.NodeLabel)
			.Append("\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, cls.Label.AsSpan());
		_ = sb.Append("</text>\n");
		_ = sb.Append("</g>\n");
	}

	private static void AppendClassBox(StringBuilder sb, PositionedClassNode cls, ClusterPalette palette)
	{
		// Same as an ER entity: darker cluster border, tinted header, plain background, border-coloured separators.
		var border = palette.NodeStroke(cls.Id);
		var headerFill = palette.HeaderFill(cls.Id);
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
		// 1. Background — no stroke so separators are not buried under the box border
		_ = sb.Append("  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
			.Append("\" width=\"").Append(width).Append("\" height=\"").Append(height)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"var(--bg)\" />\n");

		// 2. Header: rounded top, square bottom
		VisualLanguage.AppendHeaderPath(sb, x, y, width, headerHeight, r, headerFill);

		var nameY = y + (headerHeight / 2);
		if (cls.Annotation != null)
		{
			var annotY = y + 12;
			_ = sb.Append("  <text x=\"").Append(x + (width / 2)).Append("\" y=\"").Append(annotY)
				.Append("\" text-anchor=\"middle\" dy=\"").Append(RenderConstants.TextBaselineShift)
				.Append("\" font-size=\"").Append(AnnotationFontSize)
				.Append("\" font-weight=\"").Append(AnnotationFontWeight)
				.Append("\" font-style=\"italic\" fill=\"var(--_text-sec)\">&lt;&lt;");
			MultilineUtils.AppendEscapedXml(sb, cls.Annotation.AsSpan());
			_ = sb.Append("&gt;&gt;</text>\n");
			nameY = y + (headerHeight / 2) + 6;
		}

		_ = sb.Append("  ");
		MultilineUtils.AppendMultilineText(
			sb, cls.Label, x + (width / 2), nameY,
			RenderConstants.FontSizes.NodeLabel,
			ClassHeaderAttrs);
		_ = sb.Append('\n');

		var attrTop = y + headerHeight;
		_ = sb.Append("  <line x1=\"").Append(x).Append("\" y1=\"").Append(attrTop)
			.Append("\" x2=\"").Append(x + width).Append("\" y2=\"").Append(attrTop)
			.Append("\" stroke=\"").Append(border).Append("\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n");

		const double memberRowH = 20;
		var (typeColW, _) = ClassMemberColumns.Measure(cls.Attributes.Concat(cls.Methods));
		var nameX = typeColW > 0
			? x + ClassMemberColumns.PadX + typeColW + ClassMemberColumns.Gap + ClassMemberColumns.PadX
			: x + ClassMemberColumns.PadX;

		var methodTop = attrTop + attrHeight;
		// Row dividers like ER attribute rows, then a section divider between attributes and methods
		for (var i = 0; i < cls.Attributes.Count - 1; i++)
			AppendRowDivider(sb, x, width, attrTop + 4 + ((i + 1) * memberRowH), border);
		for (var i = 0; i < cls.Methods.Count - 1; i++)
			AppendRowDivider(sb, x, width, methodTop + 4 + ((i + 1) * memberRowH), border);
		_ = sb.Append("  <line x1=\"").Append(x).Append("\" y1=\"").Append(methodTop)
			.Append("\" x2=\"").Append(x + width).Append("\" y2=\"").Append(methodTop)
			.Append("\" stroke=\"").Append(border).Append("\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n");

		// Type | name column divider
		if (typeColW > 0)
		{
			var divX = x + ClassMemberColumns.PadX + typeColW + ClassMemberColumns.Gap;
			_ = sb.Append("  <line x1=\"").Append(divX).Append("\" y1=\"").Append(attrTop)
				.Append("\" x2=\"").Append(divX).Append("\" y2=\"").Append(y + height)
				.Append("\" stroke=\"").Append(border).Append("\" stroke-width=\"1\" opacity=\"0.35\" />\n");
		}

		for (var i = 0; i < cls.Attributes.Count; i++)
		{
			_ = sb.Append("  ");
			AppendMember(sb, cls.Attributes[i], x + ClassMemberColumns.PadX, nameX, attrTop + 4 + (i * memberRowH) + (memberRowH / 2), border);
			_ = sb.Append('\n');
		}

		for (var i = 0; i < cls.Methods.Count; i++)
		{
			_ = sb.Append("  ");
			AppendMember(sb, cls.Methods[i], x + ClassMemberColumns.PadX, nameX, methodTop + 4 + (i * memberRowH) + (memberRowH / 2), border);
			_ = sb.Append('\n');
		}

		// Outer border drawn last so fills cannot cover the rounded corners
		_ = sb.Append("  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
			.Append("\" width=\"").Append(width).Append("\" height=\"").Append(height)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"none\" stroke=\"").Append(border).Append("\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n");

		_ = sb.Append("</g>");
	}

	private static void AppendRowDivider(StringBuilder sb, double x, double width, double y, string border) =>
		sb.Append("  <line x1=\"").Append(x).Append("\" y1=\"").Append(y)
			.Append("\" x2=\"").Append(x + width).Append("\" y2=\"").Append(y)
			.Append("\" stroke=\"").Append(border).Append("\" stroke-width=\"1\" opacity=\"0.35\" />\n");

	// Type (muted) in the first column, then visibility symbol in the cluster's border colour and the name in full text colour.
	private static void AppendMember(StringBuilder sb, ClassMember member, double typeX, double nameX, double y, string border)
	{
		var fontStyle = member.IsAbstract ? " font-style=\"italic\"" : "";
		var decoration = member.IsStatic ? " text-decoration=\"underline\"" : "";

		if (member.Type is { Length: > 0 })
		{
			_ = sb.Append("<text class=\"mono\" x=\"").Append(typeX).Append("\" y=\"").Append(y)
				.Append("\" dy=\"").Append(RenderConstants.TextBaselineShift)
				.Append("\" font-size=\"").Append(MemberFontSize)
				.Append("\" font-weight=\"").Append(MemberFontWeight)
				.Append("\" fill=\"var(--_text-sec)\">");
			MultilineUtils.AppendEscapedXml(sb, member.Type.AsSpan());
			_ = sb.Append("</text>");
		}

		_ = sb.Append("<text class=\"mono\" x=\"").Append(nameX).Append("\" y=\"").Append(y)
			.Append("\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(MemberFontSize)
			.Append("\" font-weight=\"").Append(MemberFontWeight).Append('"')
			.Append(fontStyle).Append(decoration).Append('>');

		var vis = ClassMemberColumns.VisibilitySymbol(member);
		if (vis.Length > 0)
			_ = sb.Append("<tspan fill=\"").Append(border).Append("\" font-weight=\"700\">").Append(vis).Append(" </tspan>");

		_ = sb.Append("<tspan fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, ClassMemberColumns.DisplayName(member).AsSpan());
		_ = sb.Append("</tspan></text>");
	}

	private static void AppendRelationship(StringBuilder sb, PositionedClassRelationship rel, double cornerRadius)
	{
		if (rel.Points.Count < 2)
			return;

		var isDashed = rel.Type is ClassRelationType.Dependency or ClassRelationType.Realization;
		var dashArray = isDashed ? " stroke-dasharray=\"4 4\"" : "";
		var markers = GetMarkers(rel.Type, rel.MarkerAt);

		_ = sb.Append("\n<path class=\"class-relationship\" data-from=\"");
		MultilineUtils.AppendEscapedAttr(sb, rel.From.AsSpan());
		_ = sb.Append("\" data-to=\"");
		MultilineUtils.AppendEscapedAttr(sb, rel.To.AsSpan());
		_ = sb.Append("\" data-type=\"").Append(rel.Type.ToLower());
		_ = sb.Append("\" data-marker-at=\"").Append(rel.MarkerAt == ClassMarkerAt.From ? "from" : "to").Append('"');
		if (rel.Label != null)
		{
			_ = sb.Append(" data-label=\"");
			MultilineUtils.AppendEscapedAttr(sb, rel.Label.AsSpan());
			_ = sb.Append('"');
		}
		_ = sb.Append(" d=\"");
		if (SvgRenderer.IsOrthogonal(rel.Points))
			SvgRenderer.BuildOrthogonalPath(sb, rel.Points, cornerRadius);
		else
			SvgRenderer.BuildRoundedPath(sb, rel.Points, cornerRadius);
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
			// Lollipop: no marker — the target node itself renders as a circle
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
			var pos = rel.LabelPosition ?? VisualLanguage.PathMidpoint(rel.Points);
			var metrics = TextMetrics.MeasureMultiline(rel.Label.AsSpan(), RenderConstants.FontSizes.EdgeLabel, RenderConstants.FontWeights.EdgeLabel);
			_ = sb.Append('\n');
			VisualLanguage.AppendLabelPill(sb, pos.X, pos.Y, metrics.Width, metrics.Height);
			_ = sb.Append('\n');
			MultilineUtils.AppendMultilineText(
				sb, rel.Label, pos.X, pos.Y,
				RenderConstants.FontSizes.EdgeLabel,
				PillLabelAttrs);
		}

		if (rel.FromCardinality != null)
			AppendCardinality(sb, rel.FromCardinality, rel.Points[0], rel.Points[1]);

		if (rel.ToCardinality != null)
			AppendCardinality(sb, rel.ToCardinality, rel.Points[^1], rel.Points[^2]);
	}

	// A cardinality is a small pill on the line next to the class it belongs to (clear of the end marker).
	private static void AppendCardinality(StringBuilder sb, string text, Point end, Point toward)
	{
		const double distance = 30;
		var dx = toward.X - end.X;
		var dy = toward.Y - end.Y;
		var len = Math.Sqrt((dx * dx) + (dy * dy));
		var (ux, uy) = len < 0.01 ? (0.0, 1.0) : (dx / len, dy / len);
		var along = Math.Min(distance, Math.Max(len - 8, 8));
		var cx = end.X + (ux * along);
		var cy = end.Y + (uy * along);
		var metrics = TextMetrics.MeasureMultiline(text.AsSpan(), RenderConstants.FontSizes.EdgeLabel, RenderConstants.FontWeights.EdgeLabel);
		_ = sb.Append('\n');
		VisualLanguage.AppendLabelPill(sb, cx, cy, metrics.Width, metrics.Height);
		_ = sb.Append('\n');
		MultilineUtils.AppendMultilineText(sb, text, cx, cy, RenderConstants.FontSizes.EdgeLabel, PillLabelAttrs);
	}

	private static void AppendNamespaceBox(StringBuilder sb, PositionedClassNamespace ns, ClusterPalette palette)
	{
		// A namespace is a subgraph: palette-tinted box, solid border, title in the border colour.
		var stroke = palette.GroupStroke(ns.Name);
		_ = sb.Append("\n<g class=\"ns-box\">\n");
		_ = sb.Append("  <rect x=\"").Append(ns.X).Append("\" y=\"").Append(ns.Y)
			.Append("\" width=\"").Append(ns.Width).Append("\" height=\"").Append(ns.Height)
			.Append("\" rx=\"").Append(RenderConstants.Radii.Group).Append("\" ry=\"").Append(RenderConstants.Radii.Group)
			.Append("\" fill=\"").Append(palette.GroupFill(ns.Name, 0)).Append("\" stroke=\"").Append(stroke)
			.Append("\" stroke-width=\"").Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n  ");
		MultilineUtils.AppendMultilineText(
			sb, ns.Name, ns.X + 12, ns.Y + 16,
			RenderConstants.FontSizes.GroupHeader,
			RenderConstants.TextAttrs.GroupHeaderFill + stroke + "\"");
		_ = sb.Append("\n</g>");
	}
}
