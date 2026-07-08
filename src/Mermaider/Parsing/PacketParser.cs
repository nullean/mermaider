using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class PacketParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^packet(?:-beta)?(?:\s+title\s+(.+))?$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex HeaderPattern();

	[GeneratedRegex(@"^title\s+(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex TitlePattern();

	// Range: 0-15: "Label"  |  single bit: 106: "Label"  |  bit-count: +16: "Label"
	[GeneratedRegex(
		@"^(?:\+(\d+)|(\d+)(?:-(\d+))?)\s*:\s*""([^""]*)""\s*$",
		RegexOptions.None,
		TimeoutMs)]
	private static partial Regex FieldPattern();

	internal static PacketDiagram Parse(string[] lines)
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

	private static PacketDiagram ParseCore(string[] lines)
	{
		string? title = null;
		var fields = new List<PacketField>();
		var nextBit = 0;

		if (lines.Length > 0)
		{
			var headerMatch = HeaderPattern().Match(lines[0]);
			if (headerMatch.Success && headerMatch.Groups[1].Success)
				title = headerMatch.Groups[1].Value.Trim();
		}

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			var titleMatch = TitlePattern().Match(line);
			if (titleMatch.Success)
			{
				title = titleMatch.Groups[1].Value.Trim();
				continue;
			}

			var fieldMatch = FieldPattern().Match(line);
			if (!fieldMatch.Success)
				continue;

			var label = fieldMatch.Groups[4].Value;
			int start;
			int end;

			if (fieldMatch.Groups[1].Success)
			{
				// +count form
				if (!int.TryParse(fieldMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
					|| count <= 0)
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
				if (!int.TryParse(fieldMatch.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out start)
					|| start < 0
					|| start > PacketDiagram.MaxBitIndex)
					continue;

				if (fieldMatch.Groups[3].Success)
				{
					if (!int.TryParse(fieldMatch.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out end)
						|| end < start
						|| end > PacketDiagram.MaxBitIndex)
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
}
