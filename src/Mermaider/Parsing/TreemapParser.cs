using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class TreemapParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^""([^""]+)""\s*:\s*(\d+(?:\.\d+)?)\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex LeafPattern();

	[GeneratedRegex(@"^""([^""]+)""\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex SectionPattern();

	[GeneratedRegex(@"^(?:\d[\d,]*(?:\.\d*)?|\.\d+)", RegexOptions.None, TimeoutMs)]
	private static partial Regex ValuePattern();

	[GeneratedRegex(@"^(?:title|accTitle|accDescr|classDef)(?:\s|:|$)", RegexOptions.None, TimeoutMs)]
	private static partial Regex DirectivePattern();

	internal static TreemapDiagram Parse(string[] lines, bool strict = true)
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

	// Mermaid refuses what the lenient reading below would turn into a stray label: a quote that is never
	// closed, and a value that is missing or not a number.
	private static void Validate(string[] lines)
	{
		for (var i = 1; i < lines.Length; i++)
		{
			var raw = lines[i];
			var text = raw.Trim();
			if (text.Length == 0 || DirectivePattern().IsMatch(text))
				continue;

			// Mermaid also refuses a name without quotes, but this parser has always read one as a section label
			var quote = text[0];
			if (quote is not ('"' or '\''))
				continue;

			var close = text.IndexOf(quote, 1);
			if (close < 0)
			{
				// A quoted name may run over several lines; it must still be closed before the end.
				var inString = true;
				for (i++; i < lines.Length && inString; i++)
				{
					foreach (var c in lines[i])
					{
						if (c == quote)
							inString = !inString;
					}
				}

				i--;
				if (inString)
					throw new MermaidParseException("quoted name is never closed", text, $"a closing {quote}", column: 1);
				continue;
			}

			var rest = text[(close + 1)..].TrimStart();
			if (rest.Length == 0 || rest[0] is '"' or '\'' or ',' || rest.StartsWith(":::", StringComparison.Ordinal))
				continue;

			var column = text.Length - rest.Length + 1;
			if (rest[0] != ':')
				throw new MermaidParseException(
					$"unexpected text '{rest}' after the name",
					text,
					"': value' for a leaf, or nothing for a section",
					column: column
				);

			var value = rest[1..].TrimStart();
			var number = ValuePattern().Match(value);
			var valueColumn = column + (rest.Length - value.Length);
			if (!number.Success)
				throw new MermaidParseException(
					value.Length == 0 ? "value after ':' is missing" : $"value '{value}' is not a number",
					text,
					"a non-negative number, e.g. \"Sales\": 10",
					column: valueColumn
				);

			var tail = value[number.Length..].TrimStart();
			if (tail.Length > 0 && tail[0] is not ('"' or '\'') && !tail.StartsWith(":::", StringComparison.Ordinal))
				throw new MermaidParseException(
					$"unexpected text '{tail}' after the value",
					text,
					"the end of the entry, or a ':::class'",
					column: valueColumn + value.Length - tail.Length
				);
		}
	}

	private static TreemapDiagram ParseCore(string[] lines)
	{
		var roots = new List<TreemapNode>();
		var stack = new List<(int Indent, List<TreemapNode> Children, string Label, double? Value)>();

		for (var i = 1; i < lines.Length; i++)
		{
			var raw = lines[i];
			var indent = 0;
			foreach (var c in raw)
			{
				if (c is ' ' or '\t')
					indent++;
				else
					break;
			}

			var trimmed = raw.Trim();
			if (trimmed.Length == 0)
				continue;

			string label;
			double? value = null;

			var leafMatch = LeafPattern().Match(trimmed);
			if (leafMatch.Success)
			{
				label = leafMatch.Groups[1].Value;
				value = double.Parse(leafMatch.Groups[2].Value, CultureInfo.InvariantCulture);
			}
			else
			{
				var sectionMatch = SectionPattern().Match(trimmed);
				label = sectionMatch.Success ? sectionMatch.Groups[1].Value : trimmed.Trim('"');
			}

			while (stack.Count > 0 && stack[^1].Indent >= indent)
			{
				var popped = stack[^1];
				stack.RemoveAt(stack.Count - 1);
				var node = new TreemapNode { Label = popped.Label, Value = popped.Value, Children = popped.Children };

				if (stack.Count > 0)
					stack[^1].Children.Add(node);
				else
					roots.Add(node);
			}

			stack.Add((indent, [], label, value));
		}

		while (stack.Count > 0)
		{
			var popped = stack[^1];
			stack.RemoveAt(stack.Count - 1);
			var node = new TreemapNode { Label = popped.Label, Value = popped.Value, Children = popped.Children };

			if (stack.Count > 0)
				stack[^1].Children.Add(node);
			else
				roots.Add(node);
		}

		return new TreemapDiagram { Roots = roots };
	}
}
