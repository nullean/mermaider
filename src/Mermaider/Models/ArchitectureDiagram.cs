namespace Mermaider.Models;

/// <summary>The side of a service/junction box an architecture edge attaches to.</summary>
public enum ArchitectureSide
{
	Left,
	Right,
	Top,
	Bottom
}

/// <summary>A container that groups services, junctions, and (optionally) nested groups.</summary>
public sealed record ArchitectureGroup
{
	public required string Id { get; init; }

	/// <summary>Icon name resolved via <see cref="Icons.IconRegistry"/>. Optional — groups may omit an icon.</summary>
	public string? Icon { get; init; }

	public required string Title { get; init; }

	/// <summary>Id of the enclosing group, for nested groups (<c>in</c> clause). Null at the top level.</summary>
	public string? ParentId { get; init; }
}

/// <summary>A single architecture service/component node, drawn with an icon and a label.</summary>
public sealed record ArchitectureService
{
	public required string Id { get; init; }

	/// <summary>Icon name resolved via <see cref="Icons.IconRegistry"/> (e.g. <c>"server"</c>, <c>"aws:database"</c>).</summary>
	public required string Icon { get; init; }

	public required string Title { get; init; }

	/// <summary>Id of the enclosing group (<c>in</c> clause). Null when not grouped.</summary>
	public string? GroupId { get; init; }
}

/// <summary>A zero-size routing point used to bend edges without representing a real service.</summary>
public sealed record ArchitectureJunction
{
	public required string Id { get; init; }

	/// <summary>Id of the enclosing group (<c>in</c> clause). Null when not grouped.</summary>
	public string? GroupId { get; init; }
}

/// <summary>A directional connection between two services/junctions, each anchored to a side.</summary>
public sealed record ArchitectureEdge
{
	public required string SourceId { get; init; }
	public ArchitectureSide? SourceSide { get; init; }

	public required string TargetId { get; init; }
	public ArchitectureSide? TargetSide { get; init; }

	/// <summary>Whether the connector draws an arrowhead at the source end.</summary>
	public bool SourceArrow { get; init; }

	/// <summary>Whether the connector draws an arrowhead at the target end.</summary>
	public bool TargetArrow { get; init; }
}

/// <summary>The parsed logical model of an <c>architecture-beta</c> diagram.</summary>
public sealed record ArchitectureDiagram
{
	public required IReadOnlyList<ArchitectureGroup> Groups { get; init; }
	public required IReadOnlyList<ArchitectureService> Services { get; init; }
	public required IReadOnlyList<ArchitectureJunction> Junctions { get; init; }
	public required IReadOnlyList<ArchitectureEdge> Edges { get; init; }
}
