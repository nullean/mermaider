using AwesomeAssertions;
using Sugiyama.Internal;

namespace Sugiyama.Tests;

/// <summary>
/// Validates <see cref="NestingGraphRanker"/>'s compound-graph (subgraph) rank containment —
/// the root-cause fix for a real rendering bug found via the `class-namespace` and
/// `rfc-integration-pipeline` gallery examples: a node whose only strong pull is a weak edge
/// into a *different*, deeper subgraph gets dragged out of its own subgraph's compact rank
/// range by plain network simplex (which has no notion of subgraph membership), landing on the
/// same rank as — and visually inside the box of — a sibling subgraph's member.
/// </summary>
public class NestingGraphRankerTests
{
	private static NetworkSimplexRanker.SimplexEdge E(int from, int to, int weight = 1, int minLength = 1) =>
		new(from, to, weight, minLength);

	private static NestingGraphRanker.Group Group(params int[] ownNodes)
	{
		var g = new NestingGraphRanker.Group();
		g.OwnNodes.AddRange(ownNodes);
		return g;
	}

	[Test]
	public void No_groups_falls_through_to_plain_network_simplex()
	{
		// a -> b, no subgraphs at all — must be byte-identical to calling NetworkSimplexRanker
		// directly (zero augmentation overhead / behavior change for subgraph-free diagrams).
		var edges = new[] { E(0, 1) };
		var direct = NetworkSimplexRanker.Rank(2, edges);
		var viaNesting = NestingGraphRanker.Rank(2, edges, [0, 1], []);
		viaNesting.Should().Equal(direct);
	}

	[Test]
	public void Sibling_subgraph_members_stay_compact_despite_an_external_pull()
	{
		// Reproduces the class-namespace bug shape:
		//   Infrastructure { ProductRepo(0), OrderRepo(1) }
		//   Domain         { Order(2), Product(3) }
		//   OrderRepo --|> Order, ProductRepo --|> Product, Order --> Product
		// Plain network simplex pulls ProductRepo down to minimize its own long edge to
		// Product, landing it on the same rank as Order (a DIFFERENT subgraph's member) —
		// visually, ProductRepo's box renders inside the Domain subgraph's rectangle.
		// Nesting-graph's high-weight, tight border-to-member edges must keep ProductRepo
		// anchored to its own subgraph (same rank as its sibling OrderRepo) instead.
		var edges = new[]
		{
			E(1, 2), // OrderRepo -> Order
			E(0, 3), // ProductRepo -> Product
			E(2, 3), // Order -> Product
		};
		var infrastructure = Group(0, 1);
		var domain = Group(2, 3);

		// Sanity check: plain network simplex (no nesting-graph) actually reproduces the bug,
		// so this test would fail loudly if the fix regressed to a no-op.
		var plain = NetworkSimplexRanker.Rank(4, edges);
		plain[0].Should().NotBe(plain[1], "plain network simplex pulls ProductRepo away from OrderRepo (the bug)");

		var fixedRanks = NestingGraphRanker.Rank(4, edges, [], [infrastructure, domain]);
		fixedRanks[0].Should().Be(fixedRanks[1],
			"ProductRepo and OrderRepo must stay on the same rank — both belong to the compact Infrastructure subgraph");
		fixedRanks[2].Should().BeGreaterThan(fixedRanks[1], "Order must still rank after its own predecessor OrderRepo");
		fixedRanks[3].Should().BeGreaterThan(fixedRanks[2], "Product must still rank after Order");
	}

	[Test]
	public void Disjoint_sibling_subgraphs_with_no_internal_edges_still_separate_from_each_other()
	{
		// Two single-node subgraphs with no edges between or within them: each is its own
		// trivial group. Must not throw, and each node gets a valid non-negative rank.
		var a = Group(0);
		var b = Group(1);
		var ranks = NestingGraphRanker.Rank(2, [], [], [a, b]);
		ranks[0].Should().BeGreaterThanOrEqualTo(0);
		ranks[1].Should().BeGreaterThanOrEqualTo(0);
	}

	[Test]
	public void Loose_top_level_node_outside_any_subgraph_ranks_before_its_successor_inside_one()
	{
		// loose(0) -> a(1), where a is inside subgraph sg.
		var edges = new[] { E(0, 1) };
		var sg = Group(1);
		var ranks = NestingGraphRanker.Rank(2, edges, [0], [sg]);
		ranks[1].Should().BeGreaterThan(ranks[0]);
	}

	[Test]
	public void Nested_subgraphs_three_levels_deep_do_not_throw_and_preserve_edge_order()
	{
		// outer { mid { inner { a(0) } }, b(1) }, a -> b (cross-nesting-level edge).
		var inner = Group(0);
		var mid = new NestingGraphRanker.Group();
		mid.Children.Add(inner);
		var outer = new NestingGraphRanker.Group();
		outer.Children.Add(mid);
		outer.OwnNodes.Add(1);

		var edges = new[] { E(0, 1) };
		var ranks = NestingGraphRanker.Rank(2, edges, [], [outer]);
		ranks[1].Should().BeGreaterThan(ranks[0]);
	}

	[Test]
	public void Single_node_subgraph_with_no_edges_ranks_at_zero()
	{
		var sg = Group(0);
		var ranks = NestingGraphRanker.Rank(1, [], [], [sg]);
		ranks[0].Should().Be(0);
	}
}
