using Mermaider.Models;

namespace Mermaider.Rendering;

/// <summary>
/// Internal bridge that calls the public <see cref="SvgSanitizer"/>
/// and maps the result to the strict-mode contract (strip vs block/throw).
/// </summary>
internal static class StrictModeSanitizer
{
	internal static string Sanitize(string svg, SvgSanitizeMode mode)
	{
		var result = SvgSanitizer.Sanitize(svg);

		if (!result.HasViolations)
			return svg;

		if (mode == SvgSanitizeMode.Block)
		{
			var first = result.Violations[0];
			throw new MermaidParseException(
				$"SVG sanitization failed: disallowed {first}.");
		}

		return result.Svg;
	}
}
