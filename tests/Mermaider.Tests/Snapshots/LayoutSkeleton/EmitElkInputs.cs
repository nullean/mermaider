using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Mermaider.Examples;
using Mermaider.Layout;
using Mermaider.Parsing;
using Mermaider.Rendering;
using Mermaider.Text;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

// Emits ELK graph inputs (our entity sizes, edges in model order) for scripts/elk-oracle.mjs.
//   ELK_EMIT=1 dotnet run --project tests/Mermaider.Tests -- --treenode-filter "/*/*/EmitElkInputs/*"
public class EmitElkInputs
{
	private static string Dir([CallerFilePath] string path = "") =>
		Path.Combine(Path.GetDirectoryName(path)!, "elk");

	public static IEnumerable<string> ErSlugs() =>
		DiagramExamples.All.Where(d => d.Category == DiagramCategory.Er).Select(d => d.Slug);

	[Test]
	[MethodDataSource(nameof(ErSlugs))]
	public async Task Emit(string slug)
	{
		if (Environment.GetEnvironmentVariable("ELK_EMIT") != "1")
			return;
		var example = DiagramExamples.All.First(d => d.Slug == slug);
		var (cleaned, _) = DiagramPreprocessor.Process(example.Source);
		var diagram = ErParser.Parse(MermaidRenderer.PreprocessLines(cleaned));
		var positioned = LightweightErLayoutEngine.Layout(diagram);

		var nodes = positioned.Entities.Select(e => new { id = e.Id, width = e.Width, height = e.Height }).ToList();
		var edges = new List<object>();
		for (var i = 0; i < diagram.Relationships.Count; i++)
		{
			var r = diagram.Relationships[i];
			if (r.Entity1 == r.Entity2)
				continue;
			double lw = 0, lh = 0;
			if (r.Label.Length > 0)
			{
				var m = TextMetrics.MeasureMultiline(r.Label.AsSpan(), RenderConstants.FontSizes.EdgeLabel, RenderConstants.FontWeights.EdgeLabel);
				lw = m.Width + 8;
				lh = m.Height + 6;
			}
			edges.Add(new { id = "e" + i.ToString(CultureInfo.InvariantCulture), source = r.Entity1, target = r.Entity2, labelWidth = lw, labelHeight = lh });
		}

		Directory.CreateDirectory(Dir());
		await File.WriteAllTextAsync(Path.Combine(Dir(), slug + ".ours.json"), JsonSerializer.Serialize(new
		{
			nodes = positioned.Entities.Select(e => new { id = e.Id, x = e.X, y = e.Y, w = e.Width, h = e.Height }),
			edges = positioned.Relationships.Select(r => new { source = r.Entity1, target = r.Entity2, label = r.Label, points = r.Points.Select(p => new[] { p.X, p.Y }) }),
		}, new JsonSerializerOptions { WriteIndented = false }));
		await File.WriteAllTextAsync(Path.Combine(Dir(), slug + ".elk.input.json"),
			JsonSerializer.Serialize(new { slug, nodes, edges }, new JsonSerializerOptions { WriteIndented = true }));
	}
}
