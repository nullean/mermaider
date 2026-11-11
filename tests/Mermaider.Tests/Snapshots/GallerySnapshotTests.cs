using AwesomeAssertions;
using Mermaider.Examples;
using Nullean.Snapshots;

namespace Mermaider.Tests.Snapshots;

// Snapshot tests for every gallery example.
// After any rendering change, run tests and review what changed as plain text with `dotnet snap`
// (`dotnet snap diff <key>`, then accept or reject) — no browser needed. Each case is stored as
// .snapshots/Snapshots/GallerySnapshotTests/Gallery.<slug>.<argument hash>.verified.svg.
//
// For mermaid.js comparison: reference SVGs live in Tests/Reference/mermaidjs/{slug}.svg.
// Regenerate them any time with: dotnet run --project tests/Mermaider.Tests -- --generate-refs
// or call MmdcRenderer.RenderAsync(source) directly in an ad-hoc test.
public class GallerySnapshotTests
{
	[Test]
	[MethodDataSource(nameof(AllExamples))]
	public void Gallery(DiagramExample example)
	{
		var svg = MermaidRenderer.RenderSvg(example.Source);
		svg.Should().MatchSnapshot(SvgSnapshotFormat.Instance, example.Slug);
	}

	public static IEnumerable<DiagramExample> AllExamples() => DiagramExamples.All;
}
