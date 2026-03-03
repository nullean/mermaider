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

	internal static TreemapDiagram Parse(string[] lines)
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
