using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class TimelineParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^title\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex TitlePattern();

	[GeneratedRegex(@"^section\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex SectionPattern();

	[GeneratedRegex(@"^(.+?)\s*:\s*(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex PeriodPattern();

	[GeneratedRegex(@"^:\s*(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ContinuationPattern();

	internal static TimelineDiagram Parse(string[] lines)
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

	private static TimelineDiagram ParseCore(string[] lines)
	{
		string? title = null;
		var sections = new List<TimelineSection>();
		string? currentSectionName = null;
		var currentPeriods = new List<TimelinePeriod>();
		TimelinePeriod? lastPeriod = null;

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
				FlushSection(sections, currentSectionName, currentPeriods);
				currentSectionName = sectionMatch.Groups[1].Value.Trim();
				currentPeriods = [];
				lastPeriod = null;
				continue;
			}

			var contMatch = ContinuationPattern().Match(line);
			if (contMatch.Success && lastPeriod != null)
			{
				var events = SplitEvents(contMatch.Groups[1].Value);
				var combined = new List<string>(lastPeriod.Events);
				combined.AddRange(events);
				var updated = new TimelinePeriod(lastPeriod.Label, combined);
				currentPeriods[^1] = updated;
				lastPeriod = updated;
				continue;
			}

			var periodMatch = PeriodPattern().Match(line);
			if (periodMatch.Success)
			{
				var periodLabel = periodMatch.Groups[1].Value.Trim();
				var events = SplitEvents(periodMatch.Groups[2].Value);
				lastPeriod = new TimelinePeriod(periodLabel, events);
				currentPeriods.Add(lastPeriod);
			}
		}

		FlushSection(sections, currentSectionName, currentPeriods);

		return new TimelineDiagram { Title = title, Sections = sections };
	}

	private static void FlushSection(List<TimelineSection> sections, string? name, List<TimelinePeriod> periods)
	{
		if (periods.Count > 0)
			sections.Add(new TimelineSection(name, periods));
	}

	private static List<string> SplitEvents(string text)
	{
		var parts = text.Split(':');
		var events = new List<string>(parts.Length);
		foreach (var part in parts)
		{
			var trimmed = part.Trim();
			if (trimmed.Length > 0)
				events.Add(trimmed);
		}
		return events;
	}
}
