using System.Runtime.CompilerServices;
using System.Text;
using AwesomeAssertions;
using Mermaider.Examples;
using Mermaider.Layout;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

// Compares Mermaider's own state-diagram layout skeleton against mermaid.js's. Mirrors
// SkeletonComparisonTests (ER) / FlowchartSkeletonComparisonTests. Needs local mermaid.js
// reference SVGs; skips gracefully when absent. Start/end pseudostates are excluded from the
// comparison entirely — see MermaidJsStateSkeletonExtractor's header for why their ids can't be
// matched string-for-string between Mermaider and mermaid.js.
public class StateSkeletonComparisonTests
{
	private static readonly IReadOnlyDictionary<string, (double MinLayerAgreement, double MinWithinLayerOrderAgreement)> Baselines =
		new Dictionary<string, (double, double)>(StringComparer.Ordinal)
		{
			// Positive/Neutral/Negative are three siblings under the `checkResult` choice
			// pseudostate, each flowing to its own distinct terminal with no other edge
			// constraining their relative order — an unconstrained sibling tie-break (same
			// class of disagreement as ER's er-complex COMMENT/TAG), not a placement defect.
			["state-choice"] = (0.95, 0.60),
		};

	private static string GetSourceDirectory([CallerFilePath] string path = "") =>
		Path.GetDirectoryName(path)!;

	private static readonly string ReferenceDir =
		Path.Combine(GetSourceDirectory(), "..", "Reference", "mermaidjs");

	public static IEnumerable<string> StateSlugsWithReferences()
	{
		if (!Directory.Exists(ReferenceDir))
			return [];
		var slugs = DiagramExamples.All
			.Where(d => d.Category == DiagramCategory.State)
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
		var graph = StateParser.Parse(lines);
		return (graph, LightweightLayoutEngine.Layout(graph));
	}

	[Test]
	[MethodDataSource(nameof(StateSlugsWithReferences))]
	public async Task Layout_matches_mermaidjs_ordering(string slug)
	{
		var example = DiagramExamples.All.Single(e => e.Slug == slug);
		var (graph, positioned) = BuildMermaider(example);
		var mermaiderSkeleton = MermaiderGraphSkeletonExtractor.Extract(graph, positioned);

		var svg = await File.ReadAllTextAsync(Path.Combine(ReferenceDir, $"{slug}.svg"));
		var mjsSkeleton = MermaidJsStateSkeletonExtractor.Extract(svg, example.Source);

		var layerAgreement = SkeletonComparer.LayerAgreement(mjsSkeleton, mermaiderSkeleton);
		var orderAgreement = SkeletonComparer.WithinLayerOrderAgreement(mjsSkeleton, mermaiderSkeleton);

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
		foreach (var slug in StateSlugsWithReferences())
		{
			var example = DiagramExamples.All.Single(e => e.Slug == slug);
			var (graph, positioned) = BuildMermaider(example);
			var mermaiderSkeleton = MermaiderGraphSkeletonExtractor.Extract(graph, positioned);
			var inversions = SkeletonComparer.TopologicalInversions(mermaiderSkeleton);

			try
			{
				var svg = await File.ReadAllTextAsync(Path.Combine(ReferenceDir, $"{slug}.svg"));
				var mjsSkeleton = MermaidJsStateSkeletonExtractor.Extract(svg, example.Source);
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
		sb.AppendLine($"# State layout skeleton agreement vs mermaid.js — {rows.Count} diagrams");
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

		var reportPath = Path.Combine(ReferenceDir, "skeleton-comparison-report-state.md");
		await File.WriteAllTextAsync(reportPath, sb.ToString());
		Console.WriteLine($"State skeleton comparison report written to {reportPath}");
	}
}
