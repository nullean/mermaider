using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Mermaid.Models;

namespace Mermaid.Parsing;

/// <summary>
/// Validates Mermaid source lines against strict mode constraints.
/// Rejects <c>classDef</c> and <c>style</c> directives; validates class references
/// against the allowed set.
/// </summary>
internal static partial class StrictModeValidator
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^classDef\s+", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassDefDirective();

	[GeneratedRegex(@"^style\s+", RegexOptions.None, TimeoutMs)]
	private static partial Regex StyleDirective();

	[GeneratedRegex(@"^class\s+[\w,-]+\s+(\w+)\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassAssignDirective();

	[GeneratedRegex(@":::([\w][\w-]*)", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassShorthand();

	internal static void Validate(string[] lines, StrictModeOptions strict)
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

	private static FrozenSet<string> BuildAllowedSet(StrictModeOptions strict) =>
		strict.AllowedClasses.Select(c => c.Name).ToFrozenSet(StringComparer.Ordinal);
}
