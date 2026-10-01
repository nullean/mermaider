using AwesomeAssertions;
using Sugiyama.Internal;

namespace Sugiyama.Tests;

/// <summary>
/// Validates <see cref="NetworkSimplexRanker"/> against dagre's own unit-test fixtures for
/// this exact algorithm (dagre's <c>test/rank/network-simplex-test.ts</c>), confirming the
/// port reproduces dagre's documented numeric results rather than just "some" valid ranking.
/// Node letters a, b, c... map to indices 0, 1, 2... in declaration order within each test.
/// </summary>
public class NetworkSimplexRankerTests
{
	private static NetworkSimplexRanker.SimplexEdge E(int from, int to, int weight = 1, int minLength = 1) =>
		new(from, to, weight, minLength);

	[Test]
	public void Single_node_has_rank_zero()
	{
		var rank = NetworkSimplexRanker.Rank(1, []);
		rank[0].Should().Be(0);
	}

	[Test]
	public void Two_node_connected_graph()
	{
		// a -> b
		var rank = NetworkSimplexRanker.Rank(2, [E(0, 1)]);
		rank[0].Should().Be(0);
		rank[1].Should().Be(1);
	}

	[Test]
	public void Diamond_assigns_balanced_ranks()
	{
		// a -> b -> d, a -> c -> d
		var rank = NetworkSimplexRanker.Rank(4, [E(0, 1), E(1, 3), E(0, 2), E(2, 3)]);
		rank[0].Should().Be(0); // a
		rank[1].Should().Be(1); // b
		rank[2].Should().Be(1); // c
		rank[3].Should().Be(2); // d
	}

	[Test]
	public void Minlen_on_an_edge_widens_the_affected_path()
	{
		// a -> b -> d, a -> c, c -> d (minlen 2)
		var rank = NetworkSimplexRanker.Rank(4, [E(0, 1), E(1, 3), E(0, 2), E(2, 3, minLength: 2)]);
		rank[0].Should().Be(0); // a
		rank[1].Should().Be(2); // b
		rank[2].Should().Be(1); // c
		rank[3].Should().Be(3); // d
	}

	[Test]
	public void Gansner_paper_example_graph()
	{
		// The worked example from Gansner et al., "A Technique for Drawing Directed Graphs" —
		// dagre's own test fixture for this exact algorithm.
		// a=0 b=1 c=2 d=3 h=4 e=5 g=6 f=7
		// Paths: a-b-c-d-h, a-e-g-h, a-f-g
		var rank = NetworkSimplexRanker.Rank(
			8,
			[
				E(0, 1), E(1, 2), E(2, 3), E(3, 4), // a-b-c-d-h
				E(0, 5), E(5, 6), E(6, 4),          // a-e-g-h
				E(0, 7), E(7, 6),                   // a-f-g
			]);

		rank[0].Should().Be(0); // a
		rank[1].Should().Be(1); // b
		rank[2].Should().Be(2); // c
		rank[3].Should().Be(3); // d
		rank[4].Should().Be(4); // h
		rank[5].Should().Be(1); // e
		rank[7].Should().Be(1); // f
		rank[6].Should().Be(2); // g
	}

	[Test]
	public void Multi_edges_between_the_same_pair_merge_to_the_strictest_minlen()
	{
		// a -> b -> c -> d, a -> e -> d (weight 2), and a SECOND b->c edge with minlen 2 —
		// dagre merges parallel edges by summing weight and taking the max minlen, so b->c
		// ends up needing minlen 2 even though the first b->c edge only asked for 1.
		var rank = NetworkSimplexRanker.Rank(
			5,
			[
				E(0, 1), E(1, 2), E(2, 3),       // a -> b -> c -> d
				E(0, 4, weight: 2), E(4, 3),     // a -> e -> d
				E(1, 2, weight: 1, minLength: 2), // second b -> c edge, minlen 2
			]);

		rank[0].Should().Be(0); // a
		rank[1].Should().Be(1); // b
		rank[2].Should().Be(3); // c (b->c is now 2 apart)
		rank[3].Should().Be(4); // d
		rank[4].Should().Be(1); // e
	}

	[Test]
	public void Disconnected_components_are_each_ranked_independently()
	{
		// a -> b (component 1), c -> d -> e (component 2) — no edges between them.
		var rank = NetworkSimplexRanker.Rank(5, [E(0, 1), E(2, 3), E(3, 4)]);
		rank[0].Should().Be(0);
		rank[1].Should().Be(1);
		rank[2].Should().Be(0);
		rank[3].Should().Be(1);
		rank[4].Should().Be(2);
	}

	[Test]
	public void Self_loop_is_ignored_and_does_not_hang()
	{
		var rank = NetworkSimplexRanker.Rank(2, [E(0, 0, minLength: 3), E(0, 1)]);
		rank[0].Should().Be(0);
		rank[1].Should().Be(1);
	}

	[Test]
	public void No_nodes_returns_empty()
	{
		var rank = NetworkSimplexRanker.Rank(0, []);
		rank.Should().BeEmpty();
	}
}
