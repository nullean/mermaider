using System.Collections.Frozen;
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
	public void Preserves_style_element()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><style>text { font-family: Inter; }</style><rect x="0" y="0" width="10" height="10"/></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeFalse();
		result.Svg.Should().Contain("<style");
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

	// ========================================================================
	// Block mode — through OutputSanitizer (internal bridge)
	// ========================================================================

	[Test]
	public void Block_throws_on_script_element()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>""";

		var act = () => OutputSanitizer.Sanitize(svg, SanitizeMode.Block);

		act.Should().Throw<MermaidParseException>()
			.WithMessage("*disallowed*script*");
	}

	[Test]
	public void Block_throws_on_event_handler()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><rect x="0" y="0" width="10" height="10" onmouseover="alert(1)"/></svg>""";

		var act = () => OutputSanitizer.Sanitize(svg, SanitizeMode.Block);

		act.Should().Throw<MermaidParseException>()
			.WithMessage("*disallowed*onmouseover*");
	}

	[Test]
	public void Block_throws_on_foreignObject()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><foreignObject width="100" height="100"/></svg>""";

		var act = () => OutputSanitizer.Sanitize(svg, SanitizeMode.Block);

		act.Should().Throw<MermaidParseException>()
			.WithMessage("*disallowed*foreignObject*");
	}

	[Test]
	public void Block_returns_original_when_clean()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><g><rect x="0" y="0" width="10" height="10" fill="#fff"/><text x="5" y="5">ok</text></g></svg>""";

		var result = OutputSanitizer.Sanitize(svg, SanitizeMode.Block);
		result.Should().BeSameAs(svg);
	}

	[Test]
	public void Strip_via_bridge_returns_cleaned_svg()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><script>x</script><rect x="0" y="0" width="10" height="10"/></svg>""";

		var result = OutputSanitizer.Sanitize(svg, SanitizeMode.Strip);
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
}
