using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class ClassSvgRenderer
{
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
		StyleBlock.AppendStyleBlock(sb, context.Styles);
		var ds = DesignSystem.For(context);

		// Same language as flowcharts and ER: one family per connected cluster of classes, modifiers keep a hashed family,
		// namespaces are the shared container and alternate through the palette (never a member's family).
		var palette = ClusterPalette.Build(
			diagram.Classes.Select(c => new ClusterBox(c.Id, c.X, c.Y, c.Width, c.Height)).ToList(),
			diagram.Relationships.Select(r => (r.From, r.To)),
			diagram.Namespaces.Select(n => new ClusterGroup(n.Name, n.X, n.Y, n.Width, n.Height, [])).ToList(),
			context.Styles.Colors,
			AnnotationSlots(diagram.Classes, context.Styles.Colors.AutoPalette().Length)).WithTint(ds.TintStrength);

		foreach (var ns in diagram.Namespaces)
			ds.AppendContainerBody(sb, ns.X, ns.Y, ns.Width, ns.Height, palette.GroupFamily(ns.Name), 0, "subgraph ns-box", NamespaceAttrs(ns));

		foreach (var rel in diagram.Relationships)
			AppendRelationship(sb, ds, rel, context.EdgeRadius);

		foreach (var ns in diagram.Namespaces)
			ds.AppendContainerHeader(sb, ns.X, ns.Y, ns.Width, ns.Name, palette.GroupFamily(ns.Name));

		foreach (var cls in diagram.Classes)
		{
			if (cls.IsLollipopTarget)
				AppendLollipopNode(sb, ds, cls);
			else
				AppendClassBox(sb, ds, cls, palette.Family(cls.Id));
		}

		foreach (var rel in diagram.Relationships)
			AppendRelationshipLabels(sb, ds, rel);

		foreach (var note in diagram.Notes)
			ds.AppendNote(sb, note.X, note.Y, note.Width, note.Height, note.Text, note.LineFrom, note.LineTo);

		ds.Close(sb);
		return sb;
	}

	private static string NamespaceAttrs(PositionedClassNamespace ns)
	{
		var attrs = new StringBuilder("data-id=\"");
		MultilineUtils.AppendEscapedAttr(attrs, ns.Name.AsSpan());
		_ = attrs.Append('"');
		return attrs.ToString();
	}

	/// <summary>
	/// Palette slot per annotated class: the modifier name is hashed (FNV-1a, so it is the same in every process and diagram) modulo the
	/// auto palette, skipping slot 0 which stays the default box colour. Every class with the same modifier
	/// (<c>&lt;&lt;abstract&gt;&gt;</c>) shares one colour; modifiers whose hashes collide in one diagram are separated by probing to the
	/// next free slot, in alphabetical order, so the result is deterministic.
	/// </summary>
	private static Dictionary<string, int> AnnotationSlots(IReadOnlyList<PositionedClassNode> classes, int paletteLength)
	{
		var result = new Dictionary<string, int>(StringComparer.Ordinal);
		if (paletteLength < 2)
			return result;

		static string? Normalise(string? annotation)
		{
			var name = annotation?.Trim().Trim('<', '>').Trim().ToLowerInvariant();
			return string.IsNullOrEmpty(name) ? null : name == "enum" ? "enumeration" : name;
		}

		var names = classes.Where(c => !c.IsLollipopTarget).Select(c => Normalise(c.Annotation)).Where(n => n is not null).Select(n => n!)
			.Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
		var slotOfName = new Dictionary<string, int>(StringComparer.Ordinal);
		var usable = paletteLength - 1;
		foreach (var name in names)
		{
			var slot = 1 + (int)(Fnv1a(name) % (uint)usable);
			for (var probe = 0; probe < usable && slotOfName.ContainsValue(slot); probe++)
				slot = 1 + (slot % usable);
			slotOfName[name] = slot;
		}

		foreach (var c in classes.Where(c => !c.IsLollipopTarget))
		{
			if (Normalise(c.Annotation) is { } name)
				result[c.Id] = slotOfName[name];
		}

		return result;
	}

	private static uint Fnv1a(string text)
	{
		var hash = 2166136261u;
		foreach (var b in Encoding.UTF8.GetBytes(text))
			hash = (hash ^ b) * 16777619u;
		return hash;
	}

	private const double LollipopRadius = 8.0;

	private static void AppendLollipopNode(StringBuilder sb, DesignSystem ds, PositionedClassNode cls)
	{
		// circle at the top of the node (where edges arrive) in the line colour, name underneath
		var cx = cls.X + (cls.Width / 2);
		var cy = cls.Y + LollipopRadius;
		_ = sb.Append("\n<g class=\"class-node lollipop\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, cls.Id.AsSpan());
		_ = sb.Append("\">\n  <circle cx=\"").Append(cx).Append("\" cy=\"").Append(cy)
			.Append("\" r=\"").Append(LollipopRadius)
			.Append("\" fill=\"var(--bg)\" stroke=\"").Append(ds.OwnMarkerColor).Append("\" stroke-width=\"").Append(ds.EdgeWidth).Append("\" />\n  ");
		ds.AppendText(sb, cls.Label, cx, cy + LollipopRadius + 14, TypeRole.Label);
		_ = sb.Append("\n</g>");
	}

	/// <summary>Height of one member row: the shared 28px row, or less when an external layout packed the rows tighter.</summary>
	private static double MemberRowHeight(double sectionHeight, int count) =>
		count == 0 ? 0 : Math.Min(DesignSystem.RowHeight, sectionHeight / count);

	private static void AppendClassBox(StringBuilder sb, DesignSystem ds, PositionedClassNode cls, ColorFamily family)
	{
		var (x, y, width, height) = (cls.X, cls.Y, cls.Width, cls.Height);
		var headerHeight = cls.HeaderHeight;

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
		_ = sb.Append(">\n  ");

		// a class without members is all header
		var memberless = cls.Attributes.Count == 0 && cls.Methods.Count == 0 && headerHeight >= height - 0.5;
		if (memberless)
		{
			ds.AppendHeaderOnlyEntity(sb, x, y, width, height, cls.Label, family, cls.Annotation is { Length: > 0 } a ? "«" + a + "»" : null);
			_ = sb.Append("\n</g>");
			return;
		}

		ds.AppendEntityFrame(sb, x, y, width, height, headerHeight, family);

		var attrTop = y + headerHeight;
		var methodTop = attrTop + cls.AttrHeight;
		var attrRow = MemberRowHeight(cls.AttrHeight, cls.Attributes.Count);
		var methodRow = MemberRowHeight(cls.MethodHeight, cls.Methods.Count);

		// row backgrounds (zebra on cards), hairlines between rows, and the compartment rule between attributes and methods
		for (var i = 0; i < cls.Attributes.Count; i++)
			ds.AppendZebraRow(sb, x, attrTop + (i * attrRow), width, attrRow, i, lastRow: false);
		for (var i = 0; i < cls.Methods.Count; i++)
			ds.AppendZebraRow(sb, x, methodTop + (i * methodRow), width, methodRow, i, lastRow: i == cls.Methods.Count - 1 && methodTop + cls.MethodHeight >= y + height - 0.5);
		for (var i = 1; i < cls.Attributes.Count; i++)
			ds.AppendEntityRowDivider(sb, x, width, attrTop + (i * attrRow));
		for (var i = 1; i < cls.Methods.Count; i++)
			ds.AppendEntityRowDivider(sb, x, width, methodTop + (i * methodRow));
		ds.AppendCompartmentDivider(sb, x, width, methodTop, family);

		_ = sb.Append("\n  ");
		ds.AppendEntityName(sb, x, y, width, headerHeight, cls.Label, family,
			cls.Annotation is { Length: > 0 } annotation ? "«" + annotation + "»" : null);

		var members = cls.Attributes.Concat(cls.Methods).ToList();
		var (typeW, _) = ClassMemberColumns.Measure(members);
		var nameX = x + EntityGrid.NameOffset(typeW, ClassMemberColumns.HasSigns(members));
		for (var i = 0; i < cls.Attributes.Count; i++)
			AppendMember(sb, ds, cls.Attributes[i], x + EntityGrid.Pad, nameX, attrTop + (i * attrRow) + (attrRow / 2), family);
		for (var i = 0; i < cls.Methods.Count; i++)
			AppendMember(sb, ds, cls.Methods[i], x + EntityGrid.Pad, nameX, methodTop + (i * methodRow) + (methodRow / 2), family);

		_ = sb.Append("\n</g>");
	}

	// type (meta, muted mono) | visibility sign (family ink) in the gutter before the name | name (body mono)
	private static void AppendMember(StringBuilder sb, DesignSystem ds, ClassMember member, double typeX, double nameX, double y, ColorFamily family)
	{
		var extra = (member.IsAbstract ? "font-style=\"italic\"" : "") + (member.IsStatic ? (member.IsAbstract ? " " : "") + "text-decoration=\"underline\"" : "");

		if (member.Type is { Length: > 0 })
		{
			_ = sb.Append("\n  ");
			ds.AppendMonoText(sb, member.Type, typeX, y, TypeRole.Meta);
		}

		var vis = ClassMemberColumns.VisibilitySymbol(member);
		if (vis.Length > 0)
		{
			_ = sb.Append("\n  ");
			ds.AppendMonoText(sb, vis, nameX - EntityGrid.SignGutter, y, TypeRole.Body, family.Ink, weight: 600);
		}

		_ = sb.Append("\n  ");
		ds.AppendMonoText(sb, ClassMemberColumns.DisplayName(member), nameX, y, TypeRole.Body, extraAttrs: extra.Length > 0 ? extra : null);
	}

	private static void AppendRelationship(StringBuilder sb, DesignSystem ds, PositionedClassRelationship rel, double cornerRadius)
	{
		if (rel.Points.Count < 2)
			return;

		// dash language: solid = structure, dashed = dependency / realization
		var isDashed = rel.Type is ClassRelationType.Dependency or ClassRelationType.Realization;
		var dashArray = isDashed ? $" stroke-dasharray=\"{DesignSystem.DashArray}\"" : "";

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
		_ = sb.Append("\" fill=\"none\" stroke=\"").Append(DesignSystem.EdgeColor).Append("\" stroke-width=\"")
			.Append(ds.EdgeWidth).Append('"').Append(dashArray);

		if (MarkerShapeOf(rel.Type) is { } shape)
		{
			var atStart = rel.MarkerAt == ClassMarkerAt.From;
			_ = sb.Append(atStart ? " marker-start=\"" : " marker-end=\"").Append(ds.Marker(shape, atStart)).Append('"');
		}

		_ = sb.Append(" />");
	}

	/// <summary>Inheritance / realization ▷, composition ◆, aggregation ◇, association → (filled), dependency ⟩ (open). Lollipop: the target is the circle.</summary>
	internal static MarkerShape? MarkerShapeOf(ClassRelationType type) => type switch
	{
		ClassRelationType.Inheritance or ClassRelationType.Realization => MarkerShape.Triangle,
		ClassRelationType.Composition => MarkerShape.Diamond,
		ClassRelationType.Aggregation => MarkerShape.DiamondHollow,
		ClassRelationType.Association => MarkerShape.Arrow,
		ClassRelationType.Dependency => MarkerShape.Open,
		_ => null,
	};

	private static void AppendRelationshipLabels(StringBuilder sb, DesignSystem ds, PositionedClassRelationship rel)
	{
		if (rel.Label == null && rel.FromCardinality == null && rel.ToCardinality == null)
			return;
		if (rel.Points.Count < 2)
			return;

		_ = sb.Append("\n<g class=\"edge-label\" data-from=\"");
		MultilineUtils.AppendEscapedAttr(sb, rel.From.AsSpan());
		_ = sb.Append("\" data-to=\"");
		MultilineUtils.AppendEscapedAttr(sb, rel.To.AsSpan());
		_ = sb.Append("\">");

		if (rel.Label != null)
		{
			var pos = rel.LabelPosition ?? VisualLanguage.PathMidpoint(rel.Points);
			_ = sb.Append("\n  ");
			ds.AppendEdgeLabel(sb, pos.X, pos.Y, rel.Label);
		}

		if (rel.FromCardinality != null)
			AppendCardinality(sb, ds, rel.FromCardinality, rel.Points[0], rel.Points[1]);

		if (rel.ToCardinality != null)
			AppendCardinality(sb, ds, rel.ToCardinality, rel.Points[^1], rel.Points[^2]);

		_ = sb.Append("\n</g>");
	}

	// A cardinality is a small edge label on the line next to the class it belongs to (clear of the end marker).
	private static void AppendCardinality(StringBuilder sb, DesignSystem ds, string text, Point end, Point toward)
	{
		const double distance = 30;
		var dx = toward.X - end.X;
		var dy = toward.Y - end.Y;
		var len = Math.Sqrt((dx * dx) + (dy * dy));
		var (ux, uy) = len < 0.01 ? (0.0, 1.0) : (dx / len, dy / len);
		var along = Math.Min(distance, Math.Max(len - 8, 8));
		_ = sb.Append("\n  ");
		ds.AppendEdgeLabel(sb, end.X + (ux * along), end.Y + (uy * along), text);
	}
}
