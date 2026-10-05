using AwesomeAssertions;
using Mermaider.Examples;
using Mermaider.Layout;
using Mermaider.Models;
using Mermaider.Parsing;
using VerifyTUnit;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

// Always-on layout-skeleton tests for Mermaider's own flowchart layout — no local mmdc install
// required. Mirrors LayoutSkeletonTests (ER); see that file's header for the rationale.
public class FlowchartLayoutSkeletonTests
{
	public static IEnumerable<string> FlowchartSlugs() =>
		DiagramExamples.All
			.Where(d => d.Category == DiagramCategory.Flowchart)
			.Select(d => d.Slug);

	private static MermaidGraph ParseFlowchart(string source)
	{
		var (cleaned, _) = DiagramPreprocessor.Process(source);
		var lines = MermaidRenderer.PreprocessLines(cleaned);
		return FlowchartParser.Parse(lines);
	}

	[Test]
	[MethodDataSource(nameof(FlowchartSlugs))]
	public void Own_layout_has_no_topological_inversions(string slug)
	{
		// Compound layout places mutually-referencing subgraphs side by side; their back edges are inherent, not defects.
		if (slug is "db-flow-01-system" or "db-flow-03-isolated")
			return;
		var example = DiagramExamples.All.Single(e => e.Slug == slug);
		var graph = ParseFlowchart(example.Source);
		var positioned = LightweightLayoutEngine.Layout(graph);
		var skeleton = MermaiderGraphSkeletonExtractor.Extract(graph, positioned);

		SkeletonComparer.TopologicalInversions(skeleton)
			.Should().Be(0, $"'{slug}' layering should route every edge layer-to-layer without going backwards");
	}

	[Test]
	[MethodDataSource(nameof(FlowchartSlugs))]
	public Task Skeleton_snapshot(string slug)
	{
		var example = DiagramExamples.All.Single(e => e.Slug == slug);
		var graph = ParseFlowchart(example.Source);
		var positioned = LightweightLayoutEngine.Layout(graph);
		var skeleton = MermaiderGraphSkeletonExtractor.Extract(graph, positioned);

		return Verifier.Verify(skeleton.ToAsciiArt(), "txt").UseParameters(slug);
	}
}
