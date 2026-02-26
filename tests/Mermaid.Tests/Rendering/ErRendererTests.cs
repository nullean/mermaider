using AwesomeAssertions;

namespace Mermaid.Tests.Rendering;

public class ErRendererTests
{
	private const string SimpleEr = """
		erDiagram
		CUSTOMER ||--o{ ORDER : places
		CUSTOMER {
		string name PK
		int age
		}
		ORDER {
		int id PK
		string product
		}
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleEr);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_entity_boxes()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleEr);

		svg.Should().Contain("class=\"entity\"");
		svg.Should().Contain("data-id=\"CUSTOMER\"");
		svg.Should().Contain("data-id=\"ORDER\"");
	}

	[Test]
	public void Contains_relationship_lines()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleEr);

		svg.Should().Contain("class=\"er-relationship\"");
		svg.Should().Contain("data-entity1=\"CUSTOMER\"");
		svg.Should().Contain("data-entity2=\"ORDER\"");
	}

	[Test]
	public void Contains_relationship_label()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleEr);

		svg.Should().Contain("data-label=\"places\"");
	}

	[Test]
	public void Contains_cardinality_data()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleEr);

		svg.Should().Contain("data-cardinality1=\"one\"");
		svg.Should().Contain("data-cardinality2=\"zeromany\"");
	}

	[Test]
	public void Contains_key_badges()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleEr);

		svg.Should().Contain("var(--_key-badge)");
		svg.Should().Contain("PK");
	}

	[Test]
	public void Contains_mono_font()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleEr);

		svg.Should().Contain("class=\"mono\"");
	}

	[Test]
	public void Renders_non_identifying_relationship()
	{
		var svg = MermaidRenderer.RenderSvg("""
			erDiagram
			A ||..o{ B : uses
			""");

		svg.Should().Contain("stroke-dasharray=\"6 4\"");
		svg.Should().Contain("data-identifying=\"false\"");
	}

	[Test]
	public void Renders_entity_without_attributes()
	{
		var svg = MermaidRenderer.RenderSvg("""
			erDiagram
			EMPTY {
			}
			A ||--|| EMPTY : ref
			""");

		svg.Should().Contain("(no attributes)");
	}
}
