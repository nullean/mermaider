using System.Xml.Linq;

namespace Mermaider.Icons;

/// <summary>Validates and sanitizes icon SVG before it enters the <see cref="IconRegistry"/>.</summary>
internal static class IconValidation
{
	internal static string ValidateAndNormalize(string name, string svg)
	{
		XDocument doc;
		try
		{
			doc = XDocument.Parse(svg);
		}
		catch (Exception ex)
		{
			throw new MermaidParseException($"Icon '{name}' is not well-formed SVG/XML.", ex);
		}

		if (doc.Root is null || doc.Root.Name.LocalName != "svg")
			throw new MermaidParseException($"Icon '{name}' must have an <svg> root element.");

		// Reuse the general-purpose sanitizer to check the icon against the same allowlist
		// enforced elsewhere (elements, attributes, and the scoped <image> href exception).
		// Registration is reject-on-violation, not strip-and-accept: an icon containing a
		// <script>, event handler, or disallowed reference is rejected outright so the caller
		// finds out immediately, rather than silently getting back a mutated icon it didn't ask for.
		var result = SvgSanitizer.Sanitize(svg);
		if (result.HasViolations)
		{
			var violations = string.Join(", ", result.Violations);
			throw new MermaidParseException($"Icon '{name}' contains disallowed content: {violations}.");
		}

		return result.Svg;
	}
}
