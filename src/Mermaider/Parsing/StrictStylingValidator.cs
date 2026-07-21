using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

/// <summary>
/// Validates Mermaid source lines against strict mode constraints.
/// Rejects <c>classDef</c> and <c>style</c> directives; validates class references
/// against the allowed set.
/// </summary>
internal static partial class StrictStylingValidator
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^classDef\s+", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassDefDirective();

	[GeneratedRegex(@"^style\s+", RegexOptions.None, TimeoutMs)]
	private static partial Regex StyleDirective();

	[GeneratedRegex(@"^linkStyle\b", RegexOptions.None, TimeoutMs)]
	private static partial Regex LinkStyleDirective();

	[GeneratedRegex(@"^Update\w*Style\b", RegexOptions.None, TimeoutMs)]
	private static partial Regex UpdateStyleDirective();

	[GeneratedRegex(@"^class\s+[\w,-]+\s+(\w+)\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassAssignDirective();

	[GeneratedRegex(@":::([\w][\w-]*)", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassShorthand();

	internal static void Validate(string[] lines, StrictStylingOptions strict)
	{
		var allowed = BuildAllowedSet(strict);

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			if (ClassDefDirective().IsMatch(line))
				throw new MermaidParseException(
					$"Strict mode: 'classDef' directives are not allowed (line {i + 1}: \"{line}\"). " +
					"Use pre-defined allowed classes instead.");

			if (StyleDirective().IsMatch(line))
				throw new MermaidParseException(
					$"Strict mode: 'style' directives are not allowed (line {i + 1}: \"{line}\"). " +
					"Use pre-defined allowed classes instead.");

			if (LinkStyleDirective().IsMatch(line))
				throw new MermaidParseException(
					$"Strict mode: 'linkStyle' directives are not allowed (line {i + 1}: \"{line}\"). " +
					"Edge styling must come from the host design system.");

			if (UpdateStyleDirective().IsMatch(line))
				throw new MermaidParseException(
					$"Strict mode: C4 'Update*Style' directives are not allowed (line {i + 1}: \"{line}\"). " +
					"Element styling must come from the host design system.");

			if (strict.RejectUnknownClasses)
			{
				ValidateClassAssignment(line, allowed, i);
				ValidateClassShorthand(line, allowed, i);
			}
		}
	}

	private static void ValidateClassAssignment(string line, FrozenSet<string> allowed, int lineIndex)
	{
		var match = ClassAssignDirective().Match(line);
		if (match.Success)
		{
			var name = match.Groups[1].Value;
			if (!allowed.Contains(name))
				throw new MermaidParseException(
					$"Strict mode: unknown class '{name}' (line {lineIndex + 1}). " +
					$"Allowed classes: {string.Join(", ", allowed)}.");
		}
	}

	private static void ValidateClassShorthand(string line, FrozenSet<string> allowed, int lineIndex)
	{
		foreach (var match in ClassShorthand().EnumerateMatches(line))
		{
			var name = line.AsSpan(match.Index + 3, match.Length - 3).ToString();
			if (!allowed.Contains(name))
				throw new MermaidParseException(
					$"Strict mode: unknown class '{name}' (line {lineIndex + 1}). " +
					$"Allowed classes: {string.Join(", ", allowed)}.");
		}
	}

	private static FrozenSet<string> BuildAllowedSet(StrictStylingOptions strict) =>
		strict.AllowedClasses.Select(c => c.Name).ToFrozenSet(StringComparer.Ordinal);
}
