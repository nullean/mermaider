using System.Text.RegularExpressions;
using AwesomeAssertions;
using Mermaider;

namespace Mermaider.Tests;

/// <summary>
/// Integration tests against the WinPrint mermaid showcase file.
/// Source: https://github.com/tig/winprint/blob/develop/testfiles/mermaid.md
///
/// Ensures Mermaider never throws unexpected exceptions when consumers render each
/// fenced diagram: valid SVG, or <see cref="MermaidParseException"/> for unsupported types.
/// </summary>
public partial class MermaidShowcaseTests
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^```mermaid\s*\r?\n(.*?)\r?\n```", RegexOptions.Singleline | RegexOptions.Multiline, TimeoutMs)]
	private static partial Regex MermaidFence();

	private static string LoadShowcaseMarkdown()
	{
		var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "mermaid.md");
		File.Exists(path).Should().BeTrue(
			$"Showcase fixture missing at {path}. Ensure Fixtures/mermaid.md is copied to the test output.");
		return File.ReadAllText(path);
	}

	private static List<string> ExtractFences(string markdown)
	{
		var fences = new List<string>();
		foreach (Match match in MermaidFence().Matches(markdown))
		{
			var source = match.Groups[1].Value.Trim();
			if (source.Length > 0)
				fences.Add(source);
		}
		return fences;
	}

	private static string FirstLine(string source)
	{
		var end = source.IndexOf('\n');
		return (end >= 0 ? source[..end] : source).Trim();
	}

	[Test]
	public void Showcase_all_fences_do_not_crash()
	{
		// Either valid SVG, or MermaidParseException for unsupported types.
		// Any other exception (NRE, IndexOutOfRange, …) is a regression.
		var fences = ExtractFences(LoadShowcaseMarkdown());
		fences.Should().NotBeEmpty();

		var failures = new List<string>();

		for (var i = 0; i < fences.Count; i++)
		{
			var source = fences[i];
			var label = $"fence[{i}] `{FirstLine(source)}`";
			try
			{
				var svg = MermaidRenderer.RenderSvg(source);
				if (string.IsNullOrEmpty(svg) || !svg.Contains("<svg") || !svg.Contains("</svg>"))
					failures.Add($"{label}: returned invalid SVG");
			}
			catch (MermaidParseException)
			{
				// Expected for unsupported diagram types (gantt, sankey, C4, …)
			}
			catch (Exception ex)
			{
				failures.Add($"{label}: {ex.GetType().Name}: {ex.Message}");
			}
		}

		failures.Should().BeEmpty(
			"every fence in mermaid.md must either render SVG or throw MermaidParseException:\n"
			+ string.Join("\n", failures));
	}

	[Test]
	public void Showcase_compact_header_titles_render()
	{
		// Stress cases from the fixture: title on the same line as the diagram keyword.
		var fences = ExtractFences(LoadShowcaseMarkdown());
		var compact = fences
			.Select(source => (Source: source, Header: FirstLine(source)))
			.Where(f =>
			{
				var titleIdx = f.Header.IndexOf(" title ", StringComparison.OrdinalIgnoreCase);
				if (titleIdx < 0)
					return false;
				return f.Header.StartsWith("pie", StringComparison.OrdinalIgnoreCase)
					|| f.Header.StartsWith("quadrantChart", StringComparison.OrdinalIgnoreCase)
					|| f.Header.StartsWith("timeline", StringComparison.OrdinalIgnoreCase);
			})
			.ToList();

		compact.Should().NotBeEmpty("fixture should include compact title-on-header stress cases");

		foreach (var (source, header) in compact)
		{
			var titleIdx = header.IndexOf(" title ", StringComparison.OrdinalIgnoreCase);
			var expectedTitle = header[(titleIdx + " title ".Length)..].Trim();

			var svg = MermaidRenderer.RenderSvg(source);

			svg.Should().Contain("<svg", because: header);
			svg.Should().Contain("</svg", because: header);
			svg.Should().Contain(expectedTitle, because: header);
		}
	}

	[Test]
	public void Showcase_pie_showData_title_on_header_renders()
	{
		var source = """
			pie showData title Pets adopted by volunteers
			"Dogs" : 386
			"Cats" : 85
			"Rats" : 15
			""";

		var svg = MermaidRenderer.RenderSvg(source);

		svg.Should().Contain("<svg");
		svg.Should().Contain("Pets adopted by volunteers");
		svg.Should().Contain("Dogs");
	}
}
