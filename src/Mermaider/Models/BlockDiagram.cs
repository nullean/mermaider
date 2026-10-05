namespace Mermaider.Models;

/// <summary>Block diagram (grid of labeled boxes).</summary>
public sealed record BlockDiagram
{
	public string? Title { get; init; }
	public int Columns { get; init; } = 1;
	public required IReadOnlyList<BlockNode> Nodes { get; init; }
	public IReadOnlyList<BlockEdge> Edges { get; init; } = [];
}

/// <summary><paramref name="Span"/> is the number of grid columns the block covers (<c>A:2</c>).</summary>
public sealed record BlockNode(string Id, string Label, bool Rounded = false, bool IsSpace = false, int Span = 1);

public sealed record BlockEdge(string From, string To, string? Label = null);
