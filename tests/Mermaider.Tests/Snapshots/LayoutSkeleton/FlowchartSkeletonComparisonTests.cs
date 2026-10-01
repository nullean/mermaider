using System.Runtime.CompilerServices;
using System.Text;
using AwesomeAssertions;
using Mermaider.Examples;
using Mermaider.Layout;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

// Compares Mermaider's own flowchart layout skeleton (layer/order/edge-side/subgraph placement)
// against mermaid.js's. Mirrors SkeletonComparisonTests (ER) — see that file's header for the
// full rationale (empirical per-slug baselines, the vacuous-pass pitfall already found and
// fixed once for ER, etc). Needs local mermaid.js reference SVGs; skips gracefully when absent.
public class FlowchartSkeletonComparisonTests
{
	private static readonly IReadOnlyDictionary<string, (double MinLayerAgreement, double MinWithinLayerOrderAgreement)> Baselines =
		new Dictionary<string, (double, double)>(StringComparer.Ordinal)
		{
			// db-flow-01-system and db-flow-03-isolated both have a genuine cluster-level cycle:
			// a subgraph member's edge crosses into another subgraph and back (e.g.
			// Isolated -> Links (Outputs) -> Codex/Assembler (Build) alongside
			// DocSet/SpecIndex (outside Build) -> Isolated). dagre's compound-graph rank
			// contraction resolves this by collapsing each cluster to a single rank for
			// cross-cluster edge purposes, pulling co-cluster siblings with no edge between them
			// onto one shared rank; Mermaider's Sugiyama ranks every node by its own longest
			// path from sources, so cluster siblings with independent dependency chains
			// (Isolated vs Codex/Assembler) land on different, individually-correct ranks
			// instead of the cluster's single collapsed one. Both are valid topological
			// layerings of the same (cyclic-at-the-cluster-level) graph. Network simplex
			// (dagre's real default rank assigner) plus NestingGraphRanker (dagre's nesting-graph
			// technique — per-subgraph border-node pairs + a dummy root that force
			// cluster-rank-containment before ranking) together resolved db-flow-01's
			// cluster-cycle disagreement entirely — it now exceeds even the original
			// pre-network-simplex baseline — and pushed db-flow-03/04 further still.
			["db-flow-01-system"] = (0.70, 0.45),
			["db-flow-03-isolated"] = (0.85, 0.70),
			["db-flow-04-composing"] = (0.85, 0.65),

			// flowchart-network and flowchart-styled-sub have a genuine NODE-level cycle (no
			// clusters involved): e.g. internet -> router -> compute -> nat -> internet. dagre's
			// network-simplex and Mermaider's CycleRemover pick different edges to reverse when
			// breaking the same cycle, so everything downstream of the break lands on a
			// different (still internally consistent) rank/order. Same tie-break class as the
			// cluster-cycle cases above, minus the cluster contraction.
			["flowchart-network"] = (0.65, 0.10),
			["flowchart-styled-sub"] = (0.40, 0.0),

			// flowchart-edges and flowchart-long-edges are each three (or two) *fully
			// disconnected* components (A fans out to B/C/D; a separate unconnected E-F pair; a
			// separate unconnected G-H pair — no edges between components at all).
			// SugiyamaLayout.ArrangeComponents lays out each disconnected component as its own
			// independent full-height column tiled side-by-side.
			//
			// This LOOKS like the same root cause as several ER diagrams above (see
			// SkeletonComparisonTests), where setting SeparateComponents = false — unifying rank
			// assignment across all components via one network-simplex pass, same as dagre's
			// always-on nesting-graph root — fixed layer agreement for every affected diagram.
			// It was tried here too, and reverted: confirmed against the mermaid.js reference
			// SVG for flowchart-long-edges, dagre's ACTUAL output tiles A-{B,C,D} and E-{F,G,H}
			// as two visually separate groups, side by side — exactly what Mermaider's existing
			// per-component tiling already produces. Unifying ranking here just interleaves the
			// two groups' children into shared ranks with no connectivity to justify it, which
			// EdgeRouter then has to route around, producing new edge crossings with no IR
			// benefit. The ER case differs: those diagrams have many SMALL disconnected pieces of
			// DIFFERING depth relative to one large main component, where dagre's root-anchoring
			// naturally slots a 2-node pair into the main component's existing layer range rather
			// than giving it a same-sized independent column — there's no equivalent "small piece
			// nested into a big one" shape here, just same-sized independent trees, so dagre's
			// own x-coordinate/ordering phase (not rank unification) is what keeps them visually
			// separate. A real fix for flowchart/class would need to replicate THAT (clustering
			// connected subsets during crossing-minimization/ordering), not rank unification —
			// still real work, still not attempted; left documented rather than faked.
			["flowchart-edges"] = (0.70, 0.95),
			["flowchart-long-edges"] = (0.60, 0.50),

			// flowchart-invisible's ONLY cross-branch ordering signal is `A ~~~ B`, an invisible
			// edge used purely to hint left/right placement. The IR intentionally excludes
			// EdgeStyle.Invisible from both skeletons' edge lists (so a ~~~ edge never counts as
			// a "topological inversion" — it carries no real dependency), but that also means
			// the order-agreement comparator has no signal at all for the one thing this diagram
			// is testing. This is an IR blind spot, not a Mermaider layout defect; 100% layer
			// agreement confirms placement is otherwise correct (confirmed by direct visual
			// comparison against the mermaid.js reference: both place the `~~~`-connected
			// sibling one rank below the other, since dagre feeds the invisible edge into
			// ranking just like network simplex now does here).
			["flowchart-invisible"] = (1.0, 0.0),
		};

	private static string GetSourceDirectory([CallerFilePath] string path = "") =>
		Path.GetDirectoryName(path)!;

	private static readonly string ReferenceDir =
		Path.Combine(GetSourceDirectory(), "..", "Reference", "mermaidjs");

	public static IEnumerable<string> FlowchartSlugsWithReferences()
	{
		if (!Directory.Exists(ReferenceDir))
			return [];
		var slugs = DiagramExamples.All
			.Where(d => d.Category == DiagramCategory.Flowchart)
			.Select(d => d.Slug)
			.ToHashSet(StringComparer.Ordinal);
		return Directory.EnumerateFiles(ReferenceDir, "*.svg")
			.Select(Path.GetFileNameWithoutExtension)
			.Where(slug => slug is not null && slugs.Contains(slug))
			.Select(slug => slug!)
			.Order(StringComparer.Ordinal);
	}

	private static (MermaidGraph Graph, Mermaider.Models.PositionedGraph Positioned) BuildMermaider(DiagramExample example)
	{
		var (cleaned, _) = DiagramPreprocessor.Process(example.Source);
		var lines = MermaidRenderer.PreprocessLines(cleaned);
		var graph = FlowchartParser.Parse(lines);
		return (graph, LightweightLayoutEngine.Layout(graph));
	}

	[Test]
	[MethodDataSource(nameof(FlowchartSlugsWithReferences))]
	public async Task Layout_matches_mermaidjs_ordering(string slug)
	{
		var example = DiagramExamples.All.Single(e => e.Slug == slug);
		var (graph, positioned) = BuildMermaider(example);
		var mermaiderSkeleton = MermaiderGraphSkeletonExtractor.Extract(graph, positioned);

		var svg = await File.ReadAllTextAsync(Path.Combine(ReferenceDir, $"{slug}.svg"));
		var mjsSkeleton = MermaidJsFlowchartSkeletonExtractor.Extract(svg, example.Source);

		var layerAgreement = SkeletonComparer.LayerAgreement(mjsSkeleton, mermaiderSkeleton);
		var orderAgreement = SkeletonComparer.WithinLayerOrderAgreement(mjsSkeleton, mermaiderSkeleton);

		// Default floor for any flowchart slug not explicitly calibrated in Baselines —
		// intentionally strict (90%) so a newly-added flowchart example that regresses or was
		// never checked shows up loudly instead of silently passing.
		var (minLayer, minOrder) = Baselines.GetValueOrDefault(slug, (0.90, 0.90));

		layerAgreement.Should().BeGreaterThanOrEqualTo(minLayer,
			$"'{slug}' entity layer placement regressed vs its calibrated baseline (got {layerAgreement:P0})");
		orderAgreement.Should().BeGreaterThanOrEqualTo(minOrder,
			$"'{slug}' within-layer ordering regressed vs its calibrated baseline (got {orderAgreement:P0})");
	}

	[Test]
	public async Task Calibration_report()
	{
		if (!Directory.Exists(ReferenceDir))
			return;

		var rows = new List<(string Slug, double LayerAgreement, double OrderAgreement, int Inversions, string? Error)>();
		foreach (var slug in FlowchartSlugsWithReferences())
		{
			var example = DiagramExamples.All.Single(e => e.Slug == slug);
			var (graph, positioned) = BuildMermaider(example);
			var mermaiderSkeleton = MermaiderGraphSkeletonExtractor.Extract(graph, positioned);
			var inversions = SkeletonComparer.TopologicalInversions(mermaiderSkeleton);

			try
			{
				var svg = await File.ReadAllTextAsync(Path.Combine(ReferenceDir, $"{slug}.svg"));
				var mjsSkeleton = MermaidJsFlowchartSkeletonExtractor.Extract(svg, example.Source);
				var layerAgreement = SkeletonComparer.LayerAgreement(mjsSkeleton, mermaiderSkeleton);
				var orderAgreement = SkeletonComparer.WithinLayerOrderAgreement(mjsSkeleton, mermaiderSkeleton);
				rows.Add((slug, layerAgreement, orderAgreement, inversions, null));
			}
			catch (Exception ex)
			{
				rows.Add((slug, -1, -1, inversions, ex.Message));
			}
		}

		var sb = new StringBuilder();
		sb.AppendLine($"# Flowchart layout skeleton agreement vs mermaid.js — {rows.Count} diagrams");
		sb.AppendLine();
		sb.AppendLine("| Slug | Layer agreement | Order agreement | Own inversions |");
		sb.AppendLine("|---|---|---|---|");
		foreach (var r in rows.OrderBy(r => r.Slug, StringComparer.Ordinal))
		{
			var cell = r.Error is null
				? $"| {r.Slug} | {r.LayerAgreement:P0} | {r.OrderAgreement:P0} | {r.Inversions} |"
				: $"| {r.Slug} | error: {r.Error} | — | {r.Inversions} |";
			sb.AppendLine(cell);
		}

		var reportPath = Path.Combine(ReferenceDir, "skeleton-comparison-report-flowchart.md");
		await File.WriteAllTextAsync(reportPath, sb.ToString());
		Console.WriteLine($"Flowchart skeleton comparison report written to {reportPath}");
	}
}
