using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class TreeViewParser
{
	private const int TimeoutMs = 2000;
	private const int TabWidth = 4;

	[GeneratedRegex(@":::([\w][\w-]*)", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassAnnotation();

	[GeneratedRegex(@"##\s*(.+?)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex DescriptionAnnotation();

	[GeneratedRegex(@"icon\(([^)]*)\)", RegexOptions.None, TimeoutMs)]
	private static partial Regex IconAnnotation();

	[GeneratedRegex(@"[├└┣┗]", RegexOptions.None, TimeoutMs)]
	private static partial Regex BoxDrawingChar();

	[GeneratedRegex(@"(?:^|\s)icon\(", RegexOptions.None, TimeoutMs)]
	private static partial Regex IconOpen();

	[GeneratedRegex(@"^(?:[\w-]*:)?[\w-]+$", RegexOptions.None, TimeoutMs)]
	private static partial Regex IconName();

	internal static TreeViewDiagram Parse(string[] lines, bool strict = true)
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

	// Mermaid refuses what the lenient reading below would drop or fold into a label: a node with no name, a quote
	// that is never closed, and an icon(...) that is unclosed or has an invalid name.
	private static void Validate(string[] lines)
	{
		var isBoxDrawing = DetectBoxDrawingMode(lines);
		string? openQuote = null;
		var openQuoteColumn = 0;

		for (var i = 1; i < lines.Length; i++)
		{
			var raw = ExpandTabs(lines[i]);
			var text = raw.Trim();
			if (text.Length == 0)
				continue;

			string body;
			var bodyOffset = 0;

			if (openQuote is not null)
			{
				// A quoted name may run over several lines; the node carries on after the closing quote.
				var close = raw.IndexOf('"');
				if (close < 0)
					continue;
				openQuote = null;
				body = raw[(close + 1)..];
				bodyOffset = close + 1;
			}
			else
			{
				var content = isBoxDrawing ? StripBoxDrawingPrefix(text) : text;
				var offset = text.Length - content.Length;
				if (content.Length == 0)
				{
					// A line of only vertical rules is a continuation; a branch with nothing after it is not.
					if (isBoxDrawing && BoxDrawingChar().IsMatch(text))
						throw new MermaidParseException("node has no name after the branch", text, "a file or directory name after the box-drawing prefix", column: text.Length + 1);
					continue;
				}

				if (content.StartsWith("##", StringComparison.Ordinal) || content.StartsWith(":::", StringComparison.Ordinal) || content.StartsWith("icon(", StringComparison.Ordinal))
					throw new MermaidParseException("node has no name, only an annotation", text, "a file or directory name before the annotation", column: offset + 1);

				body = content;
				bodyOffset = offset;
				if (content[0] == '"')
				{
					var close = content.IndexOf('"', 1);
					if (close < 0)
					{
						openQuote = text;
						openQuoteColumn = offset + 1;
						continue;
					}
					body = content[(close + 1)..];
					bodyOffset += close + 1;
				}
			}

			var descAt = body.IndexOf("##", StringComparison.Ordinal);
			if (descAt >= 0)
				body = body[..descAt];

			var open = IconOpen().Match(body);
			if (!open.Success)
				continue;

			var nameStart = open.Index + open.Length;
			var closeParen = body.IndexOf(')', nameStart);
			var iconColumn = bodyOffset + open.Index + (open.Value[0] == 'i' ? 0 : 1) + 1;
			if (closeParen < 0)
				throw new MermaidParseException("icon( is never closed", text, "')' to end 'icon(name)'", column: iconColumn);

			var name = body[nameStart..closeParen];
			if (name.Length > 0 && !IconName().IsMatch(name))
				throw new MermaidParseException($"icon name '{name}' is not valid", text, "'icon(name)' or 'icon(pack:name)' with letters, digits, '_' and '-'", column: bodyOffset + nameStart + 1);
		}

		if (openQuote is not null)
			throw new MermaidParseException("quoted name is never closed", openQuote, "a closing '\"'", column: openQuoteColumn);
	}

	private static TreeViewDiagram ParseCore(string[] lines)
	{
		if (lines.Length < 2)
			return new TreeViewDiagram { Roots = [] };

		var isBoxDrawing = DetectBoxDrawingMode(lines);

		var nodes = isBoxDrawing ? ParseBoxDrawing(lines) : ParseIndentation(lines);

		return new TreeViewDiagram { Roots = nodes };
	}

	private static bool DetectBoxDrawingMode(string[] lines)
	{
		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];
			if (line.Trim().Length == 0)
				continue;
			if (BoxDrawingChar().IsMatch(line))
				return true;
			return false;
		}
		return false;
	}

	private static IReadOnlyList<TreeViewNode> ParseIndentation(string[] lines)
	{
		var stack = new List<(int Indent, string Label, bool IsDir, string? Desc, string? CssClass, string? Icon, List<TreeViewNode> Children)>();

		for (var i = 1; i < lines.Length; i++)
		{
			var raw = ExpandTabs(lines[i]);
			var trimmed = raw.Trim();
			if (trimmed.Length == 0)
				continue;

			var indent = CountLeadingSpaces(raw);
			var (label, isDir, desc, cssClass, icon) = ParseNodeContent(trimmed);

			// Pop nodes deeper or at same level
			while (stack.Count > 0 && stack[^1].Indent >= indent)
			{
				var popped = stack[^1];
				stack.RemoveAt(stack.Count - 1);
				var node = BuildNode(popped);
				if (stack.Count > 0)
					stack[^1].Children.Add(node);
				else
					stack.Insert(0, (-1, node.Label, node.IsDirectory, node.Description, node.CssClass, node.Icon, [node]));
			}

			stack.Add((indent, label, isDir, desc, cssClass, icon, []));
		}

		// Flush remaining stack
		while (stack.Count > 1)
		{
			var popped = stack[^1];
			stack.RemoveAt(stack.Count - 1);
			var node = BuildNode(popped);
			stack[^1].Children.Add(node);
		}

		if (stack.Count == 1)
		{
			var last = stack[0];
			if (last.Indent == -1)
				return last.Children;
			return [BuildNode(last)];
		}

		return [];
	}

	private static IReadOnlyList<TreeViewNode> ParseBoxDrawing(string[] lines)
	{
		var result = new List<(int Depth, string Label, bool IsDir, string? Desc, string? CssClass, string? Icon)>();

		for (var i = 1; i < lines.Length; i++)
		{
			var raw = ExpandTabs(lines[i]);
			var trimmed = raw.Trim();
			if (trimmed.Length == 0)
				continue;

			var depth = InferBoxDrawingDepth(raw);
			var content = StripBoxDrawingPrefix(trimmed);
			var (label, isDir, desc, cssClass, icon) = ParseNodeContent(content);
			result.Add((depth, label, isDir, desc, cssClass, icon));
		}

		return BuildTreeFromFlatList(result);
	}

	private static int InferBoxDrawingDepth(string line)
	{
		var match = BoxDrawingChar().Match(line);
		if (!match.Success)
			return 0;

		var col = match.Index;
		// Each nesting level in box-drawing is typically 4 columns apart
		// (│   or ┃   before the branch character)
		return col / 4;
	}

	private static string StripBoxDrawingPrefix(string text)
	{
		var span = text.AsSpan();
		var i = 0;
		while (i < span.Length)
		{
			var c = span[i];
			if (c is '├' or '└' or '│' or '─' or '┣' or '┗' or '┃' or '━' or '┬' or '┳' or ' ')
			{
				i++;
				continue;
			}
			break;
		}
		return span[i..].ToString().TrimStart();
	}

	private static IReadOnlyList<TreeViewNode> BuildTreeFromFlatList(
		List<(int Depth, string Label, bool IsDir, string? Desc, string? CssClass, string? Icon)> items
	)
	{
		if (items.Count == 0)
			return [];

		var roots = new List<TreeViewNode>();
		var stack = new List<(int Depth, string Label, bool IsDir, string? Desc, string? CssClass, string? Icon, List<TreeViewNode> Children)>();

		foreach (var (depth, label, isDir, desc, cssClass, icon) in items)
		{
			while (stack.Count > 0 && stack[^1].Depth >= depth)
			{
				var popped = stack[^1];
				stack.RemoveAt(stack.Count - 1);
				var node = new TreeViewNode
				{
					Label = popped.Label,
					IsDirectory = popped.IsDir,
					Description = popped.Desc,
					CssClass = popped.CssClass,
					Icon = popped.Icon,
					Children = popped.Children,
				};
				if (stack.Count > 0)
					stack[^1].Children.Add(node);
				else
					roots.Add(node);
			}

			stack.Add((depth, label, isDir, desc, cssClass, icon, []));
		}

		while (stack.Count > 0)
		{
			var popped = stack[^1];
			stack.RemoveAt(stack.Count - 1);
			var node = new TreeViewNode
			{
				Label = popped.Label,
				IsDirectory = popped.IsDir,
				Description = popped.Desc,
				CssClass = popped.CssClass,
				Icon = popped.Icon,
				Children = popped.Children,
			};
			if (stack.Count > 0)
				stack[^1].Children.Add(node);
			else
				roots.Add(node);
		}

		return roots;
	}

	private static (string Label, bool IsDirectory, string? Description, string? CssClass, string? Icon) ParseNodeContent(string text)
	{
		string? desc = null;
		string? cssClass = null;
		string? icon = null;
		var working = text;

		// Extract :::className
		var classMatch = ClassAnnotation().Match(working);
		if (classMatch.Success)
		{
			cssClass = classMatch.Groups[1].Value;
			working = working[..classMatch.Index] + working[(classMatch.Index + classMatch.Length)..];
			working = working.Trim();
		}

		// Extract ## description
		var descMatch = DescriptionAnnotation().Match(working);
		if (descMatch.Success)
		{
			desc = descMatch.Groups[1].Value.Trim();
			working = working[..descMatch.Index].Trim();
		}

		// Extract icon(name)
		var iconMatch = IconAnnotation().Match(working);
		if (iconMatch.Success)
		{
			var iconName = iconMatch.Groups[1].Value.Trim();
			icon = iconName.Length == 0 || iconName.Equals("none", StringComparison.OrdinalIgnoreCase) ? "" : iconName;
			working = working[..iconMatch.Index] + working[(iconMatch.Index + iconMatch.Length)..];
			working = working.Trim();
		}

		// Detect directory (trailing /)
		var isDir = working.EndsWith('/');
		if (isDir)
			working = working[..^1];

		// Strip quotes
		if (working.Length >= 2 && working[0] == '"' && working[^1] == '"')
			working = working[1..^1];

		return (working, isDir, desc, cssClass, icon);
	}

	private static TreeViewNode BuildNode(
		(int Indent, string Label, bool IsDir, string? Desc, string? CssClass, string? Icon, List<TreeViewNode> Children) entry
	) =>
		new()
		{
			Label = entry.Label,
			IsDirectory = entry.IsDir,
			Description = entry.Desc,
			CssClass = entry.CssClass,
			Icon = entry.Icon,
			Children = entry.Children,
		};

	private static string ExpandTabs(string line)
	{
		if (!line.Contains('\t'))
			return line;
		return line.Replace("\t", new string(' ', TabWidth));
	}

	private static int CountLeadingSpaces(string line)
	{
		var n = 0;
		foreach (var c in line)
		{
			if (c == ' ')
				n++;
			else
				break;
		}
		return n;
	}
}
