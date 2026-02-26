namespace Mermaid.Models;

public sealed record ClassDiagram
{
	public required IReadOnlyList<ClassNode> Classes { get; init; }
	public required IReadOnlyList<ClassRelationship> Relationships { get; init; }
	public required IReadOnlyList<ClassNamespace> Namespaces { get; init; }
}

public sealed record ClassNode
{
	public required string Id { get; init; }
	public required string Label { get; init; }
	public string? Annotation { get; init; }
	public required IReadOnlyList<ClassMember> Attributes { get; init; }
	public required IReadOnlyList<ClassMember> Methods { get; init; }
}

public sealed record ClassMember(
	ClassVisibility Visibility,
	string Name,
	string? Type = null,
	bool IsStatic = false,
	bool IsAbstract = false,
	bool IsMethod = false,
	string? Params = null
);

public enum ClassVisibility { None, Public, Private, Protected, Package }

public sealed record ClassRelationship(
	string From,
	string To,
	ClassRelationType Type,
	ClassMarkerAt MarkerAt,
	string? Label = null,
	string? FromCardinality = null,
	string? ToCardinality = null
);

public enum ClassRelationType { Inheritance, Composition, Aggregation, Association, Dependency, Realization }
public enum ClassMarkerAt { From, To }

public sealed record ClassNamespace(string Name, IReadOnlyList<string> ClassIds);
