namespace Mermaider.Models;

public sealed record ArchitectureDiagram
{
	public required IReadOnlyList<ArchitectureGroup> Groups { get; init; }
	public required IReadOnlyList<ArchitectureService> Services { get; init; }
	public required IReadOnlyList<ArchitectureEdge> Edges { get; init; }
}

public sealed record ArchitectureGroup(
	string Id,
	string Icon,
	string Label,
	string? ParentId = null);

public sealed record ArchitectureService(
	string Id,
	string Icon,
	string Label,
	string? ParentId = null);

public enum ArchitecturePort
{
	Top,
	Bottom,
	Left,
	Right,
}

public sealed record ArchitectureEdge(
	string SourceId,
	ArchitecturePort SourcePort,
	string TargetId,
	ArchitecturePort TargetPort,
	bool ArrowToTarget = false,
	bool ArrowToSource = false);
