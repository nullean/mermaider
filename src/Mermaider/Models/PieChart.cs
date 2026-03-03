namespace Mermaider.Models;

public sealed record PieChart
{
	public string? Title { get; init; }
	public bool ShowData { get; init; }
	public required IReadOnlyList<PieSlice> Slices { get; init; }
}

public sealed record PieSlice(string Label, double Value);
