using System.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>Entity / table parts shared by class, ER and requirement boxes (and any other row-based card).</summary>
internal sealed partial class DesignSystem
{
	/// <summary>Height of one entity row (member, attribute, property).</summary>
	internal const double RowHeight = 28;

	/// <summary>Horizontal padding inside an entity cell.</summary>
	internal const double CellPad = 16;

	/// <summary>
	/// The frame of an entity: body, outline and header in the preset's entity language. Band = family band header over
	/// a page-coloured body with a family outline; Plain = page-coloured body, family outline and a 3px accent rule on top;
	/// Card = family tint body, family soft header, no outline. Draw rows and text on top. A family carrying the caller's
	/// <c>Surface</c> override paints the header in it (solid, every preset, Plain included); its <c>Border</c> override
	/// replaces the outline and header rule (Band, Plain; Card draws neither).
	/// </summary>
	internal void AppendEntityFrame(StringBuilder sb, double x, double y, double w, double h, double headerHeight, ColorFamily family)
	{
		var r = Num(Spec.Entity == EntityKind.Plain ? Spec.NodeRadius : Spec.NodeRadius);
		var body = Spec.Entity == EntityKind.Card ? family.Tint(1) : "var(--bg)";
		var stroke = Spec.Entity == EntityKind.Card ? "none" : BoxOutline(family);
		_ = sb.Append("<rect x=\"").Append(x).Append("\" y=\"").Append(y)
			.Append("\" width=\"").Append(w).Append("\" height=\"").Append(h)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"").Append(body).Append("\" stroke=\"none\" />\n  ");

		if (family.SurfaceOverride is { } surface)
		{
			// the caller's Surface override fills the header (the entity's coloured part), solid, in every preset
			AppendTopRoundedRect(sb, x, y, w, headerHeight, Spec.NodeRadius, surface);
		}
		else if (Spec.Entity == EntityKind.Band)
		{
			AppendTopRoundedRect(sb, x, y, w, headerHeight, Spec.NodeRadius,
				Gradient ? VerticalGradient("eb-" + family.Key, family.Band, family.Top) : family.Band);
		}
		else if (Spec.Entity == EntityKind.Card)
		{
			AppendTopRoundedRect(sb, x, y, w, headerHeight, Spec.NodeRadius, family.Soft);
		}

		if (Spec.Entity != EntityKind.Card)
		{
			_ = sb.Append("\n  <line x1=\"").Append(x).Append("\" y1=\"").Append(y + headerHeight)
				.Append("\" x2=\"").Append(x + w).Append("\" y2=\"").Append(y + headerHeight)
				.Append("\" stroke=\"").Append(BoxOutline(family)).Append("\" stroke-width=\"").Append(NodeStrokeWidth).Append("\" />");
		}

		_ = sb.Append("\n  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
			.Append("\" width=\"").Append(w).Append("\" height=\"").Append(h)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"").Append(NodeStrokeWidth).Append("\" />");

		if (Spec.Entity == EntityKind.Plain)
		{
			_ = sb.Append("\n  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
				.Append("\" width=\"").Append(w).Append("\" height=\"3\" fill=\"").Append(ColorFamily.AccentBase).Append("\" />");
		}
	}

	/// <summary>
	/// The entity name (and optional <c>«stereotype»</c> above it) in the header: centred for Band / Card, left aligned for Plain.
	/// </summary>
	internal void AppendEntityName(StringBuilder sb, double x, double y, double w, double headerHeight, string name, ColorFamily family, string? stereotype = null)
	{
		var left = Spec.Entity == EntityKind.Plain;
		var tx = left ? x + CellPad : x + (w / 2);
		var anchor = left ? "start" : "middle";
		if (stereotype is { Length: > 0 })
		{
			var stereoY = y + (headerHeight * 0.32);
			var nameY = y + (headerHeight * 0.66);
			AppendText(sb, stereotype, tx, stereoY, TypeRole.Tag, family.Ink, anchor);
			_ = sb.Append("\n  ");
			AppendText(sb, name, tx, nameY, TypeRole.Heading, null, anchor);
		}
		else
		{
			AppendText(sb, name, tx, y + (headerHeight / 2), TypeRole.Heading, null, anchor);
		}
	}

	/// <summary>A hairline between rows / compartments of an entity.</summary>
	internal static void AppendRowDivider(StringBuilder sb, double x1, double x2, double y) =>
		sb.Append("<line x1=\"").Append(x1).Append("\" y1=\"").Append(y)
			.Append("\" x2=\"").Append(x2).Append("\" y2=\"").Append(y)
			.Append("\" stroke=\"var(--_line-soft)\" stroke-width=\"1\" />");

	/// <summary>Zebra fill for row <paramref name="index"/> (Card preset only); null when the row is not shaded.</summary>
	internal string? RowFill(int index) => Spec.Entity == EntityKind.Card && index % 2 == 1 ? "var(--_group-fill)" : null;
}
