using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class XyChartParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^xychart(?:-beta)?(?:\s+(horizontal|vertical))?(?:\s+title\s+(.+))?$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex HeaderPattern();

	[GeneratedRegex(@"^title\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex TitlePattern();

	[GeneratedRegex(@"^x-axis\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex XAxisPattern();

	[GeneratedRegex(@"^y-axis\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex YAxisPattern();

	[GeneratedRegex(@"^(bar|line|box)(?:\s+(.+?))?\s*\[(.+)\]\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex SeriesPattern();

	[GeneratedRegex(@"^(title|x-axis|y-axis|bar|line|box)(?=[\s\[]|$)", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex KeywordPattern();

	[GeneratedRegex(@"^[+-]?(?:\d+(?:\.\d+)?|\.\d+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex NumberPattern();

	// min --> max, with an optional title in front: "Revenue" 4000 --> 11000
	[GeneratedRegex(
		@"^(?:(?:""[^""]*""|(?:(?!-->).)+?)\s+)?[+-]?(?:\d+(?:\.\d+)?|\.\d+)\s*-->\s*[+-]?(?:\d+(?:\.\d+)?|\.\d+)$",
		RegexOptions.None,
		TimeoutMs
	)]
	private static partial Regex RangePattern();

	[GeneratedRegex(@"^([^\[\]]*?)\s*\[([^\[\]]*)\]$", RegexOptions.None, TimeoutMs)]
	private static partial Regex CategoriesPattern();

	[GeneratedRegex(@"^[+-]?(?:\d+(?:\.\d+)?|\.\d+)(?:\s+""[^""]*"")?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ValuePattern();

	[GeneratedRegex(@"""[^""]*""", RegexOptions.None, TimeoutMs)]
	private static partial Regex QuotedPattern();

	[GeneratedRegex(@"^""[^""]*""$", RegexOptions.None, TimeoutMs)]
	private static partial Regex WholeQuotedPattern();

	internal static XyChart Parse(string[] lines, bool strict = true)
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

	private static XyChart ParseCore(string[] lines, bool strict)
	{
		var horizontal = false;
		string? title = null;
		string? xTitle = null;
		string? yTitle = null;
		IReadOnlyList<string>? xCategories = null;
		double? xMin = null, xMax = null, yMin = null, yMax = null;
		var series = new List<XySeries>();

		var header = HeaderPattern().Match(lines[0].Trim());
		if (strict)
		{
			var text = lines[0].Trim();
			if (!header.Success)
				throw new MermaidParseException(
					"unexpected text after 'xychart-beta'",
					text,
					"'horizontal' or 'vertical', then an optional 'title <text>'",
					column: text.IndexOf(' ') + 2
				);
			if (header.Groups[2].Success)
				RequireValidTitle(text, header.Groups[2].Value.Trim(), header.Groups[2].Index + 1);
		}

		if (header.Success)
		{
			if (header.Groups[1].Success)
				horizontal = header.Groups[1].Value.Equals("horizontal", StringComparison.OrdinalIgnoreCase);
			if (header.Groups[2].Success)
				title = Unquote(header.Groups[2].Value.Trim());
		}

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			if (strict)
				RequireValidStatement(line);

			var titleMatch = TitlePattern().Match(line);
			if (titleMatch.Success)
			{
				title = Unquote(titleMatch.Groups[1].Value.Trim());
				continue;
			}

			var xMatch = XAxisPattern().Match(line);
			if (xMatch.Success)
			{
				ParseAxis(xMatch.Groups[1].Value.Trim(), ref xTitle, ref xCategories, ref xMin, ref xMax, allowCategories: true);
				continue;
			}

			var yMatch = YAxisPattern().Match(line);
			if (yMatch.Success)
			{
				IReadOnlyList<string>? ignored = null;
				ParseAxis(yMatch.Groups[1].Value.Trim(), ref yTitle, ref ignored, ref yMin, ref yMax, allowCategories: false);
				continue;
			}

			var sMatch = SeriesPattern().Match(line);
			if (!sMatch.Success)
				continue;

			var type = sMatch.Groups[1].Value.ToLowerInvariant() switch
			{
				"bar" => XySeriesType.Bar,
				"box" => XySeriesType.Box,
				_ => XySeriesType.Line,
			};
			string? name = null;
			if (sMatch.Groups[2].Success)
			{
				var rawName = sMatch.Groups[2].Value.Trim();
				if (rawName.Length > 0)
					name = Unquote(rawName);
			}
			series.Add(new XySeries(type, name, ParseNumberList(sMatch.Groups[3].Value)));
		}

		return new XyChart
		{
			Title = title,
			Horizontal = horizontal,
			XAxisTitle = xTitle,
			XCategories = xCategories,
			XMin = xMin,
			XMax = xMax,
			YAxisTitle = yTitle,
			YMin = yMin,
			YMax = yMax,
			Series = series,
		};
	}

	private static void ParseAxis(
		string body,
		ref string? axisTitle,
		ref IReadOnlyList<string>? categories,
		ref double? min,
		ref double? max,
		bool allowCategories
	)
	{
		var bracket = body.IndexOf('[');
		if (bracket >= 0 && allowCategories)
		{
			var before = body[..bracket].Trim();
			if (before.Length > 0)
				axisTitle = Unquote(before);
			var end = body.LastIndexOf(']');
			var list = end > bracket ? body[(bracket + 1)..end] : body[(bracket + 1)..];
			categories = ParseCategoryList(list);
			return;
		}

		if (body.Contains("-->", StringComparison.Ordinal))
		{
			// title? min --> max  (title may be quoted multi-word)
			var parts = SplitAxisRange(body);
			if (parts.Title is { Length: > 0 })
				axisTitle = parts.Title;
			if (parts.Min is not null)
				min = parts.Min;
			if (parts.Max is not null)
				max = parts.Max;
			return;
		}

		if (body.Length > 0)
			axisTitle = Unquote(body);
	}

	private static (string? Title, double? Min, double? Max) SplitAxisRange(string body)
	{
		// Prefer quoted title then range: "Revenue (in $)" 4000 --> 11000
		if (body.StartsWith('"'))
		{
			var close = body.IndexOf('"', 1);
			if (close > 0)
			{
				var title = body[1..close];
				var rest = body[(close + 1)..].Trim();
				if (TryParseRange(rest, out var mn, out var mx))
					return (title, mn, mx);
				return (title, null, null);
			}
		}

		var tokens = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		for (var p = 0; p < tokens.Length; p++)
		{
			if (tokens[p] != "-->" || p < 1 || p + 1 >= tokens.Length)
				continue;
			if (
				!double.TryParse(tokens[p - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var mn) ||
				!double.TryParse(tokens[p + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var mx) || double.IsNaN(mn) || double.IsNaN(
					mx
				) || double.IsInfinity(mn) || double.IsInfinity(mx)
			)
				continue;
			string? title = null;
			if (p >= 2)
				title = Unquote(string.Join(' ', tokens.Take(p - 1)));
			return (title, mn, mx);
		}

		return (Unquote(body), null, null);
	}

	private static bool TryParseRange(string text, out double min, out double max)
	{
		min = 0;
		max = 0;
		var arrow = text.IndexOf("-->", StringComparison.Ordinal);
		if (arrow < 0)
			return false;
		var left = text[..arrow].Trim();
		var right = text[(arrow + 3)..].Trim();
		return double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out min) && double.TryParse(
			right,
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out max
		) && !double.IsNaN(min) && !double.IsNaN(max) && !double.IsInfinity(min) && !double.IsInfinity(max);
	}

	private static List<string> ParseCategoryList(string list)
	{
		var result = new List<string>();
		var sb = new System.Text.StringBuilder();
		var inQuotes = false;
		for (var i = 0; i < list.Length; i++)
		{
			var c = list[i];
			if (c == '"')
			{
				if (inQuotes && i + 1 < list.Length && list[i + 1] == '"')
				{
					_ = sb.Append('"');
					i++;
					continue;
				}
				inQuotes = !inQuotes;
				continue;
			}
			if (c == ',' && !inQuotes)
			{
				var item = sb.ToString().Trim();
				if (item.Length > 0)
					result.Add(item);
				_ = sb.Clear();
				continue;
			}
			_ = sb.Append(c);
		}
		var last = sb.ToString().Trim();
		if (last.Length > 0)
			result.Add(last);
		return result;
	}

	private static List<double> ParseNumberList(string list)
	{
		var result = new List<double>();
		foreach (var part in list.Split(','))
		{
			var trimmed = part.Trim();
			// Optional point labels: 540 "PaLM" — take leading number only
			var cut = -1;
			for (var i = 0; i < trimmed.Length; i++)
			{
				if (trimmed[i] is ' ' or '"')
				{
					cut = i;
					break;
				}
			}
			if (cut > 0)
				trimmed = trimmed[..cut].Trim();
			// Keep index alignment with categories: bad tokens become 0 (not dropped).
			if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && !double.IsNaN(v) && !double.IsInfinity(v))
				result.Add(v);
			else
				result.Add(0);
		}
		return result;
	}

	/// <summary>Throw for the first thing wrong with a statement, judged by what Mermaid accepts.</summary>
	private static void RequireValidStatement(string line)
	{
		var keyword = KeywordPattern().Match(line);
		if (!keyword.Success)
			throw new MermaidParseException(
				$"unexpected text '{line}'",
				line,
				"'title', 'x-axis', 'y-axis', 'bar' or 'line'",
				column: 1
			);

		var name = keyword.Value.ToLowerInvariant();
		var rest = line[keyword.Length..].Trim();
		var column = line.Length - line[keyword.Length..].TrimStart().Length + 1;
		switch (name)
		{
			case "title":
				if (rest.Length == 0)
					throw new MermaidParseException("title has no text", line, "a title after 'title', e.g. 'title \"Sales\"'", column: line.Length + 1);
				RequireValidTitle(line, rest, column);
				break;
			case "x-axis" or "y-axis":
				RequireValidAxis(line, name, rest, column);
				break;
			default:
				RequireValidSeries(line, name, rest, column);
				break;
		}
	}

	/// <summary>A title is one quoted string, or plain text with no quote in it.</summary>
	private static void RequireValidTitle(string line, string title, int column)
	{
		if (title[0] == '"')
		{
			if (title.Length == 1 || title.IndexOf('"', 1) < 0)
				throw new MermaidParseException("title opened with '\"' is never closed", line, "a closing '\"' after the title", column: column);
			if (title == "\"\"")
				throw new MermaidParseException("title is empty", line, "some text in the quotes", column: column);
			if (!WholeQuotedPattern().IsMatch(title))
				throw new MermaidParseException(
					"unexpected text after the quoted title",
					line,
					"the end of the line after the closing '\"'",
					column: column + title.IndexOf('"', 1) + 1
				);
		}
		else if (title.Contains('"', StringComparison.Ordinal))
		{
			throw new MermaidParseException(
				"unexpected '\"' in the title",
				line,
				"the whole title in double quotes, or no quotes in it",
				column: column + title.IndexOf('"')
			);
		}
	}

	private static void RequireValidAxis(string line, string name, string body, int column)
	{
		if (body.Length == 0)
			throw new MermaidParseException($"{name} has no title or range", line, $"a title, categories '[a, b]' or a range '0 --> 100' after '{name}'", column: line.Length + 1);

		var open = body.IndexOf('[');
		if (open >= 0 || body.Contains(']', StringComparison.Ordinal))
		{
			if (name == "y-axis")
				throw new MermaidParseException(
					"the y-axis cannot list categories",
					line,
					"a title and/or a numeric range '0 --> 100'; categories belong on the x-axis",
					column: column + Math.Max(open, 0)
				);

			var categories = CategoriesPattern().Match(body);
			if (!categories.Success)
				throw new MermaidParseException(
					body.Contains(']', StringComparison.Ordinal) && open >= 0 ? "unexpected text around the category list" : open < 0 ? "']' without a '['" : "category list is never closed",
					line,
					"categories in square brackets, e.g. 'x-axis [jan, feb, mar]'",
					column: column + (open < 0 ? body.IndexOf(']') : open)
				);

			var title = categories.Groups[1].Value.Trim();
			if (title.Length > 0)
				RequireValidAxisTitle(line, title, column);

			var list = categories.Groups[2].Value;
			if (list.Trim().Length == 0)
				throw new MermaidParseException("category list is empty", line, "at least one category, e.g. [jan, feb]", column: column + open);

			foreach (var item in SplitList(list))
			{
				var text = item.Trim();
				if (text.Length == 0)
					throw new MermaidParseException("category list has an empty entry", line, "a category between the commas", column: column + categories.Groups[2].Index);
				if (text[0] == '"' && !WholeQuotedPattern().IsMatch(text))
					throw new MermaidParseException(
						text.IndexOf('"', 1) < 0 ? "category opened with '\"' is never closed" : $"unexpected text after the quoted category {text}",
						line,
						"a category in double quotes, or plain text",
						column: column + Math.Max(body.IndexOf(text, StringComparison.Ordinal), 0)
					);
			}
			return;
		}

		if (QuotedPattern().Replace(body, "\"\"").Contains("-->", StringComparison.Ordinal))
		{
			if (!RangePattern().IsMatch(body))
				throw new MermaidParseException(
					"range is not 'min --> max' with two numbers",
					line,
					"a title (optional), then 'min --> max', e.g. '\"Revenue\" 4000 --> 11000'",
					column: column + Math.Max(body.IndexOf("-->", StringComparison.Ordinal), 0)
				);
			return;
		}

		RequireValidAxisTitle(line, body, column);
	}

	/// <summary>An axis title that opens with a quote must be that one quoted string; plain text is taken as written.</summary>
	private static void RequireValidAxisTitle(string line, string title, int column)
	{
		if (title[0] != '"' || WholeQuotedPattern().IsMatch(title))
			return;
		var close = title.IndexOf('"', 1);
		throw new MermaidParseException(
			close < 0 ? "axis title opened with '\"' is never closed" : "unexpected text after the quoted axis title",
			line,
			close < 0 ? "a closing '\"' after the title" : "the end of the line after the closing '\"'",
			column: close < 0 ? column : column + close + 1
		);
	}

	private static void RequireValidSeries(string line, string name, string body, int column)
	{
		var open = body.IndexOf('[');
		if (open < 0)
			throw new MermaidParseException($"{name} series has no values", line, $"values in square brackets, e.g. '{name} [10, 20, 30]'", column: line.Length + 1);

		var close = body.LastIndexOf(']');
		if (close < open)
			throw new MermaidParseException($"values of the {name} series are never closed", line, "']' after the last value", column: column + open);
		if (close != body.Length - 1)
			throw new MermaidParseException($"unexpected text after the values of the {name} series", line, "the end of the line after ']'", column: column + close + 1);

		var title = body[..open].Trim();
		if (title.Length > 0 && title[0] == '"' && !WholeQuotedPattern().IsMatch(title))
			throw new MermaidParseException(
				title.IndexOf('"', 1) < 0 ? $"name of the {name} series opened with '\"' is never closed" : $"unexpected text after the quoted name of the {name} series",
				line,
				"the name in double quotes, then the values in square brackets",
				column: column
			);

		var list = body[(open + 1)..close];
		if (list.Trim().Length == 0)
			throw new MermaidParseException($"{name} series has no values", line, $"at least one number, e.g. '{name} [10, 20, 30]'", column: column + open);

		var offset = column + open + 1;
		foreach (var item in SplitList(list))
		{
			var value = item.Trim();
			if (value.Length == 0)
				throw new MermaidParseException($"the {name} series has an empty value", line, "a number between the commas", column: offset);
			if (!ValuePattern().IsMatch(value))
				throw new MermaidParseException(
					$"value '{value}' of the {name} series is not a number",
					line,
					"numbers such as 12, -3.5 or .5, separated by commas",
					column: offset + (item.Length - item.TrimStart().Length)
				);
			offset += item.Length + 1;
		}
	}

	/// <summary>Split a list on commas outside double quotes.</summary>
	private static List<string> SplitList(string list)
	{
		var items = new List<string>();
		var start = 0;
		var quoted = false;
		for (var i = 0; i < list.Length; i++)
		{
			if (list[i] == '"')
				quoted = !quoted;
			else if (list[i] == ',' && !quoted)
			{
				items.Add(list[start..i]);
				start = i + 1;
			}
		}
		items.Add(list[start..]);
		return items;
	}

	private static string Unquote(string value)
	{
		value = value.Trim();
		if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
			return value[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
		return value;
	}
}
