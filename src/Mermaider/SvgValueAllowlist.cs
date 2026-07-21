using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Mermaider;

/// <summary>Positive value grammars for SVG presentation and reference attributes.</summary>
internal static partial class SvgValueAllowlist
{
	private const int TimeoutMs = 2000;

	private static readonly FrozenSet<string> RootStyleProperties = new[]
	{
		"--bg", "--fg", "--line", "--accent", "--muted", "--surface", "--border",
	}.ToFrozenSet(StringComparer.Ordinal);

	[GeneratedRegex(@"^url\(#[A-Za-z_][A-Za-z0-9_.:-]*\)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex LocalFragmentUrlPattern();

	[GeneratedRegex(@"^#[0-9A-Fa-f](?:[0-9A-Fa-f]{2}|[0-9A-Fa-f]{3}|[0-9A-Fa-f]{5}|[0-9A-Fa-f]{7})$", RegexOptions.None, TimeoutMs)]
	private static partial Regex HexColorPattern();

	[GeneratedRegex(@"^[A-Za-z][A-Za-z-]*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex NamedPaintPattern();

	[GeneratedRegex(@"^(?:rgb|rgba|hsl|hsla|hwb|lab|lch|oklab|oklch|color)\([A-Za-z0-9#%.,/+\-\s]+\)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex ColorFunctionPattern();

	[GeneratedRegex(@"^var\(--(?:bg|fg|line|accent|muted|surface|border|_text|_text-sec|_text-muted|_text-faint|_line|_arrow|_node-fill|_node-stroke|_group-fill|_group-hdr|_group-stroke|_inner-stroke|_key-badge|_accent-fill|_accent-stroke|_accent-text)\)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex InternalColorVariablePattern();

	internal static bool IsAllowedLocalReference(string value) =>
		value.Length <= 512
		&& (value == "none" || IsMatch(LocalFragmentUrlPattern(), value));

	internal static bool IsAllowedPaint(string value)
	{
		var trimmed = value.AsSpan().Trim();
		if (trimmed.Length is 0 or > 512)
			return false;

		var text = trimmed.ToString();
		return IsMatch(LocalFragmentUrlPattern(), text) || IsAllowedColor(text);
	}

	internal static bool IsAllowedColor(string value)
	{
		var trimmed = value.AsSpan().Trim();
		if (trimmed.Length is 0 or > 512)
			return false;

		var text = trimmed.ToString();
		return IsAllowedBasicColor(text) || IsAllowedColorMix(text);
	}

	internal static bool IsAllowedHexColor(string value)
	{
		var trimmed = value.AsSpan().Trim();
		return trimmed.Length is 4 or 5 or 7 or 9
			&& IsMatch(HexColorPattern(), trimmed.ToString());
	}

	internal static bool IsAllowedColorMix(string value)
	{
		const string prefix = "color-mix(in srgb, ";
		const string suffix = "%, var(--bg))";
		if (value.Length is 0 or > 512
			|| !value.StartsWith(prefix, StringComparison.Ordinal)
			|| !value.EndsWith(suffix, StringComparison.Ordinal))
			return false;

		var components = value.AsSpan(prefix.Length, value.Length - prefix.Length - suffix.Length);
		var separator = components.LastIndexOf(' ');
		if (separator <= 0 || separator == components.Length - 1)
			return false;

		var color = components[..separator].ToString();
		var percentage = components[(separator + 1)..];
		var safeColor = color == "var(--accent, var(--fg))" || IsAllowedBasicColor(color);

		return safeColor
			&& double.TryParse(percentage, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
			&& double.IsFinite(amount)
			&& amount is >= 0 and <= 100;
	}

	internal static bool IsAllowedRootStyle(string style)
	{
		if (style.Length is 0 or > 4_096)
			return false;

		var declarations = style.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		if (declarations.Length == 0)
			return false;

		foreach (var declaration in declarations)
		{
			var separator = declaration.IndexOf(':');
			if (separator <= 0 || separator == declaration.Length - 1)
				return false;

			var property = declaration[..separator].Trim();
			var value = declaration[(separator + 1)..].Trim();
			if (property == "background")
			{
				if (value != "var(--bg)")
					return false;
				continue;
			}

			if (!RootStyleProperties.Contains(property) || !IsAllowedColor(value))
				return false;
		}

		return true;
	}

	private static bool IsAllowedBasicColor(string value) =>
		IsMatch(HexColorPattern(), value)
		|| IsMatch(InternalColorVariablePattern(), value)
		|| IsMatch(ColorFunctionPattern(), value)
		|| IsMatch(NamedPaintPattern(), value);

	private static bool IsMatch(Regex regex, string value)
	{
		try
		{
			return regex.IsMatch(value);
		}
		catch (RegexMatchTimeoutException)
		{
			return false;
		}
	}
}
