using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class MindmapParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"\(\((.+?)\)\)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex CircleShape();

	[GeneratedRegex(@"\[(.+?)\]$", RegexOptions.None, TimeoutMs)]
	private static partial Regex SquareShape();

	[GeneratedRegex(@"(?<!\()\(([^()]+?)\)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex RoundedShape();

	[GeneratedRegex(@"\)\)(.+?)\(\($", RegexOptions.None, TimeoutMs)]
	private static partial Regex CloudShape();

	[GeneratedRegex(@"\{\{(.+?)\}\}$", RegexOptions.None, TimeoutMs)]
	private static partial Regex HexagonShape();

	[GeneratedRegex(@"(?<!\))\)(.+?)\($", RegexOptions.None, TimeoutMs)]
	private static partial Regex BangShape();

	internal static MindmapDiagram Parse(string[] lines)
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

	private static MindmapDiagram ParseCore(string[] lines)
	{
		if (lines.Length < 2)
			return new MindmapDiagram { Root = new MindmapNode { Label = "mindmap", Children = [] } };

		var stack = new List<(int Indent, string Label, MindmapShape Shape, List<MindmapNode> Children)>();

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

			var (label, shape) = ParseNodeText(trimmed);

			while (stack.Count > 0 && stack[^1].Indent >= indent)
			{
				var popped = stack[^1];
				stack.RemoveAt(stack.Count - 1);
				var node = new MindmapNode { Label = popped.Label, Shape = popped.Shape, Children = popped.Children };
				if (stack.Count > 0)
					stack[^1].Children.Add(node);
				else
					return new MindmapDiagram { Root = node };
			}

			stack.Add((indent, label, shape, []));
		}

		while (stack.Count > 1)
		{
			var popped = stack[^1];
			stack.RemoveAt(stack.Count - 1);
			var node = new MindmapNode { Label = popped.Label, Shape = popped.Shape, Children = popped.Children };
			stack[^1].Children.Add(node);
		}

		if (stack.Count == 1)
		{
			var root = stack[0];
			return new MindmapDiagram { Root = new MindmapNode { Label = root.Label, Shape = root.Shape, Children = root.Children } };
		}

		return new MindmapDiagram { Root = new MindmapNode { Label = "mindmap", Children = [] } };
	}

	private static (string Label, MindmapShape Shape) ParseNodeText(string text)
	{
		var m = CircleShape().Match(text);
		if (m.Success)
			return (m.Groups[1].Value, MindmapShape.Circle);

		m = CloudShape().Match(text);
		if (m.Success)
			return (m.Groups[1].Value, MindmapShape.Cloud);

		m = HexagonShape().Match(text);
		if (m.Success)
			return (m.Groups[1].Value, MindmapShape.Hexagon);

		m = BangShape().Match(text);
		if (m.Success)
			return (m.Groups[1].Value, MindmapShape.Bang);

		m = SquareShape().Match(text);
		if (m.Success)
			return (m.Groups[1].Value, MindmapShape.Square);

		m = RoundedShape().Match(text);
		if (m.Success)
			return (m.Groups[1].Value, MindmapShape.Rounded);

		return (text, MindmapShape.Default);
	}
}
