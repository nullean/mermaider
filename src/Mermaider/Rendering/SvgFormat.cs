using System.Globalization;

namespace Mermaider.Rendering;

/// <summary>Shared SVG number formatting (invariant, trimmed).</summary>
internal static class SvgFormat
{
	internal static string F(double value) =>
		value.ToString("0.##", CultureInfo.InvariantCulture);
}
