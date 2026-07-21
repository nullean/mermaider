using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Rendering;

/// <summary>
/// Computed font scale with 4 tiers derived from a configurable base size.
/// CSS values use the original unit; numeric px values are used for layout measurement.
/// </summary>
internal sealed partial record FontScale
{
	internal string Xs { get; }
	internal string S { get; }
	internal string M { get; }
	internal string L { get; }

	internal double XsPx { get; }
	internal double SPx { get; }
	internal double MPx { get; }
	internal double LPx { get; }

	private const string DefaultBase = "1rem";
	private const double DefaultXsRatio = 0.75;
	private const double DefaultSRatio = 0.875;
	private const double DefaultLRatio = 1.125;
	private const double DefaultBasePx = 16;

	internal static readonly FontScale Default = From(null);

	internal static FontScale From(RenderOptions? options)
	{
		var baseStr = options?.FontSize ?? DefaultBase;
		var xsRatio = NormalizeRatio(options?.FontSizeExtraSmall, DefaultXsRatio);
		var sRatio = NormalizeRatio(options?.FontSizeSmall, DefaultSRatio);
		var lRatio = NormalizeRatio(options?.FontSizeLarge, DefaultLRatio);

		return new FontScale(baseStr, xsRatio, sRatio, lRatio);
	}

	private FontScale(string baseSize, double xsRatio, double sRatio, double lRatio)
	{
		var (value, unit) = ParseSize(baseSize);

		M = FormatSize(value, unit);
		Xs = FormatSize(value * xsRatio, unit);
		S = FormatSize(value * sRatio, unit);
		L = FormatSize(value * lRatio, unit);

		var basePx = unit == "px" ? value : value * DefaultBasePx;
		MPx = basePx;
		XsPx = basePx * xsRatio;
		SPx = basePx * sRatio;
		LPx = basePx * lRatio;
	}

	private static (double Value, string Unit) ParseSize(string size)
	{
		if (size.Length > 64)
			return (1, "rem");

		var match = SizePattern().Match(size);
		if (!match.Success
			|| !double.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
			|| !double.IsFinite(value))
			return (1, "rem");

		var unit = match.Groups[2].Value.ToLowerInvariant();
		return (value, unit);
	}

	private static double NormalizeRatio(double? candidate, double fallback) =>
		candidate is > 0 && double.IsFinite(candidate.Value) ? candidate.Value : fallback;

	private static string FormatSize(double value, string unit) =>
		$"{value.ToString("0.###", CultureInfo.InvariantCulture)}{unit}";

	[GeneratedRegex(@"^((?:0|[1-9][0-9]*)(?:\.[0-9]{1,3})?)\s*(px|rem|em|%)$", RegexOptions.IgnoreCase, 2000)]
	private static partial Regex SizePattern();
}
