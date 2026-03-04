using System.Collections.ObjectModel;

namespace Mermaider.Models;

/// <summary>A parsed Mermaid diagram — the output of the parser stage.</summary>
public sealed record MermaidGraph
{
	public required Direction Direction { get; init; }

	public required IReadOnlyDictionary<string, MermaidNode> Nodes { get; init; }

	/// <summary>Node IDs in the order they were first defined in the source.</summary>
	public IReadOnlyList<string> NodeOrder { get; init; } = [];

	public required IReadOnlyList<MermaidEdge> Edges { get; init; }

	public required IReadOnlyList<MermaidSubgraph> Subgraphs { get; init; }

	public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ClassDefs { get; init; } =
		ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>.Empty;

	public IReadOnlyDictionary<string, string> ClassAssignments { get; init; } =
		ReadOnlyDictionary<string, string>.Empty;

	public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> NodeStyles { get; init; } =
		ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>.Empty;

	/// <summary>
	/// Per-edge inline styles from <c>linkStyle</c> directives, keyed by edge index.
	/// </summary>
	public IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> EdgeStyles { get; init; } =
		ReadOnlyDictionary<int, IReadOnlyDictionary<string, string>>.Empty;

	/// <summary>
	/// Default edge style applied to all edges not individually styled, from <c>linkStyle default</c>.
	/// </summary>
	public IReadOnlyDictionary<string, string>? DefaultEdgeStyle { get; init; }

	/// <summary>
	/// Tracks edges that were redirected from subgraph IDs to child nodes.
	/// Key = original edge index; value = (source subgraph ID if redirected, target subgraph ID if redirected).
	/// </summary>
	public IReadOnlyDictionary<int, (string? SourceSubgraph, string? TargetSubgraph)> SubgraphEdgeRedirections { get; init; } =
		ReadOnlyDictionary<int, (string? SourceSubgraph, string? TargetSubgraph)>.Empty;

	public IReadOnlyList<GraphNote> Notes { get; init; } = [];
}

public sealed record GraphNote(string TargetNodeId, string Text, GraphNotePosition Position);

public enum GraphNotePosition { Left, Right }

/// <summary>A single node in the parsed graph.</summary>
public readonly record struct MermaidNode(string Id, string Label, NodeShape Shape, bool IsMarkdown = false);

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
