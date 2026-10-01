using Mermaider.Models;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

/// <summary>
/// Builds a <see cref="LayoutSkeleton"/> directly from Mermaider's own
/// <see cref="PositionedErDiagram"/> — no SVG parsing involved. Reading the internal model
/// straight (available via <c>InternalsVisibleTo</c>) is more robust than re-parsing our own
/// rendered SVG: it can't be thrown off by a change to path-drawing syntax, rounded-corner
/// markup, or label rect styling, none of which affect layout decisions.
/// </summary>
internal static class MermaiderSkeletonExtractor
{
	public static LayoutSkeleton Extract(PositionedErDiagram diagram)
	{
		var boxes = diagram.Entities
			.Select(e => (e.Id, e.X, e.Y, e.Width, e.Height))
			.ToList();

		var edges = diagram.Relationships
			.Where(r => r.Points.Count >= 2)
			.Select(r => (r.Entity1, r.Entity2, r.Label, Start: r.Points[0], End: r.Points[^1]))
			.ToList();

		return LayoutSkeletonBuilder.Build(boxes, edges);
	}
}
