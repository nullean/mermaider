namespace Mermaider.Models;

public sealed record MindmapDiagram
{
	public required MindmapNode Root { get; init; }
}

public sealed record MindmapNode
{
	public required string Label { get; init; }
	public MindmapShape Shape { get; init; } = MindmapShape.Rounded;
	public required IReadOnlyList<MindmapNode> Children { get; init; }
}

public enum MindmapShape
{
	Default,
	Rounded,
	Square,
	Circle,
	Bang,
	Cloud,
	Hexagon,
}
