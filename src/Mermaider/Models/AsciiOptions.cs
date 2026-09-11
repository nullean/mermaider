namespace Mermaider.Models;

/// <summary>How a diagram is drawn as text.</summary>
public sealed record AsciiOptions
{
	public static readonly AsciiOptions Default = new();

	/// <summary>Plain ASCII rather than box-drawing characters, for a terminal or a log that cannot be trusted with Unicode.</summary>
	public bool Ascii { get; init; }

	/// <summary>
	/// How wide the drawing may be, in characters. A flowchart wider than this is still drawn; the value is
	/// what label truncation is judged against, so a long label loses its tail rather than the picture losing
	/// its shape. Default: 120.
	/// </summary>
	public int Width { get; init; } = 120;

	/// <summary>Draw subgraphs as labelled frames. Default: true.</summary>
	public bool Groups { get; init; } = true;

	/// <summary>Draw edge labels on the line where there is room for them. Default: true.</summary>
	public bool EdgeLabels { get; init; } = true;

	/// <summary>How tall a chart's plot area is, in characters. Default: 16.</summary>
	public int Height { get; init; } = 16;
}
