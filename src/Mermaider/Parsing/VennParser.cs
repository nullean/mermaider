using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class VennParser
{
	private const int TimeoutMs = 2000;

	private const string Statements = "'set <id>', 'union <id>, <id>', 'text ...', 'style ...' or 'title <text>'";

	[GeneratedRegex(@"^set\s+(?:""([^""]+)""|([^\s\[:]+))(?:\s*\[\s*(?:""([^""]*)""|'([^']*)'|([^\]""']+?))\s*\])?\s*(?::\s*(-?\d+(?:\.\d+)?))?\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex SetPattern();

	[GeneratedRegex(@"^union\s+(.+?)\s*(?:\[\s*(?:""([^""]*)""|'([^']*)'|([^\]""']+?))\s*\])?\s*(?::\s*(-?\d+(?:\.\d+)?))?\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex UnionPattern();

	[GeneratedRegex(@"^[A-Za-z_][\w-]*", RegexOptions.None, TimeoutMs)]
	private static partial Regex IdPattern();

	[GeneratedRegex(@"^(set|union|text|style|title)(?![\w-])", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex KeywordPattern();

	internal static VennDiagram Parse(string[] lines, bool strict = true)
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

	private static VennDiagram ParseCore(string[] lines, bool strict)
	{
		var sets = new List<VennSet>();
		var unions = new List<VennUnion>();
		var declared = new HashSet<string>(StringComparer.Ordinal);

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			if (strict)
				RequireValidStatement(line, declared);

			var setMatch = SetPattern().Match(line);
			if (setMatch.Success)
			{
				var id = setMatch.Groups[1].Success ? setMatch.Groups[1].Value : setMatch.Groups[2].Value;
				var label = FirstLabel(setMatch, 3) ?? id;
				double? size = setMatch.Groups[6].Success ? Size(setMatch.Groups[6].Value) : null;
				sets.Add(new VennSet(id, label, size));
				continue;
			}

			var unionMatch = UnionPattern().Match(line);
			if (unionMatch.Success)
			{
				var idsText = unionMatch.Groups[1].Value;
				var label = FirstLabel(unionMatch, 2);
				double? size = unionMatch.Groups[5].Success ? Size(unionMatch.Groups[5].Value) : null;

				var ids = new List<string>();
				foreach (var part in idsText.Split(','))
				{
					var trimmed = part.Trim().Trim('"');
					if (trimmed.Length > 0)
						ids.Add(trimmed);
				}

				if (ids.Count >= 2)
					unions.Add(new VennUnion(ids, label, size));
			}
		}

		return new VennDiagram { Sets = sets, Unions = unions };
	}

	private static string? FirstLabel(Match match, int firstGroup)
	{
		for (var g = firstGroup; g < firstGroup + 3; g++)
		{
			if (match.Groups[g].Success && match.Groups[g].Value.Length > 0)
				return match.Groups[g].Value;
		}
		return null;
	}

	// A size that is not positive says nothing about the region, so it is left to the layout.
	private static double? Size(string text)
	{
		var size = double.Parse(text, CultureInfo.InvariantCulture);
		return size > 0 ? size : null;
	}

	/// <summary>Throw for the first thing wrong with a statement, judged by what Mermaid accepts; record the sets a statement declares.</summary>
	private static void RequireValidStatement(string line, HashSet<string> declared)
	{
		var keyword = KeywordPattern().Match(line);
		if (!keyword.Success)
			throw new MermaidParseException($"unexpected text '{line}'", line, Statements, column: 1);

		var name = keyword.Value.ToLowerInvariant();
		var rest = line[keyword.Length..];
		switch (name)
		{
			case "set":
				{
					var pos = 0;
					var id = ReadId(line, keyword.Length, ref pos, "set");
					ReadLabelAndSize(line, ref pos, $"set '{id}'");
					declared.Add(id);
					break;
				}
			case "union":
				{
					var pos = keyword.Length;
					var ids = new List<string>();
					while (true)
					{
						var at = pos;
						var id = ReadId(line, pos, ref pos, "union", ids.Count == 0 ? "a set id" : "a set id after the comma");
						if (!declared.Contains(id))
							throw new MermaidParseException(
								$"union refers to set '{id}', which is not declared",
								line,
								$"a set declared earlier with 'set {id}'",
								column: Math.Max(line.IndexOf(id, at, StringComparison.Ordinal), at) + 1
							);
						ids.Add(id);
						pos = SkipSpaces(line, pos);
						if (pos < line.Length && line[pos] == ',')
						{
							pos++;
							continue;
						}
						break;
					}

					if (ids.Count < 2)
						throw new MermaidParseException(
							$"union of '{ids[0]}' has only one set",
							line,
							"at least two sets separated by commas, e.g. 'union A, B'",
							column: line.Length + 1
						);
					ReadLabelAndSize(line, ref pos, $"union of {string.Join(", ", ids)}");
					break;
				}
			case "text":
				if (rest.Trim().Length == 0)
					throw new MermaidParseException("text has nothing to show", line, "text after 'text'", column: line.Length + 1);
				break;
			case "style":
				if (rest.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2)
					throw new MermaidParseException(
						"style has no properties",
						line,
						"the sets to style and their properties, e.g. 'style A fill:#ff0000'",
						column: line.Length + 1
					);
				break;
		}
	}

	private static int SkipSpaces(string line, int pos)
	{
		while (pos < line.Length && char.IsWhiteSpace(line[pos]))
			pos++;
		return pos;
	}

	/// <summary>An id is a quoted string, or letters, digits, '_' and '-' starting with a letter or '_'.</summary>
	private static string ReadId(string line, int from, ref int pos, string what, string expected = "a set id")
	{
		var start = SkipSpaces(line, from);
		if (start >= line.Length)
			throw new MermaidParseException($"{what} has no set id", line, $"{expected} after '{what}'", column: line.Length + 1);

		if (line[start] == '"')
		{
			var close = line.IndexOf('"', start + 1);
			if (close < 0)
				throw new MermaidParseException($"set id opened with '\"' is never closed", line, "a closing '\"'", column: start + 1);
			pos = close + 1;
			return line[(start + 1)..close];
		}

		var match = IdPattern().Match(line[start..]);
		if (!match.Success)
		{
			var bad = line[start..].Split(' ', ',', '[', ':')[0];
			throw new MermaidParseException(
				bad.Length == 0 ? $"{what} has an empty set id" : $"'{bad}' is not a valid set id",
				line,
				"letters, digits, '_' or '-' starting with a letter (put anything else in double quotes)",
				column: start + 1
			);
		}
		pos = start + match.Length;
		return match.Value;
	}

	/// <summary>After the ids: an optional <c>["label"]</c> and an optional <c>: size</c>, then the end of the line.</summary>
	private static void ReadLabelAndSize(string line, ref int pos, string what)
	{
		pos = SkipSpaces(line, pos);
		if (pos < line.Length && line[pos] == '[')
		{
			var close = line.IndexOf(']', pos);
			if (close < 0)
				throw new MermaidParseException($"label of {what} is never closed", line, "']' after the label", column: pos + 1);
			var label = line[(pos + 1)..close].Trim();
			if (label.Length == 0)
				throw new MermaidParseException($"label of {what} is empty", line, "a label in the brackets, e.g. [\"Frontend\"]", column: pos + 1);
			if (label[0] is '"' or '\'' && (label.Length < 2 || label[^1] != label[0]))
				throw new MermaidParseException($"label of {what} is not closed", line, $"a closing {label[0]} after the label", column: pos + 2);
			pos = SkipSpaces(line, close + 1);
		}

		if (pos < line.Length && line[pos] == ':')
		{
			var value = line[(pos + 1)..].Trim();
			if (value.Length == 0)
				throw new MermaidParseException($"size of {what} has no value", line, "a number after ':', e.g. ': 30'", column: line.Length + 1);
			if (!double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _))
				throw new MermaidParseException(
					$"size '{value}' of {what} is not a number",
					line,
					"a number, e.g. ': 30'",
					column: line.IndexOf(value, pos, StringComparison.Ordinal) + 1
				);
			pos = line.Length;
		}

		if (pos < line.Length)
			throw new MermaidParseException(
				$"unexpected text '{line[pos..]}' after {what}",
				line,
				"an optional [\"label\"] and an optional ': size', then the end of the line",
				column: pos + 1
			);
	}
}
