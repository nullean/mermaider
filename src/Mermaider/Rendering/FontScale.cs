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
		var xsRatio = options?.FontSizeExtraSmall ?? DefaultXsRatio;
		var sRatio = options?.FontSizeSmall ?? DefaultSRatio;
		var lRatio = options?.FontSizeLarge ?? DefaultLRatio;

		return new FontScale(baseStr, xsRatio, sRatio, lRatio);
	}

	private FontScale(string baseSize, double xsRatio, double sRatio, double lRatio)
	{
		var (value, unit) = ParseSize(baseSize);

		M = baseSize;
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
		var match = SizePattern().Match(size);
		if (!match.Success)
			return (1, "rem");

		var value = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
		var unit = match.Groups[2].Value;
		return (value, unit);
	}

	private static string FormatSize(double value, string unit) =>
		$"{value.ToString("0.###", CultureInfo.InvariantCulture)}{unit}";

	[GeneratedRegex(@"^([\d.]+)\s*([a-z%]+)$", RegexOptions.None, 2000)]
	private static partial Regex SizePattern();
}
