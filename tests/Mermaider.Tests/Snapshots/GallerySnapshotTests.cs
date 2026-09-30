using Mermaider.Examples;
using VerifyTUnit;

namespace Mermaider.Tests.Snapshots;

// Snapshot tests for every gallery example.
// After any rendering change, run tests and diff .received.svg files against
// .verified.svg to review what changed as plain text — no browser needed.
//
// For mermaid.js comparison: reference SVGs live in Tests/Reference/mermaidjs/{slug}.svg.
// Regenerate them any time with: dotnet run --project tests/Mermaider.Tests -- --generate-refs
// or call MmdcRenderer.RenderAsync(source) directly in an ad-hoc test.
public class GallerySnapshotTests
{
	[Test]
	[MethodDataSource(nameof(AllExamples))]
	public Task Gallery(DiagramExample example)
	{
		var svg = MermaidRenderer.RenderSvg(example.Source);
		return Verifier.Verify(svg, "svg").UseParameters(example.Slug);
	}

	public static IEnumerable<DiagramExample> AllExamples() =>
		DiagramExamples.All;
}
