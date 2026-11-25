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

	internal static KanbanDiagram Parse(string[] lines, bool strict = true)
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

	private static KanbanDiagram ParseCore(string[] lines, bool strict)
	{
		string? title = null;
		var columns = new List<KanbanColumn>();
		string? currentColumnId = null;
		string? currentColumnTitle = null;
		var currentTasks = new List<KanbanTask>();
		var columnIndent = -1;
		var firstIndent = -1;

		for (var i = 0; i < lines.Length; i++)
		{
			var raw = lines[i];
			var stripped = raw.Trim();
			if (stripped.Length == 0)
				continue;

			// Skip diagram header
			if (i == 0 && stripped.StartsWith("kanban", StringComparison.OrdinalIgnoreCase))
				continue;

			// Mermaid refuses lines that follow a line indented less than the first line of the body
			// (a 'title' counts as that first line): "items without section".
			var indent = CountLeadingWhitespace(raw);
			if (strict)
			{
				firstIndent = firstIndent < 0 ? indent : firstIndent;
				if (indent < firstIndent && HasLaterContent(lines, i))
					throw new MermaidParseException(
						$"'{stripped}' is indented less than the first line, so the lines after it have no column",
						stripped,
						"every column indented at least as far as the first line",
						column: 1
					);
			}

			var titleMatch = TitlePattern().Match(stripped);
			if (titleMatch.Success)
			{
				title = titleMatch.Groups[1].Value.Trim();
				continue;
			}

			if (strict)
				RequireWellFormed(lines, i, stripped);

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

	/// <summary>
	/// A column or task is <c>id[label]</c> then an optional <c>@{ key: 'value', … }</c> block. Mermaid's lexer lets a label or
	/// block run over following lines, so an unclosed one is only refused when nothing later could close it.
	/// </summary>
	private static void RequireWellFormed(string[] lines, int index, string stripped)
	{
		var metaSearchFrom = 0;
		var open = stripped.IndexOf('[');
		if (open >= 0)
		{
			var close = stripped.IndexOf(']', open + 1);
			if (close < 0)
			{
				if (!LaterLineContains(lines, index, ']'))
					throw new MermaidParseException("label opened with '[' is never closed", stripped, "']' to end the label", column: open + 1);
				return;
			}

			if (close == open + 1)
				throw new MermaidParseException("label between '[' and ']' is empty", stripped, "label text", column: open + 1);
			var nested = stripped.IndexOf('[', open + 1, close - open - 1);
			if (nested >= 0)
				throw new MermaidParseException("label contains a second '['", stripped, "label text without '['", column: nested + 1);

			var rest = stripped[(close + 1)..];
			if (rest.Length > 0 && !rest.StartsWith("@{", StringComparison.Ordinal))
				throw new MermaidParseException($"unexpected text '{rest}' after the label", stripped, "the end of the line or '@{ … }' metadata", column: close + 2);
			metaSearchFrom = close + 1;
		}

		var meta = stripped.IndexOf("@{", metaSearchFrom, StringComparison.Ordinal);
		if (meta < 0)
			return;

		var bodyStart = meta + 2;
		var end = stripped.IndexOf('}', bodyStart);
		if (end < 0)
		{
			// The block may run on to the first later '}'. Mermaid then accepts it only as a single entry:
			// no comma anywhere, and a value on this line leaves nothing between it and the '}'.
			var opener = stripped[bodyStart..].Trim();
			var rest = new System.Text.StringBuilder();
			var closed = false;
			for (var j = index + 1; j < lines.Length && !closed; j++)
			{
				var next = lines[j];
				var brace = next.IndexOf('}');
				closed = brace >= 0;
				rest.Append(' ').Append(closed ? next[..brace] : next);
			}
			var continuation = rest.ToString().Trim();
			if (!closed)
				throw new MermaidParseException("metadata opened with '@{' is never closed", stripped, "'}' to end the metadata", column: meta + 1);
			var valueOnOpener = opener.Length > 0 && !opener.EndsWith(':');
			if (opener.Contains(',', StringComparison.Ordinal) || continuation.Contains(',', StringComparison.Ordinal)
				|| (valueOnOpener && continuation.Length > 0) || (opener.Length == 0 && continuation.Length == 0))
				throw new MermaidParseException("metadata opened with '@{' is not a valid key: value block before its '}'", stripped, "'@{ key: 'value', … }' closed on the same line", column: meta + 1);
			return;
		}

		if (end + 1 < stripped.Length)
			throw new MermaidParseException($"unexpected text '{stripped[(end + 1)..].Trim()}' after the metadata", stripped, "the end of the line", column: end + 2);

		RequireFlowEntries(stripped, bodyStart, stripped[bodyStart..end]);
	}

	private static bool HasLaterContent(string[] lines, int index)
	{
		for (var j = index + 1; j < lines.Length; j++)
		{
			if (lines[j].Trim().Length > 0)
				return true;
		}
		return false;
	}

	private static bool LaterLineContains(string[] lines, int index, char c)
	{
		for (var j = index + 1; j < lines.Length; j++)
		{
			if (lines[j].Contains(c, StringComparison.Ordinal))
				return true;
		}
		return false;
	}

	/// <summary>Metadata is read as a YAML flow mapping: quoted strings must close and entries are separated by commas.</summary>
	private static void RequireFlowEntries(string stripped, int bodyStart, string body)
	{
		var pos = 0;
		while (true)
		{
			SkipSpaces(body, ref pos);
			if (pos >= body.Length)
				return;

			ReadNode(stripped, bodyStart, body, ref pos);
			SkipSpaces(body, ref pos);
			if (pos < body.Length && body[pos] == ':')
			{
				pos++;
				SkipSpaces(body, ref pos);
				if (pos < body.Length && body[pos] != ',')
					ReadNode(stripped, bodyStart, body, ref pos);
				SkipSpaces(body, ref pos);
			}

			if (pos >= body.Length)
				return;
			if (body[pos] != ',')
				throw new MermaidParseException("metadata entries must be separated by ','", stripped, "',' between key: value pairs", column: bodyStart + pos + 1);
			pos++;
		}
	}

	private static void ReadNode(string stripped, int bodyStart, string body, ref int pos)
	{
		var c = body[pos];
		if (c is '\'' or '"')
		{
			var i = pos + 1;
			while (i < body.Length)
			{
				if (body[i] == c)
				{
					if (c == '\'' && i + 1 < body.Length && body[i + 1] == '\'')
					{
						i += 2;
						continue;
					}
					pos = i + 1;
					return;
				}
				if (c == '"' && body[i] == '\\')
					i++;
				i++;
			}
			throw new MermaidParseException($"string opened with {c} in the metadata is never closed", stripped, $"a closing {c}", column: bodyStart + pos + 1);
		}

		while (pos < body.Length && body[pos] != ',' && !(body[pos] == ':' && (pos + 1 == body.Length || char.IsWhiteSpace(body[pos + 1]))))
			pos++;
	}

	private static void SkipSpaces(string text, ref int pos)
	{
		while (pos < text.Length && char.IsWhiteSpace(text[pos]))
			pos++;
	}

	private static void FlushColumn(List<KanbanColumn> columns, string? id, string? title, List<KanbanTask> tasks)
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

		// Bare bracket form: [Title] with no leading id — strip brackets
		if (text.Length >= 2 && text[0] == '[' && text[^1] == ']')
		{
			var inner = text[1..^1];
			return (inner, inner);
		}

		// Bare text: id = label
		return (text, text);
	}

	private static void ParseMetadata(string body, ref string? assigned, ref string? ticket, ref string? priority)
	{
		foreach (Match m in MetadataPairPattern().Matches(body))
		{
			var key = m.Groups[1].Value;
			var value = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value.Trim();

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
