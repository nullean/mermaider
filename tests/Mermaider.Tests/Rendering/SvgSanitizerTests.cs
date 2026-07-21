using System.Collections.Frozen;
using System.Text;
using System.Xml.Linq;
using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Rendering;

namespace Mermaider.Tests.Rendering;

public class SvgSanitizerTests
{
	// ========================================================================
	// Public API — SvgSanitizer.Sanitize(svg)
	// ========================================================================

	[Test]
	public void Strips_script_element()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script><rect x="0" y="0" width="10" height="10"/></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("<script");
		result.Svg.Should().NotContain("alert");
		result.Svg.Should().Contain("<rect");
		result.Violations.Should().Contain(v => v.Kind == "element" && v.Name == "script");
	}

	[Test]
	public void Strips_foreignObject_element()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><foreignObject><body xmlns="http://www.w3.org/1999/xhtml"><div>hack</div></body></foreignObject><rect x="0" y="0" width="10" height="10"/></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("foreignObject");
		result.Svg.Should().NotContain("hack");
	}

	[Test]
	public void Strips_onclick_attribute()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><rect x="0" y="0" width="10" height="10" onclick="alert(1)"/></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("onclick");
		result.Svg.Should().NotContain("alert");
		result.Svg.Should().Contain("<rect");
		result.Violations.Should().Contain(v => v.Kind == "attribute" && v.Name == "onclick");
	}

	[Test]
	public void Strips_onload_attribute()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><rect x="0" y="0" width="10" height="10" onload="fetch('http://evil.com')"/></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("onload");
	}

	[Test]
	public void Strips_href_attribute()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"><use xlink:href="javascript:alert(1)"/></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("javascript");
	}

	[Test]
	public void Strips_unsafe_href_from_image_element_but_keeps_the_element()
	{
		// <image> is allowed structurally (architecture-diagram icons render as <image> with a
		// base64 data URI — see SvgSanitizerTests further below), but the actual attack vector —
		// an external/tracking href — is always stripped regardless.
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><image href="http://evil.com/tracker.png"/><rect x="0" y="0" width="10" height="10"/></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().Contain("<image");
		result.Svg.Should().NotContain("evil.com");
		result.Svg.Should().NotContain("href");
	}

	[Test]
	public void Strips_animate_element()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><rect x="0" y="0" width="10" height="10"><animate attributeName="x" to="100"/></rect></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("<animate");
	}

	[Test]
	public void Preserves_allowed_elements_and_attributes()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><g class="node" data-id="A"><rect x="0" y="0" width="50" height="30" rx="6" fill="#fff" stroke="#000" stroke-width="1.5"/><text x="25" y="15" text-anchor="middle" font-size="14">Hello</text></g></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeFalse();
		result.Svg.Should().BeSameAs(svg);
	}

	[Test]
	public void Returns_original_string_when_clean()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><rect x="0" y="0" width="10" height="10"/></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeFalse();
		result.Svg.Should().BeSameAs(svg);
	}

	[Test]
	public void Preserves_data_attributes()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><g data-id="A" data-label="hello" data-shape="rectangle"><rect x="0" y="0" width="10" height="10"/></g></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeFalse();
		result.Svg.Should().Contain("data-id");
		result.Svg.Should().Contain("data-label");
	}

	[Test]
	public void Preserves_marker_elements()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><defs><marker id="arrow" markerWidth="12" markerHeight="12" refX="12" refY="6" orient="auto" markerUnits="userSpaceOnUse"><polygon points="0 0, 12 6, 0 12" fill="#000"/></marker></defs></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeFalse();
		result.Svg.Should().Contain("<marker");
	}

	[Test]
	public void Strips_style_element_from_standalone_untrusted_svg()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><style>text { font-family: Inter; }</style><rect x="0" y="0" width="10" height="10"/></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("<style");
		result.Svg.Should().Contain("<rect");
	}

	[Test]
	public void Violations_list_contains_all_issues()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><script>x</script><rect onclick="y" onload="z" x="0" y="0" width="10" height="10"/></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.Violations.Should().HaveCountGreaterThanOrEqualTo(3);
	}

	private static readonly string[] SourceArray = ["svg", "rect"];
	private static readonly string[] SourceArray0 = ["xmlns", "x", "y", "width", "height"];
	private static readonly string[] SourceArray1 = ["svg", "rect"];

	[Test]
	public void Custom_allowlists_restrict_further()
	{
		var onlyRect = SourceArray.ToFrozenSet(StringComparer.Ordinal);
		var onlyBasic = SourceArray0.ToFrozenSet(StringComparer.Ordinal);

		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><rect x="0" y="0" width="10" height="10" fill="#f00"/></svg>""";
		var result = SvgSanitizer.Sanitize(svg, onlyRect, onlyBasic);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("fill");
		result.Svg.Should().Contain("<rect");
		result.Violations.Should().Contain(v => v.Kind == "attribute" && v.Name == "fill");
	}

	[Test]
	public void Custom_allowlists_strip_disallowed_elements()
	{
		var noText = SourceArray1.ToFrozenSet(StringComparer.Ordinal);

		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><rect x="0" y="0" width="10" height="10"/><text x="5" y="5">no</text></svg>""";
		var result = SvgSanitizer.Sanitize(svg, noText, SvgSanitizer.DefaultAllowedAttributes);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("<text");
		result.Svg.Should().Contain("<rect");
	}

	[Test]
	public void Custom_allowlists_cannot_expand_the_default_safety_ceiling()
	{
		var expandedElements = SvgSanitizer.DefaultAllowedElements
			.Append("script")
			.ToFrozenSet(StringComparer.Ordinal);
		var expandedAttributes = SvgSanitizer.DefaultAllowedAttributes
			.Append("onclick")
			.ToFrozenSet(StringComparer.Ordinal);
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script><rect width="10" height="10" onclick="alert(2)"/></svg>""";

		var result = SvgSanitizer.Sanitize(svg, expandedElements, expandedAttributes);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("<script");
		result.Svg.Should().NotContain("onclick");
		result.Svg.Should().NotContain("alert");
		result.Svg.Should().Contain("<rect");
	}

	// ========================================================================
	// Block mode — through the rendered SVG pipeline stage
	// ========================================================================

	[Test]
	public void Block_throws_on_script_element()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>""";

		var act = () => SvgSanitizationStage.Apply(svg, SanitizeMode.Block);

		act.Should().ThrowExactly<MermaidSvgException>()
			.WithMessage("*disallowed*script*");
	}

	[Test]
	public void Block_throws_on_event_handler()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><rect x="0" y="0" width="10" height="10" onmouseover="alert(1)"/></svg>""";

		var act = () => SvgSanitizationStage.Apply(svg, SanitizeMode.Block);

		act.Should().ThrowExactly<MermaidSvgException>()
			.WithMessage("*disallowed*onmouseover*");
	}

	[Test]
	public void Block_throws_on_foreignObject()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><foreignObject width="100" height="100"/></svg>""";

		var act = () => SvgSanitizationStage.Apply(svg, SanitizeMode.Block);

		act.Should().ThrowExactly<MermaidSvgException>()
			.WithMessage("*disallowed*foreignObject*");
	}

	[Test]
	public void Block_returns_original_when_clean()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><g><rect x="0" y="0" width="10" height="10" fill="#fff"/><text x="5" y="5">ok</text></g></svg>""";

		var result = SvgSanitizationStage.Apply(svg, SanitizeMode.Block);
		result.Should().BeSameAs(svg);
	}

	[Test]
	public void Strip_via_bridge_returns_cleaned_svg()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><script>x</script><rect x="0" y="0" width="10" height="10"/></svg>""";

		var result = SvgSanitizationStage.Apply(svg, SanitizeMode.Strip);
		result.Should().NotContain("<script");
		result.Should().Contain("<rect");
	}

	// ========================================================================
	// Integration — sanitization is non-optional and decoupled from strict mode
	// ========================================================================

	[Test]
	public void SanitizeMode_defaults_to_strip()
	{
		new RenderOptions().SanitizeMode.Should().Be(SanitizeMode.Strip);
	}

	[Test]
	public void FallbackSvg_is_the_canonical_safe_empty_document()
	{
		MermaidRenderer.FallbackSvg.Should().Be("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
		SvgSanitizer.Sanitize(MermaidRenderer.FallbackSvg).HasViolations.Should().BeFalse();
	}

	[Test]
	public void Strip_returns_cleaned_svg_when_renderer_output_is_rejected()
	{
		var result = SvgSanitizationStage.Apply(
			"<svg xmlns=\"http://www.w3.org/2000/svg\"><script>attack</script></svg>",
			SanitizeMode.Strip);

		result.Should().NotBe(MermaidRenderer.FallbackSvg);
		XDocument.Parse(result).Root!.Elements().Should().BeEmpty();
	}

	[Test]
	public void Strip_preserves_safe_siblings_while_removing_a_violation()
	{
		var result = SvgSanitizationStage.Apply(
			"<svg xmlns=\"http://www.w3.org/2000/svg\"><script>attack</script><text>Safe</text></svg>",
			SanitizeMode.Strip);

		result.Should().NotContain("<script");
		result.Should().NotContain("attack");
		result.Should().Contain("Safe");
	}

	[Test]
	public void Block_throws_the_dedicated_blocked_exception()
	{
		var act = () => SvgSanitizationStage.Apply(
			"<svg xmlns=\"http://www.w3.org/2000/svg\"><script>primary</script></svg>",
			SanitizeMode.Block);

		act.Should().ThrowExactly<MermaidSvgException>();
	}

	[Test]
	public void Sanitization_runs_on_default_path_with_no_options()
	{
		// No RenderOptions and no strict mode — sanitization is non-optional, so the
		// always-on allowlist pass still runs. Clean rendered output passes through.
		var svg = MermaidRenderer.RenderSvg("graph TD\n  A --> B");
		svg.Should().Contain("</svg>");
	}

	[Test]
	public void Block_mode_passes_clean_rendered_output()
	{
		var options = new RenderOptions { SanitizeMode = SanitizeMode.Block };
		var act = () => MermaidRenderer.RenderSvg("graph TD\n  A --> B", options);
		act.Should().NotThrow();
	}

	[Test]
	public void Sanitization_is_not_gated_by_strict_mode()
	{
		// Strict mode no longer carries a Sanitize toggle; sanitization runs either way.
		var strict = MermaidRenderer.RenderSvg("graph TD\n  A --> B",
			new RenderOptions { Strict = new StrictStylingOptions { AllowedClasses = [] } });
		var plain = MermaidRenderer.RenderSvg("graph TD\n  A --> B");

		strict.Should().Contain("</svg>");
		plain.Should().Contain("</svg>");
	}

	// ========================================================================
	// The scoped <image> exception — architecture-diagram icons
	// ========================================================================

	[Test]
	public void Allows_image_element_with_safe_base64_svg_data_uri()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><image x="0" y="0" width="10" height="10" href="data:image/svg+xml;base64,PHN2ZyAvPg=="/></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeFalse();
		result.Svg.Should().Contain("<image");
		result.Svg.Should().Contain("data:image/svg+xml;base64,");
	}

	[Test]
	public void Allows_image_element_with_safe_base64_png_data_uri()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><image x="0" y="0" width="10" height="10" href="data:image/png;base64,iVBORw0KGgo="/></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeFalse();
		result.Svg.Should().Contain("data:image/png;base64,");
	}

	[Test]
	public void Strips_http_href_on_image_element()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><image x="0" y="0" width="10" height="10" href="http://evil.example/x.png"/></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("evil.example");
	}

	[Test]
	public void Strips_javascript_href_on_image_element()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><image x="0" y="0" width="10" height="10" href="javascript:alert(1)"/></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("javascript:");
	}

	[Test]
	public void Strips_non_image_data_uri_on_image_element()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><image x="0" y="0" width="10" height="10" href="data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg=="/></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("data:text/html");
	}

	[Test]
	public void Strips_children_of_image_element_even_when_individually_allowlisted()
	{
		// <image> is defined as an empty element in the SVG spec. A <rect> child would
		// individually pass the allowlist, but must still be removed because it's nested
		// under <image> — per-element allowlisting alone doesn't catch this.
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><image href="data:image/svg+xml;base64,PHN2ZyAvPg=="><rect x="0" y="0" width="4" height="4"/></image></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().Contain("<image");
		result.Svg.Should().NotContain("<rect");
		result.Violations.Should().Contain(v => v.Kind == "element" && v.Name == "rect" && v.ParentElement == "image");
	}

	[Test]
	public void Strips_href_on_non_image_element_even_if_it_looks_like_a_safe_data_uri()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"><rect xlink:href="data:image/svg+xml;base64,PHN2ZyAvPg==" x="0" y="0" width="10" height="10"/></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("xlink:href");
	}

	// ========================================================================
	// Root, namespace, CSS, paint, and nested-image value rules
	// ========================================================================

	[Test]
	public void Rejects_non_svg_root_without_throwing()
	{
		var result = SvgSanitizer.Sanitize("<html><svg/></html>");

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().Be("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
		result.Violations.Should().Contain(v => v.Kind == "element" && v.Name == "html");
	}

	[Test]
	public void Rejects_prefixed_svg_root_that_would_not_enter_svg_mode_in_html()
	{
		var result = SvgSanitizer.Sanitize("""<s:svg xmlns:s="http://www.w3.org/2000/svg"><s:rect/></s:svg>""");

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().Be("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
	}

	[Test]
	public void Strips_allowlisted_local_name_from_a_foreign_namespace()
	{
		var result = SvgSanitizer.Sanitize("""<svg xmlns="http://www.w3.org/2000/svg" xmlns:e="https://attacker.invalid/ns"><e:rect width="10" height="10"/><circle cx="5" cy="5" r="5"/></svg>""");

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("e:rect");
		result.Svg.Should().Contain("<circle");
	}

	[Test]
	public void Strips_unapproved_namespace_declarations_and_namespaced_data_attributes()
	{
		var result = SvgSanitizer.Sanitize("""<svg xmlns="http://www.w3.org/2000/svg" xmlns:e="https://attacker.invalid/ns"><rect e:data-value="x" width="10" height="10"/></svg>""");

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("attacker.invalid");
		result.Svg.Should().NotContain("data-value");
	}

	[Test]
	public void Allows_exact_xlink_namespace_for_a_validated_image_data_uri()
	{
		var result = SvgSanitizer.Sanitize("""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"><image xlink:href="data:image/svg+xml;base64,PHN2ZyAvPg=="/></svg>""");

		result.HasViolations.Should().BeFalse();
		result.Svg.Should().Contain("xlink:href");
	}

	[Test]
	public void Strips_external_paint_server_urls()
	{
		var result = SvgSanitizer.Sanitize("""<svg xmlns="http://www.w3.org/2000/svg"><rect fill="url(https://attacker.invalid/paint.svg#x)" stroke="url(data:image/svg+xml;base64,AAAA)"/></svg>""");

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("attacker.invalid");
		result.Svg.Should().NotContain("data:image");
	}

	[Test]
	public void Allows_only_same_document_fragment_paint_urls()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><defs><linearGradient id="safe-gradient"><stop offset="0" stop-color="#fff"/></linearGradient></defs><rect fill="url(#safe-gradient)"/></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeFalse();
		result.Svg.Should().BeSameAs(svg);
	}

	[Test]
	public void Strips_external_marker_reference()
	{
		var result = SvgSanitizer.Sanitize("""<svg xmlns="http://www.w3.org/2000/svg"><path d="M0 0L1 1" marker-end="url(https://attacker.invalid/m.svg#x)"/></svg>""");

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("marker-end");
	}

	[Test]
	public void Allows_same_document_marker_reference()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><defs><marker id="arrow"><path d="M0 0L1 1"/></marker></defs><path d="M0 0L1 1" marker-end="url(#arrow)"/></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeFalse();
	}

	[Test]
	public void Strips_style_attribute_even_if_a_custom_name_allowlist_includes_it()
	{
		var attributes = SvgSanitizer.DefaultAllowedAttributes
			.Append("style")
			.ToFrozenSet(StringComparer.Ordinal);
		var result = SvgSanitizer.Sanitize(
			"""<svg xmlns="http://www.w3.org/2000/svg"><rect style="fill:red"/></svg>""",
			SvgSanitizer.DefaultAllowedElements,
			attributes);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("style=");
	}

	[Test]
	public void Strips_style_element_even_if_a_custom_name_allowlist_includes_it()
	{
		var elements = SvgSanitizer.DefaultAllowedElements
			.Append("style")
			.ToFrozenSet(StringComparer.Ordinal);
		var result = SvgSanitizer.Sanitize(
			"""<svg xmlns="http://www.w3.org/2000/svg"><style>body { display: none; }</style></svg>""",
			elements,
			SvgSanitizer.DefaultAllowedAttributes);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("<style");
	}

	[Test]
	public void Strips_processing_instructions_and_xml_declaration()
	{
		var result = SvgSanitizer.Sanitize("""<?xml version="1.0"?><?xml-stylesheet href="https://attacker.invalid/x.css"?><svg xmlns="http://www.w3.org/2000/svg"><rect/></svg>""");

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("<?xml");
		result.Svg.Should().NotContain("attacker.invalid");
	}

	[Test]
	public void Strip_returns_empty_svg_for_document_type_declarations()
	{
		var result = SvgSanitizer.Sanitize("""<!DOCTYPE svg [<!ENTITY x "payload">]><svg xmlns="http://www.w3.org/2000/svg"><text>&x;</text></svg>""");

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().Be("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
		result.Violations.Should().ContainSingle(v => v.Kind == "document" && v.Name == "malformed-xml");
	}

	[Test]
	public void Strip_returns_empty_svg_for_malformed_xml()
	{
		var result = SvgSanitizer.Sanitize("<svg><g></svg>", SanitizeMode.Strip);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().Be("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
		result.Violations.Should().ContainSingle(v => v.Kind == "document" && v.Name == "malformed-xml");
	}

	[Test]
	public void Block_overload_throws_for_malformed_xml()
	{
		var act = () => SvgSanitizer.Sanitize("<svg><g></svg>", SanitizeMode.Block);

		act.Should().ThrowExactly<MermaidSvgException>()
			.WithMessage("*not well-formed SVG/XML*");
	}

	[Test]
	public void Block_overload_throws_for_well_formed_policy_violation()
	{
		var act = () => SvgSanitizer.Sanitize(
			"<svg xmlns=\"http://www.w3.org/2000/svg\"><script>attack</script></svg>",
			SanitizeMode.Block);

		act.Should().ThrowExactly<MermaidSvgException>()
			.WithMessage("*disallowed*script*");
	}

	[Test]
	public void Mode_overload_rejects_unknown_enum_values()
	{
		var act = () => SvgSanitizer.Sanitize(
			"<svg xmlns=\"http://www.w3.org/2000/svg\"/>",
			(SanitizeMode)int.MaxValue);

		act.Should().ThrowExactly<ArgumentOutOfRangeException>();
	}

	[Test]
	public void MermaidSvgException_keeps_an_immutable_snapshot_of_all_violations()
	{
		var source = new List<SvgViolation>
		{
			new("element", "script"),
			new("attribute", "onclick", "rect"),
		};
		var exception = new MermaidSvgException(source);

		source.Clear();

		exception.Violations.Should().HaveCount(2);
		exception.Violations.Should().Contain(new SvgViolation("element", "script"));
		var collection = (IList<SvgViolation>)exception.Violations;
		var act = () => collection[0] = new SvgViolation("element", "changed");
		act.Should().ThrowExactly<NotSupportedException>();
	}

	[Test]
	public void Strips_nested_elements_from_title_and_description()
	{
		var result = SvgSanitizer.Sanitize("""<svg xmlns="http://www.w3.org/2000/svg"><title>safe<text>nested</text></title><desc><image href="data:image/svg+xml;base64,PHN2ZyAvPg=="/></desc></svg>""");

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("<text");
		result.Svg.Should().NotContain("<image");
	}

	[Test]
	public void Metadata_elements_retain_text_nodes_only()
	{
		var result = SvgSanitizer.Sanitize("""<svg xmlns="http://www.w3.org/2000/svg"><title>safe<!-- hidden --><![CDATA[ text]]></title><desc>description<!-- hidden --></desc></svg>""");

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("<!--");
		var doc = XDocument.Parse(result.Svg);
		doc.Descendants().Where(element => element.Name.LocalName is "title" or "desc")
			.SelectMany(element => element.Nodes())
			.Should().OnlyContain(node => node is XText);
	}

	[Test]
	public void Strips_svg_image_data_uri_when_nested_svg_contains_active_content()
	{
		var nested = """<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>""";
		var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(nested));
		var result = SvgSanitizer.Sanitize($"<svg xmlns=\"http://www.w3.org/2000/svg\"><image href=\"data:image/svg+xml;base64,{data}\"/></svg>");

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("href=");
	}

	[Test]
	public void Strips_svg_image_data_uri_when_nested_svg_contains_css()
	{
		var nested = """<svg xmlns="http://www.w3.org/2000/svg"><style>body{display:none}</style><rect/></svg>""";
		var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(nested));
		var result = SvgSanitizer.Sanitize($"<svg xmlns=\"http://www.w3.org/2000/svg\"><image href=\"data:image/svg+xml;base64,{data}\"/></svg>");

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("href=");
	}

	[Test]
	public void Strips_png_data_uri_without_png_signature()
	{
		var fakePng = Convert.ToBase64String(Encoding.UTF8.GetBytes("<script>alert(1)</script>"));
		var result = SvgSanitizer.Sanitize($"<svg xmlns=\"http://www.w3.org/2000/svg\"><image href=\"data:image/png;base64,{fakePng}\"/></svg>");

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("href=");
	}

	[Test]
	public void Renderer_generated_stylesheet_survives_internal_output_sanitization()
	{
		var svg = MermaidRenderer.RenderSvg("graph TD\nA --> B");

		svg.Should().Contain("<style>");
		svg.Should().Contain("style=\"--bg:");
	}
}
