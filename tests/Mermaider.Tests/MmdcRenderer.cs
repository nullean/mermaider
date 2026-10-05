namespace Mermaider.Tests;

// Renders a Mermaid diagram source to SVG using the local mermaid-cli install
// (node_modules/.bin/mmdc). Use this to generate mermaid.js reference SVGs for
// text-based comparison against Mermaider output without needing a browser.
internal static class MmdcRenderer
{
	// Locate ./node_modules/.bin/mmdc relative to the solution root (two levels up from test binary).
	private static string MmdcPath()
	{
		var dir = AppContext.BaseDirectory;
		for (var i = 0; i < 8; i++)
		{
			var candidate = Path.Combine(dir, "node_modules", ".bin", "mmdc");
			if (File.Exists(candidate))
				return candidate;
			var parent = Directory.GetParent(dir)?.FullName;
			if (parent is null)
				break;
			dir = parent;
		}
		throw new FileNotFoundException("mmdc not found. Run: npm install (in repo root)");
	}

	public static async Task<string> RenderAsync(string source, CancellationToken ct = default)
	{
		var mmdc = MmdcPath();
		var inputFile = Path.GetTempFileName() + ".mmd";
		var outputFile = Path.ChangeExtension(inputFile, ".svg");
		try
		{
			await File.WriteAllTextAsync(inputFile, source, ct);
			var psi = new System.Diagnostics.ProcessStartInfo
			{
				FileName = mmdc,
				Arguments = $"-i \"{inputFile}\" -o \"{outputFile}\" --backgroundColor transparent",
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
			};
			using var proc = System.Diagnostics.Process.Start(psi)
				?? throw new InvalidOperationException("Failed to start mmdc");
			await proc.WaitForExitAsync(ct);
			if (proc.ExitCode != 0)
			{
				var err = await proc.StandardError.ReadToEndAsync(ct);
				throw new InvalidOperationException($"mmdc exited {proc.ExitCode}: {err}");
			}
			return await File.ReadAllTextAsync(outputFile, ct);
		}
		finally
		{
			if (File.Exists(inputFile))
				File.Delete(inputFile);
			if (File.Exists(outputFile))
				File.Delete(outputFile);
		}
	}

	// Generates mermaid.js reference SVGs for all gallery examples into the given directory.
	public static async Task GenerateReferencesAsync(
		IEnumerable<Mermaider.Examples.DiagramExample> examples,
		string outputDir,
		CancellationToken ct = default)
	{
		Directory.CreateDirectory(outputDir);
		foreach (var ex in examples)
		{
			ct.ThrowIfCancellationRequested();
			try
			{
				var svg = await RenderAsync(ex.Source, ct);
				await File.WriteAllTextAsync(Path.Combine(outputDir, $"{ex.Slug}.svg"), svg, ct);
				Console.WriteLine($"  ok  {ex.Slug}");
			}
			catch (Exception e)
			{
				Console.WriteLine($"  err {ex.Slug}: {e.Message}");
			}
		}
	}
}
