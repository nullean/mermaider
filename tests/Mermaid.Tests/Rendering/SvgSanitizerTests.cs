using System.Collections.Frozen;
using AwesomeAssertions;
using Mermaid.Models;
using Mermaid.Rendering;

namespace Mermaid.Tests.Rendering;

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
	public void Strips_image_element()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><image href="http://evil.com/tracker.png"/><rect x="0" y="0" width="10" height="10"/></svg>""";
		var result = SvgSanitizer.Sanitize(svg);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("<image");
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

	[Test]
	public void Custom_allowlists_restrict_further()
	{
		var onlyRect = new[] { "svg", "rect" }.ToFrozenSet(StringComparer.Ordinal);
		var onlyBasic = new[] { "xmlns", "x", "y", "width", "height" }.ToFrozenSet(StringComparer.Ordinal);

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
		var noText = new[] { "svg", "rect" }.ToFrozenSet(StringComparer.Ordinal);

		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><rect x="0" y="0" width="10" height="10"/><text x="5" y="5">no</text></svg>""";
		var result = SvgSanitizer.Sanitize(svg, noText, SvgSanitizer.DefaultAllowedAttributes);

		result.HasViolations.Should().BeTrue();
		result.Svg.Should().NotContain("<text");
		result.Svg.Should().Contain("<rect");
	}

	// ========================================================================
	// Block mode — through StrictModeSanitizer (internal bridge)
	// ========================================================================

	[Test]
	public void Block_throws_on_script_element()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>""";

		var act = () => StrictModeSanitizer.Sanitize(svg, SvgSanitizeMode.Block);

		act.Should().Throw<MermaidParseException>()
			.WithMessage("*disallowed*script*");
	}

	[Test]
	public void Block_throws_on_event_handler()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><rect x="0" y="0" width="10" height="10" onmouseover="alert(1)"/></svg>""";

		var act = () => StrictModeSanitizer.Sanitize(svg, SvgSanitizeMode.Block);

		act.Should().Throw<MermaidParseException>()
			.WithMessage("*disallowed*onmouseover*");
	}

	[Test]
	public void Block_throws_on_foreignObject()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><foreignObject width="100" height="100"/></svg>""";

		var act = () => StrictModeSanitizer.Sanitize(svg, SvgSanitizeMode.Block);

		act.Should().Throw<MermaidParseException>()
			.WithMessage("*disallowed*foreignObject*");
	}

	[Test]
	public void Block_returns_original_when_clean()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><g><rect x="0" y="0" width="10" height="10" fill="#fff"/><text x="5" y="5">ok</text></g></svg>""";

		var result = StrictModeSanitizer.Sanitize(svg, SvgSanitizeMode.Block);
		result.Should().BeSameAs(svg);
	}

	[Test]
	public void Strip_via_bridge_returns_cleaned_svg()
	{
		var svg = """<svg xmlns="http://www.w3.org/2000/svg"><script>x</script><rect x="0" y="0" width="10" height="10"/></svg>""";

		var result = StrictModeSanitizer.Sanitize(svg, SvgSanitizeMode.Strip);
		result.Should().NotContain("<script");
		result.Should().Contain("<rect");
	}

	// ========================================================================
	// Integration — through MermaidRenderer with strict mode
	// ========================================================================

	[Test]
	public void Strict_mode_sanitizes_by_default()
	{
		var input = "graph TD\n  A --> B";
		var options = new RenderOptions
		{
			Strict = new StrictModeOptions { AllowedClasses = [] }
		};

		var svg = MermaidRenderer.RenderSvg(input, options);
		svg.Should().Contain("</svg>");
	}

	[Test]
	public void Strict_mode_sanitize_null_skips_sanitization()
	{
		var input = "graph TD\n  A --> B";
		var options = new RenderOptions
		{
			Strict = new StrictModeOptions
			{
				AllowedClasses = [],
				Sanitize = null
			}
		};

		var svg = MermaidRenderer.RenderSvg(input, options);
		svg.Should().Contain("</svg>");
	}
}
