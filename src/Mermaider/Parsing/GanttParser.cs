using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class GanttParser
{
	private const int TimeoutMs = 2000;

	/// <summary>Stable synthetic origin when a chart has no absolute dates (never wall-clock).</summary>
	private static readonly DateTime SyntheticOrigin = new(2020, 1, 1);

	[GeneratedRegex(@"^gantt(?:\s+title\s+(.+))?\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex HeaderPattern();

	[GeneratedRegex(@"^title\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex TitlePattern();

	[GeneratedRegex(@"^dateFormat\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex DateFormatPattern();

	[GeneratedRegex(@"^section\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex SectionPattern();

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
		var netDateFormat = ToNetDateFormat(dateFormat);

		var sections = new List<GanttSection>();
		string? currentSectionName = null;
		var currentTasks = new List<GanttTask>();

		var byId = new Dictionary<string, GanttTask>(StringComparer.OrdinalIgnoreCase);
		DateTime? previousEnd = null;
		DateTime? chartOrigin = null;

		var headerMatch = HeaderPattern().Match(lines[0]);
		if (headerMatch.Success && headerMatch.Groups[1].Success)
			title = headerMatch.Groups[1].Value.Trim();

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			if (IsDeferredDirective(line))
				continue;

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
				netDateFormat = ToNetDateFormat(dateFormat);
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
			var meta = taskMatch.Groups[2].Value.Trim();
			var raw = ParseRawTask(name, meta, netDateFormat);
			var task = ResolveTask(raw, byId, previousEnd, ref chartOrigin);

			currentTasks.Add(task);
			if (task.Id is { Length: > 0 })
				byId[task.Id] = task;
			previousEnd = task.End;
		}

		FlushSection(sections, currentSectionName, currentTasks);

		if (sections.Count == 0)
			sections.Add(new GanttSection(null, []));

		return new GanttDiagram
		{
			Title = title,
			DateFormat = dateFormat,
			Sections = sections,
		};
	}

	private static bool IsDeferredDirective(string line) =>
		line.StartsWith("excludes", StringComparison.OrdinalIgnoreCase) ||
		line.StartsWith("axisFormat", StringComparison.OrdinalIgnoreCase) ||
		line.StartsWith("tickInterval", StringComparison.OrdinalIgnoreCase) ||
		line.StartsWith("todayMarker", StringComparison.OrdinalIgnoreCase) ||
		line.StartsWith("weekday", StringComparison.OrdinalIgnoreCase);

	private static void FlushSection(List<GanttSection> sections, string? name, List<GanttTask> tasks)
	{
		if (tasks.Count == 0 && name is null)
			return;
		if (tasks.Count == 0)
			return;
		sections.Add(new GanttSection(name, tasks));
	}

	private sealed class RawTask
	{
		public required string Name { get; init; }
		public string? Id { get; init; }
		public GanttTaskTags Tags { get; init; }
		public DateTime? StartDate { get; init; }
		public string[]? AfterIds { get; init; }
		public DateTime? EndDate { get; init; }
		public TimeSpan? Duration { get; init; }
	}

	/// <summary>
	/// Left-to-right token consumer: tags*, [id], [start|after], [end|duration].
	/// A token is a date only when it parses under the active dateFormat.
	/// </summary>
	private static RawTask ParseRawTask(string name, string meta, string netDateFormat)
	{
		var tags = GanttTaskTags.None;
		string? id = null;
		DateTime? startDate = null;
		string[]? afterIds = null;
		DateTime? endDate = null;
		TimeSpan? duration = null;

		if (meta.Length == 0)
		{
			return new RawTask
			{
				Name = name,
				Duration = TimeSpan.FromDays(1),
			};
		}

		var tokens = SplitMeta(meta);
		var i = 0;

		while (i < tokens.Count && TryParseTag(tokens[i], out var tag))
		{
			tags |= tag;
			i++;
		}

		// Optional id: first remaining token that is not after / duration / date
		if (i < tokens.Count &&
			!IsAfter(tokens[i]) &&
			!IsDuration(tokens[i]) &&
			ParseDate(tokens[i], netDateFormat) is null)
		{
			id = tokens[i];
			i++;
		}

		// Optional start: after … | date
		if (i < tokens.Count)
		{
			if (IsAfter(tokens[i]))
			{
				afterIds = ParseAfterIds(tokens[i]);
				i++;
			}
			else if (ParseDate(tokens[i], netDateFormat) is { } sd)
			{
				startDate = sd;
				i++;
			}
		}

		// Optional end: duration | date
		if (i < tokens.Count)
		{
			if (IsDuration(tokens[i]))
				duration = ParseDuration(tokens[i]);
			else if (ParseDate(tokens[i], netDateFormat) is { } ed)
				endDate = ed;
		}

		// Defaults when end is unspecified
		if (duration is null && endDate is null)
		{
			duration = (tags & GanttTaskTags.Milestone) != 0
				? TimeSpan.Zero
				: TimeSpan.FromDays(1);
		}

		return new RawTask
		{
			Name = name,
			Id = id,
			Tags = tags,
			StartDate = startDate,
			AfterIds = afterIds,
			EndDate = endDate,
			Duration = duration,
		};
	}

	private static GanttTask ResolveTask(
		RawTask raw,
		Dictionary<string, GanttTask> byId,
		DateTime? previousEnd,
		ref DateTime? chartOrigin)
	{
		if (raw.StartDate is { } absStart)
			chartOrigin ??= absStart;

		var origin = chartOrigin ?? SyntheticOrigin;

		var start = ResolveStart(raw, byId, previousEnd, origin);
		var end = ResolveEnd(raw, start);
		if (end < start)
			end = start;

		return new GanttTask(raw.Name, raw.Id, start, end, raw.Tags);
	}

	private static DateTime ResolveStart(
		RawTask raw,
		Dictionary<string, GanttTask> byId,
		DateTime? previousEnd,
		DateTime origin)
	{
		if (raw.AfterIds is { Length: > 0 })
		{
			var maxEnd = DateTime.MinValue;
			var found = false;
			foreach (var refId in raw.AfterIds)
			{
				if (byId.TryGetValue(refId, out var dep) && dep.End > maxEnd)
				{
					maxEnd = dep.End;
					found = true;
				}
			}
			return found ? maxEnd : previousEnd ?? origin;
		}

		if (raw.StartDate is { } sd)
			return sd;

		return previousEnd ?? origin;
	}

	private static DateTime ResolveEnd(RawTask raw, DateTime start)
	{
		if (raw.Duration is { } dur)
			return start + dur;
		if (raw.EndDate is { } ed)
			return ed;
		if ((raw.Tags & GanttTaskTags.Milestone) != 0)
			return start;
		return start.AddDays(1);
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

	private static string ToNetDateFormat(string mermaid) =>
		mermaid
			.Replace("YYYY", "yyyy", StringComparison.Ordinal)
			.Replace("YY", "yy", StringComparison.Ordinal)
			.Replace("DD", "dd", StringComparison.Ordinal)
			.Replace("D", "d", StringComparison.Ordinal);

	private static DateTime? ParseDate(string text, string netFormat)
	{
		// Unspecified kind throughout — never wall-clock, never forced UTC.
		if (DateTime.TryParseExact(
				text, netFormat, CultureInfo.InvariantCulture,
				DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault,
				out var dt))
		{
			return DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
		}

		if (DateTime.TryParse(
				text, CultureInfo.InvariantCulture,
				DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault,
				out dt))
		{
			return DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
		}

		return null;
	}
}
