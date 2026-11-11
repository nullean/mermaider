using Nullean.Snapshots;

namespace Mermaider.Tests.Snapshots;

/// <summary>
/// Rendered SVG snapshots: stored as <c>.verified.svg</c> so they open in a browser or image viewer, and compared line by
/// line exactly like text, since a layout change should show up as the coordinates that moved.
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
