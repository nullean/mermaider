using Mermaider.Models;
using Mermaider.Text;

namespace Mermaider.Rendering;

/// <summary>
/// Class members are laid out like ER attributes: a muted type column, a divider, then the name column
/// (visibility symbol, name, parameters). Layout (box width) and renderer share these measurements so the text always fits.
/// </summary>
internal static class ClassMemberColumns
{
	internal const double PadX = 8;
	internal const double Gap = 5;

	/// <summary>Extra room: the monospace width is an estimate that runs a little short of real glyph widths.</summary>
	internal const double Slack = 8;

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
			if (m.Type is { Length: > 0 })
				typeW = Math.Max(typeW, TextMetrics.EstimateMonoTextWidth(m.Type, RenderConstants.FontSizes.Member));
			var name = VisibilitySymbol(m) is { Length: > 0 } v ? $"{v} {DisplayName(m)}" : DisplayName(m);
			nameW = Math.Max(nameW, TextMetrics.EstimateMonoTextWidth(name, RenderConstants.FontSizes.Member));
		}

		return (typeW, nameW);
	}

	/// <summary>Box width needed for these members.</summary>
	internal static double BoxWidth(IEnumerable<ClassMember> members)
	{
		var (typeW, nameW) = Measure(members);
		if (nameW <= 0)
			return 0;
		return typeW > 0
			? PadX + typeW + Gap + PadX + nameW + PadX + Slack
			: PadX + nameW + PadX + Slack;
	}
}
