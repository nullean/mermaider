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

	/// <summary>
	/// Mermaid's statement shape: an optional period, then events each opened by ':' plus a whitespace character.
	/// An event may hold a ':' that is not followed by whitespace (<c>http://x</c>). A trailing ':' is left to the caller.
	/// </summary>
	[GeneratedRegex(@"^[^:]*(?::\s(?:[^:]|:(?!\s))+)*:?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex StatementShapePattern();

	internal static TimelineDiagram Parse(string[] lines, bool strict = true)
	{
		try
		{
			return ParseCore(lines, strict);
		}
		catch (RegexMatchTimeoutException ex)
		{
			throw new MermaidParseException($"Parsing timed out after {ex.MatchTimeout.TotalSeconds}s — input may contain pathological patterns.", ex);
		}
	}

	private static TimelineDiagram ParseCore(string[] lines, bool strict)
	{
		var anyPeriod = false;
		var (_, title) = DiagramHeaderOptions.Parse(lines[0], "timeline");
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
				if (strict && sectionMatch.Groups[1].Value.IndexOf(':') is var colon and >= 0)
					throw new MermaidParseException(
						"section name contains ':'",
						line,
						"a section name without ':'",
						column: sectionMatch.Groups[1].Index + colon + 1
					);
				FlushSection(sections, currentSectionName, currentPeriods);
				currentSectionName = sectionMatch.Groups[1].Value.Trim();
				currentPeriods = [];
				lastPeriod = null;
				continue;
			}

			if (strict)
				RequireStatementShape(line, i == lines.Length - 1, anyPeriod);

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

			if (!line.StartsWith(':'))
				anyPeriod = true;
		}

		FlushSection(sections, currentSectionName, currentPeriods);

		return new TimelineDiagram { Title = title, Sections = sections };
	}

	private static void RequireStatementShape(string line, bool isLast, bool anyPeriod)
	{
		if (!StatementShapePattern().IsMatch(line))
		{
			// Point at the first ':' that is not followed by whitespace, else the first empty event.
			var bad = -1;
			for (var c = line.IndexOf(':'); c >= 0 && bad < 0; c = line.IndexOf(':', c + 1))
			{
				if (c + 1 >= line.Length || !char.IsWhiteSpace(line[c + 1]) || c + 2 >= line.Length || line[c + 2] == ':')
					bad = c;
			}
			var problem = bad >= 0 && bad + 1 < line.Length && !char.IsWhiteSpace(line[bad + 1])
				? "':' must be followed by a space before the event text"
				: "event after ':' has no text";
			throw new MermaidParseException(problem, line, "'<period> : <event>' with a space after each ':'", column: bad + 1);
		}

		// A ':' ending the line takes the next line as its event text in Mermaid; at the end of input it takes nothing.
		if (isLast && line.EndsWith(':'))
			throw new MermaidParseException("event after ':' has no text", line, "event text after ':'", column: line.Length);

		if (line.StartsWith(':') && !anyPeriod)
			throw new MermaidParseException("event has no period before it", line, "a period such as '2024 : event' before a ': event' line", column: 1);
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
