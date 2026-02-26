using AwesomeAssertions;
using Sugiyama;

namespace Sugiyama.Tests;

public class SugiyamaLayoutTests
{
	[Test]
	public void Single_node_produces_valid_result()
	{
		var graph = new LayoutGraph(
			LayoutDirection.TD,
			[new LayoutNode("A", 100, 40)],
			[],
			[]);

		var result = SugiyamaLayout.Compute(graph);

		result.Nodes.Should().HaveCount(1);
		result.Nodes[0].Id.Should().Be("A");
		result.Width.Should().BeGreaterThan(0);
		result.Height.Should().BeGreaterThan(0);
	}

	[Test]
	public void Two_nodes_one_edge_lays_out_vertically()
	{
		var graph = new LayoutGraph(
			LayoutDirection.TD,
			[new LayoutNode("A", 100, 40), new LayoutNode("B", 100, 40)],
			[new LayoutEdge("A", "B")],
			[]);

		var result = SugiyamaLayout.Compute(graph);

		result.Nodes.Should().HaveCount(2);
		result.Edges.Should().HaveCount(1);

		var a = result.Nodes.First(n => n.Id == "A");
		var b = result.Nodes.First(n => n.Id == "B");
		a.Y.Should().BeLessThan(b.Y, "A should be above B in TD layout");
	}

	[Test]
	public void LR_direction_places_nodes_horizontally()
	{
		var graph = new LayoutGraph(
			LayoutDirection.LR,
			[new LayoutNode("A", 100, 40), new LayoutNode("B", 100, 40)],
			[new LayoutEdge("A", "B")],
			[]);

		var result = SugiyamaLayout.Compute(graph);

		var a = result.Nodes.First(n => n.Id == "A");
		var b = result.Nodes.First(n => n.Id == "B");
		a.X.Should().BeLessThan(b.X, "A should be left of B in LR layout");
	}

	[Test]
	public void Diamond_graph_assigns_correct_layers()
	{
		var graph = new LayoutGraph(
			LayoutDirection.TD,
			[
				new LayoutNode("A", 80, 40),
				new LayoutNode("B", 80, 40),
				new LayoutNode("C", 80, 40),
				new LayoutNode("D", 80, 40),
			],
			[
				new LayoutEdge("A", "B"),
				new LayoutEdge("A", "C"),
				new LayoutEdge("B", "D"),
				new LayoutEdge("C", "D"),
			],
			[]);

		var result = SugiyamaLayout.Compute(graph);

		result.Nodes.Should().HaveCount(4);
		result.Edges.Should().HaveCount(4);

		var a = result.Nodes.First(n => n.Id == "A");
		var b = result.Nodes.First(n => n.Id == "B");
		var c = result.Nodes.First(n => n.Id == "C");
		var d = result.Nodes.First(n => n.Id == "D");

		a.Y.Should().BeLessThan(b.Y);
		a.Y.Should().BeLessThan(c.Y);
		b.Y.Should().BeLessThan(d.Y);
		c.Y.Should().BeLessThan(d.Y);
	}

	[Test]
	public void Cyclic_graph_does_not_throw()
	{
		var graph = new LayoutGraph(
			LayoutDirection.TD,
			[
				new LayoutNode("A", 80, 40),
				new LayoutNode("B", 80, 40),
				new LayoutNode("C", 80, 40),
			],
			[
				new LayoutEdge("A", "B"),
				new LayoutEdge("B", "C"),
				new LayoutEdge("C", "A"),
			],
			[]);

		var result = SugiyamaLayout.Compute(graph);

		result.Nodes.Should().HaveCount(3);
		result.Edges.Should().HaveCount(3);
		result.Width.Should().BeGreaterThan(0);
		result.Height.Should().BeGreaterThan(0);
	}

	[Test]
	public void Subgraph_bounds_are_computed()
	{
		var graph = new LayoutGraph(
			LayoutDirection.TD,
			[
				new LayoutNode("A", 80, 40),
				new LayoutNode("B", 80, 40),
				new LayoutNode("C", 80, 40),
			],
			[
				new LayoutEdge("A", "B"),
				new LayoutEdge("B", "C"),
			],
			[
				new LayoutSubgraph("sg1", "My Group", ["A", "B"], []),
			]);

		var result = SugiyamaLayout.Compute(graph);

		result.Groups.Should().HaveCount(1);
		var group = result.Groups[0];
		group.Id.Should().Be("sg1");
		group.Label.Should().Be("My Group");
		group.Width.Should().BeGreaterThan(0);
		group.Height.Should().BeGreaterThan(0);
	}

	[Test]
	public void Edge_points_are_not_empty()
	{
		var graph = new LayoutGraph(
			LayoutDirection.TD,
			[new LayoutNode("A", 100, 40), new LayoutNode("B", 100, 40)],
			[new LayoutEdge("A", "B")],
			[]);

		var result = SugiyamaLayout.Compute(graph);

		result.Edges.Should().HaveCount(1);
		result.Edges[0].Points.Should().HaveCountGreaterThan(1);
	}

	[Test]
	public void Large_graph_completes_without_error()
	{
		var nodes = Enumerable.Range(0, 20)
			.Select(i => new LayoutNode($"N{i}", 80, 40))
			.ToList();

		var edges = Enumerable.Range(0, 19)
			.Select(i => new LayoutEdge($"N{i}", $"N{i + 1}"))
			.ToList();

		var graph = new LayoutGraph(LayoutDirection.TD, nodes, edges, []);

		var result = SugiyamaLayout.Compute(graph);

		result.Nodes.Should().HaveCount(20);
		result.Edges.Should().HaveCount(19);
		result.Width.Should().BeGreaterThan(0);
		result.Height.Should().BeGreaterThan(0);
	}

	[Test]
	public void BT_direction_reverses_vertical_order()
	{
		var graph = new LayoutGraph(
			LayoutDirection.BT,
			[new LayoutNode("A", 100, 40), new LayoutNode("B", 100, 40)],
			[new LayoutEdge("A", "B")],
			[]);

		var result = SugiyamaLayout.Compute(graph);

		var a = result.Nodes.First(n => n.Id == "A");
		var b = result.Nodes.First(n => n.Id == "B");
		a.Y.Should().BeGreaterThan(b.Y, "A should be below B in BT layout");
	}

	[Test]
	public void RL_direction_reverses_horizontal_order()
	{
		var graph = new LayoutGraph(
			LayoutDirection.RL,
			[new LayoutNode("A", 100, 40), new LayoutNode("B", 100, 40)],
			[new LayoutEdge("A", "B")],
			[]);

		var result = SugiyamaLayout.Compute(graph);

		var a = result.Nodes.First(n => n.Id == "A");
		var b = result.Nodes.First(n => n.Id == "B");
		a.X.Should().BeGreaterThan(b.X, "A should be right of B in RL layout");
	}

	[Test]
	public void Disconnected_graph_handles_gracefully()
	{
		var graph = new LayoutGraph(
			LayoutDirection.TD,
			[
				new LayoutNode("A", 80, 40),
				new LayoutNode("B", 80, 40),
				new LayoutNode("C", 80, 40),
			],
			[new LayoutEdge("A", "B")],
			[]);

		var result = SugiyamaLayout.Compute(graph);

		result.Nodes.Should().HaveCount(3);
		result.Edges.Should().HaveCount(1);
	}

	[Test]
	public void Custom_spacing_options_are_respected()
	{
		var graph = new LayoutGraph(
			LayoutDirection.TD,
			[new LayoutNode("A", 80, 40), new LayoutNode("B", 80, 40)],
			[new LayoutEdge("A", "B")],
			[]);

		var tight = SugiyamaLayout.Compute(graph, new LayoutOptions { NodeSpacing = 10, LayerSpacing = 20 });
		var wide = SugiyamaLayout.Compute(graph, new LayoutOptions { NodeSpacing = 100, LayerSpacing = 200 });

		wide.Height.Should().BeGreaterThan(tight.Height, "wider spacing should produce larger output");
	}
}
