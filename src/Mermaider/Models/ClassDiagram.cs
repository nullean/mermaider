using System.Collections.ObjectModel;

namespace Mermaider.Models;

public sealed record ClassDiagram
{
	public required IReadOnlyList<ClassNode> Classes { get; init; }
	public required IReadOnlyList<ClassRelationship> Relationships { get; init; }
	public required IReadOnlyList<ClassNamespace> Namespaces { get; init; }
	public Direction? Direction { get; init; }
	public IReadOnlyList<ClassNote> Notes { get; init; } = [];

	/// <summary>Named style definitions: classDef name fill:#f9f,stroke:#333</summary>
	public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ClassDefs { get; init; } =
		ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>.Empty;

	/// <summary>Class-to-style assignments via cssClass or :::</summary>
	public IReadOnlyDictionary<string, string> ClassAssignments { get; init; } =
		ReadOnlyDictionary<string, string>.Empty;

	/// <summary>Per-node inline styles via style directive.</summary>
	public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> NodeStyles { get; init; } =
		ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>.Empty;
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

public enum ClassRelationType { Inheritance, Composition, Aggregation, Association, Dependency, Realization, Lollipop }
public enum ClassMarkerAt { From, To }

public sealed record ClassNamespace(string Name, IReadOnlyList<string> ClassIds);

public sealed record ClassNote(string? TargetClassId, string Text);
