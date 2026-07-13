namespace Mermaider.Models;

/// <summary>A fully laid-out architecture diagram with absolute coordinates, ready for SVG rendering.</summary>
public sealed record PositionedArchitectureDiagram
{
	public required double Width { get; init; }
	public required double Height { get; init; }
	public required IReadOnlyList<PositionedArchitectureGroup> Groups { get; init; }
	public required IReadOnlyList<PositionedArchitectureService> Services { get; init; }
	public required IReadOnlyList<PositionedArchitectureJunction> Junctions { get; init; }
	public required IReadOnlyList<PositionedArchitectureEdge> Edges { get; init; }
}

/// <summary>A positioned group bounding box.</summary>
public sealed record PositionedArchitectureGroup
{
	public required string Id { get; init; }
	public string? Icon { get; init; }
	public required string Title { get; init; }
	public required double X { get; init; }
	public required double Y { get; init; }
	public required double Width { get; init; }
	public required double Height { get; init; }
}

/// <summary>A positioned service node with absolute coordinates and dimensions.</summary>
public sealed record PositionedArchitectureService
{
	public required string Id { get; init; }
	public required string Icon { get; init; }
	public required string Title { get; init; }
	public required double X { get; init; }
	public required double Y { get; init; }
	public required double Width { get; init; }
	public required double Height { get; init; }
}

/// <summary>A positioned junction — a zero-size routing point.</summary>
public sealed record PositionedArchitectureJunction
{
	public required string Id { get; init; }
	public required double X { get; init; }
	public required double Y { get; init; }
}

/// <summary>A positioned edge with a full polyline path.</summary>
public sealed record PositionedArchitectureEdge
{
	public required string SourceId { get; init; }
	public required string TargetId { get; init; }
	public required bool SourceArrow { get; init; }
	public required bool TargetArrow { get; init; }
	public required IReadOnlyList<Point> Points { get; init; }
}
