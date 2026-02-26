using System.Text.RegularExpressions;
using Mermaid.Models;
using Mermaid.Text;

namespace Mermaid.Parsing;

internal static partial class FlowchartParser
{
	private const int TimeoutMs = 2000;

	// ========================================================================
	// Generated regex patterns
	// ========================================================================

	[GeneratedRegex(@"^(?:graph|flowchart)\s+(TD|TB|LR|BT|RL)\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex HeaderPattern();

	[GeneratedRegex(@"^classDef\s+(\w+)\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassDefPattern();

	[GeneratedRegex(@"^class\s+([\w,-]+)\s+(\w+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassAssignPattern();

	[GeneratedRegex(@"^style\s+([\w,-]+)\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex StylePattern();

	[GeneratedRegex(@"^direction\s+(TD|TB|LR|BT|RL)\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex DirectionPattern();

	[GeneratedRegex(@"^subgraph\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex SubgraphPattern();

	[GeneratedRegex(@"^([\w-]+)\s*\[(.+)\]$", RegexOptions.None, TimeoutMs)]
	private static partial Regex SubgraphBracketPattern();

	[GeneratedRegex(@"^(<)?(-{2,}>|-{3,}|-\.+->|-\.+-|={2,}>|={3,})(?:\|([^|]*)\|)?", RegexOptions.None, TimeoutMs)]
	private static partial Regex ArrowPattern();

	[GeneratedRegex(@"^(<)?(-{2}|={2}|-\.)\s+(.+?)\s+(-{2}>|={2}>|\.->)", RegexOptions.None, TimeoutMs)]
	private static partial Regex InlineTextArrowPattern();

	[GeneratedRegex(@"^:::([\w][\w-]*)", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassShorthandPattern();

	[GeneratedRegex(@"^([\w-]+)\(\(\((.+?)\)\)\)", RegexOptions.None, TimeoutMs)]
	private static partial Regex DoubleCirclePattern();

	[GeneratedRegex(@"^([\w-]+)\(\[(.+?)\]\)", RegexOptions.None, TimeoutMs)]
	private static partial Regex StadiumPattern();

	[GeneratedRegex(@"^([\w-]+)\(\((.+?)\)\)", RegexOptions.None, TimeoutMs)]
	private static partial Regex CirclePattern();

	[GeneratedRegex(@"^([\w-]+)\[\[(.+?)\]\]", RegexOptions.None, TimeoutMs)]
	private static partial Regex SubroutinePattern();

	[GeneratedRegex(@"^([\w-]+)\[\((.+?)\)\]", RegexOptions.None, TimeoutMs)]
	private static partial Regex CylinderPattern();

	[GeneratedRegex(@"^([\w-]+)\[/(.+?)\\\]", RegexOptions.None, TimeoutMs)]
	private static partial Regex TrapezoidPattern();

	[GeneratedRegex(@"^([\w-]+)\[\\(.+?)/\]", RegexOptions.None, TimeoutMs)]
	private static partial Regex TrapezoidAltPattern();

	[GeneratedRegex(@"^([\w-]+)>(.+?)\]", RegexOptions.None, TimeoutMs)]
	private static partial Regex AsymmetricPattern();

	[GeneratedRegex(@"^([\w-]+)\{\{(.+?)\}\}", RegexOptions.None, TimeoutMs)]
	private static partial Regex HexagonPattern();

	[GeneratedRegex(@"^([\w-]+)\[(.+?)\]", RegexOptions.None, TimeoutMs)]
	private static partial Regex RectanglePattern();

	[GeneratedRegex(@"^([\w-]+)\((.+?)\)", RegexOptions.None, TimeoutMs)]
	private static partial Regex RoundedPattern();

	[GeneratedRegex(@"^([\w-]+)\{(.+?)\}", RegexOptions.None, TimeoutMs)]
	private static partial Regex DiamondPattern();

	[GeneratedRegex(@"^([\w]+(?:-[\w]+)*)", RegexOptions.None, TimeoutMs)]
	private static partial Regex BareNodePattern();

	private static readonly (Func<Regex> Pattern, NodeShape Shape)[] s_nodePatterns =
	[
		(DoubleCirclePattern, NodeShape.DoubleCircle),
		(StadiumPattern, NodeShape.Stadium),
		(CirclePattern, NodeShape.Circle),
		(SubroutinePattern, NodeShape.Subroutine),
		(CylinderPattern, NodeShape.Cylinder),
		(TrapezoidPattern, NodeShape.Trapezoid),
		(TrapezoidAltPattern, NodeShape.TrapezoidAlt),
		(AsymmetricPattern, NodeShape.Asymmetric),
		(HexagonPattern, NodeShape.Hexagon),
		(RectanglePattern, NodeShape.Rectangle),
		(RoundedPattern, NodeShape.Rounded),
		(DiamondPattern, NodeShape.Diamond),
	];

	// ========================================================================
	// Parse entry point
	// ========================================================================

	internal static MermaidGraph Parse(string[] lines)
	{
		try
		{
			return ParseCore(lines);
		}
		catch (RegexMatchTimeoutException ex)
		{
			throw new MermaidParseException(
				$"Parsing timed out after {ex.MatchTimeout.TotalSeconds}s — input may contain pathological patterns.",
				ex);
		}
	}

	private static MermaidGraph ParseCore(string[] lines)
	{
		var headerMatch = HeaderPattern().Match(lines[0]);
		if (!headerMatch.Success)
			throw new MermaidParseException($"Invalid mermaid header: \"{lines[0]}\". Expected \"graph TD\", \"flowchart LR\", etc.");

		var direction = Enum.Parse<Direction>(headerMatch.Groups[1].Value.ToUpperInvariant());

		var nodes = new Dictionary<string, MermaidNode>();
		var edges = new List<MermaidEdge>();
		var subgraphs = new List<MermaidSubgraph>();
		var classDefs = new Dictionary<string, IReadOnlyDictionary<string, string>>();
		var classAssignments = new Dictionary<string, string>();
		var nodeStyles = new Dictionary<string, Dictionary<string, string>>();
		var subgraphStack = new Stack<(string Id, string Label, List<string> NodeIds, List<MermaidSubgraph> Children, Direction? Dir)>();

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			var classDefMatch = ClassDefPattern().Match(line);
			if (classDefMatch.Success)
			{
				var name = classDefMatch.Groups[1].Value;
				var propsStr = classDefMatch.Groups[2].Value;
				classDefs[name] = ParseStyleProps(propsStr);
				continue;
			}

			var classAssignMatch = ClassAssignPattern().Match(line);
			if (classAssignMatch.Success)
			{
				var nodeIds = classAssignMatch.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries);
				var className = classAssignMatch.Groups[2].Value;
				foreach (var id in nodeIds)
					classAssignments[id] = className;
				continue;
			}

			var styleMatch = StylePattern().Match(line);
			if (styleMatch.Success)
			{
				var nodeIds = styleMatch.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries);
				var props = ParseStyleProps(styleMatch.Groups[2].Value);
				foreach (var id in nodeIds)
				{
					if (!nodeStyles.TryGetValue(id, out var existing))
					{
						existing = new Dictionary<string, string>();
						nodeStyles[id] = existing;
					}
					foreach (var kvp in props)
						existing[kvp.Key] = kvp.Value;
				}
				continue;
			}

			var dirMatch = DirectionPattern().Match(line);
			if (dirMatch.Success && subgraphStack.Count > 0)
			{
				var current = subgraphStack.Pop();
				current.Dir = Enum.Parse<Direction>(dirMatch.Groups[1].Value.ToUpperInvariant());
				subgraphStack.Push(current);
				continue;
			}

			var subgraphMatch = SubgraphPattern().Match(line);
			if (subgraphMatch.Success)
			{
				var rest = subgraphMatch.Groups[1].Value.Trim();
				var bracketMatch = SubgraphBracketPattern().Match(rest);
				string id, label;
				if (bracketMatch.Success)
				{
					id = bracketMatch.Groups[1].Value;
					label = MultilineUtils.NormalizeBrTags(bracketMatch.Groups[2].Value);
				}
				else
				{
					label = MultilineUtils.NormalizeBrTags(rest);
					id = Regex.Replace(rest.Replace(' ', '_'), @"[^\w]", "", RegexOptions.None, TimeSpan.FromSeconds(2));
				}
				subgraphStack.Push((id, label, [], [], null));
				continue;
			}

			if (line == "end")
			{
				if (subgraphStack.Count > 0)
				{
					var completed = subgraphStack.Pop();
					if (!completed.NodeIds.Contains(completed.Id))
						completed.NodeIds.Add(completed.Id);
					var sg = new MermaidSubgraph
					{
						Id = completed.Id,
						Label = completed.Label,
						NodeIds = completed.NodeIds,
						Children = completed.Children,
						Direction = completed.Dir
					};
					if (subgraphStack.Count > 0)
					{
						var parent = subgraphStack.Pop();
						parent.Children.Add(sg);
						subgraphStack.Push(parent);
					}
					else
					{
						subgraphs.Add(sg);
					}
				}
				continue;
			}

			ParseEdgeLine(line, nodes, edges, classAssignments, subgraphStack);
		}

		return new MermaidGraph
		{
			Direction = direction,
			Nodes = nodes,
			NodeOrder = nodes.Keys.ToList(),
			Edges = edges,
			Subgraphs = subgraphs,
			ClassDefs = classDefs,
			ClassAssignments = classAssignments,
			NodeStyles = nodeStyles.ToDictionary(
				kvp => kvp.Key,
				kvp => (IReadOnlyDictionary<string, string>)kvp.Value)
		};
	}

	// ========================================================================
	// Edge line parsing
	// ========================================================================

	private static void ParseEdgeLine(
		string line,
		Dictionary<string, MermaidNode> nodes,
		List<MermaidEdge> edges,
		Dictionary<string, string> classAssignments,
		Stack<(string Id, string Label, List<string> NodeIds, List<MermaidSubgraph> Children, Direction? Dir)> subgraphStack)
	{
		var remaining = line.Trim();

		var (firstIds, rest) = ConsumeNodeGroup(remaining, nodes, classAssignments, subgraphStack);
		if (firstIds.Count == 0)
			return;

		remaining = rest.Trim();
		var prevGroupIds = firstIds;

		while (remaining.Length > 0)
		{
			bool hasArrowStart;
			string arrowOp;
			string? edgeLabel;
			int consumedLength;

			var inlineMatch = InlineTextArrowPattern().Match(remaining);
			if (inlineMatch.Success)
			{
				hasArrowStart = inlineMatch.Groups[1].Success;
				arrowOp = inlineMatch.Groups[4].Value;
				var rawLabel = inlineMatch.Groups[3].Value.Trim();
				edgeLabel = rawLabel.Length > 0 ? MultilineUtils.NormalizeBrTags(rawLabel) : null;
				consumedLength = inlineMatch.Length;
			}
			else
			{
				var arrowMatch = ArrowPattern().Match(remaining);
				if (!arrowMatch.Success)
					break;

				hasArrowStart = arrowMatch.Groups[1].Success;
				arrowOp = arrowMatch.Groups[2].Value;
				var rawLabel = arrowMatch.Groups[3].Success ? arrowMatch.Groups[3].Value.Trim() : null;
				edgeLabel = rawLabel is { Length: > 0 } ? MultilineUtils.NormalizeBrTags(rawLabel) : null;
				consumedLength = arrowMatch.Length;
			}

			remaining = remaining[consumedLength..].Trim();

			var style = ArrowStyleFromOp(arrowOp);
			var hasArrowEnd = arrowOp.EndsWith('>');

			var (nextIds, nextRest) = ConsumeNodeGroup(remaining, nodes, classAssignments, subgraphStack);
			if (nextIds.Count == 0)
				break;

			remaining = nextRest.Trim();

			foreach (var sourceId in prevGroupIds)
			{
				foreach (var targetId in nextIds)
				{
					edges.Add(new MermaidEdge(sourceId, targetId, edgeLabel, style, hasArrowStart, hasArrowEnd));
				}
			}

			prevGroupIds = nextIds;
		}
	}

	private static (List<string> Ids, string Remaining) ConsumeNodeGroup(
		string text,
		Dictionary<string, MermaidNode> nodes,
		Dictionary<string, string> classAssignments,
		Stack<(string Id, string Label, List<string> NodeIds, List<MermaidSubgraph> Children, Direction? Dir)> subgraphStack)
	{
		var (firstId, rest) = ConsumeNode(text, nodes, classAssignments, subgraphStack);
		if (firstId is null)
			return ([], text);

		var ids = new List<string> { firstId };
		var remaining = rest.Trim();

		while (remaining.StartsWith('&'))
		{
			remaining = remaining[1..].Trim();
			var (nextId, nextRest) = ConsumeNode(remaining, nodes, classAssignments, subgraphStack);
			if (nextId is null)
				break;
			ids.Add(nextId);
			remaining = nextRest.Trim();
		}

		return (ids, remaining);
	}

	private static (string? Id, string Remaining) ConsumeNode(
		string text,
		Dictionary<string, MermaidNode> nodes,
		Dictionary<string, string> classAssignments,
		Stack<(string Id, string Label, List<string> NodeIds, List<MermaidSubgraph> Children, Direction? Dir)> subgraphStack)
	{
		string? id = null;
		var remaining = text;

		foreach (var (patternFunc, shape) in s_nodePatterns)
		{
			var match = patternFunc().Match(text);
			if (match.Success)
			{
				id = match.Groups[1].Value;
				var label = MultilineUtils.NormalizeBrTags(match.Groups[2].Value);
				RegisterNode(nodes, subgraphStack, new MermaidNode(id, label, shape));
				remaining = text[match.Length..];
				break;
			}
		}

		if (id is null)
		{
			var bareMatch = BareNodePattern().Match(text);
			if (bareMatch.Success)
			{
				id = bareMatch.Groups[1].Value;
				if (nodes.ContainsKey(id))
					AddToCurrentSubgraph(subgraphStack, id);
				else
					RegisterNode(nodes, subgraphStack, new MermaidNode(id, id, NodeShape.Rectangle));
				remaining = text[bareMatch.Length..];
			}
		}

		if (id is null)
			return (null, text);

		var classMatch = ClassShorthandPattern().Match(remaining);
		if (classMatch.Success)
		{
			classAssignments[id] = classMatch.Groups[1].Value;
			remaining = remaining[classMatch.Length..];
		}

		return (id, remaining);
	}

	// ========================================================================
	// Helpers
	// ========================================================================

	private static void RegisterNode(
		Dictionary<string, MermaidNode> nodes,
		Stack<(string Id, string Label, List<string> NodeIds, List<MermaidSubgraph> Children, Direction? Dir)> subgraphStack,
		MermaidNode node)
	{
		nodes.TryAdd(node.Id, node);
		AddToCurrentSubgraph(subgraphStack, node.Id);
	}

	private static void AddToCurrentSubgraph(
		Stack<(string Id, string Label, List<string> NodeIds, List<MermaidSubgraph> Children, Direction? Dir)> subgraphStack,
		string nodeId)
	{
		if (subgraphStack.Count > 0)
		{
			var current = subgraphStack.Peek();
			if (!current.NodeIds.Contains(nodeId))
				current.NodeIds.Add(nodeId);
		}
	}

	private static EdgeStyle ArrowStyleFromOp(string op)
	{
		if (op.Contains('.'))
			return EdgeStyle.Dotted;
		if (op.Contains('='))
			return EdgeStyle.Thick;
		return EdgeStyle.Solid;
	}

	private static IReadOnlyDictionary<string, string> ParseStyleProps(string propsStr)
	{
		var props = new Dictionary<string, string>();
		foreach (var pair in propsStr.Split(','))
		{
			var colonIdx = pair.IndexOf(':');
			if (colonIdx <= 0) continue;
			var key = pair[..colonIdx].Trim();
			var val = pair[(colonIdx + 1)..].Trim();
			if (key.Length > 0 && val.Length > 0)
				props[key] = val;
		}
		return props;
	}

}
