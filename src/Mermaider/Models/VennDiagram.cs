namespace Mermaider.Models;

public sealed record VennDiagram
{
	public required IReadOnlyList<VennSet> Sets { get; init; }
	public required IReadOnlyList<VennUnion> Unions { get; init; }
}

public sealed record VennSet(string Id, string Label, double? Size = null);
public sealed record VennUnion(IReadOnlyList<string> SetIds, string? Label = null, double? Size = null);
