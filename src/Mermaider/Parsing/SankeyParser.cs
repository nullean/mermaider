using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class SankeyParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^sankey(?:-beta)?\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex HeaderPattern();

	internal static SankeyDiagram Parse(string[] lines)
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

	private static SankeyDiagram ParseCore(string[] lines)
	{
		var links = new List<SankeyLink>();

		// First line is header; remaining are CSV rows (source,target,value)
		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];
			if (line.Length == 0)
				continue;
			// Comment lines already stripped by preprocess; skip header-ish comments
			if (line.StartsWith("%%", StringComparison.Ordinal))
				continue;

			var fields = ParseCsvFields(line);
			if (fields.Count < 3)
				continue;

			var source = fields[0].Trim();
			var target = fields[1].Trim();
			if (source.Length == 0 || target.Length == 0)
				continue;

			if (!double.TryParse(fields[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
				continue;
			if (value <= 0)
				continue;

			links.Add(new SankeyLink(source, target, value));
		}

		return new SankeyDiagram { Links = links };
	}

	/// <summary>RFC 4180-ish CSV field split (quotes, doubled quotes).</summary>
	internal static List<string> ParseCsvFields(string line)
	{
		var fields = new List<string>(3);
		var sb = new StringBuilder();
		var inQuotes = false;

		for (var i = 0; i < line.Length; i++)
		{
			var c = line[i];
			if (c == '"')
			{
				if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
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
				fields.Add(sb.ToString());
				_ = sb.Clear();
				continue;
			}

			_ = sb.Append(c);
		}

		fields.Add(sb.ToString());
		return fields;
	}
}
