using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class SequenceRendererTests
{
	private const string SimpleSequence =
		"""
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

		// messages use the shared markers: filled for sync, open for async replies
		svg.Should().MatchRegex("id=\"m[0-9a-f]{8}-mk-arrow\"");
		MermaidRenderer.RenderSvg("sequenceDiagram\nA->B: open").Should().MatchRegex("id=\"m[0-9a-f]{8}-mk-open\"");
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
		var svg = MermaidRenderer.RenderSvg(
			"""
			sequenceDiagram
			loop Every minute
			A->>B: Ping
			end
			"""
		);

		svg.Should().Contain("class=\"block\"");
		svg.Should().Contain("data-type=\"loop\"");
	}

	[Test]
	public void Renders_with_note()
	{
		var svg = MermaidRenderer.RenderSvg(
			"""
			sequenceDiagram
			participant A
			participant B
			A->>B: Hello
			Note right of B: Important
			"""
		);

		svg.Should().Contain("class=\"note\"");
		svg.Should().Contain("data-position=\"right\"");
	}

	[Test]
	public void Note_after_self_message_sits_below_the_loop_and_its_label()
	{
		var diagram = Mermaider
			.Parsing
			.SequenceParser
			.Parse(["sequenceDiagram", "A->>J: hi", "J->>J: Fight against hypochondria", "Note right of J: Rational thoughts", "J-->>A: Great",]);

		var positioned = Mermaider.Layout.SequenceLayout.Layout(diagram);

		var self = positioned.Messages[1];
		var next = positioned.Messages[2];
		var note = positioned.Notes.Should().ContainSingle().Subject;
		self.IsSelf.Should().BeTrue();
		// the loop drops 20px and its label pill is centred on the loop, so its bottom edge is past the loop
		note.Y.Should().BeGreaterThanOrEqualTo(self.Y + 30, "the note must clear the self-message loop and its label");
		(note.Y + note.Height).Should().BeLessThan(next.Y, "the note stays above the next message");
	}

	[Test]
	public void Renders_alt_block_with_dividers()
	{
		var svg = MermaidRenderer.RenderSvg(
			"""
			sequenceDiagram
			alt Valid
			A->>B: OK
			else Invalid
			A->>B: Error
			end
			"""
		);

		svg.Should().Contain("data-type=\"alt\"");
		svg.Should().Contain($"stroke-dasharray=\"{Mermaider.Rendering.DesignSystem.DashArray}\"", "else separators use the dashed edge language");
		svg.Should().Contain(">Invalid</text>", "the separator carries its condition as a subsection header");
		svg.Should().NotContain("[Invalid]", "conditions are shown without brackets");
	}

	[Test]
	public void Renders_actor_stick_figure()
	{
		var svg = MermaidRenderer.RenderSvg(
			"""
			sequenceDiagram
			actor U as User
			participant S as Server
			U->>S: Request
			"""
		);

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

	[Test]
	public void Renders_lost_messages_with_a_cross()
	{
		var svg = MermaidRenderer.RenderSvg("""
			sequenceDiagram
			A--xB: async lost
			A-xB: sync lost
			""");

		svg.Should().Contain("data-arrow-head=\"cross\"");
		svg.Should().NotContain("data-arrow-head=\"filled\"");
		svg.Should().NotContain("marker-end=");
		svg.Should().MatchRegex("data-label=\"async lost\"[^\\n]*\\n[^\\n]*<line[^>]*/>\\n\\s*<path d=\"M[^\"]* M[^\"]*\"");
	}
}
