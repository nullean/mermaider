namespace Mermaider.Models;

public sealed record TreeViewDiagram
{
	public required IReadOnlyList<TreeViewNode> Roots { get; init; }
}

public sealed record TreeViewNode
{
	public required string Label { get; init; }
	public bool IsDirectory { get; init; }
	public string? Description { get; init; }
	public string? CssClass { get; init; }
	/// <summary>Explicit icon name. Null = use default (file/folder). Empty string = hide icon.</summary>
	public string? Icon { get; init; }
	public required IReadOnlyList<TreeViewNode> Children { get; init; }
}
