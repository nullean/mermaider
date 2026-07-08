namespace Mermaider.Models;

/// <summary>Sankey flow diagram (source → target weighted links).</summary>
public sealed record SankeyDiagram
{
	public required IReadOnlyList<SankeyLink> Links { get; init; }
}

public sealed record SankeyLink(string Source, string Target, double Value);
