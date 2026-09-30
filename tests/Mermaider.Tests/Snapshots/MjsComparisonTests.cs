using System.Text.RegularExpressions;
using Mermaider.Examples;

namespace Mermaider.Tests.Snapshots;

// Compares Mermaider output dimensions to mermaid.js reference SVGs.
#pragma warning disable SYSLIB1045
//
// Two ways to use this class:
//   1. Run the full suite normally — Summary() prints a quality table to test output whenever
//      reference SVGs exist at Tests/Snapshots/Reference/mermaidjs/{slug}.svg.
//   2. Generate references first (once, requires local mmdc):
//        dotnet run --project tests/Mermaider.Tests -- --treenode-filter "*MmdcReferenceTests*"
//      Then re-run normally to see the table.
public partial class MjsComparisonTests
{
	private const int TimeoutMs = 2000;
	private static readonly string ReferenceDir =
		Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Snapshots", "Reference", "mermaidjs");

	private static readonly string SnapshotDir =
		Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Snapshots");

	[Test]
	[Skip("Individual slug comparisons — run Summary() for the full table")]
	[MethodDataSource(nameof(SlugsWithReferences))]
	public void Compare(string slug)
	{
		var refPath = Path.Combine(ReferenceDir, $"{slug}.svg");
		var verPath = Path.Combine(SnapshotDir, $"GallerySnapshotTests.Gallery_example={slug}.verified.svg");

		if (!File.Exists(verPath))
		{
			Console.WriteLine($"| {slug} | (no verified snapshot) | ? | — | — |");
			return;
		}

		var mjs = ExtractViewBox(File.ReadAllText(refPath));
		var mer = ExtractViewBox(File.ReadAllText(verPath));

		if (mjs is null || mer is null)
		{
			Console.WriteLine($"| {slug} | could not parse viewBox | ? | — | — |");
			return;
		}

		var wDiff = (mer.Value.w - mjs.Value.w) / mjs.Value.w * 100;
		var hDiff = (mer.Value.h - mjs.Value.h) / mjs.Value.h * 100;
		var wIcon = Math.Abs(wDiff) <= 10 ? "✅" : Math.Abs(wDiff) <= 25 ? "⚠️" : "❌";
		var hIcon = Math.Abs(hDiff) <= 10 ? "✅" : Math.Abs(hDiff) <= 25 ? "⚠️" : "❌";

		Console.WriteLine(
			$"| {slug,-40} | {mer.Value.w,6:0}×{mer.Value.h,4:0} | {mjs.Value.w,6:0}×{mjs.Value.h,4:0} | {wIcon} {wDiff:+0.0;-0.0}% | {hIcon} {hDiff:+0.0;-0.0}% |");
	}

	// Writes a Markdown quality report to Tests/Snapshots/Reference/comparison-report.md
	// (gitignored). Run the suite normally — this test auto-updates the file whenever
	// reference SVGs exist, giving a persistent quality snapshot after each test run.
	[Test]
	public void Summary()
	{
		if (!Directory.Exists(ReferenceDir))
			return; // no references yet — run MmdcReferenceTests first

		var slugs = SlugsWithReferences().ToList();
		var rows = new List<(string slug, double wDiff, double hDiff, double merW, double merH, double mjsW, double mjsH)>();

		foreach (var slug in slugs)
		{
			var refPath = Path.Combine(ReferenceDir, $"{slug}.svg");
			var verPath = Path.Combine(SnapshotDir, $"GallerySnapshotTests.Gallery_example={slug}.verified.svg");
			if (!File.Exists(verPath)) continue;
			var mjs = ExtractViewBox(File.ReadAllText(refPath));
			var mer = ExtractViewBox(File.ReadAllText(verPath));
			if (mjs is null || mer is null) continue;
			var wDiff = (mer.Value.w - mjs.Value.w) / mjs.Value.w * 100;
			var hDiff = (mer.Value.h - mjs.Value.h) / mjs.Value.h * 100;
			rows.Add((slug, wDiff, hDiff, mer.Value.w, mer.Value.h, mjs.Value.w, mjs.Value.h));
		}

		var excellent = rows.Count(r => Math.Abs(r.wDiff) <= 10 && Math.Abs(r.hDiff) <= 10);
		var good = rows.Count(r => Math.Abs(r.wDiff) <= 25 && Math.Abs(r.hDiff) <= 25) - excellent;
		var poor = rows.Count - excellent - good;

		var sb = new System.Text.StringBuilder();
		sb.AppendLine($"# Mermaider vs mjs Layout Quality — {rows.Count} diagrams with references");
		sb.AppendLine($"> ✅ {excellent} within 10%  ⚠️ {good} within 25%  ❌ {poor} over 25%");
		sb.AppendLine();
		sb.AppendLine("| Slug | Mermaider W×H | mjs W×H | Width diff | Height diff |");
		sb.AppendLine("|---|---|---|---|---|");
		foreach (var (slug, wDiff, hDiff, merW, merH, mjsW, mjsH) in rows)
		{
			var wIcon = Math.Abs(wDiff) <= 10 ? "✅" : Math.Abs(wDiff) <= 25 ? "⚠️" : "❌";
			var hIcon = Math.Abs(hDiff) <= 10 ? "✅" : Math.Abs(hDiff) <= 25 ? "⚠️" : "❌";
			sb.AppendLine(
				$"| {slug,-40} | {merW,6:0}×{merH,4:0} | {mjsW,6:0}×{mjsH,4:0} | {wIcon} {wDiff:+0.0;-0.0}% | {hIcon} {hDiff:+0.0;-0.0}% |");
		}

		var reportPath = Path.Combine(ReferenceDir, "comparison-report.md");
		File.WriteAllText(reportPath, sb.ToString());
		Console.WriteLine($"Quality report written to {reportPath}");
		Console.WriteLine($"✅ {excellent}  ⚠️ {good}  ❌ {poor}  (of {rows.Count})");
	}

	public static IEnumerable<string> SlugsWithReferences()
	{
		if (!Directory.Exists(ReferenceDir))
			return [];
		return Directory.EnumerateFiles(ReferenceDir, "*.svg")
			.Select(f => Path.GetFileNameWithoutExtension(f))
			.Order();
	}

	[GeneratedRegex(@"viewBox=""[^""]*?\s([\d.]+)\s([\d.]+)""", RegexOptions.None, TimeoutMs)]
	private static partial Regex ViewBoxPattern();

	private static (double w, double h)? ExtractViewBox(string svg)
	{
		var m = ViewBoxPattern().Match(svg);
		if (!m.Success) return null;
		if (!double.TryParse(m.Groups[1].Value, out var w)) return null;
		if (!double.TryParse(m.Groups[2].Value, out var h)) return null;
		return (w, h);
	}
}
