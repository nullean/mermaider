namespace Mermaid.Models;

public sealed record PositionedErDiagram
{
	public required double Width { get; init; }
	public required double Height { get; init; }
	public required IReadOnlyList<PositionedErEntity> Entities { get; init; }
	public required IReadOnlyList<PositionedErRelationship> Relationships { get; init; }
}

public sealed record PositionedErEntity
{
	public required string Id { get; init; }
	public required string Label { get; init; }
	public required IReadOnlyList<ErAttribute> Attributes { get; init; }
	public required double X { get; init; }
	public required double Y { get; init; }
	public required double Width { get; init; }
	public required double Height { get; init; }
	public required double HeaderHeight { get; init; }
	public required double RowHeight { get; init; }
}

public sealed record PositionedErRelationship
{
	public required string Entity1 { get; init; }
	public required string Entity2 { get; init; }
	public required ErCardinality Cardinality1 { get; init; }
	public required ErCardinality Cardinality2 { get; init; }
	public required string Label { get; init; }
	public required bool Identifying { get; init; }
	public required IReadOnlyList<Point> Points { get; init; }
}
