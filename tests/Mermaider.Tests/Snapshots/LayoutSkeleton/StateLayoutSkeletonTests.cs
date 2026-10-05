using AwesomeAssertions;
using Mermaider.Examples;
using Mermaider.Layout;
using Mermaider.Models;
using Mermaider.Parsing;
using VerifyTUnit;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

// Always-on layout-skeleton tests for Mermaider's own state-diagram layout — no local mmdc
// install required. Mirrors LayoutSkeletonTests (ER) / FlowchartLayoutSkeletonTests.
public class StateLayoutSkeletonTests
{
	public static IEnumerable<string> StateSlugs() =>
		DiagramExamples.All
			.Where(d => d.Category == DiagramCategory.State)
			.Select(d => d.Slug);

	private static MermaidGraph ParseState(string source)
	{
		var (cleaned, _) = DiagramPreprocessor.Process(source);
		var lines = MermaidRenderer.PreprocessLines(cleaned);
		return StateParser.Parse(lines);
	}

	[Test]
	[MethodDataSource(nameof(StateSlugs))]
	public void Own_layout_has_no_topological_inversions(string slug)
	{
		var example = DiagramExamples.All.Single(e => e.Slug == slug);
		var graph = ParseState(example.Source);
		var positioned = LightweightLayoutEngine.Layout(graph);
		var skeleton = MermaiderGraphSkeletonExtractor.Extract(graph, positioned);

		SkeletonComparer.TopologicalInversions(skeleton)
			.Should().Be(0, $"'{slug}' layering should route every edge layer-to-layer without going backwards");
	}

	[Test]
	[MethodDataSource(nameof(StateSlugs))]
	public Task Skeleton_snapshot(string slug)
	{
		var example = DiagramExamples.All.Single(e => e.Slug == slug);
		var graph = ParseState(example.Source);
		var positioned = LightweightLayoutEngine.Layout(graph);
		var skeleton = MermaiderGraphSkeletonExtractor.Extract(graph, positioned);

		return Verifier.Verify(skeleton.ToAsciiArt(), "txt").UseParameters(slug);
	}
}
