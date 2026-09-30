using System.Runtime.CompilerServices;
using Mermaider.Examples;

namespace Mermaider.Tests.Snapshots;

// Run this test once to (re)generate mermaid.js reference SVGs for all gallery examples.
// Reference SVGs land in Tests/Snapshots/Reference/mermaidjs/{slug}.svg alongside the
// Mermaider snapshots so they can be diffed as plain text.
//
//   dotnet run --project tests/Mermaider.Tests -- --treenode-filter "*MmdcReferenceTests*"
//
// Not run in CI (requires local mmdc install, slow due to Puppeteer).
[Skip("Run manually to refresh mermaid.js reference SVGs")]
public class MmdcReferenceTests
{
	private static string GetSourceDirectory([CallerFilePath] string path = "") =>
		Path.GetDirectoryName(path)!;

	private static readonly string ReferenceDir =
		Path.Combine(GetSourceDirectory(), "Reference", "mermaidjs");

	[Test]
	[MethodDataSource(nameof(AllExamples))]
	public async Task Generate_reference(DiagramExample example)
	{
		Directory.CreateDirectory(ReferenceDir);
		var svg = await MmdcRenderer.RenderAsync(example.Source);
		await File.WriteAllTextAsync(Path.Combine(ReferenceDir, $"{example.Slug}.svg"), svg);
	}

	public static IEnumerable<DiagramExample> AllExamples() =>
		DiagramExamples.All;
}
