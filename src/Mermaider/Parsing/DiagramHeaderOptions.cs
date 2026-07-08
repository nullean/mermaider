namespace Mermaider.Parsing;

/// <summary>
/// Parses optional tokens that Mermaid allows on the opening header line after the diagram keyword.
/// Supported forms: <c>[showData] [title &lt;text&gt;]</c>.
/// </summary>
internal static class DiagramHeaderOptions
{
	/// <summary>
	/// Extract optional <c>showData</c> and/or <c>title …</c> that follow <paramref name="keyword"/>
	/// on the first line (e.g. <c>pie showData title Pets</c>, <c>timeline title History</c>).
	/// </summary>
	internal static (bool ShowData, string? Title) Parse(string headerLine, string keyword)
	{
		var span = headerLine.AsSpan().Trim();
		if (span.StartsWith(keyword, StringComparison.OrdinalIgnoreCase) &&
			(span.Length == keyword.Length || char.IsWhiteSpace(span[keyword.Length])))
		{
			span = span[keyword.Length..].TrimStart();
		}

		var showData = false;
		string? title = null;

		if (span.StartsWith("showData", StringComparison.OrdinalIgnoreCase) &&
			(span.Length == 8 || char.IsWhiteSpace(span[8])))
		{
			showData = true;
			span = span[8..].TrimStart();
		}

		if (span.StartsWith("title", StringComparison.OrdinalIgnoreCase) &&
			span.Length > 5 && char.IsWhiteSpace(span[5]))
		{
			title = span[5..].Trim().ToString();
		}

		return (showData, title);
	}
}
