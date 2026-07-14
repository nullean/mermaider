namespace Mermaider.Models;

/// <summary>XY chart (bar and/or line series).</summary>
public sealed record XyChart
{
	public string? Title { get; init; }
	public bool Horizontal { get; init; }
	public string? XAxisTitle { get; init; }
	public IReadOnlyList<string>? XCategories { get; init; }
	public double? XMin { get; init; }
	public double? XMax { get; init; }
	public string? YAxisTitle { get; init; }
	public double? YMin { get; init; }
	public double? YMax { get; init; }
	public required IReadOnlyList<XySeries> Series { get; init; }
}

public sealed record XySeries(XySeriesType Type, string? Name, IReadOnlyList<double> Values);

public enum XySeriesType
{
	Bar,
	Line,
}
