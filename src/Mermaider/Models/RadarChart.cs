namespace Mermaider.Models;

public sealed record RadarChart
{
	public string? Title { get; init; }
	public required IReadOnlyList<RadarAxis> Axes { get; init; }
	public required IReadOnlyList<RadarCurve> Curves { get; init; }
	public double Min { get; init; }
	public double Max { get; init; } = 5;
	public int Ticks { get; init; } = 5;
	public RadarGraticule Graticule { get; init; } = RadarGraticule.Circle;
	public bool ShowLegend { get; init; } = true;
}

public sealed record RadarAxis(string Id, string Label);
public sealed record RadarCurve(string Id, string Label, IReadOnlyList<double> Values);

public enum RadarGraticule { Circle, Polygon }
