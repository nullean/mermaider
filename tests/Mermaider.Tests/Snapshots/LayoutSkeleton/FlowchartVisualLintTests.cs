using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Mermaider.Examples;
using Mermaider.Layout;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

/// <summary>
/// Ratcheted visual lint for every flowchart / state example (see <see cref="VisualLint"/>). The baseline file lists the
/// violations that exist today; a check may never get worse, and the baseline only ever shrinks. Run with
/// <c>VISUAL_LINT_OUT=&lt;dir&gt;</c> to write a fresh <c>visual-lint-baseline.txt</c> and a detailed <c>visual-lint-report.md</c>.
/// </summary>
public class FlowchartVisualLintTests
{
	private static string BaselinePath([CallerFilePath] string here = "") =>
		Path.Combine(Path.GetDirectoryName(here)!, "visual-lint-baseline.txt");

	public static IEnumerable<string> Slugs() =>
		DiagramExamples.All
			.Where(d => d.Source.TrimStart().StartsWith("flowchart", StringComparison.Ordinal)
				|| d.Source.TrimStart().StartsWith("graph", StringComparison.Ordinal)
				|| d.Source.TrimStart().StartsWith("stateDiagram", StringComparison.Ordinal))
			.Select(d => d.Slug);

	internal static (MermaidGraph Graph, PositionedGraph Positioned) Layout(string slug)
	{
		var example = DiagramExamples.All.Single(e => e.Slug == slug);
		var (cleaned, _) = DiagramPreprocessor.Process(example.Source);
		var lines = MermaidRenderer.PreprocessLines(cleaned);
		var graph = example.Source.TrimStart().StartsWith("stateDiagram", StringComparison.Ordinal)
			? StateParser.Parse(lines)
			: FlowchartParser.Parse(lines);
		return (graph, LightweightLayoutEngine.Layout(graph));
	}

	private static Dictionary<(string Slug, string Check), int> LoadBaseline()
	{
		var map = new Dictionary<(string, string), int>();
		var path = BaselinePath();
		if (!File.Exists(path))
			return map;
		foreach (var line in File.ReadAllLines(path))
		{
			var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == 3 && int.TryParse(parts[2], out var n))
				map[(parts[0], parts[1])] = n;
		}

		return map;
	}

	[Test]
	[MethodDataSource(nameof(Slugs))]
	public void Visual_lint_does_not_regress(string slug)
	{
		var (graph, positioned) = Layout(slug);
		var violations = VisualLint.Run(positioned, graph.Direction);
		var baseline = LoadBaseline();
		foreach (var check in VisualLint.AllChecks)
		{
			var found = violations.Where(v => v.Check == check).ToList();
			var allowed = baseline.GetValueOrDefault((slug, check), 0);
			found.Count.Should().BeLessThanOrEqualTo(
				allowed,
				$"'{slug}' {check}: {string.Join("; ", found.Select(f => f.Detail))}");
		}
	}

	[Test]
	public void Write_baseline_and_report()
	{
		var outDir = Environment.GetEnvironmentVariable("VISUAL_LINT_OUT");
		if (string.IsNullOrEmpty(outDir))
			return;

		Directory.CreateDirectory(outDir);
		var baseline = new List<string>();
		var report = new List<string> { "# Visual lint report", string.Empty };
		var totals = VisualLint.AllChecks.ToDictionary(c => c, _ => 0);
		foreach (var slug in Slugs())
		{
			var (graph, positioned) = Layout(slug);
			var violations = VisualLint.Run(positioned, graph.Direction);
			foreach (var check in VisualLint.AllChecks)
			{
				var n = violations.Count(v => v.Check == check);
				totals[check] += n;
				if (n > 0)
					baseline.Add($"{slug} {check} {n}");
			}

			if (violations.Count > 0)
			{
				report.Add($"## {slug} ({violations.Count})");
				report.AddRange(violations.Select(v => $"- **{v.Check}**: {v.Detail}"));
				report.Add(string.Empty);
			}
		}

		report.Insert(2, "Totals: " + string.Join(", ", totals.Select(t => $"{t.Key}={t.Value}")) + "\n");
		File.WriteAllLines(Path.Combine(outDir, "visual-lint-baseline.txt"), baseline);
		File.WriteAllLines(Path.Combine(outDir, "visual-lint-report.md"), report);
	}
}
