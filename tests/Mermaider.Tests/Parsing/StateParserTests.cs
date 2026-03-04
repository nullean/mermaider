using AwesomeAssertions;
using Mermaider.Models;

namespace Mermaider.Tests.Parsing;

public class StateParserTests
{
	[Test]
	public void ParsesClassDef()
	{
		var graph = MermaidRenderer.Parse("""
			stateDiagram-v2
			classDef badEvent fill:#f00,color:white
			[*] --> Crash
			Crash --> [*]
			""");

		graph.ClassDefs.Should().ContainKey("badEvent");
		graph.ClassDefs["badEvent"].Should().ContainKey("fill");
		graph.ClassDefs["badEvent"]["fill"].Should().Be("#f00");
		graph.ClassDefs["badEvent"]["color"].Should().Be("white");
	}

	[Test]
	public void ParsesClassAssignment()
	{
		var graph = MermaidRenderer.Parse("""
			stateDiagram-v2
			classDef badEvent fill:#f00,color:white
			[*] --> Crash
			Crash --> [*]
			class Crash badEvent
			""");

		graph.ClassAssignments.Should().ContainKey("Crash");
		graph.ClassAssignments["Crash"].Should().Be("badEvent");
	}

	[Test]
	public void ParsesMultipleClassAssignments()
	{
		var graph = MermaidRenderer.Parse("""
			stateDiagram-v2
			classDef highlight fill:#ff0
			[*] --> A
			A --> B
			B --> [*]
			class A,B highlight
			""");

		graph.ClassAssignments.Should().ContainKey("A");
		graph.ClassAssignments.Should().ContainKey("B");
		graph.ClassAssignments["A"].Should().Be("highlight");
		graph.ClassAssignments["B"].Should().Be("highlight");
	}

	[Test]
	public void ParsesClassShorthandStandalone()
	{
		var graph = MermaidRenderer.Parse("""
			stateDiagram-v2
			classDef movement font-style:italic
			[*] --> Moving
			Moving --> [*]
			Moving:::movement
			""");

		graph.ClassAssignments.Should().ContainKey("Moving");
		graph.ClassAssignments["Moving"].Should().Be("movement");
	}

	[Test]
	public void ParsesClassShorthandInTransitionSource()
	{
		var graph = MermaidRenderer.Parse("""
			stateDiagram-v2
			classDef movement font-style:italic
			[*] --> Moving
			Moving:::movement --> Still
			""");

		graph.ClassAssignments.Should().ContainKey("Moving");
		graph.ClassAssignments["Moving"].Should().Be("movement");
		graph.Edges.Should().HaveCount(2);
	}

	[Test]
	public void ParsesClassShorthandInTransitionTarget()
	{
		var graph = MermaidRenderer.Parse("""
			stateDiagram-v2
			classDef badEvent fill:#f00
			Moving --> Crash:::badEvent
			""");

		graph.ClassAssignments.Should().ContainKey("Crash");
		graph.ClassAssignments["Crash"].Should().Be("badEvent");
	}

	[Test]
	public void ParsesStyleDirective()
	{
		var graph = MermaidRenderer.Parse("""
			stateDiagram-v2
			[*] --> Active
			Active --> [*]
			style Active fill:#0f0,stroke:#333
			""");

		graph.NodeStyles.Should().ContainKey("Active");
		graph.NodeStyles["Active"]["fill"].Should().Be("#0f0");
		graph.NodeStyles["Active"]["stroke"].Should().Be("#333");
	}

	[Test]
	public void ParsesClassDefDefault()
	{
		var graph = MermaidRenderer.Parse("""
			stateDiagram-v2
			classDef default fill:#ddd
			[*] --> Still
			Still --> Moving
			Moving --> [*]
			""");

		graph.ClassDefs.Should().ContainKey("default");
		graph.ClassAssignments.Should().ContainKey("Still");
		graph.ClassAssignments.Should().ContainKey("Moving");
		graph.ClassAssignments["Still"].Should().Be("default");
		graph.ClassAssignments["Moving"].Should().Be("default");
	}

	[Test]
	public void ClassDefDefaultDoesNotOverrideExplicit()
	{
		var graph = MermaidRenderer.Parse("""
			stateDiagram-v2
			classDef default fill:#ddd
			classDef special fill:#f00
			[*] --> Still
			Still --> Moving
			Moving --> [*]
			class Moving special
			""");

		graph.ClassAssignments["Still"].Should().Be("default");
		graph.ClassAssignments["Moving"].Should().Be("special");
	}

	[Test]
	public void ParsesFullExample()
	{
		var graph = MermaidRenderer.Parse("""
			stateDiagram-v2
			classDef badBadEvent fill:#f00,color:white,font-weight:bold,stroke-width:2px,stroke:yellow
			classDef movement font-style:italic
			classDef default fill:#ddd
			[*] --> Still
			Still --> [*]
			Still --> Moving
			Moving --> Still
			Moving --> Crash
			Crash --> [*]
			class Still default
			class Moving movement
			class Crash badBadEvent
			SomeState:::movement
			""");

		graph.ClassDefs.Should().HaveCount(3);
		graph.ClassDefs.Should().ContainKey("badBadEvent");
		graph.ClassDefs.Should().ContainKey("movement");
		graph.ClassDefs.Should().ContainKey("default");

		graph.ClassAssignments["Still"].Should().Be("default");
		graph.ClassAssignments["Moving"].Should().Be("movement");
		graph.ClassAssignments["Crash"].Should().Be("badBadEvent");
		graph.ClassAssignments["SomeState"].Should().Be("movement");

		graph.Nodes.Should().ContainKey("SomeState");
	}

	[Test]
	public void TransitionWithClassPreservesLabel()
	{
		var graph = MermaidRenderer.Parse("""
			stateDiagram-v2
			classDef highlight fill:#ff0
			[*] --> Active
			Active:::highlight --> Done : finished
			""");

		graph.ClassAssignments["Active"].Should().Be("highlight");
		var edge = graph.Edges[1];
		edge.Source.Should().Be("Active");
		edge.Target.Should().Be("Done");
		edge.Label.Should().Be("finished");
	}

	[Test]
	public void StyleDirectiveMultipleNodes()
	{
		var graph = MermaidRenderer.Parse("""
			stateDiagram-v2
			[*] --> A
			A --> B
			B --> [*]
			style A,B fill:#0f0
			""");

		graph.NodeStyles.Should().ContainKey("A");
		graph.NodeStyles.Should().ContainKey("B");
		graph.NodeStyles["A"]["fill"].Should().Be("#0f0");
		graph.NodeStyles["B"]["fill"].Should().Be("#0f0");
	}

	[Test]
	public void ExistingTransitionsStillWork()
	{
		var graph = MermaidRenderer.Parse("""
			stateDiagram-v2
			[*] --> Still
			Still --> [*]
			Still --> Moving
			Moving --> Still
			Moving --> Crash
			Crash --> [*]
			""");

		graph.Edges.Should().HaveCount(6);
		graph.Nodes.Should().ContainKey("Still");
		graph.Nodes.Should().ContainKey("Moving");
		graph.Nodes.Should().ContainKey("Crash");
	}

	[Test]
	public void TransitionWithLabelStillWorks()
	{
		var graph = MermaidRenderer.Parse("""
			stateDiagram-v2
			[*] --> Active
			Active --> Done : task complete
			""");

		graph.Edges.Should().HaveCount(2);
		graph.Edges[1].Label.Should().Be("task complete");
	}
}
