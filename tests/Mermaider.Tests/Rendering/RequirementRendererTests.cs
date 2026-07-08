using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class RequirementRendererTests
{
	private const string Basic = """
		requirementDiagram

		requirement test_req {
		id: 1
		text: the test text.
		risk: high
		verifymethod: test
		}

		element test_entity {
		type: simulation
		}

		test_entity - satisfies -> test_req
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_requirement_and_element_names()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().Contain("test_req");
		svg.Should().Contain("test_entity");
	}

	[Test]
	public void Contains_requirement_fields()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().Contain("Id: 1");
		svg.Should().Contain("the test text.");
		svg.Should().Contain("Risk: High");
		svg.Should().Contain("Verification: Test");
	}

	[Test]
	public void Contains_relation_label()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().Contain("satisfies");
		svg.Should().Contain("marker-end=\"url(#req-arrow)\"");
	}

	[Test]
	public void Contains_kind_labels()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().Contain("Requirement");
		svg.Should().Contain("Element");
	}

	[Test]
	public void Renders_with_direction_LR()
	{
		var svg = MermaidRenderer.RenderSvg("""
			requirementDiagram
			direction LR
			requirement a {
			id: A
			}
			element b {
			type: x
			}
			b - verifies -> a
			""");

		svg.Should().StartWith("<svg");
		svg.Should().Contain("verifies");
	}

	[Test]
	public void Renders_title()
	{
		var svg = MermaidRenderer.RenderSvg("""
			requirementDiagram
			title My Requirements
			requirement a {
			}
			""");

		svg.Should().Contain("My Requirements");
	}

	[Test]
	public void Renders_bare_requirement_header()
	{
		var svg = MermaidRenderer.RenderSvg("""
			requirement
			requirement a {
			id: 1
			}
			""");

		svg.Should().StartWith("<svg");
		svg.Should().Contain("a");
	}

	[Test]
	public void Renders_empty_diagram()
	{
		var svg = MermaidRenderer.RenderSvg("requirementDiagram");

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Duplicate_names_first_wins_does_not_throw()
	{
		// Requirement first, then element with same name — keep requirement, skip element
		var svg = MermaidRenderer.RenderSvg("""
			requirementDiagram
			requirement shared {
			id: REQ-1
			text: first wins
			}
			element shared {
			type: should_be_skipped
			}
			""");

		svg.Should().StartWith("<svg");
		svg.Should().Contain("REQ-1");
		svg.Should().Contain("first wins");
		svg.Should().Contain("Requirement");
		svg.Should().NotContain("should_be_skipped");
		svg.Should().NotContain("Type:");
	}

	[Test]
	public void Long_text_wraps_inside_box_without_overflow_glyphs()
	{
		var source =
			"requirementDiagram\n" +
			"requirement long_req {\n" +
			"id: L1\n" +
			"text: Users must authenticate with multi-factor authentication before accessing any protected administrative resources in the system.\n" +
			"risk: high\n" +
			"verifymethod: test\n" +
			"}";
		var svg = MermaidRenderer.RenderSvg(source);

		svg.Should().StartWith("<svg");
		// Wrapped segments appear as separate <text> nodes; full sentence still present across them
		svg.Should().Contain("Users must authenticate");
		svg.Should().Contain("administrative");
		// Count small-font text nodes: kind + id + multi-line text + risk + verify
		// Unwrapped would be ~5 (kind, id, text, risk, verify); wrap adds more.
		var smallFont = "font-size=\"var(--fs-s)\"";
		var idx = 0;
		var count = 0;
		while ((idx = svg.IndexOf(smallFont, idx, StringComparison.Ordinal)) >= 0)
		{
			count++;
			idx += smallFont.Length;
		}
		count.Should().BeGreaterThan(5);
	}

	[Test]
	public void Renders_docRef_and_functional_kind_label()
	{
		var svg = MermaidRenderer.RenderSvg("""
			requirementDiagram
			functionalRequirement login {
			id: REQ-1
			text: Users must authenticate.
			}
			element auth_service {
			type: service
			docRef: design/auth.md
			}
			auth_service - satisfies -> login
			""");

		svg.Should().Contain("Functional Requirement");
		svg.Should().Contain("Doc ref: design/auth.md");
	}

	[Test]
	public void Escapes_special_characters_in_text()
	{
		var svg = MermaidRenderer.RenderSvg("""
			requirementDiagram
			requirement xss {
			id: 1
			text: use <script> & "quotes"
			}
			""");

		svg.Should().Contain("&lt;script&gt;");
		svg.Should().Contain("&amp;");
		svg.Should().Contain("&quot;quotes&quot;");
		svg.Should().NotContain("<script>");
	}
}
