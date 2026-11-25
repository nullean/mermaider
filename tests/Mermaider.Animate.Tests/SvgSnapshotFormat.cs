using Nullean.Snapshots;

namespace Mermaider.Animate.Tests;

/// <summary>
/// Frames stored as <c>.verified.svg</c>, so they open in a browser, and compared line by line like text, so a change shows
/// up as the values that moved.
/// </summary>
internal sealed class SvgSnapshotFormat : SnapshotFormat
{
	public static SvgSnapshotFormat Instance { get; } = new();

	private SvgSnapshotFormat()
	{ }

	public override string Name => "SVG";

	public override string Extension => "svg";

	public override SnapshotComparison Compare(string verified, string actual) => SnapshotFormats.Text.Compare(verified, actual);
}
