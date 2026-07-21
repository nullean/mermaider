namespace Mermaider.Icons;

/// <summary>Validates and sanitizes icon SVG before it enters the <see cref="IconRegistry"/>.</summary>
internal static class IconValidation
{
	internal static string ValidateAndNormalize(string svg)
	{
		// Reuse the general-purpose sanitizer to check the icon against the same allowlist
		// enforced elsewhere (elements, attributes, and the scoped <image> href exception).
		// Registration is reject-on-violation, not strip-and-accept: an icon containing a
		// <script>, event handler, or disallowed reference is rejected outright so the caller
		// finds out immediately, rather than silently getting back a mutated icon it didn't ask for.
		return SvgSanitizer.Sanitize(svg, Models.SanitizeMode.Block).Svg;
	}
}
