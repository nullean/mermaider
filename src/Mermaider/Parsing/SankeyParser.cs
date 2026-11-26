using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class SankeyParser
{
	private const int TimeoutMs = 2000;

	// sankey[-beta], and nothing else on the opening line
	[GeneratedRegex(@"^sankey(?:-beta)?$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex HeaderPattern();

	internal static SankeyDiagram Parse(string[] lines, bool strict = true)
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

	private static SankeyDiagram ParseCore(string[] lines, bool strict)
	{
		var links = new List<SankeyLink>();
		var records = 0;

		var header = lines[0].Trim();
		if (strict && !HeaderPattern().IsMatch(header))
			throw new MermaidParseException(
				"unexpected text after 'sankey-beta'",
				header,
				"nothing else on the opening line; the rows 'source,target,value' follow below",
				column: header.IndexOf(' ') + 2
			);

		// First line is header; remaining are CSV rows (source,target,value)
		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];
			if (line.Length == 0)
				continue;
			// Comment lines already stripped by preprocess; skip header-ish comments
			if (line.StartsWith("%%", StringComparison.Ordinal))
				continue;

			if (strict)
			{
				RequireValidRecord(line);
				records++;
			}

			var fields = ParseCsvFields(line);
			if (fields.Count < 3)
				continue;

			var source = fields[0].Trim();
			var target = fields[1].Trim();
			if (source.Length == 0 || target.Length == 0)
				continue;

			if (!double.TryParse(fields[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
				continue;
			if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
				continue;

			links.Add(new SankeyLink(source, target, value));
		}

		if (strict && records == 0)
			throw new MermaidParseException(
				"the sankey diagram has no links",
				header,
				"rows of 'source,target,value' after the opening line, e.g. 'Coal,Power,40'"
			);

		return new SankeyDiagram { Links = links };
	}

	/// <summary>
	/// A row is three comma-separated fields. A field is quoted (with "" for a quote inside) or plain; a quoted field has nothing
	/// before or after its quotes, and a plain one holds no quote. The value is not checked: Mermaid only reads it when drawing.
	/// </summary>
	private static void RequireValidRecord(string line)
	{
		const string Form = "'source,target,value', e.g. 'Coal,Power,40'";
		var count = 0;
		var i = 0;
		while (true)
		{
			count++;
			var start = i;
			if (i < line.Length && line[i] == '"')
			{
				i++;
				while (true)
				{
					if (i >= line.Length)
						throw new MermaidParseException($"quoted field {count} is never closed", line, "a closing '\"' for the field", column: start + 1);
					if (line[i] == '"')
					{
						if (i + 1 < line.Length && line[i + 1] == '"')
						{
							i += 2;
							continue;
						}
						break;
					}
					i++;
				}

				if (i == start + 1)
					throw new MermaidParseException($"field {count} is empty", line, Form, column: start + 1);
				i++;
				if (i < line.Length && line[i] != ',')
					throw new MermaidParseException($"unexpected text after the quoted field {count}", line, "',' after the closing quote", column: i + 1);
			}
			else
			{
				var comma = line.IndexOf(',', i);
				var end = comma < 0 ? line.Length : comma;
				var quote = line.IndexOf('"', i, end - i);
				if (quote >= 0)
					throw new MermaidParseException(
						$"quote inside the unquoted field {count}",
						line,
						"the whole field in quotes, or no quotes in it",
						column: quote + 1
					);
				if (end == i && count > 1)
					throw new MermaidParseException(count > 3 ? "row has more than three fields" : $"field {count} is empty", line, Form, column: i + 1);
				i = end;
			}

			if (i >= line.Length)
				break;
			i++; // the comma
		}

		if (count < 3)
			throw new MermaidParseException(
				$"row has {count} field{(count == 1 ? "" : "s")}, but a link needs a source, a target and a value",
				line,
				Form,
				column: line.Length + 1
			);
		if (count > 3)
			throw new MermaidParseException("row has more than three fields", line, Form, column: line.IndexOf(',', line.IndexOf(',') + 1) + 2);
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
