using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class GanttParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^gantt(?:\s+title\s+(.+))?\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex HeaderPattern();

	[GeneratedRegex(@"^title\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex TitlePattern();

	[GeneratedRegex(@"^dateFormat\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex DateFormatPattern();

	[GeneratedRegex(@"^section\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex SectionPattern();

	// taskName : metadata
	[GeneratedRegex(@"^(.+?)\s*:\s*(.*)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex TaskPattern();

	[GeneratedRegex(@"^\d+(?:\.\d+)?[smhdw]$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex DurationPattern();

	[GeneratedRegex(@"^after\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex AfterPattern();

	internal static GanttDiagram Parse(string[] lines)
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

	private static GanttDiagram ParseCore(string[] lines)
	{
		string? title = null;
		var dateFormat = "YYYY-MM-DD";
		var sections = new List<GanttSection>();

		var headerMatch = HeaderPattern().Match(lines[0]);
		if (headerMatch.Success && headerMatch.Groups[1].Success)
			title = headerMatch.Groups[1].Value.Trim();

		// First pass: collect raw task specs so we can resolve after/duration in order
		var rawTasks = new List<RawTask>();
		var sectionBreaks = new List<(int TaskIndex, string? Name)>();

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			// Skip known no-op / deferred directives for v1
			if (line.StartsWith("excludes", StringComparison.OrdinalIgnoreCase) ||
				line.StartsWith("axisFormat", StringComparison.OrdinalIgnoreCase) ||
				line.StartsWith("tickInterval", StringComparison.OrdinalIgnoreCase) ||
				line.StartsWith("todayMarker", StringComparison.OrdinalIgnoreCase) ||
				line.StartsWith("weekday", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var titleMatch = TitlePattern().Match(line);
			if (titleMatch.Success)
			{
				title = titleMatch.Groups[1].Value.Trim();
				continue;
			}

			var dfMatch = DateFormatPattern().Match(line);
			if (dfMatch.Success)
			{
				dateFormat = dfMatch.Groups[1].Value.Trim();
				continue;
			}

			var sectionMatch = SectionPattern().Match(line);
			if (sectionMatch.Success)
			{
				sectionBreaks.Add((rawTasks.Count, sectionMatch.Groups[1].Value.Trim()));
				continue;
			}

			var taskMatch = TaskPattern().Match(line);
			if (taskMatch.Success)
			{
				var name = taskMatch.Groups[1].Value.Trim();
				// Avoid treating directive-like lines as tasks
				if (name.Equals("title", StringComparison.OrdinalIgnoreCase) ||
					name.Equals("dateFormat", StringComparison.OrdinalIgnoreCase) ||
					name.Equals("section", StringComparison.OrdinalIgnoreCase) ||
					name.Equals("gantt", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				var meta = taskMatch.Groups[2].Value.Trim();
				rawTasks.Add(ParseRawTask(name, meta));
			}
		}

		// If no section was declared before tasks, start with a default section
		if (sectionBreaks.Count == 0 || sectionBreaks[0].TaskIndex > 0)
			sectionBreaks.Insert(0, (0, null));

		var resolved = ResolveTasks(rawTasks, dateFormat);

		for (var s = 0; s < sectionBreaks.Count; s++)
		{
			var start = sectionBreaks[s].TaskIndex;
			var end = s + 1 < sectionBreaks.Count ? sectionBreaks[s + 1].TaskIndex : resolved.Count;
			if (end <= start)
				continue;
			var slice = resolved.GetRange(start, end - start);
			sections.Add(new GanttSection(sectionBreaks[s].Name, slice));
		}

		// Drop empty default section if we only had empty breaks
		if (sections.Count == 0)
			sections.Add(new GanttSection(null, []));

		return new GanttDiagram
		{
			Title = title,
			DateFormat = dateFormat,
			Sections = sections,
		};
	}

	private sealed class RawTask
	{
		public required string Name { get; init; }
		public string? Id { get; init; }
		public GanttTaskTags Tags { get; init; }
		public string? StartDateText { get; init; }
		public string[]? AfterIds { get; init; }
		public string? EndDateText { get; init; }
		public TimeSpan? Duration { get; init; }
	}

	private static RawTask ParseRawTask(string name, string meta)
	{
		var tags = GanttTaskTags.None;
		string? id = null;
		string? startDate = null;
		string[]? afterIds = null;
		string? endDate = null;
		TimeSpan? duration = null;

		if (meta.Length == 0)
		{
			return new RawTask
			{
				Name = name,
				Tags = tags,
				Duration = TimeSpan.FromDays(1),
			};
		}

		// Split on commas not inside after-lists — after lists use spaces, not commas for ids
		var tokens = SplitMeta(meta);

		var i = 0;
		// Leading tags
		while (i < tokens.Count && TryParseTag(tokens[i], out var tag))
		{
			tags |= tag;
			i++;
		}

		// Remaining: [id], [start|after …], [end|duration]
		var remaining = tokens.Count - i;
		if (remaining == 0)
		{
			duration = TimeSpan.FromDays(1);
		}
		else if (remaining == 1)
		{
			// Duration only, date only (as end?), or id only — treat as duration or end
			var t = tokens[i];
			if (IsDuration(t))
				duration = ParseDuration(t);
			else if (LooksLikeDate(t))
				endDate = t; // single date = end; start falls back to previous
			else
				id = t;
		}
		else
		{
			// 2+ tokens after tags
			// Possible patterns:
			//   id, start, end
			//   id, after X, duration
			//   after X, duration
			//   start, end
			//   start, duration
			//   id, duration
			//   crit, done already consumed as tags

			// If first remaining is not after/date/duration → id
			if (!IsAfter(tokens[i]) && !LooksLikeDate(tokens[i]) && !IsDuration(tokens[i]))
			{
				id = tokens[i];
				i++;
			}

			// Start
			if (i < tokens.Count)
			{
				if (IsAfter(tokens[i]))
				{
					afterIds = ParseAfterIds(tokens[i]);
					i++;
				}
				else if (LooksLikeDate(tokens[i]))
				{
					startDate = tokens[i];
					i++;
				}
				// else leave start unset (previous task)
			}

			// End / duration
			if (i < tokens.Count)
			{
				if (IsDuration(tokens[i]))
					duration = ParseDuration(tokens[i]);
				else if (LooksLikeDate(tokens[i]))
					endDate = tokens[i];
			}
		}

		if (duration is null && endDate is null && (tags & GanttTaskTags.Milestone) != 0)
			duration = TimeSpan.Zero;
		else if (duration is null && endDate is null && startDate is null && afterIds is null)
			duration = TimeSpan.FromDays(1);

		return new RawTask
		{
			Name = name,
			Id = id,
			Tags = tags,
			StartDateText = startDate,
			AfterIds = afterIds,
			EndDateText = endDate,
			Duration = duration,
		};
	}

	private static List<string> SplitMeta(string meta)
	{
		var parts = meta.Split(',');
		var tokens = new List<string>(parts.Length);
		foreach (var p in parts)
		{
			var t = p.Trim();
			if (t.Length > 0)
				tokens.Add(t);
		}
		return tokens;
	}

	private static bool TryParseTag(string token, out GanttTaskTags tag)
	{
		if (token.Equals("done", StringComparison.OrdinalIgnoreCase))
		{
			tag = GanttTaskTags.Done;
			return true;
		}
		if (token.Equals("active", StringComparison.OrdinalIgnoreCase))
		{
			tag = GanttTaskTags.Active;
			return true;
		}
		if (token.Equals("crit", StringComparison.OrdinalIgnoreCase))
		{
			tag = GanttTaskTags.Crit;
			return true;
		}
		if (token.Equals("milestone", StringComparison.OrdinalIgnoreCase))
		{
			tag = GanttTaskTags.Milestone;
			return true;
		}
		tag = GanttTaskTags.None;
		return false;
	}

	private static bool IsAfter(string token) =>
		token.StartsWith("after", StringComparison.OrdinalIgnoreCase) &&
		(token.Length == 5 || char.IsWhiteSpace(token[5]));

	private static string[] ParseAfterIds(string token)
	{
		var m = AfterPattern().Match(token);
		if (!m.Success)
			return [];
		return m.Groups[1].Value
			.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	private static bool IsDuration(string token) => DurationPattern().IsMatch(token);

	private static TimeSpan ParseDuration(string token)
	{
		var unit = char.ToLowerInvariant(token[^1]);
		var number = double.Parse(token.AsSpan(0, token.Length - 1), CultureInfo.InvariantCulture);
		return unit switch
		{
			's' => TimeSpan.FromSeconds(number),
			'm' => TimeSpan.FromMinutes(number),
			'h' => TimeSpan.FromHours(number),
			'd' => TimeSpan.FromDays(number),
			'w' => TimeSpan.FromDays(number * 7),
			_ => TimeSpan.FromDays(number),
		};
	}

	private static bool LooksLikeDate(string token)
	{
		// Heuristic: contains digit and is not a duration / after / pure identifier
		if (IsDuration(token) || IsAfter(token))
			return false;
		var hasDigit = false;
		var hasSep = false;
		foreach (var c in token)
		{
			if (char.IsDigit(c))
				hasDigit = true;
			else if (c is '-' or '/' or ':' or ' ' or 'T')
				hasSep = true;
		}
		return hasDigit && hasSep;
	}

	private static List<GanttTask> ResolveTasks(List<RawTask> rawTasks, string mermaidDateFormat)
	{
		var netFormat = ToNetDateFormat(mermaidDateFormat);
		var byId = new Dictionary<string, GanttTask>(StringComparer.OrdinalIgnoreCase);
		var resolved = new List<GanttTask>(rawTasks.Count);
		DateTime? previousEnd = null;
		DateTime? chartStart = null;

		foreach (var raw in rawTasks)
		{
			var start = ResolveStart(raw, byId, previousEnd, chartStart, netFormat);
			var end = ResolveEnd(raw, start, netFormat);
			if (end < start)
				end = start;

			var task = new GanttTask(raw.Name, raw.Id, start, end, raw.Tags);
			resolved.Add(task);
			if (raw.Id is { Length: > 0 })
				byId[raw.Id] = task;

			previousEnd = end;
			chartStart ??= start;
		}

		return resolved;
	}

	private static DateTime ResolveStart(
		RawTask raw,
		Dictionary<string, GanttTask> byId,
		DateTime? previousEnd,
		DateTime? chartStart,
		string netFormat)
	{
		if (raw.AfterIds is { Length: > 0 })
		{
			var maxEnd = DateTime.MinValue;
			var found = false;
			foreach (var refId in raw.AfterIds)
			{
				if (byId.TryGetValue(refId, out var dep))
				{
					if (dep.End > maxEnd)
						maxEnd = dep.End;
					found = true;
				}
			}
			return found ? maxEnd : previousEnd ?? DateTime.Today;
		}

		if (raw.StartDateText is { } sd)
			return ParseDate(sd, netFormat) ?? previousEnd ?? DateTime.Today;

		return previousEnd ?? chartStart ?? DateTime.Today;
	}

	private static DateTime ResolveEnd(RawTask raw, DateTime start, string netFormat)
	{
		if (raw.Duration is { } dur)
			return start + dur;
		if (raw.EndDateText is { } ed)
			return ParseDate(ed, netFormat) ?? start.AddDays(1);
		if ((raw.Tags & GanttTaskTags.Milestone) != 0)
			return start;
		return start.AddDays(1);
	}

	private static string ToNetDateFormat(string mermaid)
	{
		// Mermaid uses moment-like tokens. Map common ones to .NET custom format.
		// Replace longer tokens first.
		return mermaid
			.Replace("YYYY", "yyyy", StringComparison.Ordinal)
			.Replace("YY", "yy", StringComparison.Ordinal)
			.Replace("DD", "dd", StringComparison.Ordinal)
			.Replace("D", "d", StringComparison.Ordinal)
			.Replace("HH", "HH", StringComparison.Ordinal)
			.Replace("mm", "mm", StringComparison.Ordinal)
			.Replace("ss", "ss", StringComparison.Ordinal);
	}

	private static DateTime? ParseDate(string text, string netFormat)
	{
		if (DateTime.TryParseExact(text, netFormat, CultureInfo.InvariantCulture,
				DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
				out var dt))
			return dt;

		// Fallbacks for common absolute dates
		if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dt))
			return dt;

		return null;
	}
}
