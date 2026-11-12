using AwesomeAssertions;
using Mermaider.Models;

namespace Mermaider.Tests.Parsing;

public class StateParserTests
{
	[Test]
	public void ParsesClassDef()
	{
		var graph = MermaidRenderer.Parse(
			"""
			stateDiagram-v2
			classDef badEvent fill:#f00,color:white
			[*] --> Crash
			Crash --> [*]
			"""
		);

		graph.ClassDefs.Should().ContainKey("badEvent");
		graph.ClassDefs["badEvent"].Should().ContainKey("fill");
		graph.ClassDefs["badEvent"]["fill"].Should().Be("#f00");
		graph.ClassDefs["badEvent"]["color"].Should().Be("white");
	}

	[Test]
	public void ParsesClassAssignment()
	{
		var graph = MermaidRenderer.Parse(
			"""
			stateDiagram-v2
			classDef badEvent fill:#f00,color:white
			[*] --> Crash
			Crash --> [*]
			class Crash badEvent
			"""
		);

		graph.ClassAssignments.Should().ContainKey("Crash");
		graph.ClassAssignments["Crash"].Should().Be("badEvent");
	}

	[Test]
	public void ParsesMultipleClassAssignments()
	{
		var graph = MermaidRenderer.Parse(
			"""
			stateDiagram-v2
			classDef highlight fill:#ff0
			[*] --> A
			A --> B
			B --> [*]
			class A,B highlight
			"""
		);

		graph.ClassAssignments.Should().ContainKey("A");
		graph.ClassAssignments.Should().ContainKey("B");
		graph.ClassAssignments["A"].Should().Be("highlight");
		graph.ClassAssignments["B"].Should().Be("highlight");
	}

	[Test]
	public void ParsesClassShorthandStandalone()
	{
		var graph = MermaidRenderer.Parse(
			"""
			stateDiagram-v2
			classDef movement font-style:italic
			[*] --> Moving
			Moving --> [*]
			Moving:::movement
			"""
		);

		graph.ClassAssignments.Should().ContainKey("Moving");
		graph.ClassAssignments["Moving"].Should().Be("movement");
	}

	[Test]
	public void ParsesClassShorthandInTransitionSource()
	{
		var graph = MermaidRenderer.Parse(
			"""
			stateDiagram-v2
			classDef movement font-style:italic
			[*] --> Moving
			Moving:::movement --> Still
			"""
		);

		graph.ClassAssignments.Should().ContainKey("Moving");
		graph.ClassAssignments["Moving"].Should().Be("movement");
		graph.Edges.Should().HaveCount(2);
	}

	[Test]
	public void ParsesClassShorthandInTransitionTarget()
	{
		var graph = MermaidRenderer.Parse(
			"""
			stateDiagram-v2
			classDef badEvent fill:#f00
			Moving --> Crash:::badEvent
			"""
		);

		graph.ClassAssignments.Should().ContainKey("Crash");
		graph.ClassAssignments["Crash"].Should().Be("badEvent");
	}

	[Test]
	public void ParsesStyleDirective()
	{
		var graph = MermaidRenderer.Parse(
			"""
			stateDiagram-v2
			[*] --> Active
			Active --> [*]
			style Active fill:#0f0,stroke:#333
			"""
		);

		graph.NodeStyles.Should().ContainKey("Active");
		graph.NodeStyles["Active"]["fill"].Should().Be("#0f0");
		graph.NodeStyles["Active"]["stroke"].Should().Be("#333");
	}

	[Test]
	public void ParsesClassDefDefault()
	{
		var graph = MermaidRenderer.Parse(
			"""
			stateDiagram-v2
			classDef default fill:#ddd
			[*] --> Still
			Still --> Moving
			Moving --> [*]
			"""
		);

		graph.ClassDefs.Should().ContainKey("default");
		graph.ClassAssignments.Should().ContainKey("Still");
		graph.ClassAssignments.Should().ContainKey("Moving");
		graph.ClassAssignments["Still"].Should().Be("default");
		graph.ClassAssignments["Moving"].Should().Be("default");
	}

	[Test]
	public void ClassDefDefaultDoesNotOverrideExplicit()
	{
		var graph = MermaidRenderer.Parse(
			"""
			stateDiagram-v2
			classDef default fill:#ddd
			classDef special fill:#f00
			[*] --> Still
			Still --> Moving
			Moving --> [*]
			class Moving special
			"""
		);

		graph.ClassAssignments["Still"].Should().Be("default");
		graph.ClassAssignments["Moving"].Should().Be("special");
	}

	[Test]
	public void ParsesFullExample()
	{
		var graph = MermaidRenderer.Parse(
			"""
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
			"""
		);

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
		var graph = MermaidRenderer.Parse(
			"""
			stateDiagram-v2
			classDef highlight fill:#ff0
			[*] --> Active
			Active:::highlight --> Done : finished
			"""
		);

		graph.ClassAssignments["Active"].Should().Be("highlight");
		var edge = graph.Edges[1];
		edge.Source.Should().Be("Active");
		edge.Target.Should().Be("Done");
		edge.Label.Should().Be("finished");
	}

	[Test]
	public void StyleDirectiveMultipleNodes()
	{
		var graph = MermaidRenderer.Parse(
			"""
			stateDiagram-v2
			[*] --> A
			A --> B
			B --> [*]
			style A,B fill:#0f0
			"""
		);

		graph.NodeStyles.Should().ContainKey("A");
		graph.NodeStyles.Should().ContainKey("B");
		graph.NodeStyles["A"]["fill"].Should().Be("#0f0");
		graph.NodeStyles["B"]["fill"].Should().Be("#0f0");
	}

	[Test]
	public void ExistingTransitionsStillWork()
	{
		var graph = MermaidRenderer.Parse(
			"""
			stateDiagram-v2
			[*] --> Still
			Still --> [*]
			Still --> Moving
			Moving --> Still
			Moving --> Crash
			Crash --> [*]
			"""
		);

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

	// #81: a transition naming a composite nested in another composite, or a composite that holds only composites
	[Test]
	[Arguments("stateDiagram-v2\nstate P {\n  a --> R\n  state R {\n    [*] --> f\n  }\n}")]
	[Arguments("stateDiagram-v2\nstate P {\n  state R {\n    [*] --> f\n  }\n  a --> R\n}")]
	[Arguments("stateDiagram-v2\nstate P {\n  a\n  state R {\n    [*] --> f\n  }\n}\nR --> a")]
	[Arguments("stateDiagram-v2\nstate P {\n  state R {\n    [*] --> f\n  }\n}\na --> P")]
	[Arguments("stateDiagram-v2\nstate P {\n  state R {\n    [*] --> f\n  }\n}\nP --> a")]
	public void Edges_to_nested_composites_resolve_to_states_inside_them(string source)
	{
		var graph = MermaidRenderer.Parse(source);

		foreach (var edge in graph.Edges)
		{
			graph.Nodes.Should().ContainKey(edge.Source, "every edge endpoint must be a laid-out node");
			graph.Nodes.Should().ContainKey(edge.Target, "every edge endpoint must be a laid-out node");
		}
		AllNodeIds(graph.Subgraphs).Should().OnlyContain(id => graph.Nodes.ContainsKey(id), "a composite is not a node of its parent");

		var svg = MermaidRenderer.RenderSvg(source);
		svg.Should().Contain("<svg");
	}

	[Test]
	public void Edge_to_nested_composite_enters_at_its_start_state()
	{
		var graph = MermaidRenderer.Parse(
			"""
			stateDiagram-v2
			state P {
				a --> R
				state R {
					[*] --> f
				}
			}
			"""
		);

		var edge = graph.Edges.Single(e => e.Source == "a");
		graph.Nodes[edge.Target].Shape.Should().Be(NodeShape.StateStart);
		graph.SubgraphEdgeRedirections.Values.Should().Contain(r => r.TargetSubgraph == "R");
		graph.Subgraphs.Single().NodeIds.Should().Equal("a");
	}

	[Test]
	public void Edge_to_composite_holding_only_composites_enters_through_its_first_child()
	{
		var graph = MermaidRenderer.Parse(
			"""
			stateDiagram-v2
			state P {
				state R {
					[*] --> f
				}
			}
			a --> P
			"""
		);

		var edge = graph.Edges.Single(e => e.Source == "a");
		graph.Nodes[edge.Target].Shape.Should().Be(NodeShape.StateStart);
		graph.SubgraphEdgeRedirections.Values.Should().Contain(r => r.TargetSubgraph == "P");
	}

	private static IEnumerable<string> AllNodeIds(IReadOnlyList<MermaidSubgraph> subgraphs) =>
		subgraphs.SelectMany(sg => sg.NodeIds.Concat(AllNodeIds(sg.Children)));
}
