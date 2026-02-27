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

		return DiagramType.Flowchart;
	}
}
