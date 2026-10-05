using Mermaider.Examples;

namespace Mermaider.Gallery;

// Gallery-level wrapper around the shared catalog. All examples (including the
// docs-builder flowchart/ER diagrams, formerly injected here as "private" extras) now
// live directly in Mermaider.Examples.DiagramExamples.All, so this is a thin pass-through
// kept for the Gallery's existing ByCategory/CategorySlug/CategoryLabel call sites.
public static class DiagramExamples
{
	public static readonly DiagramExample[] All = Mermaider.Examples.DiagramExamples.All;

	public static DiagramExample[] ByCategory(DiagramCategory category) =>
		All.Where(e => e.Category == category).ToArray();

	public static string CategorySlug(DiagramCategory c) =>
		Mermaider.Examples.DiagramExamples.CategorySlug(c);

	public static string CategoryLabel(DiagramCategory c) =>
		Mermaider.Examples.DiagramExamples.CategoryLabel(c);
}
