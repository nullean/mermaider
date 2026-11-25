using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

/// <summary>Headers that Mermaid accepts are detected, and ones it refuses are not (nullean/mermaider#151).</summary>
public class DiagramHeaderTests
{
	[Test]
	[Arguments("treemap\n\"A\": 1\n", DiagramType.Treemap)]
	[Arguments("treemap-beta\n\"A\": 1\n", DiagramType.Treemap)]
	[Arguments("treemap title Sizes\n\"A\": 1\n", DiagramType.Treemap)]
	[Arguments("treemap-beta title Sizes\n\"A\": 1\n", DiagramType.Treemap)]
	[Arguments("gitGraph:\n  commit\n", DiagramType.GitGraph)]
	[Arguments("gitGraph TB:\n  commit\n", DiagramType.GitGraph)]
	[Arguments("radar-beta title Skills\naxis a, b\ncurve c{1, 2}\n", DiagramType.Radar)]
	[Arguments("venn-beta title Teams\nset A\nset B\n", DiagramType.Venn)]
	public void Detects_header_that_mermaid_accepts(string source, DiagramType expected) => DiagramDetector.Detect(source.AsSpan()).Should().Be(expected);

	[Test]
	[Arguments("treemap\n\"A\": 1\n")]
	[Arguments("treemap title Sizes\n\"A\": 1\n")]
	[Arguments("gitGraph:\n  commit\n")]
	[Arguments("radar-beta title Skills\naxis a, b\ncurve c{1, 2}\n")]
	[Arguments("venn-beta title Teams\nset A\nset B\n")]
	public void Renders_header_that_mermaid_accepts(string source) => MermaidRenderer.RenderSvg(source).Should().StartWith("<svg");

	[Test]
	[Arguments("radar\naxis a, b\ncurve c{1, 2}\n")]
	[Arguments("radar title x\naxis a, b\n")]
	[Arguments("venn\nset A\nset B\n")]
	[Arguments("gitGraph LR\n  commit\n")]
	public void Still_refuses_header_that_mermaid_refuses(string source)
	{
		var act = () => MermaidRenderer.RenderSvg(source);
		act.Should().Throw<MermaidParseException>();
	}

	[Test]
	public void Radar_title_on_the_header_line_is_the_title() =>
		MermaidRenderer.RenderSvg("radar-beta title Skills Radar\naxis a, b, c\ncurve x{1, 2, 3}\n").Should().Contain("Skills Radar");
}
