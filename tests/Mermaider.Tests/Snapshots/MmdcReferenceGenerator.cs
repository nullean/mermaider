using System.Runtime.CompilerServices;
using Mermaider.Examples;

namespace Mermaider.Tests.Snapshots;

// Standalone helper: run this to regenerate mermaid.js reference SVGs.
// Usage from repo root:
//   dotnet run --project tests/Mermaider.Tests -- --generate-mermaidjs-refs
// Output lands in tests/Mermaider.Tests/Snapshots/Reference/mermaidjs/{slug}.svg
public static class MmdcReferenceGenerator
{
	private static string GetSourceDirectory([CallerFilePath] string path = "") =>
		Path.GetDirectoryName(path)!;

	public static async Task RunAsync(string[] slugFilter, CancellationToken ct = default)
	{
		var outDir = Path.Combine(GetSourceDirectory(), "Reference", "mermaidjs");
		Directory.CreateDirectory(outDir);

		var examples = DiagramExamples.All
			.Where(e => slugFilter.Length == 0 ||
				slugFilter.Any(f => e.Slug.Contains(f, StringComparison.OrdinalIgnoreCase) ||
					e.Category.ToString().Contains(f, StringComparison.OrdinalIgnoreCase)));

		foreach (var ex in examples)
		{
			ct.ThrowIfCancellationRequested();
			var outFile = Path.Combine(outDir, $"{ex.Slug}.svg");
			try
			{
				var svg = await MmdcRenderer.RenderAsync(ex.Source, ct);
				await File.WriteAllTextAsync(outFile, svg, ct);
				Console.WriteLine($"  ok  {ex.Slug}");
			}
			catch (Exception e)
			{
				Console.WriteLine($"  err {ex.Slug}: {e.Message}");
			}
		}
	}
}
