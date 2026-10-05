using AwesomeAssertions;
using Mermaider.Examples;
using Mermaider.Layout;
using Mermaider.Models;
using Mermaider.Parsing;
using VerifyTUnit;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

// Always-on layout-skeleton tests for Mermaider's own ER layout — no local mmdc install
// required (contrast with the mermaid.js-comparison tests, which need local references; see
// MermaidJsSkeletonExtractor / MjsComparisonTests.cs).
//
// 1. Own_layout_has_no_topological_inversions: every ER diagram's own layering should route
//    edges layer-to-layer without going backwards — a topological inversion means the
//    layering itself contradicts the diagram's own edges, independent of anything mermaid.js
//    does.
// 2. Skeleton_snapshot: Verify-snapshots the ToAsciiArt() dump of Mermaider's own layout
//    skeleton for every ER example, so any change to layering/ordering shows up as a readable
//    text diff in review instead of only a pixel diff in a .verified.svg.
public class LayoutSkeletonTests
{
	public static IEnumerable<string> ErSlugs() =>
		DiagramExamples.All
			.Where(d => d.Category == DiagramCategory.Er)
			.Select(d => d.Slug);

	private static ErDiagram ParseEr(string source)
	{
		var (cleaned, _) = DiagramPreprocessor.Process(source);
		var lines = MermaidRenderer.PreprocessLines(cleaned);
		return ErParser.Parse(lines);
	}

	[Test]
	[MethodDataSource(nameof(ErSlugs))]
	public void Own_layout_has_no_topological_inversions(string slug)
	{
		var example = DiagramExamples.All.Single(e => e.Slug == slug);
		var positioned = LightweightErLayoutEngine.Layout(ParseEr(example.Source));
		var skeleton = MermaiderSkeletonExtractor.Extract(positioned);

		SkeletonComparer.TopologicalInversions(skeleton)
			.Should().Be(0, $"'{slug}' layering should route every edge layer-to-layer without going backwards");
	}

	[Test]
	[MethodDataSource(nameof(ErSlugs))]
	public Task Skeleton_snapshot(string slug)
	{
		var example = DiagramExamples.All.Single(e => e.Slug == slug);
		var positioned = LightweightErLayoutEngine.Layout(ParseEr(example.Source));
		var skeleton = MermaiderSkeletonExtractor.Extract(positioned);

		return Verifier.Verify(skeleton.ToAsciiArt(), "txt").UseParameters(slug);
	}
}
