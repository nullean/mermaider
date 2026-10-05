using Mermaider.Models;
using Mermaider.Text;

namespace Mermaider.Rendering;

/// <summary>
/// The one column grid class members, ER attributes and requirement properties share:
/// <c>pad | type (meta, muted mono) | gap | sign gutter | name (body) … badge | pad</c>. Layout (box width) and the renderers
/// measure with these helpers so the text always fits, and the grid is identical across the three diagram types.
/// Geometry only: nothing here depends on the style preset.
/// </summary>
internal static class EntityGrid
{
	/// <summary>Left / right cell padding.</summary>
	internal const double Pad = DesignSystem.CellPad;

	/// <summary>Space between the type (or key) column and the name (or value) column.</summary>
	internal const double ColumnGap = 12;

	/// <summary>Gutter before the name holding the visibility sign (class members only).</summary>
	internal const double SignGutter = 14;

	/// <summary>Space between the name and a right-aligned key badge.</summary>
	internal const double BadgeGap = 10;

	/// <summary>Extra room: the monospace width is an estimate that runs a little short of real glyph widths.</summary>
	internal const double Slack = 6;

	/// <summary>Px of the type column (meta tier).</summary>
	internal static double TypePx => DesignSystem.Px(TypeRole.Meta);

	/// <summary>Px of the name column (body tier).</summary>
	internal static double NamePx => DesignSystem.Px(TypeRole.Body);

	internal static double TypeWidth(string? type) =>
		type is { Length: > 0 } ? TextMetrics.EstimateMonoTextWidth(type, TypePx) : 0;

	internal static double NameWidth(string name) => TextMetrics.EstimateMonoTextWidth(name, NamePx);

	/// <summary>Width a key / count badge takes in any preset (chip or <c>[bracketed]</c>), so layout never depends on the style.</summary>
	internal static double BadgeWidth(string text)
	{
		var px = DesignSystem.Px(TypeRole.Tag);
		return Math.Max(TextMetrics.MeasureTextWidth(text, px, 600) + 12, TextMetrics.MeasureTextWidth("[" + text + "]", px, 600));
	}

	/// <summary>Offset of the name column from the box's left edge.</summary>
	internal static double NameOffset(double typeWidth, bool signs) =>
		Pad + (typeWidth > 0 ? typeWidth + ColumnGap : 0) + (signs ? SignGutter : 0);

	/// <summary>Box width that fits the widest type, name and badge.</summary>
	internal static double BoxWidth(double typeWidth, double nameWidth, bool signs, double badgeWidth) =>
		NameOffset(typeWidth, signs) + nameWidth + (badgeWidth > 0 ? BadgeGap + badgeWidth : 0) + Pad + Slack;

	/// <summary>Width an entity / class name (heading tier, measured at the heaviest preset weight) needs.</summary>
	internal static double HeadingWidth(string name) =>
		TextMetrics.MeasureMultiline(name.AsSpan(), DesignSystem.Px(TypeRole.Heading), 700).Width + (Pad * 2);

	/// <summary>Width a <c>«stereotype»</c> (tag tier) needs.</summary>
	internal static double StereotypeWidth(string stereotype) =>
		TextMetrics.MeasureTextWidth(stereotype, DesignSystem.Px(TypeRole.Tag), 600) + (Pad * 2);
}

/// <summary>Class members on the shared <see cref="EntityGrid"/>: type | visibility sign + name (and parameters).</summary>
internal static class ClassMemberColumns
{
	internal static string VisibilitySymbol(ClassMember m) => m.Visibility switch
	{
		ClassVisibility.Public => "+",
		ClassVisibility.Private => "-",
		ClassVisibility.Protected => "#",
		ClassVisibility.Package => "~",
		_ => "",
	};

	internal static string DisplayName(ClassMember m) => m.IsMethod ? $"{m.Name}({m.Params ?? ""})" : m.Name;

	/// <summary>Width of the type column (0 when no member declares a type) and of the name column, in px.</summary>
	internal static (double TypeWidth, double NameWidth) Measure(IEnumerable<ClassMember> members)
	{
		var typeW = 0.0;
		var nameW = 0.0;
		foreach (var m in members)
		{
			typeW = Math.Max(typeW, EntityGrid.TypeWidth(m.Type));
			nameW = Math.Max(nameW, EntityGrid.NameWidth(DisplayName(m)));
		}

		return (typeW, nameW);
	}

	internal static bool HasSigns(IEnumerable<ClassMember> members) => members.Any(m => m.Visibility != ClassVisibility.None);

	/// <summary>Box width needed for these members.</summary>
	internal static double BoxWidth(IEnumerable<ClassMember> members)
	{
		var list = members as IReadOnlyCollection<ClassMember> ?? members.ToList();
		var (typeW, nameW) = Measure(list);
		return nameW <= 0 ? 0 : EntityGrid.BoxWidth(typeW, nameW, HasSigns(list), 0);
	}
}
