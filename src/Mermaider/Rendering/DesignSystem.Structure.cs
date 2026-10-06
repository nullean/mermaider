using System.Text;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>Row-level parts of the entity tables shared by class, ER and requirement diagrams.</summary>
internal sealed partial class DesignSystem
{
	/// <summary>Horizontal inset of a row hairline from the entity border.</summary>
	internal const double RowDividerInset = 12;

	/// <summary>Colour of markers a renderer draws itself (ER crow's feet, requirement ⊕): the accent for thin heads, else the line.</summary>
	internal string OwnMarkerColor => Spec.Marker == MarkerKind.Thin ? ColorFamily.AccentBase : EdgeColor;

	/// <summary>
	/// Background of entity row <paramref name="index"/>: a zebra band in the Card preset (rounded at the bottom when it is the
	/// last row, so it never pokes past the card's corners), nothing otherwise.
	/// </summary>
	internal void AppendZebraRow(StringBuilder sb, double x, double y, double w, double h, int index, bool lastRow)
	{
		if (RowFill(index) is not { } fill)
			return;

		_ = sb.Append("\n  ");
		if (!lastRow)
		{
			_ = sb.Append("<rect x=\"").Append(x).Append("\" y=\"").Append(y).Append("\" width=\"").Append(w)
				.Append("\" height=\"").Append(h).Append("\" fill=\"").Append(fill).Append("\" />");
			return;
		}

		var r = Math.Min(Spec.NodeRadius, Math.Min(w / 2, h));
		_ = sb.Append("<path d=\"M").Append(x).Append(',').Append(y)
			.Append(" L").Append(x + w).Append(',').Append(y)
			.Append(" L").Append(x + w).Append(',').Append(y + h - r)
			.Append(" Q").Append(x + w).Append(',').Append(y + h).Append(' ').Append(x + w - r).Append(',').Append(y + h)
			.Append(" L").Append(x + r).Append(',').Append(y + h)
			.Append(" Q").Append(x).Append(',').Append(y + h).Append(' ').Append(x).Append(',').Append(y + h - r)
			.Append(" Z\" fill=\"").Append(fill).Append("\" />");
	}

	/// <summary>
	/// An entity without rows (memberless class, attribute-less ER entity, property-less requirement): the node recipe with the
	/// name (and stereotype) centred, so it reads like any other box instead of a header band with nothing under it.
	/// </summary>
	internal void AppendHeaderOnlyEntity(StringBuilder sb, double x, double y, double w, double h, string name, ColorFamily family, string? stereotype = null)
	{
		AppendBox(sb, x, y, w, h, family);
		_ = sb.Append("\n  ");
		var cx = x + (w / 2);
		if (stereotype is { Length: > 0 })
		{
			AppendText(sb, stereotype, cx, y + (h / 2) - 9, TypeRole.Tag, family.Ink);
			_ = sb.Append("\n  ");
			AppendText(sb, name, cx, y + (h / 2) + 8, TypeRole.Heading);
		}
		else
		{
			AppendText(sb, name, cx, y + (h / 2), TypeRole.Heading);
		}
	}

	/// <summary>A hairline between two rows (inset from the border); the Card preset separates rows by zebra instead.</summary>
	internal void AppendEntityRowDivider(StringBuilder sb, double x, double w, double y)
	{
		if (Spec.Entity == EntityKind.Card)
			return;
		_ = sb.Append("\n  ");
		AppendRowDivider(sb, x + RowDividerInset, x + w - RowDividerInset, y);
	}

	/// <summary>The divider between two compartments (class attributes | methods): a family rule, inset and soft on a card.</summary>
	internal void AppendCompartmentDivider(StringBuilder sb, double x, double w, double y, ColorFamily family)
	{
		var card = Spec.Entity == EntityKind.Card;
		var (x1, x2) = card ? (x + RowDividerInset, x + w - RowDividerInset) : (x, x + w);
		_ = sb.Append("\n  <line x1=\"").Append(x1).Append("\" y1=\"").Append(y)
			.Append("\" x2=\"").Append(x2).Append("\" y2=\"").Append(y)
			.Append("\" stroke=\"").Append(card ? family.Band : family.Stroke).Append("\" stroke-width=\"").Append(card ? "2" : NodeStrokeWidth).Append("\" />");
	}

	/// <summary>Text attributes for an entity cell in the mono font (types, names, keys), whatever the preset's xs setting.</summary>
	internal string MonoAttributes(TypeRole role, string? color = null, string anchor = "start", int? weight = null)
	{
		var attrs = TextAttributes(role, color, anchor, weight);
		return IsMono(role) ? attrs : "class=\"mono\" " + attrs;
	}

	/// <summary>Appends a single-line mono cell text vertically centred on <paramref name="cy"/>.</summary>
	internal void AppendMonoText(StringBuilder sb, string text, double x, double cy, TypeRole role, string? color = null, string anchor = "start", int? weight = null, string? extraAttrs = null) =>
		MultilineUtils.AppendMultilineText(sb, text, x, cy, Px(role),
			MonoAttributes(role, color, anchor, weight) + (extraAttrs is null ? "" : " " + extraAttrs));

	/// <summary>
	/// A role chip (dot + word) starting at <paramref name="x"/>, centred on <paramref name="cy"/>: requirement risk, critical
	/// markers. The word always accompanies the colour so it never relies on colour alone. Returns its width.
	/// </summary>
	internal double AppendRoleChip(StringBuilder sb, double x, double cy, string word, ColorFamily role)
	{
		var px = Px(TypeRole.Tag);
		var textW = TextMetrics.MeasureTextWidth(word, px, 600);
		const double dotR = 3;
		var w = textW + 25;
		if (Spec.Badge != BadgeKind.Bracket)
		{
			_ = sb.Append("<rect x=\"").Append(x).Append("\" y=\"").Append(cy - 9)
				.Append("\" width=\"").Append(w).Append("\" height=\"18\" rx=\"9\" ry=\"9\" fill=\"")
				.Append(Spec.Badge == BadgeKind.Chip ? role.Soft : role.Band).Append("\" />");
		}

		var dotX = Spec.Badge == BadgeKind.Bracket ? x + dotR : x + 9;
		_ = sb.Append("<circle cx=\"").Append(dotX).Append("\" cy=\"").Append(cy).Append("\" r=\"").Append(dotR)
			.Append("\" fill=\"").Append(role.Ink).Append("\" />");
		AppendText(sb, word, dotX + dotR + 4, cy, TypeRole.Tag, role.Ink, anchor: "start");
		return Spec.Badge == BadgeKind.Bracket ? textW + (dotR * 2) + 4 : w;
	}
}
