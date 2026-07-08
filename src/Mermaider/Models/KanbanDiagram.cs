namespace Mermaider.Models;

public sealed record KanbanDiagram
{
	public string? Title { get; init; }
	public required IReadOnlyList<KanbanColumn> Columns { get; init; }
}

public sealed record KanbanColumn(string Id, string Title, IReadOnlyList<KanbanTask> Tasks);

public sealed record KanbanTask(
	string Id,
	string Title,
	string? Assigned = null,
	string? Ticket = null,
	string? Priority = null);
