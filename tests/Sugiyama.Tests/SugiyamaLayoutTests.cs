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

	// ====================================================================
	// Self-loop / cycle regression tests (issue #49)
	// ====================================================================

	// ====================================================================
	// Fan-out layout regression tests (issue #3780 / #3841)
	// ====================================================================

	[Test]
	public void FanOut_children_on_same_side_edges_do_not_pass_through_sibling_nodes()
	{
		// Regression: VX has 2 children (AX, AU) that both land far left of VX
		// after layout (because layer 2 has 4 nodes and centering pushes VX right).
		// The EdgeRouter must NOT exit VX from its left side at center-Y — that
		// horizontal segment at layer-1 center-Y passes through the "O" sibling node.
		// Instead it must exit from VX's bottom center.
		var graph = new LayoutGraph(
			LayoutDirection.TD,
			[
				new LayoutNode("S",  240, 52),
				new LayoutNode("O",  150, 52),
				new LayoutNode("VX", 110, 52),
				new LayoutNode("VY", 110, 52),
				new LayoutNode("AX", 153, 52),
				new LayoutNode("AU", 157, 52),
				new LayoutNode("NG", 166, 52),
				new LayoutNode("AP", 181, 52),
			],
			[
				new LayoutEdge("S", "O"),
				new LayoutEdge("S", "VX"),
				new LayoutEdge("S", "VY"),
				new LayoutEdge("VX", "AX"),
				new LayoutEdge("VX", "AU"),
				new LayoutEdge("VY", "NG"),
				new LayoutEdge("VY", "AP"),
			],
			[]);

		var result = SugiyamaLayout.Compute(graph);

		var o  = result.Nodes.First(n => n.Id == "O");
		var oRight  = o.X + o.Width;
		var oBottom = o.Y + o.Height;

		// No edge point from VX's outgoing edges should lie inside O's bounding box.
		var vxEdges = result.Edges
			.Where(e => e.OriginalIndex == 3 || e.OriginalIndex == 4) // VX→AX, VX→AU
			.ToList();

		foreach (var edge in vxEdges)
		{
			foreach (var pt in edge.Points)
			{
				var insideX = pt.X > o.X && pt.X < oRight;
				var insideY = pt.Y > o.Y && pt.Y < oBottom;
				(insideX && insideY).Should().BeFalse(
					$"edge point ({pt.X:F1},{pt.Y:F1}) lies inside the 'other-events' node bounding box [{o.X:F1},{o.Y:F1},{oRight:F1},{oBottom:F1}]");
			}
		}
	}

	[Test]
	public void Self_loop_with_outgoing_edge_does_not_stackoverflow()
	{
		// A-->A self-loop plus A-->B: previously caused infinite recursion
		// in ShiftForkDescendants via BuildRealOutgoing including the self-edge.
		var graph = new LayoutGraph(
			LayoutDirection.TD,
			[new LayoutNode("A", 100, 40), new LayoutNode("B", 100, 40)],
			[new LayoutEdge("A", "A"), new LayoutEdge("A", "B")],
			[]);

		var result = SugiyamaLayout.Compute(graph);

		result.Nodes.Should().HaveCount(2);
		result.Edges.Should().HaveCount(2, "self-loop edge is still present in the output");
	}

	[Test]
	public void Multiple_self_loops_do_not_stackoverflow()
	{
		// Mirrors the real-world firewall state machine from issue #49.
		var graph = new LayoutGraph(
			LayoutDirection.TD,
			[
				new LayoutNode("Start", 60, 30),
				new LayoutNode("Ready", 100, 40),
				new LayoutNode("Stopped", 100, 40),
			],
			[
				new LayoutEdge("Start", "Ready"),
				new LayoutEdge("Ready", "Ready"),
				new LayoutEdge("Ready", "Ready"),
				new LayoutEdge("Ready", "Ready"),
				new LayoutEdge("Ready", "Stopped"),
			],
			[]);

		var result = SugiyamaLayout.Compute(graph);

		result.Nodes.Should().HaveCount(3);
	}

	[Test]
	public void Two_node_cycle_does_not_stackoverflow()
	{
		// A-->B-->A: any directed cycle is a latent risk for ShiftForkDescendants
		// without a visited-set guard.
		var graph = new LayoutGraph(
			LayoutDirection.TD,
			[new LayoutNode("A", 100, 40), new LayoutNode("B", 100, 40)],
			[new LayoutEdge("A", "B"), new LayoutEdge("B", "A")],
			[]);

		var result = SugiyamaLayout.Compute(graph);

		result.Nodes.Should().HaveCount(2);
	}

	[Test]
	public void Self_loop_only_node_does_not_stackoverflow()
	{
		// A node with only a self-loop and no other edges.
		var graph = new LayoutGraph(
			LayoutDirection.TD,
			[new LayoutNode("A", 100, 40)],
			[new LayoutEdge("A", "A")],
			[]);

		var result = SugiyamaLayout.Compute(graph);

		result.Nodes.Should().HaveCount(1);
		result.Edges.Should().HaveCount(1);
	}
}
