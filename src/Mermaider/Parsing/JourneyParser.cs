using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class JourneyParser
{
	private const int TimeoutMs = 2000;
	private const int MinScore = 1;
	private const int MaxScore = 5;

	[GeneratedRegex(@"^journey(?:\s+title\s+(.+))?\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex HeaderPattern();

	[GeneratedRegex(@"^title\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex TitlePattern();

	[GeneratedRegex(@"^section\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex SectionPattern();

	// Task name: score: actors  OR  Task name: score
	[GeneratedRegex(@"^(.+?)\s*:\s*(\d+)\s*(?::\s*(.*))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex TaskPattern();

	internal static JourneyDiagram Parse(string[] lines)
	{
		try
		{
			return ParseCore(lines);
		}
		catch (RegexMatchTimeoutException ex)
		{
			throw new MermaidParseException(
				$"Parsing timed out after {ex.MatchTimeout.TotalSeconds}s — input may contain pathological patterns.",
				ex);
		}
	}

	private static JourneyDiagram ParseCore(string[] lines)
	{
		string? title = null;
		var sections = new List<JourneySection>();
		string? currentSectionName = null;
		var currentTasks = new List<JourneyTask>();

		var headerMatch = HeaderPattern().Match(lines[0]);
		if (headerMatch.Success && headerMatch.Groups[1].Success)
			title = headerMatch.Groups[1].Value.Trim();

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			var titleMatch = TitlePattern().Match(line);
			if (titleMatch.Success)
			{
				title = titleMatch.Groups[1].Value.Trim();
				continue;
			}

			var sectionMatch = SectionPattern().Match(line);
			if (sectionMatch.Success)
			{
				FlushSection(sections, currentSectionName, currentTasks);
				currentSectionName = sectionMatch.Groups[1].Value.Trim();
				currentTasks = [];
				continue;
			}

			var taskMatch = TaskPattern().Match(line);
			if (!taskMatch.Success)
				continue;

			var name = taskMatch.Groups[1].Value.Trim();
			if (!int.TryParse(taskMatch.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var score))
				continue;

			score = Math.Clamp(score, MinScore, MaxScore);
			var actors = ParseActors(taskMatch.Groups[3].Success ? taskMatch.Groups[3].Value : null);
			currentTasks.Add(new JourneyTask(name, score, actors));
		}

		FlushSection(sections, currentSectionName, currentTasks);

		if (sections.Count == 0)
			sections.Add(new JourneySection(null, []));

		return new JourneyDiagram { Title = title, Sections = sections };
	}

	private static void FlushSection(List<JourneySection> sections, string? name, List<JourneyTask> tasks)
	{
		if (tasks.Count == 0)
			return;
		sections.Add(new JourneySection(name, tasks));
	}

	private static IReadOnlyList<string> ParseActors(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return [];

		var parts = text.Split(',');
		var actors = new List<string>(parts.Length);
		foreach (var part in parts)
		{
			var trimmed = part.Trim();
			if (trimmed.Length > 0)
				actors.Add(trimmed);
		}
		return actors;
	}
}
