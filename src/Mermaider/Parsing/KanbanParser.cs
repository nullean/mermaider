using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class KanbanParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^title\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex TitlePattern();

	[GeneratedRegex(@"^(\S+)\[([^\]]*)\]\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex IdTitlePattern();

	[GeneratedRegex(@"^(.+?)\s*@\{([^}]*)\}\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex MetadataSuffixPattern();

	[GeneratedRegex(@"(\w+)\s*:\s*(?:'([^']*)'|""([^""]*)""|([^,}\s][^,}]*)?)", RegexOptions.None, TimeoutMs)]
	private static partial Regex MetadataPairPattern();

	internal static KanbanDiagram Parse(string[] lines)
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

	private static KanbanDiagram ParseCore(string[] lines)
	{
		string? title = null;
		var columns = new List<KanbanColumn>();
		string? currentColumnId = null;
		string? currentColumnTitle = null;
		var currentTasks = new List<KanbanTask>();
		var columnIndent = -1;

		for (var i = 0; i < lines.Length; i++)
		{
			var raw = lines[i];
			var stripped = raw.Trim();
			if (stripped.Length == 0)
				continue;

			// Skip diagram header
			if (i == 0 && stripped.StartsWith("kanban", StringComparison.OrdinalIgnoreCase))
				continue;

			var titleMatch = TitlePattern().Match(stripped);
			if (titleMatch.Success)
			{
				title = titleMatch.Groups[1].Value.Trim();
				continue;
			}

			var indent = CountLeadingWhitespace(raw);

			// First content line establishes column indent level
			if (columnIndent < 0)
				columnIndent = indent;

			if (indent <= columnIndent)
			{
				FlushColumn(columns, currentColumnId, currentColumnTitle, currentTasks);
				var (id, label) = ParseIdTitle(stripped);
				currentColumnId = id;
				currentColumnTitle = label;
				currentTasks = [];
			}
			else
			{
				// Task under current column (or orphan before any column → invent one)
				if (currentColumnId is null)
				{
					currentColumnId = "default";
					currentColumnTitle = "";
					currentTasks = [];
				}

				currentTasks.Add(ParseTask(stripped));
			}
		}

		FlushColumn(columns, currentColumnId, currentColumnTitle, currentTasks);

		return new KanbanDiagram { Title = title, Columns = columns };
	}

	private static void FlushColumn(
		List<KanbanColumn> columns,
		string? id,
		string? title,
		List<KanbanTask> tasks)
	{
		if (id is null)
			return;
		columns.Add(new KanbanColumn(id, title ?? id, tasks));
	}

	private static KanbanTask ParseTask(string text)
	{
		string? assigned = null;
		string? ticket = null;
		string? priority = null;
		var body = text;

		var metaMatch = MetadataSuffixPattern().Match(text);
		if (metaMatch.Success)
		{
			body = metaMatch.Groups[1].Value.Trim();
			ParseMetadata(metaMatch.Groups[2].Value, ref assigned, ref ticket, ref priority);
		}

		var (id, title) = ParseIdTitle(body);
		return new KanbanTask(id, title, assigned, ticket, priority);
	}

	private static (string Id, string Title) ParseIdTitle(string text)
	{
		var m = IdTitlePattern().Match(text);
		if (m.Success)
			return (m.Groups[1].Value, m.Groups[2].Value);

		// Bare text: id = label
		return (text, text);
	}

	private static void ParseMetadata(string body, ref string? assigned, ref string? ticket, ref string? priority)
	{
		foreach (Match m in MetadataPairPattern().Matches(body))
		{
			var key = m.Groups[1].Value;
			var value = m.Groups[2].Success
				? m.Groups[2].Value
				: m.Groups[3].Success
					? m.Groups[3].Value
					: m.Groups[4].Value.Trim();

			if (key.Equals("assigned", StringComparison.OrdinalIgnoreCase))
				assigned = value;
			else if (key.Equals("ticket", StringComparison.OrdinalIgnoreCase))
				ticket = value;
			else if (key.Equals("priority", StringComparison.OrdinalIgnoreCase))
				priority = value;
		}
	}

	private static int CountLeadingWhitespace(string line)
	{
		var n = 0;
		foreach (var c in line)
		{
			if (c is ' ' or '\t')
				n++;
			else
				break;
		}
		return n;
	}
}
