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

	[GeneratedRegex(@"^([A-Za-z_][\w-]*)\s*-->\s*([A-Za-z_][\w-]*)\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex EdgePattern();

	internal static BlockDiagram Parse(string[] lines)
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
				edges.Add(new BlockEdge(edgeMatch.Groups[1].Value, edgeMatch.Groups[2].Value));
				continue;
			}

			ParseNodeLine(line, nodes, seenIds);
		}

		return new BlockDiagram
		{
			Title = title,
			Columns = columns,
			Nodes = nodes,
			Edges = edges,
		};
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

			if (!TryParseNode(span, ref i, out var id, out var label, out var rounded))
				break;

			if (id.Equals("space", StringComparison.OrdinalIgnoreCase))
			{
				// Spacer cell: empty slot in the grid
				nodes.Add(new BlockNode($"__space_{nodes.Count}", "", Rounded: false));
				continue;
			}

			if (!seenIds.Add(id))
				continue;

			nodes.Add(new BlockNode(id, label, rounded));
		}
	}

	private static bool TryParseNode(ReadOnlySpan<char> span, ref int i, out string id, out string label, out bool rounded)
	{
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

		if (span[i] == '[')
		{
			rounded = false;
			if (!TryParseDelimitedLabel(span, ref i, '[', ']', out label))
			{
				i = start;
				return false;
			}
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
			return true;
		}

		return true;
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

	private static string Unquote(string value) =>
		value is ['"', .., '"'] or ['\'', .., '\''] ? value[1..^1] : value;
}
