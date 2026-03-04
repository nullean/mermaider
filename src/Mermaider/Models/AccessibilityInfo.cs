namespace Mermaider.Models;

/// <summary>
/// Accessibility metadata extracted from <c>accTitle</c> and <c>accDescr</c> directives.
/// </summary>
public sealed record AccessibilityInfo
{
	/// <summary>The accessible title from <c>accTitle: ...</c>.</summary>
	public string? Title { get; init; }

	/// <summary>The accessible description from <c>accDescr: ...</c> or multi-line <c>accDescr { ... }</c>.</summary>
	public string? Description { get; init; }

	/// <summary>Returns <c>true</c> when at least one accessibility directive is present.</summary>
	public bool HasContent => Title is { Length: > 0 } || Description is { Length: > 0 };
}
