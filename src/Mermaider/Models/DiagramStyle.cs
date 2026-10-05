namespace Mermaider.Models;

/// <summary>
/// Visual style presets. Each is a set of renderer knobs (radii, outline, fill, container, edge, marker, label, note,
/// badge, title mark, terminals, chart marks, weights, elevation) fed by the same colour inputs, so switching preset
/// restyles every diagram type, light and dark, without changing layout.
/// </summary>
public enum DiagramStyle
{
	/// <summary>Calm default: tinted boxes with a soft gradient, accent as the look-here colour.</summary>
	Quiet,

	/// <summary>Technical drawing: outline-first, square corners, mono captions, accent heads and rules.</summary>
	Blueprint,

	/// <summary>Friendly tonal blocks: filled, no outlines, generous radii, soft elevation.</summary>
	Tonal,
}
