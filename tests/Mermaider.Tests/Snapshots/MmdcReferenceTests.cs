using System.Runtime.CompilerServices;
using Mermaider.Examples;
using Mermaider.Layout;
using Mermaider.Tests.Snapshots.LayoutSkeleton;
using SkeletonIr = Mermaider.Tests.Snapshots.LayoutSkeleton.LayoutSkeleton;

namespace Mermaider.Tests.Snapshots;

// Run this test once to (re)generate mermaid.js reference SVGs for all gallery examples,
// and simultaneously emit committed `.mjs.ir.txt` files for the skeleton comparison tests.
//
//   dotnet run --project tests/Mermaider.Tests -- --treenode-filter "*MmdcReferenceTests*"
//
// Not run in CI (requires local mmdc install, slow due to Puppeteer).
//
// Reference SVGs land in Tests/Snapshots/Reference/mermaidjs/{slug}.svg (gitignored).
// IR files land in Tests/Snapshots/LayoutSkeleton/Reference/{slug}.mjs.ir.txt (committed).
// The skeleton comparison tests read the IR files; they don't need the SVGs, so they run in CI.
[Skip("Run manually to refresh mermaid.js reference SVGs and committed skeleton IR files")]
public class MmdcReferenceTests
{
	private static string GetSourceDirectory([CallerFilePath] string path = "") =>
		Path.GetDirectoryName(path)!;

	private static readonly string ReferenceDir =
		Path.Combine(GetSourceDirectory(), "Reference", "mermaidjs");

	// Committed IR files for skeleton tests — does NOT contain the gitignored SVGs.
	private static readonly string IrReferenceDir =
		Path.Combine(GetSourceDirectory(), "LayoutSkeleton", "Reference");

	[Test]
	[MethodDataSource(nameof(AllExamples))]
	public async Task Generate_reference(DiagramExample example)
	{
		Directory.CreateDirectory(ReferenceDir);
		Directory.CreateDirectory(IrReferenceDir);

		var svg = await MmdcRenderer.RenderAsync(example.Source);
		await File.WriteAllTextAsync(Path.Combine(ReferenceDir, $"{example.Slug}.svg"), svg);

		// Also extract and commit the skeleton IR so comparison tests run without SVG or node.
		var irText = TryExtractIr(example, svg);
		if (irText is not null)
			await File.WriteAllTextAsync(Path.Combine(IrReferenceDir, $"{example.Slug}.mjs.ir.txt"), irText);
	}

	public static IEnumerable<DiagramExample> AllExamples() =>
		DiagramExamples.All;

	// Returns null (silently, not a test failure) when the diagram category has no mjs extractor.
	// A failure inside an extractor is re-thrown so it surfaces as a red test, not a missing file.
	private static string? TryExtractIr(DiagramExample example, string svg)
	{
		SkeletonIr? skeleton = example.Category switch
		{
			DiagramCategory.Er => MermaidJsSkeletonExtractor.Extract(svg, example.Source),
			DiagramCategory.Flowchart => MermaidJsFlowchartSkeletonExtractor.Extract(svg, example.Source),
			DiagramCategory.State => MermaidJsStateSkeletonExtractor.Extract(svg, example.Source),
			_ => null,
		};

		return skeleton?.Serialize();
	}
}
