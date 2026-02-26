using AwesomeAssertions;

namespace Mermaid.Tests.Rendering;

public class SequenceRendererTests
{
	private const string SimpleSequence = """
		sequenceDiagram
		participant A as Alice
		participant B as Bob
		A->>B: Hello Bob
		B-->>A: Hi Alice
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleSequence);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_actor_boxes()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleSequence);

		svg.Should().Contain("class=\"actor\"");
		svg.Should().Contain("data-id=\"A\"");
		svg.Should().Contain("data-id=\"B\"");
	}

	[Test]
	public void Contains_lifelines()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleSequence);

		svg.Should().Contain("class=\"lifeline\"");
	}

	[Test]
	public void Contains_messages()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleSequence);

		svg.Should().Contain("class=\"message\"");
		svg.Should().Contain("data-from=\"A\"");
		svg.Should().Contain("data-to=\"B\"");
	}

	[Test]
	public void Contains_arrow_markers()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleSequence);

		svg.Should().Contain("id=\"seq-arrow\"");
		svg.Should().Contain("id=\"seq-arrow-open\"");
	}

	[Test]
	public void Renders_with_activation()
	{
		var svg = MermaidRenderer.RenderSvg("""
			sequenceDiagram
			A->>+B: Request
			B-->>-A: Response
			""");

		svg.Should().Contain("class=\"activation\"");
	}

	[Test]
	public void Renders_with_loop_block()
	{
		var svg = MermaidRenderer.RenderSvg("""
			sequenceDiagram
			loop Every minute
			A->>B: Ping
			end
			""");

		svg.Should().Contain("class=\"block\"");
		svg.Should().Contain("data-type=\"loop\"");
	}

	[Test]
	public void Renders_with_note()
	{
		var svg = MermaidRenderer.RenderSvg("""
			sequenceDiagram
			participant A
			participant B
			A->>B: Hello
			Note right of B: Important
			""");

		svg.Should().Contain("class=\"note\"");
		svg.Should().Contain("data-position=\"right\"");
	}

	[Test]
	public void Renders_alt_block_with_dividers()
	{
		var svg = MermaidRenderer.RenderSvg("""
			sequenceDiagram
			alt Valid
			A->>B: OK
			else Invalid
			A->>B: Error
			end
			""");

		svg.Should().Contain("data-type=\"alt\"");
		svg.Should().Contain("stroke-dasharray=\"6 4\"");
	}

	[Test]
	public void Renders_actor_stick_figure()
	{
		var svg = MermaidRenderer.RenderSvg("""
			sequenceDiagram
			actor U as User
			participant S as Server
			U->>S: Request
			""");

		svg.Should().Contain("data-type=\"actor\"");
		svg.Should().Contain("data-type=\"participant\"");
	}

	[Test]
	public void Renders_self_message_loop()
	{
		var svg = MermaidRenderer.RenderSvg("""
			sequenceDiagram
			A->>A: Self call
			""");

		svg.Should().Contain("data-self=\"true\"");
		svg.Should().Contain("<polyline");
	}

	[Test]
	public void Contains_style_block()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleSequence);

		svg.Should().Contain("<style>");
		svg.Should().Contain("--_text:");
		svg.Should().Contain("--_line:");
	}
}
