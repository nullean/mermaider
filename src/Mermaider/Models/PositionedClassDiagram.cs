namespace Mermaider.Models;

public sealed record PositionedClassDiagram
{
	public required double Width { get; init; }
	public required double Height { get; init; }
	public required IReadOnlyList<PositionedClassNode> Classes { get; init; }
	public required IReadOnlyList<PositionedClassRelationship> Relationships { get; init; }
	public IReadOnlyList<PositionedGraphNote> Notes { get; init; } = [];
	public IReadOnlyList<PositionedGroup> Namespaces { get; init; } = [];
}

public sealed record PositionedClassNode
{
	public required string Id { get; init; }
	public required string Label { get; init; }
	public string? Annotation { get; init; }
	public required IReadOnlyList<ClassMember> Attributes { get; init; }
	public required IReadOnlyList<ClassMember> Methods { get; init; }
	public required double X { get; init; }
	public required double Y { get; init; }
	public required double Width { get; init; }
	public required double Height { get; init; }
	public required double HeaderHeight { get; init; }
	public required double AttrHeight { get; init; }
	public required double MethodHeight { get; init; }

	/// <summary>Resolved inline style (classDef + style directive merged).</summary>
	public IReadOnlyDictionary<string, string>? InlineStyle { get; init; }
}

public sealed record PositionedClassRelationship
{
	public required string From { get; init; }
	public required string To { get; init; }
	public required ClassRelationType Type { get; init; }
	public required ClassMarkerAt MarkerAt { get; init; }
	public string? Label { get; init; }
	public string? FromCardinality { get; init; }
	public string? ToCardinality { get; init; }
	public required IReadOnlyList<Point> Points { get; init; }
	public Point? LabelPosition { get; init; }
}
