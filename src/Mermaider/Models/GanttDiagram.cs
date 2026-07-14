namespace Mermaider.Models;

/// <summary>Parsed Mermaid gantt chart.</summary>
public sealed record GanttDiagram
{
	public string? Title { get; init; }
	/// <summary>Mermaid dateFormat string (e.g. <c>YYYY-MM-DD</c>).</summary>
	public string DateFormat { get; init; } = "YYYY-MM-DD";
	public required IReadOnlyList<GanttSection> Sections { get; init; }
}

public sealed record GanttSection(string? Name, IReadOnlyList<GanttTask> Tasks);

public sealed record GanttTask(
	string Name,
	string? Id,
	DateTime Start,
	DateTime End,
	GanttTaskTags Tags);

[Flags]
public enum GanttTaskTags
{
	None = 0,
	Done = 1,
	Active = 2,
	Crit = 4,
	Milestone = 8,
}
