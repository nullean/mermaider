using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class VennParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^set\s+(?:""([^""]+)""|([^\s\[]+))(?:\s*\[""([^""]+)""\])?\s*(?::\s*(\d+(?:\.\d+)?))?\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex SetPattern();

	[GeneratedRegex(@"^union\s+(.+?)\s*(?:\[""([^""]+)""\])?\s*(?::\s*(\d+(?:\.\d+)?))?\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex UnionPattern();

	internal static VennDiagram Parse(string[] lines)
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

	private static VennDiagram ParseCore(string[] lines)
	{
		var sets = new List<VennSet>();
		var unions = new List<VennUnion>();

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			var setMatch = SetPattern().Match(line);
			if (setMatch.Success)
			{
				var id = setMatch.Groups[1].Success ? setMatch.Groups[1].Value : setMatch.Groups[2].Value;
				var label = setMatch.Groups[3].Success ? setMatch.Groups[3].Value : id;
				double? size = setMatch.Groups[4].Success
					? double.Parse(setMatch.Groups[4].Value, CultureInfo.InvariantCulture)
					: null;
				sets.Add(new VennSet(id, label, size));
				continue;
			}

			var unionMatch = UnionPattern().Match(line);
			if (unionMatch.Success)
			{
				var idsText = unionMatch.Groups[1].Value;
				var label = unionMatch.Groups[2].Success ? unionMatch.Groups[2].Value : null;
				double? size = unionMatch.Groups[3].Success
					? double.Parse(unionMatch.Groups[3].Value, CultureInfo.InvariantCulture)
					: null;

				var ids = new List<string>();
				foreach (var part in idsText.Split(','))
				{
					var trimmed = part.Trim().Trim('"');
					if (trimmed.Length > 0)
						ids.Add(trimmed);
				}

				if (ids.Count >= 2)
					unions.Add(new VennUnion(ids, label, size));
			}
		}

		return new VennDiagram { Sets = sets, Unions = unions };
	}
}
