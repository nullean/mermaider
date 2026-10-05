using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class SubgraphEdgeEndpointTests
{
	[Test]
	public void EdgeToSubgraphThatOnlyContainsNestedSubgraphsRenders()
	{
		var svg = MermaidRenderer.RenderSvg("""
			flowchart LR
			  UI --> X
			  subgraph OUTER["Outer"]
			    subgraph INNER["Inner"]
			      A --> B
			    end
			  end
			  UI --> OUTER
			  OUTER --> Z
			""");

		svg.Should().StartWith("<svg");
	}

	[Test]
	public void EdgeToEmptySubgraphRenders()
	{
		var svg = MermaidRenderer.RenderSvg("""
			flowchart LR
			  subgraph E["Empty"]
			  end
			  UI --> E
			""");

		svg.Should().StartWith("<svg");
	}

	[Test]
	public void UnexpectedLayoutFailureNamesTheDiagramType()
	{
		var ex = new MermaidRenderException(Models.DiagramType.Flowchart, new KeyNotFoundException("boom"));

		ex.Message.Should().Contain("Flowchart").And.Contain("boom");
		ex.InnerException.Should().BeOfType<KeyNotFoundException>();
	}
}
