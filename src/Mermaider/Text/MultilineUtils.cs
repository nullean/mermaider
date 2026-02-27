using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.ObjectPool;

namespace Mermaider.Text;

/// <summary>
/// Shared utilities for rendering multi-line text in SVG with inline formatting.
/// </summary>
internal static partial class MultilineUtils
{
	private const int TimeoutMs = 2000;

	private static readonly ObjectPool<StringBuilder> s_sbPool =
		new DefaultObjectPoolProvider().CreateStringBuilderPool();

	private static readonly SearchValues<char> s_xmlSpecial = SearchValues.Create("&<>\"'");

	[GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex BrTagPattern();

	[GeneratedRegex(@"\\n", RegexOptions.None, TimeoutMs)]
	private static partial Regex EscapedNewlinePattern();

	[GeneratedRegex(@"</?(?:sub|sup|small|mark)\s*>", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex UnsupportedTagPattern();

	[GeneratedRegex(@"\*\*(.+?)\*\*", RegexOptions.None, TimeoutMs)]
	private static partial Regex MarkdownBoldPattern();

	[GeneratedRegex(@"(?<!\*)\*([^\s*](?:[^*]*[^\s*])?)\*(?!\*)", RegexOptions.None, TimeoutMs)]
	private static partial Regex MarkdownItalicPattern();

	[GeneratedRegex(@"~~(.+?)~~", RegexOptions.None, TimeoutMs)]
	private static partial Regex MarkdownStrikethroughPattern();

	[GeneratedRegex(@"<(/?)(b|strong|i|em|u|s|del)\s*>", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex FormatTagPattern();

	[GeneratedRegex(@"</?(?:b|strong|i|em|u|s|del)\s*>", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex HasFormatTagPattern();

	/// <summary>
	/// Normalize label text: strip quotes, convert &lt;br&gt; tags and \n to newlines,
	/// strip unsupported tags, convert markdown formatting to HTML tags.
	/// </summary>
	internal static string NormalizeBrTags(string label)
	{
		var unquoted = label is ['"', .., '"'] ? label[1..^1] : label;
		var result = BrTagPattern().Replace(unquoted, "\n");
		result = EscapedNewlinePattern().Replace(result, "\n");
		result = UnsupportedTagPattern().Replace(result, "");
		result = MarkdownBoldPattern().Replace(result, "<b>$1</b>");
		result = MarkdownItalicPattern().Replace(result, "<i>$1</i>");
		result = MarkdownStrikethroughPattern().Replace(result, "<s>$1</s>");
		return result;
	}

	internal static void AppendEscapedXml(StringBuilder sb, ReadOnlySpan<char> value)
	{
		if (!value.ContainsAny(s_xmlSpecial))
		{
			sb.Append(value);
			return;
		}

		foreach (var c in value)
		{
			switch (c)
			{
				case '&': sb.Append("&amp;"); break;
				case '<': sb.Append("&lt;"); break;
				case '>': sb.Append("&gt;"); break;
				case '"': sb.Append("&quot;"); break;
				case '\'': sb.Append("&#39;"); break;
				default: sb.Append(c); break;
			}
		}
	}

	internal static void AppendEscapedAttr(StringBuilder sb, ReadOnlySpan<char> value)
	{
		if (!value.ContainsAny(s_xmlSpecial))
		{
			sb.Append(value);
			return;
		}

		foreach (var c in value)
		{
			switch (c)
			{
				case '&': sb.Append("&amp;"); break;
				case '"': sb.Append("&quot;"); break;
				case '<': sb.Append("&lt;"); break;
				case '>': sb.Append("&gt;"); break;
				default: sb.Append(c); break;
			}
		}
	}

	internal static void AppendLineContent(StringBuilder sb, ReadOnlySpan<char> line)
	{
		if (!line.Contains('<'))
		{
			AppendEscapedXml(sb, line);
			return;
		}

		var lineStr = line.ToString();
		if (!HasFormatTagPattern().IsMatch(lineStr))
		{
			AppendEscapedXml(sb, line);
			return;
		}

		var bold = false;
		var italic = false;
		var underline = false;
		var strikethrough = false;
		var lastIndex = 0;

		foreach (var match in FormatTagPattern().EnumerateMatches(lineStr))
		{
			var m = FormatTagPattern().Match(lineStr, match.Index, match.Length);
			if (m.Index > lastIndex)
				AppendStyledSegment(sb, lineStr.AsSpan(lastIndex, m.Index - lastIndex), bold, italic, underline, strikethrough);

			lastIndex = m.Index + m.Length;
			var isClosing = m.Groups[1].Value == "/";
			var tag = m.Groups[2].Value.ToLowerInvariant();

			switch (tag)
			{
				case "b" or "strong":
					bold = !isClosing;
					break;
				case "i" or "em":
					italic = !isClosing;
					break;
				case "u":
					underline = !isClosing;
					break;
				case "s" or "del":
					strikethrough = !isClosing;
					break;
			}
		}

		if (lastIndex < lineStr.Length)
			AppendStyledSegment(sb, lineStr.AsSpan(lastIndex), bold, italic, underline, strikethrough);
	}

	private static void AppendStyledSegment(
		StringBuilder sb,
		ReadOnlySpan<char> text,
		bool bold, bool italic, bool underline, bool strikethrough)
	{
		if (!bold && !italic && !underline && !strikethrough)
		{
			AppendEscapedXml(sb, text);
			return;
		}

		sb.Append("<tspan");
		if (bold) sb.Append(" font-weight=\"bold\"");
		if (italic) sb.Append(" font-style=\"italic\"");
		if (underline || strikethrough)
		{
			sb.Append(" text-decoration=\"");
			if (underline) sb.Append("underline");
			if (underline && strikethrough) sb.Append(' ');
			if (strikethrough) sb.Append("line-through");
			sb.Append('"');
		}
		sb.Append('>');
		AppendEscapedXml(sb, text);
		sb.Append("</tspan>");
	}

	internal static void AppendMultilineText(
		StringBuilder sb,
		string text,
		double cx, double cy,
		double fontSize,
		string attrs,
		double baselineShift = 0.35)
	{
		var lines = text.Split('\n');

		if (lines.Length == 1)
		{
			var dy = fontSize * baselineShift;
			sb.Append("<text x=\"").Append(cx).Append("\" y=\"").Append(cy)
				.Append("\" ").Append(attrs)
				.Append(" dy=\"").Append(dy).Append("\">");
			AppendLineContent(sb, text.AsSpan());
			sb.Append("</text>");
			return;
		}

		var lineHeight = fontSize * TextMetrics.LineHeightRatio;
		var firstDy = -((lines.Length - 1) / 2.0) * lineHeight + fontSize * baselineShift;

		sb.Append("<text x=\"").Append(cx).Append("\" y=\"").Append(cy)
			.Append("\" ").Append(attrs).Append('>');

		for (var i = 0; i < lines.Length; i++)
		{
			var dy = i == 0 ? firstDy : lineHeight;
			sb.Append("<tspan x=\"").Append(cx).Append("\" dy=\"").Append(dy).Append("\">");
			AppendLineContent(sb, lines[i].AsSpan());
			sb.Append("</tspan>");
		}

		sb.Append("</text>");
	}

	internal static void AppendMultilineTextWithBackground(
		StringBuilder sb,
		string text,
		double cx, double cy,
		double textWidth, double textHeight,
		double fontSize,
		double padding,
		string textAttrs,
		string bgAttrs)
	{
		var bgWidth = textWidth + padding * 2;
		var bgHeight = textHeight + padding * 2;

		sb.Append("<rect x=\"").Append(cx - bgWidth / 2)
			.Append("\" y=\"").Append(cy - bgHeight / 2)
			.Append("\" width=\"").Append(bgWidth)
			.Append("\" height=\"").Append(bgHeight)
			.Append("\" ").Append(bgAttrs).Append(" />\n");

		AppendMultilineText(sb, text, cx, cy, fontSize, textAttrs);
	}
}
