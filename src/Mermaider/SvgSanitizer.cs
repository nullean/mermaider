using System.Collections.Frozen;
using System.Xml.Linq;

namespace Mermaider;

/// <summary>Describes a single element or attribute that violated the allowlist.</summary>
public readonly record struct SvgViolation(string Kind, string Name, string? ParentElement = null)
{
	public override string ToString() => ParentElement is not null
		? $"{Kind} '{Name}' on <{ParentElement}>"
		: $"{Kind} <{Name}>";
}

/// <summary>Result of an SVG sanitization pass.</summary>
public sealed record SvgSanitizeResult
{
	/// <summary>The (possibly cleaned) SVG string.</summary>
	public required string Svg { get; init; }

	/// <summary>True when violations were found (and stripped in strip mode).</summary>
	public required bool HasViolations { get; init; }

	/// <summary>All violations found during the pass.</summary>
	public required IReadOnlyList<SvgViolation> Violations { get; init; }
}

/// <summary>
/// General-purpose SVG sanitizer. Walks the XML tree and enforces an
/// element/attribute allowlist. Blocks the main XSS vectors in SVG:
/// <c>&lt;script&gt;</c>, <c>&lt;foreignObject&gt;</c>, event handler attributes,
/// and <c>href</c>/<c>xlink:href</c> (which can carry <c>javascript:</c> URIs).
/// <para>
/// Usable standalone — not tied to the Mermaid rendering pipeline.
/// </para>
/// </summary>
public static class SvgSanitizer
{
	// ========================================================================
	// Default allowlists
	// ========================================================================

	/// <summary>Default set of allowed SVG element local names.</summary>
	public static readonly FrozenSet<string> DefaultAllowedElements = new[]
	{
		"svg", "g", "defs", "style", "title", "desc",
		"rect", "circle", "ellipse", "polygon", "polyline", "line", "path",
		"text", "tspan",
		"marker",
		"clipPath", "mask",
		"linearGradient", "radialGradient", "stop",
		"filter", "feGaussianBlur", "feOffset", "feBlend", "feFlood",
		"feComposite", "feMerge", "feMergeNode", "feDropShadow",
		"feColorMatrix", "feMorphology",
	}.ToFrozenSet(StringComparer.Ordinal);

	/// <summary>Default set of allowed SVG attribute local names.</summary>
	public static readonly FrozenSet<string> DefaultAllowedAttributes = new[]
	{
		"id", "class", "style", "transform",
		"xmlns", "viewBox", "preserveAspectRatio", "width", "height",
		"x", "y", "cx", "cy", "r", "rx", "ry",
		"x1", "y1", "x2", "y2",
		"points", "d",
		"fill", "stroke", "stroke-width", "stroke-dasharray", "stroke-dashoffset",
		"stroke-linecap", "stroke-linejoin", "stroke-miterlimit",
		"stroke-opacity", "fill-opacity", "fill-rule", "clip-rule", "opacity",
		"font-family", "font-size", "font-weight", "font-style",
		"text-anchor", "dominant-baseline", "alignment-baseline",
		"baseline-shift", "text-decoration", "letter-spacing",
		"word-spacing", "direction", "unicode-bidi", "writing-mode",
		"color", "color-interpolation", "color-interpolation-filters",
		"marker-end", "marker-start", "marker-mid",
		"markerWidth", "markerHeight", "markerUnits",
		"refX", "refY", "orient",
		"offset", "stop-color", "stop-opacity",
		"gradientUnits", "gradientTransform", "spreadMethod",
		"fx", "fy", "fr",
		"in", "in2", "result", "stdDeviation", "dx", "dy", "mode",
		"flood-color", "flood-opacity", "operator",
		"k1", "k2", "k3", "k4",
		"type", "values",
		"filterUnits", "primitiveUnits",
		"clipPathUnits", "maskUnits", "maskContentUnits",
		"display", "visibility",
		"patternUnits", "patternContentUnits", "patternTransform",
	}.ToFrozenSet(StringComparer.Ordinal);

	// ========================================================================
	// Public API
	// ========================================================================

	/// <summary>
	/// Sanitize an SVG string against the default allowlists.
	/// Disallowed elements and attributes are stripped from the output.
	/// </summary>
	/// <param name="svg">Raw SVG markup.</param>
	/// <returns>A result containing the cleaned SVG and any violations found.</returns>
	public static SvgSanitizeResult Sanitize(string svg) =>
		Sanitize(svg, DefaultAllowedElements, DefaultAllowedAttributes);

	/// <summary>
	/// Sanitize an SVG string against custom allowlists.
	/// Disallowed elements and attributes are stripped from the output.
	/// </summary>
	/// <param name="svg">Raw SVG markup.</param>
	/// <param name="allowedElements">Set of allowed element local names.</param>
	/// <param name="allowedAttributes">Set of allowed attribute local names.
	/// <c>data-*</c> attributes are always allowed. <c>on*</c> event handlers
	/// and <c>href</c>/<c>xlink:href</c> are always blocked regardless of this set.</param>
	/// <returns>A result containing the cleaned SVG and any violations found.</returns>
	public static SvgSanitizeResult Sanitize(
		string svg,
		FrozenSet<string> allowedElements,
		FrozenSet<string> allowedAttributes)
	{
		var doc = XDocument.Parse(svg, LoadOptions.PreserveWhitespace);
		if (doc.Root is null)
			return new SvgSanitizeResult { Svg = svg, HasViolations = false, Violations = [] };

		var violations = new List<SvgViolation>();

		var elementsToRemove = doc.Root.DescendantsAndSelf()
			.Where(el => !allowedElements.Contains(el.Name.LocalName))
			.ToList();

		foreach (var el in elementsToRemove)
		{
			violations.Add(new SvgViolation("element", el.Name.LocalName));
			el.Remove();
		}

		var attrsToRemove = doc.Root.DescendantsAndSelf()
			.SelectMany(el => el.Attributes().Select(a => (Element: el, Attr: a)))
			.Where(pair => !IsAllowedAttribute(pair.Attr, allowedAttributes))
			.ToList();

		foreach (var (el, attr) in attrsToRemove)
		{
			violations.Add(new SvgViolation("attribute", attr.Name.LocalName, el.Name.LocalName));
			attr.Remove();
		}

		if (violations.Count == 0)
			return new SvgSanitizeResult { Svg = svg, HasViolations = false, Violations = [] };

		return new SvgSanitizeResult
		{
			Svg = doc.ToString(SaveOptions.DisableFormatting),
			HasViolations = true,
			Violations = violations,
		};
	}

	// ========================================================================
	// Attribute rules (always enforced regardless of allowlist)
	// ========================================================================

	private static bool IsAllowedAttribute(XAttribute attr, FrozenSet<string> allowed)
	{
		var name = attr.Name.LocalName;

		if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
			return false;

		if (name.StartsWith("data-", StringComparison.Ordinal))
			return true;

		if (attr.Name.NamespaceName.Length > 0)
		{
			if (name is "href" || attr.Name.NamespaceName.Contains("xlink"))
				return false;

			if (attr.Name.NamespaceName.Contains("xmlns") || name == "xmlns")
				return true;
		}

		return allowed.Contains(name);
	}
}
