namespace Mermaider.Models;

public sealed record TimelineDiagram
{
	public string? Title { get; init; }
	public required IReadOnlyList<TimelineSection> Sections { get; init; }
}

public sealed record TimelineSection(string? Name, IReadOnlyList<TimelinePeriod> Periods);

public sealed record TimelinePeriod(string Label, IReadOnlyList<string> Events);
