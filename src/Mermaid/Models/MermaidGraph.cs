using System.Collections.Frozen;

namespace Mermaid.Models;

/// <summary>A parsed Mermaid diagram — the output of the parser stage.</summary>
public sealed record MermaidGraph
{
	public required Direction Direction { get; init; }

	public required FrozenDictionary<string, MermaidNode> Nodes { get; init; }

	public required IReadOnlyList<MermaidEdge> Edges { get; init; }

	public required IReadOnlyList<MermaidSubgraph> Subgraphs { get; init; }

	public FrozenDictionary<string, FrozenDictionary<string, string>> ClassDefs { get; init; } =
		FrozenDictionary<string, FrozenDictionary<string, string>>.Empty;

	public FrozenDictionary<string, string> ClassAssignments { get; init; } =
		FrozenDictionary<string, string>.Empty;

	public FrozenDictionary<string, FrozenDictionary<string, string>> NodeStyles { get; init; } =
		FrozenDictionary<string, FrozenDictionary<string, string>>.Empty;
}

/// <summary>A single node in the parsed graph.</summary>
public readonly record struct MermaidNode(string Id, string Label, NodeShape Shape);

/// <summary>A directed edge between two nodes.</summary>
public sealed record MermaidEdge(
	string Source,
	string Target,
	string? Label,
	EdgeStyle Style,
	bool HasArrowStart,
	bool HasArrowEnd
);

/// <summary>A subgraph (compound node) containing child nodes and nested subgraphs.</summary>
public sealed record MermaidSubgraph
{
	public required string Id { get; init; }
	public required string Label { get; init; }
	public required IReadOnlyList<string> NodeIds { get; init; }
	public required IReadOnlyList<MermaidSubgraph> Children { get; init; }
	public Direction? Direction { get; init; }
}
