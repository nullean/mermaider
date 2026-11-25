using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class PacketParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^packet(?:-beta)?(?:\s+title(?:\s+(.*))?)?$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex HeaderPattern();

	[GeneratedRegex(@"^title\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex TitlePattern();

	// Range: 0-15: "Label"  |  single bit: 106: "Label"  |  bit-count: +16: "Label"
	// A label is double- or single-quoted; a backslash escapes the next character.
	[GeneratedRegex(
		@"^(?:\+\s*(\d+)|(\d+)(?:\s*-\s*(\d+))?)\s*:\s*(?:""((?:\\.|[^""\\])*)""|'((?:\\.|[^'\\])*)')\s*$",
		RegexOptions.None,
		TimeoutMs
	)]
	private static partial Regex FieldPattern();

	[GeneratedRegex(@"^(?:\+\s*\d+|\d+(?:\s*-\s*\d+)?)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex BitsPattern();

	[GeneratedRegex(@"\\(.)", RegexOptions.None, TimeoutMs)]
	private static partial Regex EscapePattern();

	internal static PacketDiagram Parse(string[] lines, bool strict = true)
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

	private static PacketDiagram ParseCore(string[] lines, bool strict)
	{
		string? title = null;
		var fields = new List<PacketField>();
		var nextBit = 0;

		// The bit the next field has to start at. Counted without the renderer's cap, which only drops what is drawn.
		var expectedStart = 0L;

		if (lines.Length > 0)
		{
			var header = lines[0].Trim();
			var headerMatch = HeaderPattern().Match(header);
			if (strict && !headerMatch.Success)
				throw new MermaidParseException(
					"unexpected text after 'packet'",
					header,
					"an optional 'title <text>' on the opening line",
					column: header.IndexOf(' ') + 2
				);
			if (headerMatch.Success && headerMatch.Groups[1].Success)
				title = headerMatch.Groups[1].Value.Trim();
		}

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			var titleMatch = TitlePattern().Match(line);
			if (titleMatch.Success && (!strict || line.StartsWith("title", StringComparison.Ordinal)))
			{
				title = titleMatch.Groups[1].Value.Trim();
				continue;
			}

			var fieldMatch = FieldPattern().Match(line);
			if (strict)
			{
				if (line == "title")
					continue;
				RequireValidField(line, fieldMatch, ref expectedStart);
			}

			if (!fieldMatch.Success)
				continue;

			var label = Unescape(fieldMatch.Groups[4].Success ? fieldMatch.Groups[4].Value : fieldMatch.Groups[5].Value);
			int start;
			int end;

			if (fieldMatch.Groups[1].Success)
			{
				// +count form
				if (!int.TryParse(fieldMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count <= 0)
					continue;

				// Reject when cursor is already past the cap (or would overflow).
				if (nextBit > PacketDiagram.MaxBitIndex)
					continue;

				start = nextBit;
				// Use long to avoid int overflow on large counts.
				var endLong = (long)start + count - 1;
				if (endLong > PacketDiagram.MaxBitIndex || endLong < start)
					continue;
				end = (int)endLong;
			}
			else
			{
				if (
					!int.TryParse(fieldMatch.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out start) || start <
						0 || start > PacketDiagram.MaxBitIndex
				)
					continue;

				if (fieldMatch.Groups[3].Success)
				{
					if (
						!int.TryParse(fieldMatch.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out end) || end <
							start || end > PacketDiagram.MaxBitIndex
					)
						continue;
				}
				else
				{
					// Single-bit form: N: "Label"
					end = start;
				}
			}

			fields.Add(new PacketField(start, end, label));
			// Safe: end <= MaxBitIndex so nextBit <= MaxBitIndex + 1
			nextBit = end + 1;
		}

		return new PacketDiagram { Title = title, Fields = fields };
	}

	private static string Unescape(string label) => label.Contains('\\', StringComparison.Ordinal) ? EscapePattern().Replace(label, "$1") : label;

	/// <summary>
	/// Throw for a line that is not a title or a field Mermaid accepts: a field has to give its bits, a colon and a quoted label,
	/// and start where the previous one ended (the first at bit 0).
	/// </summary>
	private static void RequireValidField(string line, Match match, ref long expectedStart)
	{
		if (!match.Success)
		{
			ExplainBadField(line);
			return;
		}

		long start;
		long end;
		if (match.Groups[1].Success)
		{
			// Counts too large for a long are left to the renderer's cap
			if (!long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
				return;
			if (count == 0)
				throw new MermaidParseException("field '+0' has no bits", line, "a bit count of at least 1, e.g. '+8: \"Flags\"'", column: 1);
			start = expectedStart;
			end = start + count - 1;
		}
		else
		{
			end = 0;
			if (
				!long.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out start) ||
				(match.Groups[3].Success && !long.TryParse(match.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out end))
			)
				return;

			if (!match.Groups[3].Success)
				end = start;
			if (end < start)
				throw new MermaidParseException(
					$"bit range {start}-{end} ends before it starts",
					line,
					$"an end bit of at least {start}",
					column: match.Groups[3].Index + 1
				);

			if (start != expectedStart)
				throw new MermaidParseException(
					expectedStart == 0 ? $"the first field starts at bit {start}" : $"field starts at bit {start}, but the previous field ended at bit {expectedStart - 1}",
					line,
					$"a field starting at bit {expectedStart} (or '+<count>', which continues from there)",
					column: match.Groups[2].Index + 1
				);
		}

		expectedStart = end + 1;
	}

	private static void ExplainBadField(string line)
	{
		var colon = line.IndexOf(':');
		if (colon < 0)
			throw new MermaidParseException(
				$"unexpected text '{line}'",
				line,
				"a field like '0-15: \"Source port\"', '106: \"Flag\"' or '+16: \"Length\"', or 'title <text>'",
				column: 1
			);

		var bits = line[..colon].Trim();
		if (!BitsPattern().IsMatch(bits))
			throw new MermaidParseException(
				$"'{bits}' is not a bit or bit range",
				line,
				"a bit number, a range like '0-15' or a count like '+16' before the colon",
				column: 1
			);

		var label = line[(colon + 1)..].Trim();
		if (label.Length == 0)
			throw new MermaidParseException($"field '{bits}' has no label", line, "a quoted label after the colon, e.g. '0-15: \"Source port\"'", column: line.Length + 1);

		if (label[0] is not ('"' or '\''))
			throw new MermaidParseException(
				$"label of field '{bits}' is not quoted",
				line,
				"the label in double quotes, e.g. '0-15: \"Source port\"'",
				column: line.IndexOf(label, colon, StringComparison.Ordinal) + 1
			);

		throw new MermaidParseException(
			$"label of field '{bits}' is not closed properly",
			line,
			$"the label closed with {label[0]} and nothing after it",
			column: line.IndexOf(label, colon, StringComparison.Ordinal) + 1
		);
	}
}
