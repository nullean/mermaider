using Mermaider.Models;

namespace Mermaider.Rendering;

/// <summary>
/// Internal bridge that runs every rendered SVG through the public allowlist-based
/// <see cref="SvgSanitizer"/> before it leaves the library. Sanitization is non-optional
/// (see <see cref="MermaidRenderer.RenderSvg"/>); the <see cref="SanitizeMode"/> only
/// selects what happens when a violation is found — silently strip it, or throw.
/// </summary>
internal static class OutputSanitizer
{
	internal static string Sanitize(string svg, SanitizeMode mode)
	{
		var result = SvgSanitizer.Sanitize(svg);

		if (!result.HasViolations)
			return svg;

		if (mode == SanitizeMode.Block)
		{
			var first = result.Violations[0];
			throw new MermaidParseException(
				$"SVG sanitization failed: disallowed {first}.");
		}

		return result.Svg;
	}
}
