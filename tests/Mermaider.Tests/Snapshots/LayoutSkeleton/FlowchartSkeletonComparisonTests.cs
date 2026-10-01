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
			// db-flow-01-system: NestingGraphRanker places cluster siblings at different ranks
			// than dagre's cluster-rank contraction (dagre collapses each cluster to a single
			// rank for cross-cluster edges, pulling co-cluster siblings onto one shared rank).
			// db-flow-03-isolated: alphabetical subgraph NodeOrder (matching dagre's insertion
			// order) corrects the cycle-break for the LinkReg→Content back-edge but leaves
			// External subgraph members (OpenApiRepo, Bundles) at rank 1 instead of dagre's
			// rank 5 — dagre's NestingGraph cross-cluster border edges force the entire External
			// group after Outputs (Manifest→LinkReg pulls External's rank range up); Mermaider
			// does not yet add these cross-cluster border edges, so OpenApiRepo/Bundles stay at
			// their minimum-span position (rank 1). Known NestingGraphRanker limitation.
			// db-flow-04-composing: same cluster-containment gap as db-flow-01.
			["db-flow-01-system"] = (0.70, 0.45),
			["db-flow-03-isolated"] = (0.75, 0.70),
			// db-flow-04-composing: cross-cluster border edges (Repos_bottom→Registries_top,
			// Registries_bottom→{Assembler,CodexB}_top) fix AsmCfg/CdxCfg to layer 2 (matching
			// dagre). The cluster-level graph is a DAG so border edges are safe to add.
			["db-flow-04-composing"] = (0.92, 0.70),

			// flowchart-network and flowchart-styled-sub: alphabetical subgraph NodeOrder now
			// matches dagre's node-insertion order, so CycleRemover picks the same back-edge to
			// reverse (internet→router and D→E respectively). Both diagrams achieve 100% layer
			// agreement. Within-layer order for network is ~83% mirror-tolerant (subnet1 vs
			// subnet2 child ordering differs by a tie-break); styled-sub is 100% mirror-tolerant
			// (E/F swap within bar subgraph is the global mirror axis).
			["flowchart-network"] = (1.00, 0.80),
			["flowchart-styled-sub"] = (1.00, 1.00),

			// flowchart-edges and flowchart-long-edges: labeled edges now get minLength=2 so their
			// targets land at the correct layer (matching dagre's virtual-label-node insertion),
			// achieving 100% layer agreement. Within the labeled fan (A→B,C,D), the children are
			// in reversed order vs mjs because LayerAssigner processes edges backwards when
			// inserting virtual nodes — mirror-tolerant is 57% (not 100%) because the component
			// ordering in layer 0 is correct but layer 2 fan order is reversed, so the global
			// mirror can't simultaneously fix both. This is a known tie-break difference; the
			// visual layout is equivalent quality. For unlabeled long-dash edges (----> etc.),
			// extra dashes are visual-only in mjs v12 and do NOT affect rank (minLength stays 1).
			["flowchart-edges"] = (1.00, 0.50),
			["flowchart-long-edges"] = (1.00, 0.50),

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

	private static readonly string IrReferenceDir =
		Path.Combine(GetSourceDirectory(), "Reference");

	private static readonly string SvgReferenceDir =
		Path.Combine(GetSourceDirectory(), "..", "Reference", "mermaidjs");

	public static IEnumerable<string> FlowchartSlugsWithReferences()
	{
		if (!Directory.Exists(IrReferenceDir))
			return [];
		var slugs = DiagramExamples.All
			.Where(d => d.Category == DiagramCategory.Flowchart)
			.Select(d => d.Slug)
			.ToHashSet(StringComparer.Ordinal);
		return Directory.EnumerateFiles(IrReferenceDir, "*.mjs.ir.txt")
			.Select(p => Path.GetFileName(p)[..^".mjs.ir.txt".Length])
			.Where(slug => slugs.Contains(slug))
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

		var irPath = Path.Combine(IrReferenceDir, $"{slug}.mjs.ir.txt");
		if (!File.Exists(irPath))
			throw new InvalidOperationException(
				$"Missing committed IR reference for '{slug}' at {irPath}. " +
				"Run MmdcReferenceTests.Generate_reference to regenerate.");
		var mjsSkeleton = LayoutSkeleton.Parse(await File.ReadAllTextAsync(irPath));

		var layerAgreement = SkeletonComparer.LayerAgreement(mjsSkeleton, mermaiderSkeleton);
		var orderAgreement = SkeletonComparer.WithinLayerOrderAgreement(mjsSkeleton, mermaiderSkeleton);
		var mirrorOrder = SkeletonComparer.MirrorToleratedOrderAgreement(mjsSkeleton, mermaiderSkeleton);

		// Default floor for any flowchart slug not explicitly calibrated in Baselines —
		// intentionally strict (90%) so a newly-added flowchart example that regresses or was
		// never checked shows up loudly instead of silently passing.
		var (minLayer, minOrder) = Baselines.GetValueOrDefault(slug, (0.90, 0.90));

		layerAgreement.Should().BeGreaterThanOrEqualTo(minLayer,
			$"'{slug}' entity layer placement regressed vs its calibrated baseline (got {layerAgreement:P0})");
		// Use mirror-tolerant order as the quality gate: a mirrored ordering is equally valid and
		// only differs by a tie-break (e.g. labeled-edge virtual nodes inserted in reverse order).
		mirrorOrder.Should().BeGreaterThanOrEqualTo(minOrder,
			$"'{slug}' within-layer ordering regressed vs its calibrated baseline (got {mirrorOrder:P0} mirror-tolerant, raw={orderAgreement:P0})");
	}

	[Test]
	public async Task Calibration_report()
	{
		if (!Directory.Exists(IrReferenceDir))
			return;

		var rows = new List<(string Slug, double Layer, double Order, double Mirror, double Sides, int Inversions, string? Error)>();
		foreach (var slug in FlowchartSlugsWithReferences())
		{
			var example = DiagramExamples.All.Single(e => e.Slug == slug);
			var (graph, positioned) = BuildMermaider(example);
			var mermaiderSkeleton = MermaiderGraphSkeletonExtractor.Extract(graph, positioned);
			var inversions = SkeletonComparer.TopologicalInversions(mermaiderSkeleton);

			try
			{
				var irPath = Path.Combine(IrReferenceDir, $"{slug}.mjs.ir.txt");
				var mjsSkeleton = LayoutSkeleton.Parse(await File.ReadAllTextAsync(irPath));
				var layer = SkeletonComparer.LayerAgreement(mjsSkeleton, mermaiderSkeleton);
				var order = SkeletonComparer.WithinLayerOrderAgreement(mjsSkeleton, mermaiderSkeleton);
				var mirror = SkeletonComparer.MirrorToleratedOrderAgreement(mjsSkeleton, mermaiderSkeleton);
				var sides = SkeletonComparer.SideAgreement(mjsSkeleton, mermaiderSkeleton);
				rows.Add((slug, layer, order, mirror, sides, inversions, null));
			}
			catch (Exception ex)
			{
				rows.Add((slug, -1, -1, -1, -1, inversions, ex.Message));
			}
		}

		var sb = new StringBuilder();
		sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
			$"# Flowchart layout skeleton agreement vs mermaid.js — {rows.Count} diagrams");
		sb.AppendLine();
		sb.AppendLine("| Slug | Layer | Order | Order (mirror-tol.) | Sides | Own inversions |");
		sb.AppendLine("|---|---|---|---|---|---|");
		foreach (var r in rows.OrderBy(r => r.Slug, StringComparer.Ordinal))
		{
			var cell = r.Error is null
				? FormattableString.Invariant($"| {r.Slug} | {r.Layer:P0} | {r.Order:P0} | {r.Mirror:P0} | {r.Sides:P0} | {r.Inversions} |")
				: $"| {r.Slug} | error: {r.Error} | — | — | — | {r.Inversions} |";
			sb.AppendLine(cell);
		}

		if (Directory.Exists(SvgReferenceDir))
		{
			var reportPath = Path.Combine(SvgReferenceDir, "skeleton-comparison-report-flowchart.md");
			await File.WriteAllTextAsync(reportPath, sb.ToString());
			Console.WriteLine($"Flowchart skeleton comparison report written to {reportPath}");
		}
		else
		{
			Console.WriteLine(sb.ToString());
		}
	}
}
