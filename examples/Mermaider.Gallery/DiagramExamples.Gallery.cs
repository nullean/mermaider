using Mermaider.Examples;

namespace Mermaider.Gallery;

// Gallery-level wrapper: combines the shared catalog with optional local-only private diagrams.
// DiagramExamples.DocsBuilderErd.cs (untracked) can implement AppendPrivateExamples to inject extras.
public static partial class DiagramExamples
{
	public static readonly DiagramExample[] All = BuildAll();

	public static DiagramExample[] ByCategory(DiagramCategory category) =>
		All.Where(e => e.Category == category).ToArray();

	public static string CategorySlug(DiagramCategory c) =>
		Mermaider.Examples.DiagramExamples.CategorySlug(c);

	public static string CategoryLabel(DiagramCategory c) =>
		Mermaider.Examples.DiagramExamples.CategoryLabel(c);

	static partial void AppendPrivateExamples(List<DiagramExample> list);

	private static DiagramExample[] BuildAll()
	{
		var list = new List<DiagramExample>(Mermaider.Examples.DiagramExamples.All);
		AppendPrivateExamples(list);
		return [.. list];
	}
}
