using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

/// <summary>
/// Extracts <c>accTitle</c> and <c>accDescr</c> accessibility directives from diagram lines.
/// Consumes matched lines so downstream parsers never see them.
/// </summary>
internal static partial class AccessibilityParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^accTitle\s*:\s*(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex AccTitlePattern();

	[GeneratedRegex(@"^accDescr\s*:\s*(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex AccDescrSinglePattern();

	[GeneratedRegex(@"^accDescr\s*\{", RegexOptions.None, TimeoutMs)]
	private static partial Regex AccDescrMultiOpenPattern();

	/// <summary>
	/// Extract accessibility info from <paramref name="lines"/> and return
	/// a filtered array with the accessibility lines removed.
	/// </summary>
	internal static (AccessibilityInfo Accessibility, string[] FilteredLines) Extract(string[] lines)
	{
		string? title = null;
		string? description = null;
		var filtered = new List<string>(lines.Length);

		for (var i = 0; i < lines.Length; i++)
		{
			var line = lines[i];

			var titleMatch = AccTitlePattern().Match(line);
			if (titleMatch.Success)
			{
				title = titleMatch.Groups[1].Value.Trim();
				continue;
			}

			var descrSingleMatch = AccDescrSinglePattern().Match(line);
			if (descrSingleMatch.Success)
			{
				description = descrSingleMatch.Groups[1].Value.Trim();
				continue;
			}

			if (AccDescrMultiOpenPattern().IsMatch(line))
			{
				var descrLines = new List<string>();
				i++;
				while (i < lines.Length)
				{
					var inner = lines[i];
					if (inner.TrimEnd() == "}" || inner.TrimStart().StartsWith('}'))
						break;
					descrLines.Add(inner.Trim());
					i++;
				}
				description = string.Join("\n", descrLines);
				continue;
			}

			filtered.Add(line);
		}

		var info = new AccessibilityInfo { Title = title, Description = description };
		return (info, filtered.ToArray());
	}
}
