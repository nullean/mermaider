using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Mermaider.Models;

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
	private const int MaxNestedImageDepth = 4;
	private const string SvgNamespace = "http://www.w3.org/2000/svg";
	private const string XLinkNamespace = "http://www.w3.org/1999/xlink";

	/// <summary>
	/// Matches a same-document-safe base64 image data URI — the only <c>href</c> value
	/// ever permitted, and only on an <c>&lt;image&gt;</c> element (see <see cref="IsAllowedAttribute"/>).
	/// Deliberately excludes <c>http(s):</c>, <c>javascript:</c>, and any non-image data URI.
	/// </summary>
	[GeneratedRegex(@"^data:image/(?<type>svg\+xml|png);base64,(?<payload>(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex SafeImageDataUriPattern();

	// ========================================================================
	// Default allowlists
	// ========================================================================

	/// <summary>Default set of allowed SVG element local names.</summary>
	public static readonly FrozenSet<string> DefaultAllowedElements = new[]
	{
		"svg", "g", "defs", "title", "desc",
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
		"id", "class", "transform", "role",
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

	private static readonly FrozenSet<string> PaintAttributes = new[]
	{
		"color", "fill", "stroke", "stop-color", "flood-color",
	}.ToFrozenSet(StringComparer.Ordinal);

	private static readonly FrozenSet<string> LocalReferenceAttributes = new[]
	{
		"marker-start", "marker-mid", "marker-end",
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
		Sanitize(
			svg,
			DefaultAllowedElements,
			DefaultAllowedAttributes,
			allowRendererStyles: false,
			nestedImageDepth: 0);

	/// <summary>
	/// Sanitize an SVG string against the default allowlists, selecting whether violations
	/// are stripped or rejected. Strip mode returns a valid empty SVG for malformed XML.
	/// Block mode throws <see cref="MermaidSvgException"/> on any violation.
	/// </summary>
	public static SvgSanitizeResult Sanitize(string svg, SanitizeMode mode) =>
		ApplyMode(Sanitize(svg), mode);

	/// <summary>
	/// Sanitize an SVG string against custom allowlists. Custom sets can only restrict the
	/// built-in safety allowlists; adding a name that is not in a default set never permits it.
	/// Disallowed elements and attributes are stripped from the output.
	/// </summary>
	/// <param name="svg">Raw SVG markup.</param>
	/// <param name="allowedElements">Set of allowed element local names.</param>
	/// <param name="allowedAttributes">Set of allowed attribute local names. Only names in
	/// this set and <see cref="DefaultAllowedAttributes"/> are kept (plus always-allowed <c>data-*</c> attributes and XML namespace
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
		=> Sanitize(
			svg,
			allowedElements,
			allowedAttributes,
			allowRendererStyles: false,
			nestedImageDepth: 0);

	/// <summary>
	/// Sanitize an SVG string against custom allowlists, selecting whether violations are
	/// stripped or rejected. Custom sets are intersected with the default safety sets;
	/// structural and value-level safety rules remain mandatory.
	/// </summary>
	public static SvgSanitizeResult Sanitize(
		string svg,
		FrozenSet<string> allowedElements,
		FrozenSet<string> allowedAttributes,
		SanitizeMode mode) =>
		ApplyMode(Sanitize(svg, allowedElements, allowedAttributes), mode);

	/// <summary>
	/// Sanitizes output produced by Mermaider's own renderers. The renderer emits one fixed,
	/// direct-child stylesheet and a constrained root style declaration for theming; standalone
	/// untrusted SVG sanitization deliberately does not permit either CSS surface.
	/// </summary>
	internal static SvgSanitizeResult SanitizeRendererOutput(string svg) =>
		Sanitize(
			svg,
			DefaultAllowedElements,
			DefaultAllowedAttributes,
			allowRendererStyles: true,
			nestedImageDepth: 0);

	private static SvgSanitizeResult ApplyMode(SvgSanitizeResult result, SanitizeMode mode)
	{
		if (mode is not (SanitizeMode.Strip or SanitizeMode.Block))
			throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown SVG sanitization mode.");

		if (mode == SanitizeMode.Block && result.HasViolations)
			throw new MermaidSvgException(result.Violations);

		return result;
	}

	private static SvgSanitizeResult Sanitize(
		string svg,
		FrozenSet<string> allowedElements,
		FrozenSet<string> allowedAttributes,
		bool allowRendererStyles,
		int nestedImageDepth)
	{
		ArgumentNullException.ThrowIfNull(svg);
		ArgumentNullException.ThrowIfNull(allowedElements);
		ArgumentNullException.ThrowIfNull(allowedAttributes);

		var settings = new XmlReaderSettings
		{
			DtdProcessing = DtdProcessing.Prohibit,
			XmlResolver = null,
		};

		XDocument doc;
		try
		{
			using var stringReader = new StringReader(svg);
			using var reader = XmlReader.Create(stringReader, settings);
			doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
		}
		catch (XmlException)
		{
			return new SvgSanitizeResult
			{
				Svg = SvgDocuments.Empty,
				HasViolations = true,
				Violations = [new SvgViolation("document", "malformed-xml")],
			};
		}

		if (doc.Root is null)
			return new SvgSanitizeResult { Svg = svg, HasViolations = false, Violations = [] };

		var violations = new List<SvgViolation>();
		var root = doc.Root;
		var rootNamespace = root.Name.NamespaceName;

		if (root.Name.LocalName != "svg"
			|| !allowedElements.Contains("svg")
			|| rootNamespace is not ("" or SvgNamespace)
			|| (rootNamespace == SvgNamespace && root.GetDefaultNamespace().NamespaceName != SvgNamespace))
		{
			violations.Add(new SvgViolation("element", root.Name.LocalName));
			return new SvgSanitizeResult
			{
				Svg = SvgDocuments.Empty,
				HasViolations = true,
				Violations = violations,
			};
		}

		if (doc.Declaration is not null)
		{
			violations.Add(new SvgViolation("node", "xml-declaration"));
			doc.Declaration = null;
		}

		var processingInstructions = doc.DescendantNodes()
			.OfType<XProcessingInstruction>()
			.Concat(doc.Nodes().OfType<XProcessingInstruction>())
			.Distinct()
			.ToList();

		foreach (var instruction in processingInstructions)
		{
			violations.Add(new SvgViolation("node", instruction.Target));
			instruction.Remove();
		}

		var rendererStyle = allowRendererStyles
			? root.Elements().FirstOrDefault(IsSafeRendererStyleElement)
			: null;

		var elementsToRemove = root.DescendantsAndSelf()
			.Where(el =>
				el.Name.NamespaceName != rootNamespace
					|| (el.Name.LocalName == "style"
						? !ReferenceEquals(el, rendererStyle)
						: !allowedElements.Contains(el.Name.LocalName)
							|| !DefaultAllowedElements.Contains(el.Name.LocalName)))
			.ToList();

		foreach (var el in elementsToRemove)
			violations.Add(new SvgViolation("element", el.Name.LocalName));

		var elementsToRemoveSet = elementsToRemove.ToHashSet();
		foreach (var el in elementsToRemove)
		{
			if (!el.Ancestors().Any(elementsToRemoveSet.Contains))
				el.Remove();
		}

		var attrsToRemove = root.DescendantsAndSelf()
			.SelectMany(el => el.Attributes().Select(a => (Element: el, Attr: a)))
			.Where(pair => !IsAllowedAttribute(
				pair.Element,
				pair.Attr,
				root,
				allowedAttributes,
				allowRendererStyles,
				nestedImageDepth))
			.ToList();

		foreach (var (el, attr) in attrsToRemove)
		{
			violations.Add(new SvgViolation("attribute", attr.Name.LocalName, el.Name.LocalName));
			attr.Remove();
		}

		// <image> is defined as an empty element in the SVG spec — it must never have children,
		// regardless of whether those children would individually be allowlisted. Strip them
		// unconditionally rather than trusting per-element allowlisting to cover this case.
		var imagesWithChildren = root.DescendantsAndSelf()
			.Where(el => el.Name.LocalName == "image" && el.Nodes().Any())
			.ToList();

		foreach (var image in imagesWithChildren)
		{
			foreach (var child in image.Nodes().ToList())
			{
				if (child is XElement childElement)
					violations.Add(new SvgViolation("element", childElement.Name.LocalName, "image"));
				else
					violations.Add(new SvgViolation("content", child.NodeType.ToString(), "image"));
				child.Remove();
			}
		}

		// <title> and <desc> are metadata text containers, not alternate markup roots.
		// Keeping nested allowlisted elements would create surprising parser state when the
		// sanitized string is embedded into HTML, so retain text only.
		var metadataElementsWithChildren = root.DescendantsAndSelf()
			.Where(el => el.Name.LocalName is "title" or "desc")
			.ToList();

		foreach (var metadataElement in metadataElementsWithChildren)
		{
			foreach (var child in metadataElement.Nodes().Where(node => node is not XText).ToList())
			{
				if (child is XElement childElement)
					violations.Add(new SvgViolation("element", childElement.Name.LocalName, metadataElement.Name.LocalName));
				else
					violations.Add(new SvgViolation("content", child.NodeType.ToString(), metadataElement.Name.LocalName));
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

	private static bool IsAllowedAttribute(
		XElement element,
		XAttribute attr,
		XElement root,
		FrozenSet<string> allowed,
		bool allowRendererStyles,
		int nestedImageDepth)
	{
		// Pure allowlist: an attribute is denied unless one of the positive rules below
		// affirms it as safe. There is deliberately NO blocklist of "known bad" names
		// (e.g. on* handlers) — those are denied simply by not appearing in the allowlist,
		// so safety never depends on us having enumerated every dangerous attribute.
		var name = attr.Name.LocalName;

		// Namespace declarations can change how an otherwise allowlisted local name is
		// interpreted. Permit only the canonical SVG default namespace and the legacy xlink
		// namespace needed for a safe image data URI.
		if (attr.IsNamespaceDeclaration)
		{
			if (!ReferenceEquals(element, root))
				return false;

			return (name == "xmlns" && attr.Value == SvgNamespace)
				|| (name == "xlink" && attr.Value == XLinkNamespace);
		}

		// Scoped positive exception: an <image> may carry a validated base64 image data URI.
		// This is the ONLY href/xlink:href ever allowed.
		if (name == "href")
		{
			var validNamespace = attr.Name.NamespaceName is "" or XLinkNamespace;
			return validNamespace
				&& element.Name.LocalName == "image"
				&& IsSafeImageDataUri(attr.Value, nestedImageDepth);
		}

		// The allowlist covers plain attributes only. Check this before the data-/aria-
		// convenience rule so a namespaced lookalike cannot bypass namespace enforcement.
		if (attr.Name.NamespaceName.Length > 0)
			return false;

		// data-* attributes carry data, not behavior; aria-* attributes are accessibility
		// metadata. Neither can execute script.
		if (name.StartsWith("data-", StringComparison.Ordinal)
			|| name.StartsWith("aria-", StringComparison.Ordinal))
			return true;

		if (name == "style")
			return allowRendererStyles && ReferenceEquals(element, root) && SvgValueAllowlist.IsAllowedRootStyle(attr.Value);

		if (!allowed.Contains(name) || !DefaultAllowedAttributes.Contains(name))
			return false;

		if (LocalReferenceAttributes.Contains(name))
			return SvgValueAllowlist.IsAllowedLocalReference(attr.Value);

		if (PaintAttributes.Contains(name))
			return SvgValueAllowlist.IsAllowedPaint(attr.Value);

		return true;
	}

	private static bool IsSafeImageDataUri(string value, int nestedImageDepth)
	{
		Match match;
		try
		{
			match = SafeImageDataUriPattern().Match(value);
		}
		catch (RegexMatchTimeoutException)
		{
			return false;
		}
		if (!match.Success || match.Groups["payload"].Length == 0)
			return false;

		byte[] bytes;
		try
		{
			bytes = Convert.FromBase64String(match.Groups["payload"].Value);
		}
		catch (FormatException)
		{
			return false;
		}

		if (match.Groups["type"].Value == "png")
		{
			ReadOnlySpan<byte> pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
			return bytes.AsSpan().StartsWith(pngSignature);
		}

		if (nestedImageDepth >= MaxNestedImageDepth)
			return false;

		string nestedSvg;
		try
		{
			nestedSvg = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
				.GetString(bytes);
		}
		catch (DecoderFallbackException)
		{
			return false;
		}

		var nestedResult = Sanitize(
			nestedSvg,
			DefaultAllowedElements,
			DefaultAllowedAttributes,
			allowRendererStyles: false,
			nestedImageDepth + 1);
		return !nestedResult.HasViolations;
	}

	private static bool IsSafeRendererStyleElement(XElement element) => element.Name.LocalName == "style"
		&& !element.HasAttributes
		&& element.Nodes().All(node => node is XText)
		&& RendererStylesheetAllowlist.IsAllowed(element.Value);
}
