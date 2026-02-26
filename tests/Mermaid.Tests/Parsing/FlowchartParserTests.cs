using AwesomeAssertions;
using Mermaid.Models;

namespace Mermaid.Tests.Parsing;

public class FlowchartParserTests
{
	[Test]
	public void ParsesSimpleFlowchart()
	{
		var graph = MermaidRenderer.Parse("graph TD\n  A --> B");

		graph.Direction.Should().Be(Direction.TD);
		graph.Nodes.Should().HaveCount(2);
		graph.Edges.Should().HaveCount(1);
		graph.Nodes.Should().ContainKey("A");
		graph.Nodes.Should().ContainKey("B");
	}

	[Test]
	public void ParsesDirectionLR()
	{
		var graph = MermaidRenderer.Parse("flowchart LR\n  X --> Y");

		graph.Direction.Should().Be(Direction.LR);
	}

	[Test]
	public void ParsesNodeShapes()
	{
		var graph = MermaidRenderer.Parse(
			"""
			graph TD
			  A[Rectangle]
			  B(Rounded)
			  C{Diamond}
			  D([Stadium])
			  E((Circle))
			  F --> A
			""");

		graph.Nodes["A"].Shape.Should().Be(NodeShape.Rectangle);
		graph.Nodes["B"].Shape.Should().Be(NodeShape.Rounded);
		graph.Nodes["C"].Shape.Should().Be(NodeShape.Diamond);
		graph.Nodes["D"].Shape.Should().Be(NodeShape.Stadium);
		graph.Nodes["E"].Shape.Should().Be(NodeShape.Circle);
	}

	[Test]
	public void ParsesEdgeStyles()
	{
		var graph = MermaidRenderer.Parse(
			"""
			graph TD
			  A --> B
			  B -.-> C
			  C ==> D
			""");

		graph.Edges.Should().HaveCount(3);
		graph.Edges[0].Style.Should().Be(EdgeStyle.Solid);
		graph.Edges[1].Style.Should().Be(EdgeStyle.Dotted);
		graph.Edges[2].Style.Should().Be(EdgeStyle.Thick);
	}

	[Test]
	public void ParsesEdgeLabels()
	{
		var graph = MermaidRenderer.Parse("graph TD\n  A -->|yes| B");

		graph.Edges.Should().HaveCount(1);
		graph.Edges[0].Label.Should().Be("yes");
	}

	[Test]
	public void ParsesSubgraphs()
	{
		var graph = MermaidRenderer.Parse(
			"""
			graph TD
			  subgraph sg [My Group]
			    A --> B
			  end
			  C --> A
			""");

		graph.Subgraphs.Should().HaveCount(1);
		graph.Subgraphs[0].Id.Should().Be("sg");
		graph.Subgraphs[0].Label.Should().Be("My Group");
		graph.Subgraphs[0].NodeIds.Should().Contain("A");
		graph.Subgraphs[0].NodeIds.Should().Contain("B");
	}

	[Test]
	public void ParsesChainedEdges()
	{
		var graph = MermaidRenderer.Parse("graph TD\n  A --> B --> C");

		graph.Edges.Should().HaveCount(2);
		graph.Edges[0].Source.Should().Be("A");
		graph.Edges[0].Target.Should().Be("B");
		graph.Edges[1].Source.Should().Be("B");
		graph.Edges[1].Target.Should().Be("C");
	}

	[Test]
	public void ParsesParallelLinks()
	{
		var graph = MermaidRenderer.Parse("graph TD\n  A & B --> C");

		graph.Edges.Should().HaveCount(2);
		graph.Edges[0].Source.Should().Be("A");
		graph.Edges[0].Target.Should().Be("C");
		graph.Edges[1].Source.Should().Be("B");
		graph.Edges[1].Target.Should().Be("C");
	}

	[Test]
	public void ParsesParallelLinksOnBothSides()
	{
		var graph = MermaidRenderer.Parse("graph TD\n  A & B --> C & D");

		graph.Edges.Should().HaveCount(4);
	}

	[Test]
	public void ParsesClassDefAndAssignment()
	{
		var graph = MermaidRenderer.Parse("""
			graph TD
			classDef red fill:#f00,color:#fff
			A --> B
			class A red
			""");

		graph.ClassDefs.Should().ContainKey("red");
		graph.ClassAssignments.Should().ContainKey("A");
		graph.ClassAssignments["A"].Should().Be("red");
	}

	[Test]
	public void ParsesClassShorthand()
	{
		var graph = MermaidRenderer.Parse("""
			graph TD
			classDef highlight fill:#ff0
			A:::highlight --> B
			""");

		graph.ClassAssignments.Should().ContainKey("A");
		graph.ClassAssignments["A"].Should().Be("highlight");
	}

	[Test]
	public void ThrowsOnInvalidHeader()
	{
		var act = () => MermaidRenderer.Parse("invalid header");
		act.Should().Throw<MermaidParseException>();
	}

	[Test]
	public void ThrowsOnEmptyDiagram()
	{
		var act = () => MermaidRenderer.Parse("");
		act.Should().Throw<MermaidParseException>();
	}
}
