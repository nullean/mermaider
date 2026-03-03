using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class DiagramDetector
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^sequenceDiagram\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex SequenceHeader();

	[GeneratedRegex(@"^classDiagram\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex ClassHeader();

	[GeneratedRegex(@"^erDiagram\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex ErHeader();

	[GeneratedRegex(@"^stateDiagram(-v2)?\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex StateHeader();

	[GeneratedRegex(@"^pie\s*(showData)?\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex PieHeader();

	[GeneratedRegex(@"^quadrantChart\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex QuadrantHeader();

	[GeneratedRegex(@"^timeline\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex TimelineHeader();

	[GeneratedRegex(@"^gitGraph\s*(LR:|TB:|BT:)?\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex GitGraphHeader();

	[GeneratedRegex(@"^radar-beta\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex RadarHeader();

	[GeneratedRegex(@"^treemap-beta\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex TreemapHeader();

	[GeneratedRegex(@"^venn-beta\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex VennHeader();

	[GeneratedRegex(@"^mindmap\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex MindmapHeader();

	internal static DiagramType Detect(ReadOnlySpan<char> text)
	{
		var firstLineEnd = text.IndexOf('\n');
		var firstLine = firstLineEnd >= 0 ? text[..firstLineEnd] : text;
		firstLine = firstLine.Trim();

		var firstLineStr = firstLine.ToString();
		if (SequenceHeader().IsMatch(firstLineStr))
			return DiagramType.Sequence;
		if (ClassHeader().IsMatch(firstLineStr))
			return DiagramType.Class;
		if (ErHeader().IsMatch(firstLineStr))
			return DiagramType.Er;
		if (StateHeader().IsMatch(firstLineStr))
			return DiagramType.State;
		if (PieHeader().IsMatch(firstLineStr))
			return DiagramType.Pie;
		if (QuadrantHeader().IsMatch(firstLineStr))
			return DiagramType.Quadrant;
		if (TimelineHeader().IsMatch(firstLineStr))
			return DiagramType.Timeline;
		if (GitGraphHeader().IsMatch(firstLineStr))
			return DiagramType.GitGraph;
		if (RadarHeader().IsMatch(firstLineStr))
			return DiagramType.Radar;
		if (TreemapHeader().IsMatch(firstLineStr))
			return DiagramType.Treemap;
		if (VennHeader().IsMatch(firstLineStr))
			return DiagramType.Venn;
		if (MindmapHeader().IsMatch(firstLineStr))
			return DiagramType.Mindmap;

		return DiagramType.Flowchart;
	}
}
