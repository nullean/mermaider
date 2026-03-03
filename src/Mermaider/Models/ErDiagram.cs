namespace Mermaider.Models;

public sealed record ErDiagram
{
	public required IReadOnlyList<ErEntity> Entities { get; init; }
	public required IReadOnlyList<ErRelationship> Relationships { get; init; }
	public Direction? Direction { get; init; }
}

public sealed record ErEntity
{
	public required string Id { get; init; }
	public required string Label { get; init; }
	public required IReadOnlyList<ErAttributeInfo> Attributes { get; init; }
}

public sealed record ErAttributeInfo(string Type, string Name, IReadOnlyList<ErKeyType> Keys, string? Comment = null);

public enum ErKeyType { PK, FK, UK }

public enum ErCardinality { One, ZeroOne, Many, ZeroMany }

public sealed record ErRelationship(
	string Entity1,
	string Entity2,
	ErCardinality Cardinality1,
	ErCardinality Cardinality2,
	string Label,
	bool Identifying
);
