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
}
