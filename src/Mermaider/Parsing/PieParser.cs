using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class PieParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^title\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex TitlePattern();

	[GeneratedRegex(@"^(?:""([^""]+)""|'([^']+)')\s*:\s*(\d+(?:\.\d+)?)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex SlicePattern();

	// pie [showData] [title <text>], as Mermaid reads the opening line
	[GeneratedRegex(@"^(?i:pie)(?:\s+showData)?(?:\s+title(?:\s+.*)?)?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex HeaderPattern();

	[GeneratedRegex(@"^\d+(?:\.\d+)?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex NumberPattern();

	internal static PieChart Parse(string[] lines, bool strict = true)
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

	private static PieChart ParseCore(string[] lines, bool strict)
	{
		if (strict && !HeaderPattern().IsMatch(lines[0].Trim()))
			throw new MermaidParseException(
				"unexpected text after 'pie'",
				lines[0].Trim(),
				"'showData' and/or 'title <text>' on the opening line",
				column: lines[0].Trim().IndexOf(' ') + 2
			);

		var (showData, title) = DiagramHeaderOptions.Parse(lines[0], "pie");
		var slices = new List<PieSlice>();

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			if (strict && line == "title")
				continue;

			var titleMatch = TitlePattern().Match(line);
			if (titleMatch.Success)
			{
				title = titleMatch.Groups[1].Value.Trim();
				continue;
			}

			var sliceMatch = SlicePattern().Match(line);
			if (sliceMatch.Success)
			{
				var label = sliceMatch.Groups[1].Success ? sliceMatch.Groups[1].Value : sliceMatch.Groups[2].Value;
				var value = double.Parse(sliceMatch.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
				if (value > 0)
					slices.Add(new PieSlice(label, value));
				continue;
			}

			if (strict)
				RequireValidSlice(line);
		}

		return new PieChart { Title = title, ShowData = showData, Slices = slices };
	}

	/// <summary>A line that is neither a title nor a well-formed slice: say which part of it is wrong. Quoted labels may be empty.</summary>
	private static void RequireValidSlice(string line)
	{
		const string Form = "a slice like '\"Dogs\" : 386', or 'title <text>'";
		var quote = line[0];
		if (quote is not ('"' or '\''))
			throw new MermaidParseException($"unexpected text '{line}'", line, Form, column: 1);

		var close = line.IndexOf(quote, 1);
		if (close < 0)
			throw new MermaidParseException(
				$"slice label opened with {quote} is never closed",
				line,
				$"a closing {quote} after the label, then ': <value>'",
				column: 1
			);

		var label = line[1..close];
		var rest = line[(close + 1)..].TrimStart();
		if (rest.Length == 0 || rest[0] != ':')
			throw new MermaidParseException($"slice '{label}' has no value", line, "': <number>' after the label, e.g. '\"Dogs\" : 386'", column: close + 2);

		var value = rest[1..].Trim();
		if (value.Length == 0)
			throw new MermaidParseException($"slice '{label}' has no value", line, "a number after ':', e.g. '\"Dogs\" : 386'", column: line.Length + 1);

		if (!NumberPattern().IsMatch(value))
			throw new MermaidParseException(
				$"value '{value}' of slice '{label}' is not a number",
				line,
				"a non-negative number such as 386 or 3.5",
				column: line.LastIndexOf(value, StringComparison.Ordinal) + 1
			);
	}
}
