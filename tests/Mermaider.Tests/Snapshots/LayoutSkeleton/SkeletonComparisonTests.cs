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
// consistent even where it disagrees with mermaid.js — the divergence traces to two structural,
// non-bug differences between dagre and our Sugiyama implementation:
//   (a) disconnected-component packing — dagre tucks a disconnected subgraph's layers in
//       side-by-side with the main component's existing layer range; Sugiyama stacks it as new
//       layers underneath (e.g. db-erd-09-content: mermaid.js packs CrossLink/IncludeDirective
//       and CrossLinkUri/Snippet into layers 0-1 alongside the main cluster; Mermaider gives them
//       their own layers 4-5 below everything).
//   (b) unconstrained sibling order — when two entities share a parent with no other edge fixing
//       their relative order (e.g. er-complex's COMMENT/TAG, both children of POST), dagre and
//       Sugiyama can tie-break oppositely; nothing in the diagram prefers one order over the
//       other.
// Neither is "more correct" — they're different, both-valid answers to an underspecified layout
// question. Per-slug floors document today's actual divergence instead of pretending it doesn't
// exist.
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
			["db-erd-02-shared-vocab"] = (0.95, 0.65),
			["db-erd-05-building-blocks"] = (0.90, 0.25),
			["db-erd-06-catalog"] = (0.95, 0.95),
			["db-erd-07-source"] = (0.95, 0.35),
			["db-erd-08-docset"] = (0.95, 0.95),
			["db-erd-09-content"] = (0.50, 0.55), // disconnected-component packing differs — see class remarks
			["db-erd-10-navigation"] = (0.95, 0.95),
			["db-erd-11-link-graph"] = (0.95, 0.95),
			["db-erd-12-assembly"] = (0.95, 0.95),
			["db-erd-13-codex"] = (0.95, 0.95),
			["db-erd-14-release-notes"] = (0.95, 0.95),
			["db-erd-15-api-search"] = (0.95, 0.88),
			["db-erd-16-relationships-overview"] = (0.95, 0.70),
			["db-erd-17-catalog"] = (0.95, 0.95),
			["db-erd-18-source"] = (0.95, 0.15),
			["db-erd-19-docset"] = (0.65, 0.45),
			["db-erd-20-content"] = (0.50, 0.25), // disconnected-component packing differs — see class remarks
			["db-erd-21-navigation"] = (0.75, 0.40),
			["db-erd-22-link-graph"] = (0.95, 0.95),
			["db-erd-23-publishing"] = (0.95, 0.25),
			["db-erd-24-codex"] = (0.95, 0.70),
			["db-erd-25-release-notes"] = (0.78, 0.60),
			["db-erd-26-api-reference"] = (0.85, 0.25),
			["db-erd-27-search"] = (0.95, 0.95),
			["er-aliases"] = (0.95, 0.95),
			["er-basic"] = (0.95, 0.95),
			["er-complex"] = (0.95, 0.0), // unconstrained sibling tie-break (COMMENT/TAG) — see class remarks
			["er-direction"] = (0.95, 0.95),
		};

	private static string GetSourceDirectory([CallerFilePath] string path = "") =>
		Path.GetDirectoryName(path)!;

	private static readonly string ReferenceDir =
		Path.Combine(GetSourceDirectory(), "..", "Reference", "mermaidjs");

	public static IEnumerable<string> ErSlugsWithReferences()
	{
		if (!Directory.Exists(ReferenceDir))
			return [];
		var erSlugs = DiagramExamples.All
			.Where(d => d.Category == DiagramCategory.Er)
			.Select(d => d.Slug)
			.ToHashSet(StringComparer.Ordinal);
		return Directory.EnumerateFiles(ReferenceDir, "*.svg")
			.Select(Path.GetFileNameWithoutExtension)
			.Where(slug => slug is not null && erSlugs.Contains(slug))
			.Select(slug => slug!)
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

		var svg = await File.ReadAllTextAsync(Path.Combine(ReferenceDir, $"{slug}.svg"));
		var mjsSkeleton = MermaidJsSkeletonExtractor.Extract(svg, example.Source);

		var layerAgreement = SkeletonComparer.LayerAgreement(mjsSkeleton, mermaiderSkeleton);
		var orderAgreement = SkeletonComparer.WithinLayerOrderAgreement(mjsSkeleton, mermaiderSkeleton);

		Baselines.TryGetValue(slug, out var baseline).Should().BeTrue(
			$"'{slug}' has no entry in {nameof(Baselines)} — run Calibration_report(), inspect its score " +
			"(layer={layerAgreement:P0}, order={orderAgreement:P0}), and add a floored entry for it");

		layerAgreement.Should().BeGreaterThanOrEqualTo(baseline.MinLayerAgreement,
			$"'{slug}' entity layer placement regressed vs its calibrated baseline (got {layerAgreement:P0})");
		orderAgreement.Should().BeGreaterThanOrEqualTo(baseline.MinWithinLayerOrderAgreement,
			$"'{slug}' within-layer ordering regressed vs its calibrated baseline (got {orderAgreement:P0})");
	}

	// Writes a Markdown quality report to Tests/Snapshots/Reference/mermaidjs/skeleton-comparison-report.md
	// (gitignored, same directory as the reference SVGs). Run the suite normally — this test
	// auto-updates the file whenever reference SVGs exist, giving a persistent quality snapshot
	// after each run, mirroring MjsComparisonTests.Summary().
	[Test]
	public async Task Calibration_report()
	{
		if (!Directory.Exists(ReferenceDir))
			return; // no references yet — run MmdcReferenceTests first

		var rows = new List<(string Slug, double LayerAgreement, double OrderAgreement, int Inversions, string? Error)>();
		foreach (var slug in ErSlugsWithReferences())
		{
			var example = DiagramExamples.All.Single(e => e.Slug == slug);
			var mermaiderSkeleton = BuildMermaiderSkeleton(example);
			var inversions = SkeletonComparer.TopologicalInversions(mermaiderSkeleton);

			try
			{
				var svg = await File.ReadAllTextAsync(Path.Combine(ReferenceDir, $"{slug}.svg"));
				var mjsSkeleton = MermaidJsSkeletonExtractor.Extract(svg, example.Source);
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
		sb.AppendLine($"# ER layout skeleton agreement vs mermaid.js — {rows.Count} diagrams");
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

		var reportPath = Path.Combine(ReferenceDir, "skeleton-comparison-report.md");
		await File.WriteAllTextAsync(reportPath, sb.ToString());
		Console.WriteLine($"Skeleton comparison report written to {reportPath}");
	}
}
