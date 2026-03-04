namespace Mermaider.Models;

/// <summary>
/// Metadata extracted from YAML frontmatter and <c>%%{init:...}%%</c> directives
/// before the diagram body is parsed.
/// </summary>
public sealed record DiagramMetadata
{
	/// <summary>Diagram title from frontmatter <c>title:</c> or init directive.</summary>
	public string? Title { get; init; }

	/// <summary>Theme name from <c>%%{init: {"theme": "..."}}%%</c>.</summary>
	public string? Theme { get; init; }

	/// <summary>Theme variable overrides from <c>%%{init: {"themeVariables": {...}}}%%</c>.</summary>
	public IReadOnlyDictionary<string, string>? ThemeVariables { get; init; }

	/// <summary>A metadata instance with no values set.</summary>
	internal static DiagramMetadata Empty { get; } = new();
}
