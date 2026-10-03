using System.Runtime.CompilerServices;
using System.Text;
using AwesomeAssertions;
using Mermaider.Examples;
using Mermaider.Layout;
using Mermaider.Parsing;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

// Compares Mermaider's own ER layout skeleton (layer/order/edge-side) against mermaid.js's,
// via LayoutSkeletonBuilder projecting both onto the same ordinal structure.
//
// Needs local mermaid.js reference SVGs — regenerate with:
//   dotnet run --project tests/Mermaider.Tests -- --treenode-filter "*MmdcReferenceTests*"
// Skips gracefully (not a failure) when the reference directory or a specific reference file
// is absent, exactly like MjsComparisonTests.
//
// Thresholds below are EMPIRICAL, per the "run it first, then calibrate" choice: Calibration_report()
// dumps today's actual LayerAgreement/WithinLayerOrderAgreement scores for every ER example with
// a reference. Per-slug floors are then set a few points below today's actual score — regressions
// get caught, but today's known-imperfect diagrams don't need to be fixed just to make the suite
// pass.
//
// A single global threshold across all diagrams was tried first and abandoned: scores vary far
// too widely (see the table below) for one number to be honest. That variance was initially
// masked entirely — MermaidJsSkeletonExtractor only recognized the "has an attribute block"
// entity shape (<g class="outer-path">), so any diagram whose entities render as the plainer
// <rect class="basic label-container"> (attribute-less entities — the majority of the
// docs-builder examples) silently extracted zero mermaid.js nodes, and SkeletonComparer's
// "nothing to compare" fallback then reported a vacuous 100%/100%. Both are fixed now: the
// extractor handles both shapes, and SkeletonComparer throws instead of vacuously passing when
// two skeletons share zero entity ids despite both having some.
//
// With real extraction, scores genuinely range from ~57% to 100% (layer) and 0% to 100% (order).
// TopologicalInversions()==0 for every diagram confirms Mermaider's own layering stays internally
// consistent even where it disagrees with mermaid.js.
//
// Disconnected-component packing (formerly divergence (a) here) is FIXED: dagre always connects
// every top-level node to an implicit dummy root before ranking (lib/nesting-graph.ts's run() is
// unconditional, not just for explicit subgraphs/clusters), which is what lets independent
// weakly-connected pieces land in the SAME layer range as the main component instead of getting
// stacked as new layers underneath it. ER's LightweightErLayoutEngine now sets
// LayoutOptions.SeparateComponents = false so the whole graph — main component plus every
// disconnected piece — goes through one NetworkSimplexRanker/NestingGraphRanker pass together
// (see that flag's doc comment in LayoutModels.cs), matching dagre's actual behavior instead of
// Mermaider's previous bespoke grid-tiling heuristic (SugiyamaLayout.ArrangeComponents). This
// raised layer agreement to 100% for nearly every previously-affected diagram (db-erd-07/09/12
// among others), at the cost of a few extra visual edge crossings on 3 diagrams where a
// disconnected piece's un-pinned horizontal position now threads through a busier main-component
// layer (see ErEdgeCrossingTests) — a trade the IR comparison shows is net-positive structurally.
//
// The remaining divergence is (b) unconstrained sibling order — when two entities share a parent
// with no other edge fixing their relative order (e.g. er-complex's COMMENT/TAG, both children of
// POST), dagre and Sugiyama can tie-break oppositely; nothing in the diagram prefers one order
// over the other. This is not "more correct" either way — it's a both-valid answer to an
// underspecified layout question. Per-slug floors document today's actual divergence instead of
// pretending it doesn't exist.
public class SkeletonComparisonTests
{
	// (MinLayerAgreement, MinWithinLayerOrderAgreement) per slug, floored a few points below the
	// actual score Calibration_report() produced at calibration time — tight enough to catch a
	// real regression, loose enough to tolerate the rounding noise inherent in percentage
	// formatting. Every slug returned by ErSlugsWithReferences() must have an entry; a missing
	// entry fails loudly (see Layout_matches_mermaidjs_ordering) rather than silently skipping,
	// so a newly-added ER example can't accidentally go uncalibrated.
	private static readonly IReadOnlyDictionary<string, (double MinLayerAgreement, double MinWithinLayerOrderAgreement)> Baselines =
		new Dictionary<string, (double, double)>(StringComparer.Ordinal)
		{
			["db-erd-02-shared-vocab"] = (0.95, 0.75),
			["db-erd-05-building-blocks"] = (0.90, 0.25),
			["db-erd-06-catalog"] = (0.95, 0.95),
			["db-erd-07-source"] = (0.95, 0.95),
			["db-erd-08-docset"] = (0.95, 0.95),
			["db-erd-09-content"] = (0.95, 0.95),
			["db-erd-10-navigation"] = (0.95, 0.95),
			["db-erd-11-link-graph"] = (0.95, 0.95),
			["db-erd-12-assembly"] = (0.95, 0.95),
			["db-erd-13-codex"] = (0.95, 0.95),
			["db-erd-14-release-notes"] = (0.95, 0.95),
			["db-erd-15-api-search"] = (0.95, 0.88),
			["db-erd-16-relationships-overview"] = (0.95, 0.80),
			["db-erd-17-catalog"] = (0.95, 0.95),
			["db-erd-18-source"] = (0.95, 0.15),
			// Unifying rank assignment across disconnected components (dagre always connects every
			// top-level node to a dummy root — see SugiyamaLayout's SeparateComponents=false comment)
			// fixed layer agreement to 100% for both, at the cost of within-layer order agreement —
			// an explicitly-documented both-valid tie-break dimension (see class remarks), not a bug.
			["db-erd-19-docset"] = (0.95, 0.25),
			["db-erd-20-content"] = (0.95, 0.50),
			["db-erd-21-navigation"] = (0.90, 0.78),
			["db-erd-22-link-graph"] = (0.95, 0.95),
			["db-erd-23-publishing"] = (0.95, 0.25),
			// UseRealFirstTiebreaker (ELK NODES_AND_EDGES real-before-virtual seed ordering) improved
			// er-complex COMMENT/TAG from 0% to 100%, at the cost of a local-minimum shift here
			// (was 100% when virtual-first anchored these layers; real-first finds a different minimum).
			["db-erd-24-codex"] = (0.95, 0.70),
			// Network simplex improved this: 78%/60% -> 99%/76%. DFS-from-sources initial order
			// subsequently changed within-layer convergence: 99%/76% -> 99%/56%. The layer
			// agreement is excellent; the order difference is a valid-but-different local minimum.
			["db-erd-25-release-notes"] = (0.95, 0.52),
			// UseRealFirstTiebreaker shifts the local minimum in diagrams that have long skip-layer
			// edges whose virtual nodes previously anchored the ordering; 56% matches the new
			// local minimum under ELK's real-before-virtual seeding.
			["db-erd-26-api-reference"] = (0.95, 0.50),
			["db-erd-27-search"] = (0.95, 0.95),
			["er-aliases"] = (0.95, 0.95),
			["er-basic"] = (0.95, 0.95),
			// UseRealFirstTiebreaker (ELK NODES_AND_EDGES real-before-virtual seed ordering) fixes
			// COMMENT/TAG sibling order: virtual node for USER→COMMENT (edge idx 2) now seeds after
			// POST (node idx 1), giving barycenters TAG=0 COMMENT=0.5, matching mjs exactly.
			["er-complex"] = (0.95, 0.95),
			["er-direction"] = (0.95, 0.95),
		};

	private static string GetSourceDirectory([CallerFilePath] string path = "") =>
		Path.GetDirectoryName(path)!;

	// Committed skeleton IR files for the mermaid.js reference — generated by MmdcReferenceTests
	// and checked in so this suite runs in CI without any SVG or node dependency.
	private static readonly string IrReferenceDir =
		Path.Combine(GetSourceDirectory(), "Reference");

	// Gitignored SVG dir — only used for Calibration_report() where the caller already has SVGs.
	private static readonly string SvgReferenceDir =
		Path.Combine(GetSourceDirectory(), "..", "Reference", "mermaidjs");

	public static IEnumerable<string> ErSlugsWithReferences()
	{
		if (!Directory.Exists(IrReferenceDir))
			return [];
		var erSlugs = DiagramExamples.All
			.Where(d => d.Category == DiagramCategory.Er)
			.Select(d => d.Slug)
			.ToHashSet(StringComparer.Ordinal);
		return Directory.EnumerateFiles(IrReferenceDir, "*.mjs.ir.txt")
			.Select(p => Path.GetFileName(p)[..^".mjs.ir.txt".Length])
			.Where(slug => erSlugs.Contains(slug))
			.Order(StringComparer.Ordinal);
	}

	private static LayoutSkeleton BuildMermaiderSkeleton(DiagramExample example)
	{
		var (cleaned, _) = DiagramPreprocessor.Process(example.Source);
		var lines = MermaidRenderer.PreprocessLines(cleaned);
		var positioned = LightweightErLayoutEngine.Layout(ErParser.Parse(lines));
		return MermaiderSkeletonExtractor.Extract(positioned);
	}

	[Test]
	[MethodDataSource(nameof(ErSlugsWithReferences))]
	public async Task Layout_matches_mermaidjs_ordering(string slug)
	{
		var example = DiagramExamples.All.Single(e => e.Slug == slug);
		var mermaiderSkeleton = BuildMermaiderSkeleton(example);

		var irPath = Path.Combine(IrReferenceDir, $"{slug}.mjs.ir.txt");
		if (!File.Exists(irPath))
			throw new InvalidOperationException(
				$"Missing committed IR reference for '{slug}' at {irPath}. " +
				"Run MmdcReferenceTests.Generate_reference to regenerate.");
		var mjsSkeleton = LayoutSkeleton.Parse(await File.ReadAllTextAsync(irPath));

		var layerAgreement = SkeletonComparer.LayerAgreement(mjsSkeleton, mermaiderSkeleton);
		var orderAgreement = SkeletonComparer.WithinLayerOrderAgreement(mjsSkeleton, mermaiderSkeleton);
		var mirrorOrder = SkeletonComparer.MirrorToleratedOrderAgreement(mjsSkeleton, mermaiderSkeleton);
		var sideAgreement = SkeletonComparer.SideAgreement(mjsSkeleton, mermaiderSkeleton);

		Baselines.TryGetValue(slug, out var baseline).Should().BeTrue(
			$"'{slug}' has no entry in {nameof(Baselines)} — run Calibration_report(), inspect its score " +
			"(layer={layerAgreement:P0}, order={orderAgreement:P0}), and add a floored entry for it");

		layerAgreement.Should().BeGreaterThanOrEqualTo(baseline.MinLayerAgreement,
			$"'{slug}' entity layer placement regressed vs its calibrated baseline (got {layerAgreement:P0})");
		orderAgreement.Should().BeGreaterThanOrEqualTo(baseline.MinWithinLayerOrderAgreement,
			$"'{slug}' within-layer ordering regressed vs its calibrated baseline (got {orderAgreement:P0}); mirror-tolerant={mirrorOrder:P0}, sides={sideAgreement:P0}");
	}

	// Writes a Markdown quality report to Tests/Snapshots/Reference/mermaidjs/skeleton-comparison-report.md
	// (gitignored). Run the suite normally — this test auto-updates the file whenever reference
	// IR files exist.
	[Test]
	public async Task Calibration_report()
	{
		if (!Directory.Exists(IrReferenceDir))
			return; // no references yet — run MmdcReferenceTests first

		var rows = new List<(string Slug, double Layer, double Order, double Mirror, double Sides, double Routes, double Ports, int Inversions, string? Error)>();
		foreach (var slug in ErSlugsWithReferences())
		{
			var example = DiagramExamples.All.Single(e => e.Slug == slug);
			var mermaiderSkeleton = BuildMermaiderSkeleton(example);
			var inversions = SkeletonComparer.TopologicalInversions(mermaiderSkeleton);

			try
			{
				var irPath = Path.Combine(IrReferenceDir, $"{slug}.mjs.ir.txt");
				var mjsSkeleton = LayoutSkeleton.Parse(await File.ReadAllTextAsync(irPath));
				var layer = SkeletonComparer.LayerAgreement(mjsSkeleton, mermaiderSkeleton);
				var order = SkeletonComparer.WithinLayerOrderAgreement(mjsSkeleton, mermaiderSkeleton);
				var mirror = SkeletonComparer.MirrorToleratedOrderAgreement(mjsSkeleton, mermaiderSkeleton);
				var sides = SkeletonComparer.SideAgreement(mjsSkeleton, mermaiderSkeleton);
				var routes = SkeletonComparer.RouteShapeAgreement(mjsSkeleton, mermaiderSkeleton);
				var ports = SkeletonComparer.PortOrderAgreement(mjsSkeleton, mermaiderSkeleton);
				rows.Add((slug, layer, order, mirror, sides, routes, ports, inversions, null));
			}
			catch (Exception ex)
			{
				rows.Add((slug, -1, -1, -1, -1, -1, -1, inversions, ex.Message));
			}
		}

		var sb = new StringBuilder();
		sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
			$"# ER layout skeleton agreement vs mermaid.js — {rows.Count} diagrams");
		sb.AppendLine();
		sb.AppendLine("| Slug | Layer | Order | Order (mirror-tol.) | Sides | Routes | Ports | Own inversions |");
		sb.AppendLine("|---|---|---|---|---|---|---|---|");
		foreach (var r in rows.OrderBy(r => r.Slug, StringComparer.Ordinal))
		{
			var cell = r.Error is null
				? FormattableString.Invariant($"| {r.Slug} | {r.Layer:P0} | {r.Order:P0} | {r.Mirror:P0} | {r.Sides:P0} | {r.Routes:P0} | {r.Ports:P0} | {r.Inversions} |")
				: $"| {r.Slug} | error: {r.Error} | — | — | — | — | — | {r.Inversions} |";
			sb.AppendLine(cell);
		}

		if (Directory.Exists(SvgReferenceDir))
		{
			var reportPath = Path.Combine(SvgReferenceDir, "skeleton-comparison-report.md");
			await File.WriteAllTextAsync(reportPath, sb.ToString());
			Console.WriteLine($"Skeleton comparison report written to {reportPath}");
		}
		else
		{
			Console.WriteLine(sb.ToString());
		}
	}
}
