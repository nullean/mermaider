using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class QuadrantParser
{
	private const int TimeoutMs = 2000;

	private const string Statements =
		"'title <text>', 'x-axis <left> --> <right>', 'y-axis <bottom> --> <top>', 'quadrant-1' to 'quadrant-4' with a label, or a point like 'Campaign A: [0.3, 0.6]'";

	[GeneratedRegex(@"^title\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex TitlePattern();

	[GeneratedRegex(@"^x-axis\s+(.+?)(?:\s*-->\s*(.+))?$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex XAxisPattern();

	[GeneratedRegex(@"^y-axis\s+(.+?)(?:\s*-->\s*(.+))?$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex YAxisPattern();

	[GeneratedRegex(@"^quadrant-([1-4])\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex QuadrantLabelPattern();

	// Name[:::class]: [x, y] [radius: 12, color: #fff, ...]
	[GeneratedRegex(@"^(.+?)(?::::[\w-]+)?\s*:\s*\[\s*(-?[0-9]*\.?[0-9]+)\s*,\s*(-?[0-9]*\.?[0-9]+)\s*\](?:\s+[a-z-]+\s*:.*)?$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex PointPattern();

	// quadrantChart [title <text>], as Mermaid reads the opening line
	[GeneratedRegex(@"^(?i:quadrantChart)(?:\s+title(?:\s+.*)?)?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex HeaderPattern();

	[GeneratedRegex(@"^(x-axis|y-axis)(?:\s+(.*))?$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex KeywordPattern();

	[GeneratedRegex(@"^classDef(?:\s+(\S+)(?:\s+(.+))?)?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassDefPattern();

	// Coordinates are fractions of the plot: 0.x, 0 or 1
	[GeneratedRegex(@"^(?:0(?:\.[0-9]+)?|1)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex CoordinatePattern();

	[GeneratedRegex(@"^(radius|color|stroke-color|stroke-width)\s*:\s*(\S.*)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex StylePattern();

	[GeneratedRegex(@"^[0-9]+$", RegexOptions.None, TimeoutMs)]
	private static partial Regex IntegerPattern();

	[GeneratedRegex(@"""[^""]*""", RegexOptions.None, TimeoutMs)]
	private static partial Regex QuotedPattern();

	internal static QuadrantChart Parse(string[] lines, bool strict = true)
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

	private static QuadrantChart ParseCore(string[] lines, bool strict)
	{
		var header = lines[0].Trim();
		if (strict && !HeaderPattern().IsMatch(header))
			throw new MermaidParseException(
				"unexpected text after 'quadrantChart'",
				header,
				"an optional 'title <text>' on the opening line",
				column: header.IndexOf(' ') + 2
			);

		var (_, title) = DiagramHeaderOptions.Parse(lines[0], "quadrantChart");
		string? xAxisLeft = null, xAxisRight = null;
		string? yAxisBottom = null, yAxisTop = null;
		string? q1 = null, q2 = null, q3 = null, q4 = null;
		var points = new List<QuadrantPoint>();

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

			var xMatch = XAxisPattern().Match(line);
			if (xMatch.Success)
			{
				xAxisLeft = TrimArrow(xMatch.Groups[1].Value);
				xAxisRight = xMatch.Groups[2].Success ? xMatch.Groups[2].Value.Trim() : null;
				continue;
			}

			var yMatch = YAxisPattern().Match(line);
			if (yMatch.Success)
			{
				yAxisBottom = TrimArrow(yMatch.Groups[1].Value);
				yAxisTop = yMatch.Groups[2].Success ? yMatch.Groups[2].Value.Trim() : null;
				continue;
			}

			var qMatch = QuadrantLabelPattern().Match(line);
			if (qMatch.Success)
			{
				var label = qMatch.Groups[2].Value.Trim();
				switch (qMatch.Groups[1].Value)
				{
					case "1":
						q1 = label;
						break;
					case "2":
						q2 = label;
						break;
					case "3":
						q3 = label;
						break;
					case "4":
						q4 = label;
						break;
				}
				continue;
			}

			var pMatch = PointPattern().Match(line);
			if (pMatch.Success)
			{
				var label = pMatch.Groups[1].Value.Trim();
				var x = double.Parse(pMatch.Groups[2].Value, CultureInfo.InvariantCulture);
				var y = double.Parse(pMatch.Groups[3].Value, CultureInfo.InvariantCulture);
				x = Math.Clamp(x, 0, 1);
				y = Math.Clamp(y, 0, 1);
				points.Add(new QuadrantPoint(label, x, y));
			}
		}

		return new QuadrantChart
		{
			Title = title,
			XAxisLeft = xAxisLeft,
			XAxisRight = xAxisRight,
			YAxisBottom = yAxisBottom,
			YAxisTop = yAxisTop,
			Quadrant1 = q1,
			Quadrant2 = q2,
			Quadrant3 = q3,
			Quadrant4 = q4,
			Points = points,
		};
	}

	/// <summary>An axis written <c>x-axis Low --></c> names only its low end; the dangling arrow is not part of the label.</summary>
	private static string TrimArrow(string label)
	{
		label = label.Trim();
		return label.EndsWith("-->", StringComparison.Ordinal) ? label[..^3].TrimEnd() : label;
	}

	/// <summary>Throw for the first thing wrong with a statement, judged by what Mermaid accepts.</summary>
	private static void RequireValidStatement(string line)
	{
		// Mermaid reads 'title' followed by anything, including 'title: x' and a bare 'title'
		if (line.StartsWith("title", StringComparison.OrdinalIgnoreCase) && (line.Length == 5 || !char.IsLetterOrDigit(line[5])))
			return;

		var keyword = KeywordPattern().Match(line);
		if (keyword.Success)
		{
			RequireValidAxis(line, keyword.Groups[1].Value.ToLowerInvariant(), keyword.Groups[2].Value.Trim());
			return;
		}

		if (line.StartsWith("quadrant-", StringComparison.OrdinalIgnoreCase))
		{
			RequireValidQuadrantLabel(line);
			return;
		}

		var classDef = ClassDefPattern().Match(line);
		if (classDef.Success)
		{
			if (!classDef.Groups[2].Success)
				throw new MermaidParseException(
					classDef.Groups[1].Success ? $"classDef '{classDef.Groups[1].Value}' has no styles" : "classDef has no name",
					line,
					"'classDef <name> radius: 10, color: #ff0000'",
					column: line.Length + 1
				);
			RequireValidStyles(line, classDef.Groups[2].Value, classDef.Groups[2].Index);
			return;
		}

		var point = PointPattern().Match(line);
		if (point.Success)
		{
			var name = point.Groups[1].Value.Trim();
			foreach (var group in new[] { point.Groups[2], point.Groups[3] })
			{
				if (!CoordinatePattern().IsMatch(group.Value))
					throw new MermaidParseException(
						$"coordinate '{group.Value}' of point '{name}' is not a number between 0 and 1",
						line,
						"x and y between 0 and 1, e.g. [0.3, 0.6]",
						column: group.Index + 1
					);
			}
			var close = line.LastIndexOf(']');
			if (close + 1 < line.Length)
				RequireValidStyles(line, line[(close + 1)..].Trim(), line.IndexOf(line[(close + 1)..].Trim(), close, StringComparison.Ordinal));
			return;
		}

		RequireValidPoint(line);
	}

	private static void RequireValidAxis(string line, string name, string body)
	{
		if (body.Length == 0)
			throw new MermaidParseException($"{name} has no label", line, $"a label after '{name}', e.g. '{name} Low --> High'", column: line.Length + 1);

		var quotes = 0;
		var firstQuote = -1;
		for (var i = 0; i < line.Length; i++)
		{
			if (line[i] != '"')
				continue;
			if (firstQuote < 0)
				firstQuote = i;
			quotes++;
		}
		if (quotes % 2 != 0)
			throw new MermaidParseException($"quote opened in the {name} label is never closed", line, "a closing '\"'", column: firstQuote + 1);

		var plain = QuotedPattern().Replace(body, "\"\"");
		var arrow = plain.IndexOf("-->", StringComparison.Ordinal);
		if (arrow == 0)
			throw new MermaidParseException(
				$"{name} has no label before '-->'",
				line,
				$"a label before '-->', e.g. '{name} Low --> High'",
				column: line.IndexOf("-->", StringComparison.Ordinal) + 1
			);

		if (arrow > 0 && plain.IndexOf("-->", arrow + 3, StringComparison.Ordinal) >= 0)
		{
			var second = line.IndexOf("-->", line.IndexOf("-->", StringComparison.Ordinal) + 3, StringComparison.Ordinal);
			throw new MermaidParseException($"{name} has more than one '-->'", line, $"a single arrow, e.g. '{name} Low --> High'", column: second + 1);
		}
	}

	private static void RequireValidQuadrantLabel(string line)
	{
		const string Form = "'quadrant-1', 'quadrant-2', 'quadrant-3' or 'quadrant-4' followed by a label";
		if (line.Length < 10 || line[9] is < '1' or > '4')
			throw new MermaidParseException(line.Length < 10 ? "quadrant has no number" : $"there is no quadrant-{line[9]}", line, Form, column: 10);

		var label = line[10..].Trim();
		if (label.Length == 0 || label[0] == ':')
			throw new MermaidParseException(
				$"quadrant-{line[9]} has no label",
				line,
				$"a label after 'quadrant-{line[9]}', e.g. 'quadrant-{line[9]} Do first'",
				column: label.Length == 0 ? line.Length + 1 : 11
			);

		var open = line.IndexOf('"');
		if (open >= 0 && line.IndexOf('"', open + 1) < 0)
			throw new MermaidParseException($"quote opened in the quadrant-{line[9]} label is never closed", line, "a closing '\"'", column: open + 1);
	}

	private static void RequireValidStyles(string line, string styles, int column)
	{
		foreach (var part in styles.Split(','))
		{
			var text = part.Trim();
			var style = StylePattern().Match(text);
			if (!style.Success)
				throw new MermaidParseException(
					$"style '{text}' is not valid",
					line,
					"'radius: <number>', 'color: <colour>', 'stroke-color: <colour>' or 'stroke-width: <size>'",
					column: column + 1
				);

			if (style.Groups[1].Value == "radius" && !IntegerPattern().IsMatch(style.Groups[2].Value.Trim()))
				throw new MermaidParseException(
					$"radius '{style.Groups[2].Value.Trim()}' is not a number",
					line,
					"a whole number, e.g. 'radius: 12'",
					column: line.LastIndexOf(style.Groups[2].Value.Trim(), StringComparison.Ordinal) + 1
				);
		}
	}

	/// <summary>A line no keyword claims has to be a point: say which part of it is missing.</summary>
	private static void RequireValidPoint(string line)
	{
		var colon = line.IndexOf(':');
		var open = line.IndexOf('[');
		if (colon <= 0)
			throw new MermaidParseException($"unexpected text '{line}'", line, Statements, column: 1);

		var name = line[..colon].Trim();
		if (open < colon)
			throw new MermaidParseException(
				$"point '{name}' has no coordinates",
				line,
				"': [x, y]' after the name, e.g. 'Campaign A: [0.3, 0.6]'",
				column: line.Length + 1
			);

		var close = line.IndexOf(']', open);
		if (close < 0)
			throw new MermaidParseException($"coordinates of point '{name}' are never closed", line, "']' after the y coordinate", column: open + 1);

		if (line[(close + 1)..].Trim().Length > 0)
			throw new MermaidParseException($"unexpected text after the coordinates of point '{name}'", line, "the end of the line", column: close + 2);

		var parts = line[(open + 1)..close].Split(',');
		if (parts.Length != 2)
			throw new MermaidParseException($"point '{name}' needs exactly two coordinates", line, "'[x, y]', e.g. 'Campaign A: [0.3, 0.6]'", column: open + 1);

		foreach (var part in parts)
		{
			var value = part.Trim();
			if (!CoordinatePattern().IsMatch(value))
				throw new MermaidParseException(
					$"coordinate '{value}' of point '{name}' is not a number between 0 and 1",
					line,
					"x and y between 0 and 1, e.g. [0.3, 0.6]",
					column: Math.Max(line.IndexOf(value, open, StringComparison.Ordinal), open) + 1
				);
		}

		throw new MermaidParseException($"unexpected text '{line}'", line, Statements, column: 1);
	}
}
