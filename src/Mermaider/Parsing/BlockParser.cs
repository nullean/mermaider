using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class BlockParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^block(?:-beta)?(?:\s+title\s+(.+))?$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex HeaderPattern();

	[GeneratedRegex(@"^title\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex TitlePattern();

	[GeneratedRegex(@"^columns\s+(\d+)\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex ColumnsPattern();

	// A --> B, A -- "label" --> B, A -- label --> B, A -->|label| B
	[GeneratedRegex(@"^([A-Za-z_][\w-]*)\s*(?:--\s*(?:""([^""]*)""|([^>""|]+?))\s*)?-->\s*(?:\|([^|]*)\|\s*)?([A-Za-z_][\w-]*)\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex EdgePattern();

	[GeneratedRegex(@"^block(?::[^\s:]+(?::\d+)?)?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex BlockOpenPattern();

	[GeneratedRegex(@"^(?:title|class|classDef|style)(?:\s|$)", RegexOptions.None, TimeoutMs)]
	private static partial Regex FreeTextStatementPattern();

	[GeneratedRegex(@"-->\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex DanglingArrowPattern();

	// "A -" or "A --": the start of an arrow that never becomes '-->'
	[GeneratedRegex(@"\s--?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex PartialArrowPattern();

	internal static BlockDiagram Parse(string[] lines, bool strict = true)
	{
		try
		{
			if (strict)
				Validate(lines);
			return ParseCore(lines);
		}
		catch (RegexMatchTimeoutException ex)
		{
			throw new MermaidParseException($"Parsing timed out after {ex.MatchTimeout.TotalSeconds}s — input may contain pathological patterns.", ex);
		}
	}

	// Mermaid refuses what the lenient reading below would silently skip: a label or quote that is never closed,
	// a colon that is not a column span, an arrow without a target, and block/end that do not pair up.
	private static void Validate(string[] lines)
	{
		var inQuote = false;
		var quoteLine = 0;
		var quoteCol = 0;
		var blocks = new Stack<(string Text, int Line)>();
		var openers = new List<(char Char, int Col)>();

		for (var i = 1; i < lines.Length; i++)
		{
			var raw = lines[i];
			var line = raw.Trim();
			var lead = raw.Length - raw.TrimStart().Length;
			var startedInQuote = inQuote;

			if (!startedInQuote && FreeTextStatementPattern().IsMatch(line))
				continue;

			if (!startedInQuote)
			{
				if (BlockOpenPattern().IsMatch(line))
				{
					blocks.Push((line, i));
					continue;
				}

				if (line == "end")
				{
					if (blocks.Count == 0)
						throw new MermaidParseException("'end' has no matching 'block'", line, "'block' to open a group before 'end'", column: 1);
					blocks.Pop();
					continue;
				}

				if (PartialArrowPattern().IsMatch(line))
					throw new MermaidParseException("arrow is incomplete", line, "'-->' to finish the arrow", column: line.Length);

				if (line.StartsWith("-->", StringComparison.Ordinal))
					throw new MermaidParseException("arrow '-->' has no source block", line, "a block id before the arrow, e.g. 'A --> B'", column: 1);
			}

			openers.Clear();
			for (var c = 0; c < raw.Length; c++)
			{
				var ch = raw[c];
				if (ch == '"')
				{
					inQuote = !inQuote;
					if (inQuote)
					{
						quoteLine = i;
						quoteCol = c - lead + 1;
					}
					continue;
				}

				if (inQuote)
					continue;

				switch (ch)
				{
					case '[' or '(' or '{':
						openers.Add((ch, c - lead + 1));
						break;
					case ']' or ')' or '}':
						if (openers.Count > 0)
							openers.RemoveAt(openers.Count - 1);
						break;
					case ':' when openers.Count == 0 && !startedInQuote && !(c + 1 < raw.Length && char.IsAsciiDigit(raw[c + 1])):
						throw new MermaidParseException("':' is not followed by a column span", line, "a number after ':', e.g. 'A:2'", column: c - lead + 1);
				}
			}

			if (!inQuote && openers.Count > 0)
			{
				var (open, col) = openers[0];
				var close = open switch { '[' => ']', '(' => ')', _ => '}', };
				throw new MermaidParseException($"block label opened with '{open}' is never closed", line, $"'{close}' to end the label", column: col);
			}
		}

		if (inQuote)
		{
			var text = lines[quoteLine].Trim();
			throw new MermaidParseException("quoted label is never closed", text, "a closing '\"'", column: quoteCol);
		}

		if (blocks.Count > 0)
		{
			var (text, _) = blocks.Peek();
			throw new MermaidParseException($"'{text}' group is never closed", text, "'end' to close the group", column: 1);
		}

		var last = lines[^1].Trim();
		if (lines.Length > 1 && DanglingArrowPattern().IsMatch(last))
			throw new MermaidParseException("arrow has no target block", last, "a block id after the arrow, e.g. 'A --> B'", column: last.Length + 1);
	}

	private static BlockDiagram ParseCore(string[] lines)
	{
		string? title = null;
		var columns = 1;
		var nodes = new List<BlockNode>();
		var edges = new List<BlockEdge>();
		var seenIds = new HashSet<string>(StringComparer.Ordinal);

		var header = HeaderPattern().Match(lines[0]);
		if (header.Success && header.Groups[1].Success)
			title = Unquote(header.Groups[1].Value.Trim());

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			var titleMatch = TitlePattern().Match(line);
			if (titleMatch.Success)
			{
				title = Unquote(titleMatch.Groups[1].Value.Trim());
				continue;
			}

			var colMatch = ColumnsPattern().Match(line);
			if (colMatch.Success)
			{
				if (int.TryParse(colMatch.Groups[1].Value, out var n) && n > 0)
					columns = n;
				continue;
			}

			var edgeMatch = EdgePattern().Match(line);
			if (edgeMatch.Success)
			{
				var label = edgeMatch.Groups[2].Success
					? edgeMatch.Groups[2].Value
					: edgeMatch.Groups[3].Success ? edgeMatch.Groups[3].Value.Trim() : edgeMatch.Groups[4].Success ? edgeMatch.Groups[4].Value.Trim() : null;
				edges.Add(new BlockEdge(edgeMatch.Groups[1].Value, edgeMatch.Groups[5].Value, label is { Length: > 0 } ? label : null));
				continue;
			}

			ParseNodeLine(line, nodes, seenIds);
		}

		return new BlockDiagram { Title = title, Columns = columns, Nodes = nodes, Edges = edges, };
	}

	private static void ParseNodeLine(string line, List<BlockNode> nodes, HashSet<string> seenIds)
	{
		var span = line.AsSpan();
		var i = 0;
		while (i < span.Length)
		{
			while (i < span.Length && char.IsWhiteSpace(span[i]))
				i++;
			if (i >= span.Length)
				break;

			if (!TryParseNode(span, ref i, out var id, out var label, out var rounded, out var nodeSpan))
				break;

			if (id.Equals("space", StringComparison.OrdinalIgnoreCase))
			{
				// Spacer cell: empty slot in the grid (IsSpace flag, not a magic id prefix)
				nodes.Add(new BlockNode("space", "", Rounded: false, IsSpace: true, Span: nodeSpan));
				continue;
			}

			if (!seenIds.Add(id))
				continue;

			nodes.Add(new BlockNode(id, label, rounded, Span: nodeSpan));
		}
	}

	private static bool TryParseNode(ReadOnlySpan<char> span, ref int i, out string id, out string label, out bool rounded, out int nodeSpan)
	{
		nodeSpan = 1;
		id = "";
		label = "";
		rounded = false;

		var start = i;
		if (i >= span.Length || !(char.IsLetter(span[i]) || span[i] == '_'))
			return false;

		i++;
		while (i < span.Length && (char.IsLetterOrDigit(span[i]) || span[i] is '_' or '-'))
			i++;

		id = span[start..i].ToString();
		label = id;

		if (i >= span.Length)
			return true;

		if (span[i] == ':')
		{
			nodeSpan = ParseSpan(span, ref i);
			return true;
		}

		if (span[i] == '[')
		{
			rounded = false;
			if (!TryParseDelimitedLabel(span, ref i, '[', ']', out label))
			{
				i = start;
				return false;
			}
			nodeSpan = ParseSpan(span, ref i);
			return true;
		}

		if (span[i] == '(')
		{
			rounded = true;
			if (!TryParseDelimitedLabel(span, ref i, '(', ')', out label))
			{
				i = start;
				return false;
			}
			nodeSpan = ParseSpan(span, ref i);
			return true;
		}

		return true;
	}

	// `A:2` — the block covers two grid columns
	private static int ParseSpan(ReadOnlySpan<char> span, ref int i)
	{
		if (i >= span.Length || span[i] != ':')
			return 1;
		var start = i + 1;
		var end = start;
		while (end < span.Length && char.IsAsciiDigit(span[end]))
			end++;
		if (end == start || !int.TryParse(span[start..end], out var n) || n < 1)
			return 1;
		i = end;
		return n;
	}

	private static bool TryParseDelimitedLabel(ReadOnlySpan<char> span, ref int i, char open, char close, out string label)
	{
		label = "";
		if (i >= span.Length || span[i] != open)
			return false;
		i++; // skip open

		if (i < span.Length && span[i] == '"')
		{
			i++; // skip opening quote
			var start = i;
			while (i < span.Length && span[i] != '"')
				i++;
			if (i >= span.Length)
				return false;
			label = span[start..i].ToString();
			i++; // skip closing quote
			if (i >= span.Length || span[i] != close)
				return false;
			i++; // skip close
			return true;
		}

		{
			var start = i;
			while (i < span.Length && span[i] != close)
				i++;
			if (i >= span.Length)
				return false;
			label = span[start..i].ToString();
			i++; // skip close
			return true;
		}
	}

	private static string Unquote(string value) => value is ['"', .., '"'] or ['\'', .., '\''] ? value[1..^1] : value;
}
