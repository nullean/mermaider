using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

/// <summary>
/// Validates Mermaid source lines against strict mode constraints.
/// In <see cref="StrictStylingMode.Block"/> mode, throws <see cref="MermaidParseException"/>
/// on the first disallowed directive or unknown class reference.
/// In <see cref="StrictStylingMode.Strip"/> mode, reports each stripped item via
/// <see cref="StrictStylingOptions.OnStripped"/> and continues.
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

	[GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_-]*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex SafeClassName();

	/// <summary>
	/// Validates source lines against strict-mode constraints. In Block mode throws on the first
	/// violation; in Strip mode appends each violation to <paramref name="violations"/> (the caller
	/// is responsible for invoking <see cref="StrictStylingOptions.OnStripped"/> once at the end).
	/// </summary>
	internal static void Validate(string[] lines, StrictStylingOptions strict, List<StrictStylingViolation> violations)
	{
		// AllowedClasses grammar + hex-color checks always throw regardless of Mode —
		// these are host-configuration errors, not source-authored styling.
		ValidateAllowedClasses(strict);
		var allowed = BuildAllowedSet(strict);

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			if (ClassDefDirective().IsMatch(line))
			{
				var msg = $"Strict mode: 'classDef' directives are not allowed (line {i + 1}: \"{line}\"). " +
					"Use pre-defined allowed classes instead.";
				RejectOrCollect(strict, violations, StrictStylingViolationKind.ClassDefDirective, msg, i + 1, line);
				continue;
			}

			if (StyleDirective().IsMatch(line))
			{
				var msg = $"Strict mode: 'style' directives are not allowed (line {i + 1}: \"{line}\"). " +
					"Use pre-defined allowed classes instead.";
				RejectOrCollect(strict, violations, StrictStylingViolationKind.StyleDirective, msg, i + 1, line);
				continue;
			}

			if (LinkStyleDirective().IsMatch(line))
			{
				var msg = $"Strict mode: 'linkStyle' directives are not allowed (line {i + 1}: \"{line}\"). " +
					"Edge styling must come from the host design system.";
				RejectOrCollect(strict, violations, StrictStylingViolationKind.LinkStyleDirective, msg, i + 1, line);
				continue;
			}

			if (UpdateStyleDirective().IsMatch(line))
			{
				var msg = $"Strict mode: C4 'Update*Style' directives are not allowed (line {i + 1}: \"{line}\"). " +
					"Element styling must come from the host design system.";
				RejectOrCollect(strict, violations, StrictStylingViolationKind.UpdateStyleDirective, msg, i + 1, line);
				continue;
			}

			ValidateClassAssignment(line, allowed, i, strict, violations);
			ValidateClassShorthand(line, allowed, i, strict, violations);
		}
	}

	/// <summary>
	/// Throws <see cref="MermaidParseException"/> in Block mode; appends a violation to
	/// <paramref name="violations"/> in Strip mode (caller fires <see cref="StrictStylingOptions.OnStripped"/> once).
	/// </summary>
	private static void RejectOrCollect(StrictStylingOptions strict, List<StrictStylingViolation> violations,
		StrictStylingViolationKind kind, string message, int line, string source)
	{
		if (strict.Mode == StrictStylingMode.Block)
			throw new MermaidParseException(message);

		violations.Add(new StrictStylingViolation
		{
			Kind = kind,
			Message = message,
			Line = line,
			Source = source,
		});
	}

	private static void ValidateAllowedClasses(StrictStylingOptions strict)
	{
		foreach (var cls in strict.AllowedClasses)
		{
			if (!SafeClassName().IsMatch(cls.Name))
				throw new MermaidParseException(
					$"Strict mode: class name '{cls.Name}' contains characters outside the allowed class-name grammar.");

			if (cls.Fill is null && !cls.IsExternal)
				throw new MermaidParseException(
					$"Strict mode: styled class '{cls.Name}' must define Fill.");

			ValidateHexColor(cls.Name, nameof(cls.Fill), cls.Fill);
			ValidateHexColor(cls.Name, nameof(cls.Stroke), cls.Stroke);
			ValidateHexColor(cls.Name, nameof(cls.Color), cls.Color);
			ValidateHexColor(cls.Name, nameof(cls.DarkFill), cls.DarkFill);
			ValidateHexColor(cls.Name, nameof(cls.DarkStroke), cls.DarkStroke);
			ValidateHexColor(cls.Name, nameof(cls.DarkColor), cls.DarkColor);
		}
	}

	private static void ValidateHexColor(string className, string property, string? value)
	{
		if (value is not null && !SvgValueAllowlist.IsAllowedHexColor(value))
			throw new MermaidParseException(
				$"Strict mode: {property} for class '{className}' must be a 3, 4, 6, or 8 digit hexadecimal color.");
	}

	private static void ValidateClassAssignment(string line, FrozenSet<string> allowed, int lineIndex,
		StrictStylingOptions strict, List<StrictStylingViolation> violations)
	{
		var match = ClassAssignDirective().Match(line);
		if (!match.Success)
			return;
		var name = match.Groups[1].Value;
		if (allowed.Contains(name))
			return;

		var msg = $"Strict mode: unknown class '{name}' (line {lineIndex + 1}). " +
			$"Allowed classes: {string.Join(", ", allowed)}.";

		RejectOrCollect(strict, violations, StrictStylingViolationKind.UnknownClassReference, msg, lineIndex + 1, name);
	}

	private static void ValidateClassShorthand(string line, FrozenSet<string> allowed, int lineIndex,
		StrictStylingOptions strict, List<StrictStylingViolation> violations)
	{
		foreach (var match in ClassShorthand().EnumerateMatches(line))
		{
			var name = line.AsSpan(match.Index + 3, match.Length - 3).ToString();
			if (allowed.Contains(name))
				continue;

			var msg = $"Strict mode: unknown class '{name}' (line {lineIndex + 1}). " +
				$"Allowed classes: {string.Join(", ", allowed)}.";

			RejectOrCollect(strict, violations, StrictStylingViolationKind.UnknownClassReference, msg, lineIndex + 1, name);
		}
	}

	private static FrozenSet<string> BuildAllowedSet(StrictStylingOptions strict) =>
		strict.AllowedClasses.Select(c => c.Name).ToFrozenSet(StringComparer.Ordinal);
}
