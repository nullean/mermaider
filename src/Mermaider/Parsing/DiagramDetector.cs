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

	// Keyword gate only — optional showData/title on the same line are owned by the parsers.
	[GeneratedRegex(@"^pie(?:\s|$)", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex PieHeader();

	[GeneratedRegex(@"^quadrantChart(?:\s|$)", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex QuadrantHeader();

	[GeneratedRegex(@"^timeline(?:\s|$)", RegexOptions.IgnoreCase, TimeoutMs)]
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

	// Keyword gate — optional title on the same line is owned by GanttParser.
	[GeneratedRegex(@"^gantt(?:\s|$)", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex GanttHeader();

	// Keyword gate — optional title on the same line is owned by JourneyParser.
	[GeneratedRegex(@"^journey(?:\s|$)", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex JourneyHeader();
	// Keyword gate only — full header options live in C4Parser
	[GeneratedRegex(@"^C4(?:Context|Container|Component|Dynamic|Deployment)\b", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex C4Header();
	[GeneratedRegex(@"^sankey(?:-beta)?\b", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex SankeyHeader();
	[GeneratedRegex(@"^xychart(?:-beta)?\b", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex XyChartHeader();
	// Matches requirementDiagram and bare requirement (upstream detector).
	[GeneratedRegex(@"^requirement(Diagram)?\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex RequirementHeader();
	[GeneratedRegex(@"^packet(?:-beta)?\b", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex PacketHeader();

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
		if (GanttHeader().IsMatch(firstLineStr))
			return DiagramType.Gantt;
		if (JourneyHeader().IsMatch(firstLineStr))
			return DiagramType.Journey;
		if (C4Header().IsMatch(firstLineStr))
			return DiagramType.C4;
		if (SankeyHeader().IsMatch(firstLineStr))
			return DiagramType.Sankey;
		if (XyChartHeader().IsMatch(firstLineStr))
			return DiagramType.XyChart;
		if (RequirementHeader().IsMatch(firstLineStr))
			return DiagramType.Requirement;
		if (PacketHeader().IsMatch(firstLineStr))
			return DiagramType.Packet;

		return DiagramType.Flowchart;
	}
}

