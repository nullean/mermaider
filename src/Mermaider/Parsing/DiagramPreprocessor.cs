using System.Text.Json;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

/// <summary>
/// Strips YAML frontmatter and <c>%%{init:...}%%</c> directives from raw Mermaid input,
/// returning the cleaned text and any extracted <see cref="DiagramMetadata"/>.
/// </summary>
internal static partial class DiagramPreprocessor
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^%%\{init:\s*(\{.*\})\s*\}%%\s*$", RegexOptions.Singleline, TimeoutMs)]
	private static partial Regex InitDirectiveRegex();

	/// <summary>
	/// Pre-process raw Mermaid text: strip frontmatter and init directives,
	/// extract metadata, and return cleaned text ready for diagram parsing.
	/// </summary>
	internal static (string CleanedText, DiagramMetadata Metadata) Process(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return (text, DiagramMetadata.Empty);

		string? title = null;
		string? theme = null;
		IReadOnlyDictionary<string, string>? themeVariables = null;

		var cleaned = text;

		// 1. Strip YAML frontmatter (must be at the very start)
		var (afterFrontmatter, frontmatterTitle) = StripFrontmatter(cleaned);
		if (frontmatterTitle is not null)
			title = frontmatterTitle;
		cleaned = afterFrontmatter;

		// 2. Strip %%{init:...}%% directives (can appear on any line)
		var (afterInit, initTheme, initThemeVars, initTitle) = StripInitDirective(cleaned);
		cleaned = afterInit;

		// init directive values override frontmatter
		if (initTheme is not null)
			theme = initTheme;
		if (initThemeVars is not null)
			themeVariables = initThemeVars;
		if (initTitle is not null)
			title = initTitle;

		var metadata = title is null && theme is null && themeVariables is null
			? DiagramMetadata.Empty
			: new DiagramMetadata { Title = title, Theme = theme, ThemeVariables = themeVariables };

		return (cleaned, metadata);
	}

	private static (string CleanedText, string? Title) StripFrontmatter(string text)
	{
		var span = text.AsSpan().TrimStart();
		if (!span.StartsWith("---"))
			return (text, null);

		// Find the opening "---" line end
		var firstNewline = span.IndexOf('\n');
		if (firstNewline < 0)
			return (text, null);

		// Check that the first line is exactly "---" (possibly with trailing whitespace)
		var firstLine = span[..firstNewline].TrimEnd();
		if (firstLine is not "---")
			return (text, null);

		// Find closing "---"
		var rest = span[(firstNewline + 1)..];
		var closingIndex = -1;
		var searchStart = 0;
		while (searchStart < rest.Length)
		{
			var lineStart = searchStart;
			var lineEnd = rest[searchStart..].IndexOf('\n');
			var line = lineEnd >= 0
				? rest[lineStart..(lineStart + lineEnd)].Trim()
				: rest[lineStart..].Trim();

			if (line is "---")
			{
				closingIndex = lineEnd >= 0 ? lineStart + lineEnd : rest.Length;
				break;
			}

			if (lineEnd < 0)
				break;
			searchStart = lineStart + lineEnd + 1;
		}

		if (closingIndex < 0)
			return (text, null);

		// Extract YAML content between the two --- markers
		var yamlContent = rest[..closingIndex].ToString();
		var afterBlock = closingIndex < rest.Length
			? rest[(closingIndex + 1)..].ToString()
			: string.Empty;

		// Parse title from simple YAML (no full YAML parser dependency)
		string? title = null;
		foreach (var yamlLine in yamlContent.Split('\n'))
		{
			var trimmed = yamlLine.AsSpan().Trim();
			if (trimmed.StartsWith("title:"))
			{
				var value = trimmed["title:".Length..].Trim();
				// Strip surrounding quotes if present
				if (value.Length >= 2 &&
					((value[0] == '"' && value[^1] == '"') ||
					 (value[0] == '\'' && value[^1] == '\'')))
				{
					value = value[1..^1];
				}
				title = value.ToString();
				break;
			}
		}

		return (afterBlock, title);
	}

	private static (string CleanedText, string? Theme, IReadOnlyDictionary<string, string>? ThemeVariables, string? Title) StripInitDirective(string text)
	{
		var lines = text.Split('\n');
		var resultLines = new List<string>(lines.Length);
		string? theme = null;
		Dictionary<string, string>? themeVariables = null;
		string? title = null;

		foreach (var line in lines)
		{
			var trimmed = line.Trim();
			var match = InitDirectiveRegex().Match(trimmed);
			if (match.Success)
			{
				var json = match.Groups[1].Value;
				try
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;

					if (root.TryGetProperty("theme", out var themeElement) &&
						themeElement.ValueKind == JsonValueKind.String)
					{
						theme = themeElement.GetString();
					}

					if (root.TryGetProperty("themeVariables", out var varsElement) &&
						varsElement.ValueKind == JsonValueKind.Object)
					{
						themeVariables ??= [];
						foreach (var prop in varsElement.EnumerateObject())
						{
							if (prop.Value.ValueKind == JsonValueKind.String)
								themeVariables[prop.Name] = prop.Value.GetString()!;
						}
					}

					// Mermaid also supports title in init
					if (root.TryGetProperty("title", out var titleElement) &&
						titleElement.ValueKind == JsonValueKind.String)
					{
						title = titleElement.GetString();
					}
				}
				catch (JsonException)
				{
					// Malformed JSON in init directive — skip extraction, still strip the line
				}

				continue; // Strip the init directive line from output
			}

			resultLines.Add(line);
		}

		var cleaned = string.Join('\n', resultLines);
		return (cleaned, theme, themeVariables, title);
	}
}
