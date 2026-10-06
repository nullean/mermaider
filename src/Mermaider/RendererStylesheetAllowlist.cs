using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Mermaider;

/// <summary>
/// Positive grammar for the single stylesheet emitted by Mermaider's renderers.
/// Standalone untrusted SVG never uses this policy and cannot retain CSS.
/// </summary>
internal static partial class RendererStylesheetAllowlist
{
	private const int TimeoutMs = 2000;
	private const string SansFallback = "system-ui, -apple-system, 'Segoe UI', sans-serif";
	private const string MonoFallback = "ui-monospace, 'SF Mono', 'Cascadia Code', monospace";

	private static readonly FrozenSet<string> GenericFontKeywords = new[]
	{
		"serif", "sans-serif", "monospace", "cursive", "fantasy",
		"system-ui", "ui-serif", "ui-sans-serif", "ui-monospace", "ui-rounded",
		"emoji", "math", "fangsong",
	}.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	// The derived-token block is generated from the same source StyleBlock writes from.
	private static string[] FixedSvgLines => ["  svg {", .. Theming.StyleBlock.TokenLines];

	[GeneratedRegex(@"^'(?:[A-Za-z0-9 _-]|\\[0-9A-F]{1,4} )*'$", RegexOptions.None, TimeoutMs)]
	private static partial Regex QuotedFontPattern();

	[GeneratedRegex(@"^(?:0|[1-9][0-9]*)(?:\.[0-9]{1,3})?(?:px|rem|em|%)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex FontSizePattern();

	[GeneratedRegex(@"^(?<indent> {2}| {4})(?<selector>\.cls-[A-Za-z_][A-Za-z0-9_-]*) rect, \k<selector> polygon, \k<selector> circle, \k<selector> ellipse \{ fill: (?<fill>#[0-9A-Fa-f]{3,8}); stroke: (?<stroke>#[0-9A-Fa-f]{3,8}); \}$", RegexOptions.None, TimeoutMs)]
	private static partial Regex StrictShapeRulePattern();

	[GeneratedRegex(@"^(?<indent> {2}| {4})(?<selector>\.cls-[A-Za-z_][A-Za-z0-9_-]*) text \{ fill: (?<fill>#[0-9A-Fa-f]{3,8}); \}$", RegexOptions.None, TimeoutMs)]
	private static partial Regex StrictTextRulePattern();

	internal static bool IsAllowed(string stylesheet)
	{
		try
		{
			return IsAllowedCore(stylesheet);
		}
		catch (RegexMatchTimeoutException)
		{
			return false;
		}
	}

	private static bool IsAllowedCore(string stylesheet)
	{
		if (stylesheet.Contains('\r', StringComparison.Ordinal))
			return false;

		var lines = stylesheet.Split('\n', StringSplitOptions.None);
		if (lines.Length < FixedSvgLines.Length + 9 || lines[0].Length != 0 || lines[^1].Length != 0)
			return false;

		var index = 1;
		if (!IsAllowedFontRule(lines[index++], "text", SansFallback)
			|| !IsAllowedFontRule(lines[index++], ".mono", MonoFallback))
			return false;

		foreach (var expected in FixedSvgLines)
		{
			if (lines[index++] != expected)
				return false;
		}

		if (!IsAllowedFontSizeLine(lines[index++], "    --fs-xs: ")
			|| !IsAllowedFontSizeLine(lines[index++], "    --fs-s:  ")
			|| !IsAllowedFontSizeLine(lines[index++], "    --fs-m:  ")
			|| !IsAllowedFontSizeLine(lines[index++], "    --fs-l:  ")
			|| lines[index++] != "  }")
			return false;

		// zero to two elevation rules, each exactly one a preset can produce
		for (var e = 0; e < 2 && index < lines.Length - 1 && Theming.StyleBlock.AllElevationLines.Contains(lines[index]); e++)
			index++;

		if (index == lines.Length - 1)
			return true;

		var lightRuleCount = 0;
		while (index < lines.Length - 1 && lines[index] != "  @media (prefers-color-scheme: dark) {")
		{
			if (!IsAllowedStrictRule(lines[index++], "  "))
				return false;
			lightRuleCount++;
		}

		if (lightRuleCount == 0
			|| index >= lines.Length - 1
			|| lines[index++] != "  @media (prefers-color-scheme: dark) {")
			return false;

		var darkRuleCount = 0;
		while (index < lines.Length - 1 && lines[index] != "  }")
		{
			if (!IsAllowedStrictRule(lines[index++], "    "))
				return false;
			darkRuleCount++;
		}

		return darkRuleCount > 0
			&& index == lines.Length - 2
			&& lines[index] == "  }";
	}

	private static bool IsAllowedFontRule(string line, string selector, string fallback)
	{
		var prefix = $"  {selector} {{ font-family: ";
		const string ruleSuffix = "; }";
		if (!line.StartsWith(prefix, StringComparison.Ordinal)
			|| !line.EndsWith(ruleSuffix, StringComparison.Ordinal))
			return false;

		var familyList = line[prefix.Length..^ruleSuffix.Length];
		if (familyList == fallback)
			return true;

		var fallbackSuffix = $", {fallback}";
		if (!familyList.EndsWith(fallbackSuffix, StringComparison.Ordinal))
			return false;

		var font = familyList[..^fallbackSuffix.Length];
		return GenericFontKeywords.Contains(font) || QuotedFontPattern().IsMatch(font);
	}

	private static bool IsAllowedFontSizeLine(string line, string prefix) =>
		line.StartsWith(prefix, StringComparison.Ordinal)
		&& line.EndsWith(';')
		&& FontSizePattern().IsMatch(line.AsSpan(prefix.Length, line.Length - prefix.Length - 1).ToString());

	private static bool IsAllowedStrictRule(string line, string indent)
	{
		var shape = StrictShapeRulePattern().Match(line);
		if (shape.Success)
			return shape.Groups["indent"].Value == indent
				&& SvgValueAllowlist.IsAllowedHexColor(shape.Groups["fill"].Value)
				&& SvgValueAllowlist.IsAllowedHexColor(shape.Groups["stroke"].Value);

		var text = StrictTextRulePattern().Match(line);
		return text.Success
			&& text.Groups["indent"].Value == indent
			&& SvgValueAllowlist.IsAllowedHexColor(text.Groups["fill"].Value);
	}
}
