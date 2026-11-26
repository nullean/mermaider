using AwesomeAssertions;
using Mermaider.Examples;

namespace Mermaider.Tests.Parsing;

/// <summary>A diagram may open with comments and blank lines above its header, as mermaid.js allows.</summary>
public class DiagramDetectorTests
{
	public static IEnumerable<Func<DiagramExample>> Examples() =>
		DiagramExamples
			.All
			.Where(e => !e.Source.TrimStart().StartsWith("---", StringComparison.Ordinal) && !e.Source.TrimStart().StartsWith("%%", StringComparison.Ordinal))
			.GroupBy(e => e.Category)
			.Select(g => g.First())
			.Select(e => new Func<DiagramExample>(() => e));

	[Test]
	[MethodDataSource(nameof(Examples))]
	public void A_comment_above_the_header_renders_the_same_diagram(DiagramExample example)
	{
		var commented = "%% what this diagram shows\n\n  %% and a second comment\n" + example.Source;
		MermaidRenderer.RenderSvg(commented).Should().Be(MermaidRenderer.RenderSvg(example.Source));
	}
}
