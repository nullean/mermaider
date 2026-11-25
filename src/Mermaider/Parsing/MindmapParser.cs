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

	internal static MindmapDiagram Parse(string[] lines, bool strict = true)
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

	// Mermaid refuses what the lenient reading below would silently flatten: a second root, and a node whose
	// shape delimiters do not pair up ("A[open", "B(x) trailing", "C))bang").
	private static void Validate(string[] lines)
	{
		var rootIndent = -1;
		for (var i = 1; i < lines.Length; i++)
		{
			var raw = lines[i];
			var text = raw.Trim();
			if (text.Length == 0)
				continue;

			var indent = raw.Length - raw.TrimStart().Length;
			if (text.StartsWith("accTitle", StringComparison.Ordinal) || text.StartsWith("accDescr", StringComparison.Ordinal))
				continue;

			if (text.StartsWith("::", StringComparison.Ordinal))
			{
				if (text.StartsWith("::icon(", StringComparison.Ordinal) && !text.EndsWith(')'))
					throw new MermaidParseException("icon decoration is never closed", text, "')' to end '::icon(...)'", column: text.Length + 1);
				continue;
			}

			if (rootIndent < 0)
				rootIndent = indent;
			else if (indent <= rootIndent)
				throw new MermaidParseException(
					$"'{text}' is a second root; a mindmap has exactly one root",
					text,
					$"an indent deeper than the root's, to make it a child of '{RootHint(lines)}'",
					column: 1
				);

			if (!ContinuesOnLaterLine(lines, ref i, text))
				RequireClosedShape(text);
		}
	}

	// A label may run over several lines: "A[first" ... "last]" is one node.
	private static bool ContinuesOnLaterLine(string[] lines, ref int i, string text)
	{
		var k = text.AsSpan().IndexOfAny("([{)");
		if (k < 0 || text.AsSpan(0, k).IndexOfAny("\"`") >= 0)
			return false;

		var rest = text[k..];
		var closer = rest[0] switch
		{
			'[' => "]",
			'(' => ")",
			'{' => "}}",
			_ => "(",
		};
		if (rest.EndsWith(closer, StringComparison.Ordinal))
			return false;

		for (var j = i + 1; j < lines.Length; j++)
		{
			if (lines[j].TrimEnd().EndsWith(closer, StringComparison.Ordinal))
			{
				i = j;
				return true;
			}
		}
		return false;
	}

	private static string RootHint(string[] lines)
	{
		for (var i = 1; i < lines.Length; i++)
		{
			var text = lines[i].Trim();
			if (text.Length > 0 && !text.StartsWith("::", StringComparison.Ordinal))
				return text;
		}
		return "the root";
	}

	private static void RequireClosedShape(string text)
	{
		var k = text.AsSpan().IndexOfAny("([{)}");
		if (k < 0)
			return;

		var id = text.AsSpan(0, k);
		if (id.IndexOfAny("\"`") >= 0)
			return;

		var rest = text[k..];
		var column = k + 1;
		switch (rest[0])
		{
			case '[':
				RequireShape(rest.EndsWith(']'), rest.Length > 2 && rest[1..^1].Length > 0, "'[' ... ']'", "']'", text, column);
				break;
			case '(':
				var circle = rest.StartsWith("((", StringComparison.Ordinal) && rest.EndsWith("))", StringComparison.Ordinal) && rest.Length >= 4;
				var body = circle ? rest[2..^2] : rest.Length >= 2 ? rest[1..^1] : "";
				RequireShape(rest.EndsWith(')') && rest.Length >= 2, body.Length > 0, "'(' ... ')'", "')'", text, column);
				break;
			case '{':
				var hex = rest.StartsWith("{{", StringComparison.Ordinal) && rest.EndsWith("}}", StringComparison.Ordinal) && rest.Length >= 4;
				RequireShape(hex, hex && rest.Length > 4, "'{{' ... '}}'", "'}}'", text, column);
				break;
			case ')':
				var inner = rest.TrimStart(')').TrimEnd('(');
				RequireShape(rest.EndsWith('(') && rest.Length >= 2, inner.Length > 0, "')' ... '('", "'('", text, column);
				break;
			default:
				throw new MermaidParseException(
					$"unexpected '{rest[0]}' in node text",
					text,
					"a node text, or a shape such as '[text]', '(text)', '((text))', '{{text}}', ')text('",
					column: column
				);
		}
	}

	private static void RequireShape(bool closed, bool hasLabel, string shape, string closer, string text, int column)
	{
		if (!closed)
			throw new MermaidParseException(
				$"node shape {shape} is never closed, or has text after it",
				text,
				$"{closer} at the end of the node",
				column: column
			);
		if (!hasLabel)
			throw new MermaidParseException($"node shape {shape} has an empty label", text, "a label between the delimiters", column: column);
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
			return (m.Groups[1].Value, MindmapShape.Bang);

		m = HexagonShape().Match(text);
		if (m.Success)
			return (m.Groups[1].Value, MindmapShape.Hexagon);

		m = BangShape().Match(text);
		if (m.Success)
			return (m.Groups[1].Value, MindmapShape.Cloud);

		m = SquareShape().Match(text);
		if (m.Success)
			return (m.Groups[1].Value, MindmapShape.Square);

		m = RoundedShape().Match(text);
		if (m.Success)
			return (m.Groups[1].Value, MindmapShape.Rounded);

		return (text, MindmapShape.Default);
	}
}
