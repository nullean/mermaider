using System.Text;
using System.Xml.Linq;
using AwesomeAssertions;
using CsCheck;

namespace Mermaider.Tests.Rendering;

/// <summary>
/// Property-based fuzzing (CsCheck) of the complete standalone untrusted-SVG boundary:
/// <code>
/// seed SVG corpus -- token/character mutations --+
///                                                +-- SvgSanitizer.Sanitize -- safety oracle
/// element x attribute x value cross-product -----+
/// </code>
/// The mutation fuzzer attacks both layers of the boundary. It corrupts XML structure with
/// truncated tags, processing instructions, invalid characters, namespace declarations, and
/// broken quoting; it also inserts structurally valid SVG attack surfaces such as unexpected
/// elements, event-like attributes, CSS, external references, and image data URIs. Malformed
/// XML must converge to the documented valid empty SVG result instead of leaking an XML parser
/// exception.
/// <para>
/// The structured fuzzer independently combines element names, attribute names, and values.
/// This exercises namespace confusion, attribute-to-element scoping, paint and marker value
/// grammars, text/attribute escaping, image content models, and the nested-SVG data-URI rule.
/// </para>
/// <para>
/// Every result is fed to a safety oracle that parses the emitted XML, walks every node and
/// attribute, and checks the same externally observable invariants required for direct HTML
/// embedding: an SVG root, approved namespaces, explicit element/attribute allowlists, no
/// standalone CSS or processing instructions, data-image links only on empty image elements,
/// and text-only title/description metadata. A second sanitizer pass must be an exact no-op,
/// proving that sanitization converges and cannot expose a new construct after serialization.
/// Every run draws 4,000 new cases; a failure is shrunk to a minimal input and reports the seed that reproduces it.
/// </para>
/// </summary>
public class SvgSanitizerFuzzTests
{
	private const string SvgNamespace = "http://www.w3.org/2000/svg";
	private const string XLinkNamespace = "http://www.w3.org/1999/xlink";
	private static readonly HashSet<string> PaintAttributes = ["color", "fill", "stroke", "stop-color", "flood-color",];
	private static readonly HashSet<string> LocalReferenceAttributes = ["marker-start", "marker-mid", "marker-end",];

	private static readonly string[] Corpus =
	[
		"""<svg xmlns="http://www.w3.org/2000/svg"><rect width="10" height="10"/></svg>""",
		"""<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>""",
		"""<svg xmlns="http://www.w3.org/2000/svg"><foreignObject><body xmlns="http://www.w3.org/1999/xhtml">x</body></foreignObject></svg>""",
		"""<svg xmlns="http://www.w3.org/2000/svg"><image href="javascript:alert(1)"/></svg>""",
		"""<svg xmlns="http://www.w3.org/2000/svg"><rect fill="url(https://attacker.invalid/a.svg#x)"/></svg>""",
		"""<svg xmlns="http://www.w3.org/2000/svg"><style>body{display:none}</style></svg>""",
		"""<?xml-stylesheet href="https://attacker.invalid/x.css"?><svg xmlns="http://www.w3.org/2000/svg"/>""",
		"""<html><svg><rect/></svg></html>""",
		"""<svg><title><text>nested</text></title><image><rect/></image></svg>""",
		"""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"><image xlink:href="data:image/svg+xml;base64,PHN2ZyAvPg=="/></svg>""",
	];

	private static readonly string[] MutationTokens =
	[
		"<script>",
		"</script>",
		"<style>",
		"</style>",
		"<foreignObject>",
		" onclick=\"alert(1)\"",
		" onload=\"alert(1)\"",
		" href=\"javascript:alert(1)\"",
		" style=\"background:url(https://attacker.invalid/x)\"",
		"url(#local)",
		"url(https://attacker.invalid/x)",
		"data:image/svg+xml;base64,PHN2ZyAvPg==",
		"&lt;",
		"&amp;",
		"<![CDATA[",
		"]]>",
		"<!--",
		"-->",
		"<?xml-stylesheet?>",
		"xmlns:e=\"https://attacker.invalid/ns\"",
		"e:data-value=\"x\"",
		"\0",
		"\uD800",
	];

	/// <summary>
	/// One edit of a seed SVG. Positions are fractions of the current length, so an edit stays valid whatever the
	/// earlier edits did to the text, and shrinking moves an edit towards the start instead of out of range.
	/// </summary>
	private readonly record struct Mutation(int Kind, double At, double From, double Length, int Token);

	private static readonly Gen<Mutation> GenMutation = Gen.Select(
		Gen.Int[0, 4],
		Gen.Double[0, 1],
		Gen.Double[0, 1],
		Gen.Double[0, 1],
		Gen.Int[0, MutationTokens.Length - 1],
		(kind, at, from, length, token) => new Mutation(kind, at, from, length, token)
	);

	private static readonly Gen<string> GenMutatedSvg = Gen.Select(
		Gen.Int[0, Corpus.Length - 1],
		GenMutation.Array[1, 6],
		(seed, mutations) => Mutate(Corpus[seed], mutations)
	);

	[Test]
	public void Mutational_fuzzer_preserves_the_safety_invariant() =>
		GenMutatedSvg.Sample(
			input => AssertSafeAndIdempotent(input, SvgSanitizer.Sanitize(input), "the output must be safe to embed in HTML"),
			iter: 2_500,
			print: input => input
		);

	private static readonly string[] Elements =
	[
		"g",
		"rect",
		"path",
		"text",
		"title",
		"desc",
		"image",
		"style",
		"script",
		"foreignObject",
		"animate",
		"use",
		"a",
		"iframe",
		"object",
	];

	private static readonly string[] Attributes =
	[
		"fill",
		"stroke",
		"marker-end",
		"style",
		"href",
		"xlink:href",
		"onclick",
		"onload",
		"data-value",
		"aria-label",
		"unknown",
	];

	private static readonly string[] Values =
	[
		"#fff",
		"red",
		"rgb(1, 2, 3)",
		"var(--_text)",
		"url(#local)",
		"url(https://attacker.invalid/x)",
		"javascript:alert(1)",
		"data:image/svg+xml;base64,PHN2ZyAvPg==",
		"data:image/png;base64,iVBORw0KGgo=",
		"\"/><script>alert(1)</script>",
		"u\\72l(https://attacker.invalid/x)",
		"red;position:fixed",
		"<>&\"'",
	];

	[Test]
	public void Structured_fuzzer_covers_element_attribute_and_value_cross_product() =>
		Gen.Select(
			Gen.OneOf(Elements),
			Gen.OneOf(Attributes),
			Gen.OneOf(Values),
			Gen.OneOf(MutationTokens),
			(element, attribute, value, text) =>
				$"<svg xmlns=\"{SvgNamespace}\" xmlns:xlink=\"{XLinkNamespace}\"><{element} {attribute}=\"{EscapeAttribute(value)}\">{EscapeText(text)}</{element}></svg>"
		).Sample(
			input => AssertSafeAndIdempotent(input, SvgSanitizer.Sanitize(input), "the output must be safe to embed in HTML"),
			iter: 1_500,
			print: input => input
		);

	private static int Scale(double fraction, int count) => Math.Min((int)(fraction * count), count - 1);

	private static string Mutate(string seed, Mutation[] mutations)
	{
		var value = new StringBuilder(seed);

		foreach (var m in mutations)
		{
			var token = MutationTokens[m.Token];
			switch (m.Kind)
			{
				case 0:
					_ = value.Insert(Scale(m.At, value.Length + 1), token);
					break;
				case 1 when value.Length > 0:
					{
						var start = Scale(m.From, value.Length);
						var length = 1 + Scale(m.Length, Math.Min(24, value.Length - start));
						_ = value.Remove(start, length);
						break;
					}
				case 2 when value.Length > 0:
					value[Scale(m.At, value.Length)] = token[0];
					break;
				case 3 when value.Length > 0:
					{
						var start = Scale(m.From, value.Length);
						var length = Math.Min(1 + Scale(m.Length, 16), value.Length - start);
						_ = value.Insert(Scale(m.At, value.Length + 1), value.ToString(start, length));
						break;
					}
				default:
					_ = value.Append(token);
					break;
			}

			if (value.Length > 4_096)
				_ = value.Remove(4_096, value.Length - 4_096);
		}

		return value.ToString();
	}

	private static void AssertSafeAndIdempotent(string input, SvgSanitizeResult result, string because)
	{
		result.HasViolations.Should().Be(result.Violations.Count > 0, because);
		if (!result.HasViolations)
			result.Svg.Should().BeSameAs(input, because);

		var secondPass = SvgSanitizer.Sanitize(result.Svg);
		secondPass.HasViolations.Should().BeFalse(because);
		secondPass.Svg.Should().BeSameAs(result.Svg, because);

		var doc = XDocument.Parse(result.Svg);
		doc.Declaration.Should().BeNull(because);
		var root = doc.Root!;
		root.Name.LocalName.Should().Be("svg", because);
		root.Name.NamespaceName.Should().BeOneOf(["", SvgNamespace], because);

		root.DescendantsAndSelf().Should().OnlyContain(e => SvgSanitizer.DefaultAllowedElements.Contains(e.Name.LocalName), because);
		root.DescendantsAndSelf().Should().NotContain(e => e.Name.LocalName == "style", because);
		root.DescendantNodes().Should().NotContain(node => node is XProcessingInstruction, because);

		foreach (var element in root.DescendantsAndSelf())
		{
			if (element.Name.LocalName == "image")
				element.Nodes().Should().BeEmpty(because);
			if (element.Name.LocalName is "title" or "desc")
				element.Nodes().Should().OnlyContain(node => node is XText, because);

			foreach (var attribute in element.Attributes())
			{
				if (attribute.IsNamespaceDeclaration)
				{
					attribute.Value.Should().BeOneOf([SvgNamespace, XLinkNamespace], because);
					continue;
				}

				if (attribute.Name.LocalName == "href")
				{
					element.Name.LocalName.Should().Be("image", because);
					var allowedImageType = attribute.Value.StartsWith("data:image/svg+xml;base64,", StringComparison.Ordinal) || attribute.Value.StartsWith(
						"data:image/png;base64,",
						StringComparison.Ordinal
					);
					allowedImageType.Should().BeTrue(because);
					continue;
				}

				attribute.Name.NamespaceName.Should().BeEmpty(because);
				var allowedByName = SvgSanitizer.DefaultAllowedAttributes.Contains(attribute.Name.LocalName) || attribute
					.Name
					.LocalName
					.StartsWith("data-", StringComparison.Ordinal) || attribute.Name.LocalName.StartsWith("aria-", StringComparison.Ordinal);
				allowedByName.Should().BeTrue(because);

				if (PaintAttributes.Contains(attribute.Name.LocalName))
					SvgValueAllowlist.IsAllowedPaint(attribute.Value).Should().BeTrue(because);
				if (LocalReferenceAttributes.Contains(attribute.Name.LocalName))
					SvgValueAllowlist.IsAllowedLocalReference(attribute.Value).Should().BeTrue(because);
			}
		}
	}

	private static string EscapeAttribute(string value) =>
		value
			.Replace("&", "&amp;", StringComparison.Ordinal)
			.Replace("\"", "&quot;", StringComparison.Ordinal)
			.Replace("<", "&lt;", StringComparison.Ordinal)
			.Replace(">", "&gt;", StringComparison.Ordinal);

	private static string EscapeText(string value) =>
		value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
}
