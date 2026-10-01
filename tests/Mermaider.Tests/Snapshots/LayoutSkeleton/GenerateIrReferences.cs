using System.Runtime.CompilerServices;
using Mermaider.Examples;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

// One-time helper: generates committed .mjs.ir.txt skeleton reference files from the already-
// present gitignored mermaid.js SVGs. Normally MmdcReferenceTests.Generate_reference emits both
// at once; this class exists for the case where the SVGs were already generated but the IR
// files don't exist yet (e.g. the first time after IR serialization landed, or after regenerating
// SVGs without a matching IR step).
//
//   dotnet run --project tests/Mermaider.Tests -- --treenode-filter "*GenerateIrReferences*"
//
// Not run in CI (requires the gitignored SVG directory to be present).
[Skip("Run manually when mjs reference SVGs exist but IR files are missing or stale")]
public class GenerateIrReferences
{
	private static string GetSourceDirectory([CallerFilePath] string path = "") =>
		Path.GetDirectoryName(path)!;

	private static readonly string SvgReferenceDir =
		Path.Combine(GetSourceDirectory(), "..", "Reference", "mermaidjs");

	private static readonly string IrReferenceDir =
		Path.Combine(GetSourceDirectory(), "Reference");

	[Test]
	[MethodDataSource(nameof(CategoriesWithExtractor))]
	public async Task Generate_ir_from_existing_svgs(DiagramCategory category)
	{
		if (!Directory.Exists(SvgReferenceDir))
			throw new InvalidOperationException(
				$"mermaid.js SVG reference directory not found at {SvgReferenceDir}. " +
				"Run MmdcReferenceTests.Generate_reference first.");

		Directory.CreateDirectory(IrReferenceDir);

		var examples = DiagramExamples.All
			.Where(e => e.Category == category)
			.ToDictionary(e => e.Slug, e => e, StringComparer.Ordinal);

		var svgDir = new DirectoryInfo(SvgReferenceDir);
		var written = 0;
		var errors = new List<string>();

		foreach (var svgFile in svgDir.GetFiles("*.svg").OrderBy(f => f.Name))
		{
			var slug = Path.GetFileNameWithoutExtension(svgFile.Name);
			if (!examples.TryGetValue(slug, out var example))
				continue;

			var irPath = Path.Combine(IrReferenceDir, $"{slug}.mjs.ir.txt");
			var svg = await File.ReadAllTextAsync(svgFile.FullName);
			try
			{
				LayoutSkeleton skeleton = category switch
				{
					DiagramCategory.Er => MermaidJsSkeletonExtractor.Extract(svg, example.Source),
					DiagramCategory.Flowchart => MermaidJsFlowchartSkeletonExtractor.Extract(svg, example.Source),
					DiagramCategory.State => MermaidJsStateSkeletonExtractor.Extract(svg, example.Source),
					_ => throw new InvalidOperationException($"No extractor for {category}"),
				};
				await File.WriteAllTextAsync(irPath, skeleton.Serialize());
				written++;
				Console.WriteLine($"  wrote {slug}.mjs.ir.txt");
			}
			catch (Exception ex)
			{
				errors.Add($"{slug}: {ex.Message}");
				Console.WriteLine($"  ERROR {slug}: {ex.Message}");
			}
		}

		Console.WriteLine($"\n{category}: wrote {written} IR file(s), {errors.Count} error(s).");
		if (errors.Count > 0)
			throw new InvalidOperationException($"{errors.Count} extraction error(s):\n" + string.Join("\n", errors));
	}

	public static IEnumerable<DiagramCategory> CategoriesWithExtractor() =>
		[DiagramCategory.Er, DiagramCategory.Flowchart, DiagramCategory.State];
}
