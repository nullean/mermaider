using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

/// <summary>Empty or nested composites and subgraphs, and edges to them, render instead of crashing (nullean/mermaider#150).</summary>
public class EmptyCompositeTests
{
	[Test]
	[Arguments("stateDiagram-v2\n  state P {\n    state R {\n    }\n  }\n  a --> P\n")]
	[Arguments("stateDiagram-v2\n  state P {\n    a --> R\n    state R {\n    }\n  }\n")]
	[Arguments("stateDiagram-v2\n  state P {\n    state R {\n      [*]\n    }\n  }\n  a --> P\n")]
	[Arguments("flowchart LR\n  UI --> X\n  subgraph OUTER[\"Outer\"]\n    subgraph INNER[\"Inner\"]\n    end\n  end\n  UI --> OUTER\n  OUTER --> Z\n")]
	[Arguments("flowchart LR\n  subgraph A\n    subgraph B\n    end\n  end\n  X --> B\n")]
	public void Renders(string source) => MermaidRenderer.RenderSvg(source).Should().StartWith("<svg");
}
