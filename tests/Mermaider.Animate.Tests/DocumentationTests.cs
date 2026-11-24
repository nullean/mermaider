using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Mermaider.Animate.Tests;

/// <summary>The scores in the README and the agent skill compile, read straight from those files.</summary>
public partial class DocumentationTests
{
	private static string Root([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
		Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "../../src/Mermaider.Animate"));

	public static IEnumerable<Func<string>> Scores()
	{
		foreach (var file in new[] { "README.md", "skills/mermaider-animate/SKILL.md" })
		{
			var text = File.ReadAllText(Path.Combine(Root(), file));
			foreach (Match match in RawString().Matches(text))
			{
				var body = match.Groups[1].Value;
				var indent = Regex.Match(body, @"^\n?( *)").Groups[1].Value.Length;
				var source = string.Join("\n", body.Trim('\n').Split('\n').Select(l => l.Length >= indent ? l[indent..] : l.TrimStart()));
				yield return () => source;
			}
		}
	}

	[Test]
	[MethodDataSource(nameof(Scores))]
	public void Documented_scores_compile(string source)
	{
		var animation = MermaidAnimator.Compile(source);
		animation.Steps.Should().NotBeEmpty();
	}

	[GeneratedRegex("\"\"\"\\n(.*?)\\n *\"\"\"", RegexOptions.Singleline)]
	private static partial Regex RawString();
}
