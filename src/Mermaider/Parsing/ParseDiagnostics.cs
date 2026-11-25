namespace Mermaider.Parsing;

internal static class ParseDiagnostics
{
	/// <summary>
	/// Parsers see trimmed statements without comments or blank lines, so they cannot know the line number.
	/// Find the offending statement in the original text and add it to the exception.
	/// </summary>
	internal static MermaidParseException Locate(MermaidParseException ex, string originalText)
	{
		var source = ex.SourceLine;
		if (ex.Line is not null || source is null || source.Length == 0)
			return ex;

		var first = source.Split('\n')[0].Trim();
		var lines = originalText.Split('\n');
		for (var i = 0; i < lines.Length; i++)
		{
			if (lines[i].Trim() == first)
				return ex.WithLine(i + 1);
		}
		return ex;
	}
}
