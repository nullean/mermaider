namespace Mermaider.Models;

/// <summary>Parsed Mermaid user journey diagram.</summary>
public sealed record JourneyDiagram
{
	public string? Title { get; init; }
	public required IReadOnlyList<JourneySection> Sections { get; init; }
}

public sealed record JourneySection(string? Name, IReadOnlyList<JourneyTask> Tasks);

/// <param name="Name">Task label.</param>
/// <param name="Score">Satisfaction score, typically 1–5 (clamped).</param>
/// <param name="Actors">People/systems involved in the task.</param>
public sealed record JourneyTask(string Name, int Score, IReadOnlyList<string> Actors);
