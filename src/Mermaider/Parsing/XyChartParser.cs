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

	[GeneratedRegex(@"^(bar|line)(?:\s+(.+?))?\s*\[(.+)\]\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex SeriesPattern();

	internal static XyChart Parse(string[] lines)
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

	private static XyChart ParseCore(string[] lines)
	{
		var horizontal = false;
		string? title = null;
		string? xTitle = null;
		string? yTitle = null;
		IReadOnlyList<string>? xCategories = null;
		double? xMin = null, xMax = null, yMin = null, yMax = null;
		var series = new List<XySeries>();

		var header = HeaderPattern().Match(lines[0]);
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

			var type = sMatch.Groups[1].Value.Equals("bar", StringComparison.OrdinalIgnoreCase)
				? XySeriesType.Bar
				: XySeriesType.Line;
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
		bool allowCategories)
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
			if (!double.TryParse(tokens[p - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var mn))
				continue;
			if (!double.TryParse(tokens[p + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var mx))
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
		return double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out min)
			&& double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out max)
			&& !double.IsNaN(min) && !double.IsNaN(max)
			&& !double.IsInfinity(min) && !double.IsInfinity(max);
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
			if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
				&& !double.IsNaN(v) && !double.IsInfinity(v))
				result.Add(v);
		}
		return result;
	}

	private static string Unquote(string value)
	{
		value = value.Trim();
		if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
			return value[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
		return value;
	}
}
