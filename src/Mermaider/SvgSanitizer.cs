using System.Collections.Frozen;
using System.Text.RegularExpressions;
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
/// element/attribute <b>allowlist</b>: anything not explicitly affirmed as safe is removed.
/// There is no blocklist — safety does not depend on having enumerated every dangerous
/// construct. Because they are absent from the allowlist, the main XSS vectors are denied
/// as a consequence: <c>&lt;script&gt;</c>, <c>&lt;foreignObject&gt;</c>, <c>on*</c> event
/// handlers, and <c>href</c>/<c>xlink:href</c> (which can carry <c>javascript:</c> URIs).
/// The single positive exception is a base64 image data URI <c>href</c> on an <c>&lt;image&gt;</c>.
/// <para>
/// Usable standalone — not tied to the Mermaid rendering pipeline.
/// </para>
/// </summary>
public static partial class SvgSanitizer
{
	private const int TimeoutMs = 2000;

	/// <summary>
	/// Matches a same-document-safe base64 image data URI — the only <c>href</c> value
	/// ever permitted, and only on an <c>&lt;image&gt;</c> element (see <see cref="IsAllowedAttribute"/>).
	/// Deliberately excludes <c>http(s):</c>, <c>javascript:</c>, and any non-image data URI.
	/// </summary>
	[GeneratedRegex(@"^data:image/(?:svg\+xml|png);base64,[A-Za-z0-9+/]+=*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex SafeImageDataUriPattern();

	// ========================================================================
	// Default allowlists
	// ========================================================================

	/// <summary>Default set of allowed SVG element local names.</summary>
	public static readonly FrozenSet<string> DefaultAllowedElements = new[]
	{
		"svg", "g", "defs", "style", "title", "desc",
		"rect", "circle", "ellipse", "polygon", "polyline", "line", "path",
		"text", "tspan",
		"marker", "image",
		"clipPath", "mask",
		"linearGradient", "radialGradient", "stop",
		"filter", "feGaussianBlur", "feOffset", "feBlend", "feFlood",
		"feComposite", "feMerge", "feMergeNode", "feDropShadow",
		"feColorMatrix", "feMorphology",
	}.ToFrozenSet(StringComparer.Ordinal);

	/// <summary>Default set of allowed SVG attribute local names.</summary>
	public static readonly FrozenSet<string> DefaultAllowedAttributes = new[]
	{
		"id", "class", "style", "transform", "role",
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
	/// <param name="allowedAttributes">Set of allowed attribute local names. Only names in
	/// this set are kept (plus always-allowed <c>data-*</c> attributes and XML namespace
	/// declarations). <c>on*</c> handlers and <c>href</c>/<c>xlink:href</c> are denied simply
	/// by not being in the set — there is no separate blocklist. The single positive exception
	/// is an <c>&lt;image&gt;</c> element's <c>href</c> carrying a base64 <c>data:image/svg+xml</c>
	/// or <c>data:image/png</c> URI (used for diagram icons); every other scheme (<c>http(s):</c>,
	/// <c>javascript:</c>, non-image data URIs) and any namespaced attribute is stripped.</param>
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

		// <image> is defined as an empty element in the SVG spec — it must never have children,
		// regardless of whether those children would individually be allowlisted. Strip them
		// unconditionally rather than trusting per-element allowlisting to cover this case.
		var imagesWithChildren = doc.Root.DescendantsAndSelf()
			.Where(el => el.Name.LocalName == "image" && el.Nodes().Any())
			.ToList();

		foreach (var image in imagesWithChildren)
		{
			foreach (var child in image.Nodes().ToList())
			{
				if (child is XElement childElement)
					violations.Add(new SvgViolation("element", childElement.Name.LocalName, "image"));
				child.Remove();
			}
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
		// Pure allowlist: an attribute is denied unless one of the positive rules below
		// affirms it as safe. There is deliberately NO blocklist of "known bad" names
		// (e.g. on* handlers) — those are denied simply by not appearing in the allowlist,
		// so safety never depends on us having enumerated every dangerous attribute.
		var name = attr.Name.LocalName;

		// data-* attributes carry data, not behavior; aria-* attributes are accessibility
		// metadata. Neither can execute script.
		if (name.StartsWith("data-", StringComparison.Ordinal)
			|| name.StartsWith("aria-", StringComparison.Ordinal))
			return true;

		// XML namespace declarations.
		if (name == "xmlns" || attr.Name.NamespaceName.Contains("xmlns"))
			return true;

		// Scoped positive exception: an <image> may carry a same-document-safe base64 image
		// data URI (architecture/treeview icons). This is the ONLY href/xlink:href ever allowed;
		// every other href — javascript:, http(s):, non-image data URIs, or href on any other
		// element — is denied because "href" is not in the allowlist.
		if ((name is "href" || attr.Name.NamespaceName.Contains("xlink"))
			&& attr.Parent?.Name.LocalName == "image"
			&& SafeImageDataUriPattern().IsMatch(attr.Value))
			return true;

		// Any other namespaced attribute (xlink:*, custom prefixes) is not something the
		// renderer emits and is not affirmed safe — deny it. The allowlist only covers
		// plain, unnamespaced local names.
		if (attr.Name.NamespaceName.Length > 0)
			return false;

		return allowed.Contains(name);
	}
}
