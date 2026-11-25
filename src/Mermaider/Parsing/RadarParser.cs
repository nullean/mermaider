using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class RadarParser
{
	private const int TimeoutMs = 2000;

	private const string Statements = "'title', 'axis', 'curve', 'max', 'min', 'ticks', 'graticule' or 'showLegend'";

	[GeneratedRegex(@"^title\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex TitlePattern();

	[GeneratedRegex(@"^axis\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex AxisPattern();

	[GeneratedRegex(@"^curve\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex CurvePattern();

	[GeneratedRegex(@"^(?:""([^""]+)""|(\S+?))\s*(?:\[\s*""([^""]*)""\s*\])?\s*\{(.+?)\}", RegexOptions.None, TimeoutMs)]
	private static partial Regex CurveDetailPattern();

	[GeneratedRegex(@"^max\s+(\d+(?:\.\d+)?)\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex MaxPattern();

	[GeneratedRegex(@"^min\s+(\d+(?:\.\d+)?)\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex MinPattern();

	[GeneratedRegex(@"^ticks\s+(\d+)(?:\.\d+)?\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex TicksPattern();

	[GeneratedRegex(@"^graticule\s+(circle|polygon)\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex GraticulePattern();

	[GeneratedRegex(@"^showLegend\s+(true|false)\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex ShowLegendPattern();

	[GeneratedRegex(@"^(\S+?)(?=\s|$)", RegexOptions.None, TimeoutMs)]
	private static partial Regex KeywordPattern();

	[GeneratedRegex(@"^[A-Za-z_][\w-]*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex IdPattern();

	[GeneratedRegex(@"^([A-Za-z_][\w-]*)\s*(?:\[\s*""([^""]*)""\s*\])?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex AxisEntryPattern();

	[GeneratedRegex(@"^([A-Za-z_][\w-]*)\s*(?:\[\s*""([^""]*)""\s*\])?\s*\{([^{}]*)\}$", RegexOptions.None, TimeoutMs)]
	private static partial Regex CurveEntryPattern();

	[GeneratedRegex(@"^(?:([A-Za-z_][\w-]*)\s*:\s*)?(\d+(?:\.\d+)?)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ValuePattern();

	[GeneratedRegex(@"^\d+(?:\.\d+)?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex NumberPattern();

	/// <summary>A curve as written: its values in order, each with the axis it names when written <c>axis: value</c>.</summary>
	private sealed record PendingCurve(string Id, string Label, List<(string? Axis, double Value)> Values, string Source);

	internal static RadarChart Parse(string[] lines, bool strict = true)
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

	private static RadarChart ParseCore(string[] lines, bool strict)
	{
		string? title = null;
		var axes = new List<RadarAxis>();
		var pending = new List<PendingCurve>();
		var min = 0.0;
		var max = 0.0;
		var maxSet = false;
		var ticks = 5;
		var graticule = RadarGraticule.Circle;
		var showLegend = true;

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			if (strict)
				RequireValidStatement(line);

			var titleMatch = TitlePattern().Match(line);
			if (titleMatch.Success)
			{
				title = titleMatch.Groups[1].Value.Trim();
				continue;
			}

			var axisMatch = AxisPattern().Match(line);
			if (axisMatch.Success)
			{
				ParseAxes(axisMatch.Groups[1].Value, axes);
				continue;
			}

			var curveMatch = CurvePattern().Match(line);
			if (curveMatch.Success)
			{
				ParseCurves(curveMatch.Groups[1].Value, pending, line);
				continue;
			}

			var maxMatch = MaxPattern().Match(line);
			if (maxMatch.Success)
			{
				max = double.Parse(maxMatch.Groups[1].Value, CultureInfo.InvariantCulture);
				maxSet = true;
				continue;
			}

			var minMatch = MinPattern().Match(line);
			if (minMatch.Success)
			{
				min = double.Parse(minMatch.Groups[1].Value, CultureInfo.InvariantCulture);
				continue;
			}

			var ticksMatch = TicksPattern().Match(line);
			if (ticksMatch.Success)
			{
				ticks = int.Parse(ticksMatch.Groups[1].Value, CultureInfo.InvariantCulture);
				continue;
			}

			var gratMatch = GraticulePattern().Match(line);
			if (gratMatch.Success)
			{
				graticule = gratMatch.Groups[1].Value.Equals("polygon", StringComparison.OrdinalIgnoreCase) ? RadarGraticule.Polygon : RadarGraticule.Circle;
				continue;
			}

			var legendMatch = ShowLegendPattern().Match(line);
			if (legendMatch.Success)
				showLegend = legendMatch.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
		}

		var curves = ResolveCurves(pending, axes, strict);

		if (!maxSet)
			max = ComputeMax(curves);

		return new RadarChart
		{
			Title = title,
			Axes = axes,
			Curves = curves,
			Min = min,
			Max = max,
			Ticks = ticks,
			Graticule = graticule,
			ShowLegend = showLegend,
		};
	}

	private static void ParseAxes(string text, List<RadarAxis> axes)
	{
		foreach (var (part, _) in SplitTopLevel(text))
		{
			var trimmed = part.Trim();
			if (trimmed.Length == 0)
				continue;

			var bracketStart = trimmed.IndexOf('[');
			if (bracketStart >= 0)
			{
				var id = trimmed[..bracketStart].Trim();
				var label = trimmed[(bracketStart + 1)..].TrimEnd(']').Trim().Trim('"');
				axes.Add(new RadarAxis(id, label));
			}
			else
			{
				axes.Add(new RadarAxis(trimmed, trimmed));
			}
		}
	}

	private static void ParseCurves(string text, List<PendingCurve> curves, string source)
	{
		// 'curve a{1,2}, b{3,4}' declares two curves
		foreach (var (part, _) in SplitTopLevel(text))
		{
			var match = CurveDetailPattern().Match(part.Trim());
			if (!match.Success)
				continue;
			var id = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Success ? match.Groups[2].Value : "";
			var label = match.Groups[3].Success && match.Groups[3].Length > 0 ? match.Groups[3].Value : id;
			curves.Add(new PendingCurve(id, label, ParseValues(match.Groups[4].Value), source));
		}
	}

	private static List<(string? Axis, double Value)> ParseValues(string text)
	{
		var values = new List<(string?, double)>();
		foreach (var part in text.Split(','))
		{
			var trimmed = part.Trim();
			string? axis = null;
			var colonIdx = trimmed.IndexOf(':');
			if (colonIdx >= 0)
			{
				axis = trimmed[..colonIdx].Trim();
				trimmed = trimmed[(colonIdx + 1)..].Trim();
			}
			if (double.TryParse(trimmed, CultureInfo.InvariantCulture, out var v))
				values.Add((axis, v));
		}
		return values;
	}

	/// <summary>
	/// Turn written curves into values in axis order. A curve written <c>axis: value</c> is placed by its axis,
	/// whatever order it lists them in; Mermaid requires every axis to have a value.
	/// </summary>
	private static List<RadarCurve> ResolveCurves(List<PendingCurve> pending, List<RadarAxis> axes, bool strict)
	{
		var curves = new List<RadarCurve>();
		foreach (var curve in pending)
		{
			var keyed = curve.Values.Count > 0 && curve.Values.TrueForAll(v => v.Axis is not null);
			if (!keyed || axes.Count == 0)
			{
				curves.Add(new RadarCurve(curve.Id, curve.Label, curve.Values.ConvertAll(v => v.Value)));
				continue;
			}

			var byAxis = new Dictionary<string, double>(StringComparer.Ordinal);
			foreach (var (axis, value) in curve.Values)
				byAxis[axis!] = value;

			var values = new List<double>(axes.Count);
			foreach (var axis in axes)
			{
				if (byAxis.TryGetValue(axis.Id, out var value))
				{
					values.Add(value);
					continue;
				}

				if (strict)
					throw new MermaidParseException(
						$"curve '{curve.Id}' has no value for axis '{axis.Id}'",
						curve.Source,
						$"a value for every axis ({string.Join(", ", axes.ConvertAll(a => a.Id))}), e.g. '{axis.Id}: 3'",
						column: curve.Source.IndexOf('{') + 1
					);
				values.Add(0);
			}
			curves.Add(new RadarCurve(curve.Id, curve.Label, values));
		}
		return curves;
	}

	private static double ComputeMax(List<RadarCurve> curves)
	{
		var max = 5.0;
		foreach (var curve in curves)
		{
			foreach (var v in curve.Values)
			{
				if (v > max)
					max = v;
			}
		}
		return max;
	}

	/// <summary>Split on commas that are not inside brackets, braces or quotes, keeping where each part starts.</summary>
	private static List<(string Text, int Start)> SplitTopLevel(string text)
	{
		var parts = new List<(string, int)>();
		var depth = 0;
		var quoted = false;
		var start = 0;
		for (var i = 0; i < text.Length; i++)
		{
			var c = text[i];
			if (c == '"')
				quoted = !quoted;
			else if (quoted)
				continue;
			else if (c is '[' or '{')
				depth++;
			else if (c is ']' or '}')
				depth = Math.Max(0, depth - 1);
			else if (c == ',' && depth == 0)
			{
				parts.Add((text[start..i], start));
				start = i + 1;
			}
		}
		parts.Add((text[start..], start));
		return parts;
	}

	/// <summary>Throw for the first thing wrong with a statement, judged by what Mermaid accepts.</summary>
	private static void RequireValidStatement(string line)
	{
		var keyword = KeywordPattern().Match(line).Value;
		var body = line[keyword.Length..].Trim();
		var bodyStart = line.Length - line[keyword.Length..].TrimStart().Length;
		switch (keyword)
		{
			case "title":
				return;
			case "axis":
				RequireValidAxes(line, body, bodyStart);
				return;
			case "curve":
				RequireValidCurves(line, body, bodyStart);
				return;
			case "max" or "min":
				RequireNumber(line, keyword, body, bodyStart, "a non-negative number, e.g. '" + keyword + " 100'");
				return;
			case "ticks":
				RequireNumber(line, keyword, body, bodyStart, "a non-negative number, e.g. 'ticks 5'");
				return;
			case "graticule":
				if (body is not ("circle" or "polygon"))
					throw new MermaidParseException(
						body.Length == 0 ? "graticule has no shape" : $"graticule '{body}' is not a shape",
						line,
						"'circle' or 'polygon'",
						column: body.Length == 0 ? line.Length + 1 : bodyStart + 1
					);
				return;
			case "showLegend":
				if (body is not ("true" or "false"))
					throw new MermaidParseException(
						body.Length == 0 ? "showLegend has no value" : $"showLegend '{body}' is not true or false",
						line,
						"'true' or 'false'",
						column: body.Length == 0 ? line.Length + 1 : bodyStart + 1
					);
				return;
			default:
				throw new MermaidParseException($"unexpected text '{line}'", line, Statements, column: 1);
		}
	}

	private static void RequireNumber(string line, string keyword, string body, int bodyStart, string expected)
	{
		if (body.Length == 0)
			throw new MermaidParseException($"{keyword} has no value", line, expected, column: line.Length + 1);
		if (!NumberPattern().IsMatch(body))
			throw new MermaidParseException($"{keyword} value '{body}' is not a non-negative number", line, expected, column: bodyStart + 1);
	}

	private static void RequireValidAxes(string line, string body, int bodyStart)
	{
		if (body.Length == 0)
			throw new MermaidParseException("axis has no ids", line, "axis ids separated by commas, e.g. 'axis speed[\"Speed\"], power'", column: line.Length + 1);

		foreach (var (part, start) in SplitTopLevel(body))
		{
			var entry = part.Trim();
			var column = bodyStart + start + (part.Length - part.TrimStart().Length) + 1;
			if (entry.Length == 0)
				throw new MermaidParseException("axis list has an empty entry", line, "an axis id between the commas", column: column);
			if (AxisEntryPattern().IsMatch(entry))
				continue;

			var bracket = entry.IndexOf('[');
			if (bracket < 0)
				throw new MermaidParseException(
					$"'{entry}' is not a valid axis id",
					line,
					"letters, digits, '_' or '-' starting with a letter, separated by commas",
					column: column
				);
			if (!entry.EndsWith(']'))
				throw new MermaidParseException($"label of axis '{entry[..bracket].Trim()}' is never closed", line, "']' after the label", column: column + bracket);
			if (!IdPattern().IsMatch(entry[..bracket].Trim()))
				throw new MermaidParseException(
					$"'{entry[..bracket].Trim()}' is not a valid axis id",
					line,
					"letters, digits, '_' or '-' starting with a letter",
					column: column
				);
			throw new MermaidParseException(
				$"label of axis '{entry[..bracket].Trim()}' is not a quoted string",
				line,
				"the label in double quotes, e.g. speed[\"Speed\"]",
				column: column + bracket
			);
		}
	}

	private static void RequireValidCurves(string line, string body, int bodyStart)
	{
		const string Form = "'curve <id>{value, value, ...}', or 'curve <id>{axis: value, ...}', separated by commas";
		if (body.Length == 0)
			throw new MermaidParseException("curve has no data", line, Form, column: line.Length + 1);

		foreach (var (part, start) in SplitTopLevel(body))
		{
			var entry = part.Trim();
			var column = bodyStart + start + (part.Length - part.TrimStart().Length) + 1;
			if (entry.Length == 0)
				throw new MermaidParseException("curve list has an empty entry", line, Form, column: column);

			var match = CurveEntryPattern().Match(entry);
			if (!match.Success)
			{
				ExplainBadCurve(line, entry, column, Form);
				continue;
			}

			var id = match.Groups[1].Value;
			var values = match.Groups[3].Value;
			if (values.Trim().Length == 0)
				throw new MermaidParseException($"curve '{id}' has no values", line, "at least one value in the braces", column: column + entry.IndexOf('{'));

			var keys = new HashSet<string>(StringComparer.Ordinal);
			var keyed = 0;
			var parts = values.Split(',');
			foreach (var raw in parts)
			{
				var value = raw.Trim();
				var valueMatch = ValuePattern().Match(value);
				if (!valueMatch.Success)
					throw new MermaidParseException(
						value.Length == 0 ? $"curve '{id}' has an empty value" : $"value '{value}' of curve '{id}' is not a non-negative number",
						line,
						"numbers such as 4 or 2.5, optionally as 'axis: 4'",
						column: Math.Max(line.IndexOf(value, bodyStart + start, StringComparison.Ordinal), 0) + 1
					);

				if (!valueMatch.Groups[1].Success)
					continue;
				keyed++;
				if (!keys.Add(valueMatch.Groups[1].Value))
					throw new MermaidParseException(
						$"curve '{id}' gives axis '{valueMatch.Groups[1].Value}' twice",
						line,
						"one value per axis",
						column: Math.Max(line.IndexOf(value, bodyStart + start, StringComparison.Ordinal), 0) + 1
					);
			}

			if (keyed != 0 && keyed != parts.Length)
				throw new MermaidParseException($"curve '{id}' mixes plain values with 'axis: value' ones", line, "either all plain values or all 'axis: value'", column: column + entry.IndexOf('{'));
		}
	}

	private static void ExplainBadCurve(string line, string entry, int column, string form)
	{
		var open = entry.IndexOf('{');
		if (open < 0)
			throw new MermaidParseException($"curve '{entry}' has no values", line, "its values in braces, e.g. 'curve a{1, 2, 3}'", column: column);
		var close = entry.IndexOf('}');
		if (close < 0)
			throw new MermaidParseException($"values of curve '{entry[..open].Trim()}' are never closed", line, "'}' after the last value", column: column + open);
		if (close < entry.Length - 1)
			throw new MermaidParseException(
				$"unexpected text after the values of curve '{entry[..open].Trim()}'",
				line,
				"a comma before the next curve",
				column: column + close + 1
			);

		var head = entry[..open].Trim();
		if (head.Length == 0)
			throw new MermaidParseException("curve has no id", line, form, column: column);
		var bracket = head.IndexOf('[');
		var id = bracket < 0 ? head : head[..bracket].Trim();
		if (!IdPattern().IsMatch(id))
			throw new MermaidParseException($"'{id}' is not a valid curve id", line, "letters, digits, '_' or '-' starting with a letter", column: column);
		throw new MermaidParseException($"label of curve '{id}' is not a quoted string", line, "the label in double quotes, e.g. a[\"Series A\"]{1, 2}", column: column + Math.Max(bracket, 0));
	}
}
