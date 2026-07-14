using System.Globalization;

namespace Mermaider.Rendering;

internal static class SvgFormatExtensions
{
	/// <summary>
	/// Formats a <see cref="double"/> as an SVG coordinate or attribute value using invariant culture
	/// and a <c>"0.##"</c> format (no trailing zeros, up to two decimal places).
	/// </summary>
	internal static string SvgFormat(this double value) =>
		value.ToString("0.##", CultureInfo.InvariantCulture);
}
