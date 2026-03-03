using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class PieParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^title\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex TitlePattern();

	[GeneratedRegex(@"^""([^""]+)""\s*:\s*(\d+(?:\.\d+)?)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex SlicePattern();

	internal static PieChart Parse(string[] lines)
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

	private static PieChart ParseCore(string[] lines)
	{
		string? title = null;
		var showData = false;
		var slices = new List<PieSlice>();

		var firstLine = lines[0];
		if (firstLine.Contains("showData", StringComparison.OrdinalIgnoreCase))
			showData = true;

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			var titleMatch = TitlePattern().Match(line);
			if (titleMatch.Success)
			{
				title = titleMatch.Groups[1].Value.Trim();
				continue;
			}

			var sliceMatch = SlicePattern().Match(line);
			if (sliceMatch.Success)
			{
				var label = sliceMatch.Groups[1].Value;
				var value = double.Parse(sliceMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
				if (value > 0)
					slices.Add(new PieSlice(label, value));
			}
		}

		return new PieChart { Title = title, ShowData = showData, Slices = slices };
	}
}
