namespace Mermaider.Models;

/// <summary>A fully laid-out graph with absolute coordinates, ready for SVG rendering.</summary>
public sealed record PositionedGraph
{
	public required double Width { get; init; }
	public required double Height { get; init; }
	/// <summary>Leftmost X coordinate in the diagram (negative when left-side notes extend beyond x=0).</summary>
	public double MinX { get; init; }
	public required IReadOnlyList<PositionedNode> Nodes { get; init; }
	public required IReadOnlyList<PositionedEdge> Edges { get; init; }
	public required IReadOnlyList<PositionedGroup> Groups { get; init; }
	public IReadOnlyList<PositionedGraphNote> Notes { get; init; } = [];
}

/// <summary>A positioned node with absolute coordinates and dimensions.</summary>
public sealed record PositionedNode
{
	public required string Id { get; init; }
	public required string Label { get; init; }
	public required NodeShape Shape { get; init; }
	public required double X { get; init; }
	public required double Y { get; init; }
	public required double Width { get; init; }
	public required double Height { get; init; }
	public IReadOnlyDictionary<string, string>? InlineStyle { get; init; }

	/// <summary>CSS class name applied via <c>:::name</c> or <c>class</c> directive (strict mode).</summary>
	public string? CssClassName { get; init; }

	public bool IsMarkdown { get; init; }

	/// <summary>Semantic role the node opted into with the class <c>success</c>, <c>failure</c>, <c>warning</c> or <c>info</c>; null otherwise.</summary>
	public string? SemanticRole { get; init; }
}

/// <summary>A positioned edge with a full polyline path.</summary>
public sealed record PositionedEdge
{
	public required string Source { get; init; }
	public required string Target { get; init; }
	public string? Label { get; init; }
	public required EdgeStyle Style { get; init; }
	public required bool HasArrowStart { get; init; }
	public required bool HasArrowEnd { get; init; }
	public required IReadOnlyList<Point> Points { get; init; }
	public Point? LabelPosition { get; init; }
	public IReadOnlyDictionary<string, string>? InlineStyle { get; init; }
}

/// <summary>A positioned subgraph group rectangle.</summary>
public sealed record PositionedGroup
{
	public required string Id { get; init; }
	public required string Label { get; init; }
	public required double X { get; init; }
	public required double Y { get; init; }
	public required double Width { get; init; }
	public required double Height { get; init; }
	public required IReadOnlyList<PositionedGroup> Children { get; init; }
	public IReadOnlyDictionary<string, string>? InlineStyle { get; init; }
}

/// <summary>A positioned note attached to a node.</summary>
public sealed record PositionedGraphNote
{
	public required string Text { get; init; }
	public required double X { get; init; }
	public required double Y { get; init; }
	public required double Width { get; init; }
	public required double Height { get; init; }
	/// <summary>Start of the dashed connector line (on the note box edge).</summary>
	public Point? LineFrom { get; init; }
	/// <summary>End of the dashed connector line (on the target node edge).</summary>
	public Point? LineTo { get; init; }
}

/// <summary>A 2D point. Value type to avoid heap allocations.</summary>
public readonly record struct Point(double X, double Y);
