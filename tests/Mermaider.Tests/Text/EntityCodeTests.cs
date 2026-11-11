using AwesomeAssertions;
using Mermaider.Text;

namespace Mermaider.Tests.Text;

public class EntityCodeTests
{
	[Test]
	[Arguments("say #quot;hi#quot;", "say \"hi\"")]
	[Arguments("#35;1", "#1")]
	[Arguments("heart #9829;", "heart ♥")]
	[Arguments("List#lt;T#gt;", "List<T>")]
	[Arguments("#nope; #9999999999;", "#nope; #9999999999;")]
	[Arguments("C# and #x23;", "C# and #x23;")]
	public void Decodes_entity_codes(string input, string expected) =>
		MultilineUtils.DecodeEntities(input).Should().Be(expected);

	[Test]
	public void Flowchart_node_and_edge_labels_decode_entity_codes()
	{
		var graph = MermaidRenderer.Parse(
			"""
			flowchart LR
			  A["say #quot;hi#quot; #35;1"] -->|#quot;go#quot;| B["`**#35;2**`"]
			""");

		graph.Nodes["A"].Label.Should().Be("say \"hi\" #1");
		graph.Nodes["B"].Label.Should().Be("**#2**");
		graph.Edges[0].Label.Should().Be("\"go\"");
	}

	[Test]
	public void Rendered_label_escapes_the_decoded_quote()
	{
		var svg = MermaidRenderer.RenderSvg("flowchart LR\n  A[\"say #quot;hi#quot;\"]");

		svg.Should().Contain("say &quot;hi&quot;");
		svg.Should().NotContain("#quot;");
	}
}
