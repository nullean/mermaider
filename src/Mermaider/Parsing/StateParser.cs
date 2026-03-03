using System.Text.RegularExpressions;
using Mermaider.Models;
using Mermaider.Text;

namespace Mermaider.Parsing;

internal static partial class StateParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^direction\s+(TD|TB|LR|BT|RL)\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex DirectionPattern();

	[GeneratedRegex(@"^state\s+(?:""([^""]+)""\s+as\s+)?(\w+)\s*\{$", RegexOptions.None, TimeoutMs)]
	private static partial Regex CompositeStatePattern();

	[GeneratedRegex(@"^state\s+""([^""]+)""\s+as\s+(\w+)\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex StateAliasPattern();

	[GeneratedRegex(@"^(\[\*\]|[\w-]+)\s*(-->)\s*(\[\*\]|[\w-]+)(?:\s*:\s*(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex TransitionPattern();

	[GeneratedRegex(@"^([\w-]+)\s*:\s*(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex StateDescPattern();

	[GeneratedRegex(@"^state\s+(\w+)\s+<<(choice|fork|join)>>\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex StereotypePattern();

	[GeneratedRegex(@"^note\s+(left|right)\s+of\s+(\w[\w-]*)\s*:\s*(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex NoteInlinePattern();

	[GeneratedRegex(@"^note\s+(left|right)\s+of\s+(\w[\w-]*)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex NoteBlockStartPattern();

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
		var direction = Direction.TD;
		var nodes = new Dictionary<string, MermaidNode>();
		var edges = new List<MermaidEdge>();
		var subgraphs = new List<MermaidSubgraph>();
		var compositeStack = new Stack<(string Id, string Label, List<string> NodeIds, List<MermaidSubgraph> Children, Direction? Dir)>();
		var compositeStateIds = new HashSet<string>();
		var startCount = 0;
		var endCount = 0;
		var notes = new List<GraphNote>();
		string? pendingNoteTarget = null;
		var pendingNotePosition = GraphNotePosition.Right;
		var pendingNoteText = new List<string>();

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			var dirMatch = DirectionPattern().Match(line);
			if (dirMatch.Success)
			{
				var dir = Enum.Parse<Direction>(dirMatch.Groups[1].Value.ToUpperInvariant());
				if (compositeStack.Count > 0)
				{
					var current = compositeStack.Pop();
					current.Dir = dir;
					compositeStack.Push(current);
				}
				else
				{
					direction = dir;
				}
				continue;
			}

			if (pendingNoteTarget != null)
			{
				if (line.Equals("end note", StringComparison.OrdinalIgnoreCase))
				{
					notes.Add(new GraphNote(pendingNoteTarget, string.Join("\n", pendingNoteText), pendingNotePosition));
					pendingNoteTarget = null;
					pendingNoteText.Clear();
					continue;
				}
				pendingNoteText.Add(line);
				continue;
			}

			var noteInlineMatch = NoteInlinePattern().Match(line);
			if (noteInlineMatch.Success)
			{
				var pos = noteInlineMatch.Groups[1].Value.Equals("left", StringComparison.OrdinalIgnoreCase) ? GraphNotePosition.Left : GraphNotePosition.Right;
				var target = noteInlineMatch.Groups[2].Value;
				var text = noteInlineMatch.Groups[3].Value.Trim();
				notes.Add(new GraphNote(target, text, pos));
				continue;
			}

			var noteBlockMatch = NoteBlockStartPattern().Match(line);
			if (noteBlockMatch.Success)
			{
				pendingNotePosition = noteBlockMatch.Groups[1].Value.Equals("left", StringComparison.OrdinalIgnoreCase) ? GraphNotePosition.Left : GraphNotePosition.Right;
				pendingNoteTarget = noteBlockMatch.Groups[2].Value;
				continue;
			}

			var compositeMatch = CompositeStatePattern().Match(line);
			if (compositeMatch.Success)
			{
				var label = compositeMatch.Groups[1].Success ? compositeMatch.Groups[1].Value : compositeMatch.Groups[2].Value;
				var id = compositeMatch.Groups[2].Value;
				compositeStack.Push((id, label, [], [], null));
				_ = compositeStateIds.Add(id);
				_ = nodes.Remove(id);
				continue;
			}

			if (line == "}")
			{
				if (compositeStack.Count > 0)
				{
					var completed = compositeStack.Pop();
					var sg = new MermaidSubgraph
					{
						Id = completed.Id,
						Label = completed.Label,
						NodeIds = completed.NodeIds,
						Children = completed.Children,
						Direction = completed.Dir
					};
					if (compositeStack.Count > 0)
					{
						var parent = compositeStack.Pop();
						parent.Children.Add(sg);
						compositeStack.Push(parent);
					}
					else
					{
						subgraphs.Add(sg);
					}
				}
				continue;
			}

			var stereoMatch = StereotypePattern().Match(line);
			if (stereoMatch.Success)
			{
				var id = stereoMatch.Groups[1].Value;
				var stereo = stereoMatch.Groups[2].Value.ToLowerInvariant();
				var shape = stereo == "choice" ? NodeShape.Diamond : NodeShape.ForkJoin;
				RegisterStateNode(nodes, compositeStack, new MermaidNode(id, "", shape));
				continue;
			}

			var stateAliasMatch = StateAliasPattern().Match(line);
			if (stateAliasMatch.Success)
			{
				var label = MultilineUtils.NormalizeBrTags(stateAliasMatch.Groups[1].Value);
				var id = stateAliasMatch.Groups[2].Value;
				RegisterStateNode(nodes, compositeStack, new MermaidNode(id, label, NodeShape.Rounded));
				continue;
			}

			var transitionMatch = TransitionPattern().Match(line);
			if (transitionMatch.Success)
			{
				var sourceId = transitionMatch.Groups[1].Value;
				var targetId = transitionMatch.Groups[3].Value;
				var rawLabel = transitionMatch.Groups[4].Success ? transitionMatch.Groups[4].Value.Trim() : null;
				var edgeLabel = rawLabel is { Length: > 0 } ? MultilineUtils.NormalizeBrTags(rawLabel) : null;

				if (sourceId == "[*]")
				{
					startCount++;
					sourceId = startCount > 1 ? $"_start{startCount}" : "_start";
					RegisterStateNode(nodes, compositeStack, new MermaidNode(sourceId, "", NodeShape.StateStart));
				}
				else if (!compositeStateIds.Contains(sourceId))
				{
					EnsureStateNode(nodes, compositeStack, sourceId);
				}

				if (targetId == "[*]")
				{
					endCount++;
					targetId = endCount > 1 ? $"_end{endCount}" : "_end";
					RegisterStateNode(nodes, compositeStack, new MermaidNode(targetId, "", NodeShape.StateEnd));
				}
				else if (!compositeStateIds.Contains(targetId))
				{
					EnsureStateNode(nodes, compositeStack, targetId);
				}

				edges.Add(new MermaidEdge(sourceId, targetId, edgeLabel, EdgeStyle.Solid, false, true));
				continue;
			}

			var stateDescMatch = StateDescPattern().Match(line);
			if (stateDescMatch.Success)
			{
				var id = stateDescMatch.Groups[1].Value;
				var label = MultilineUtils.NormalizeBrTags(stateDescMatch.Groups[2].Value.Trim());
				RegisterStateNode(nodes, compositeStack, new MermaidNode(id, label, NodeShape.Rounded));
			}
		}

		return new MermaidGraph
		{
			Direction = direction,
			Nodes = nodes,
			NodeOrder = nodes.Keys.ToList(),
			Edges = edges,
			Subgraphs = subgraphs,
			Notes = notes,
		};
	}

	private static void RegisterStateNode(
		Dictionary<string, MermaidNode> nodes,
		Stack<(string Id, string Label, List<string> NodeIds, List<MermaidSubgraph> Children, Direction? Dir)> compositeStack,
		MermaidNode node)
	{
		_ = nodes.TryAdd(node.Id, node);
		if (compositeStack.Count > 0)
		{
			var current = compositeStack.Peek();
			if (!current.NodeIds.Contains(node.Id))
				current.NodeIds.Add(node.Id);
		}
	}

	private static void EnsureStateNode(
		Dictionary<string, MermaidNode> nodes,
		Stack<(string Id, string Label, List<string> NodeIds, List<MermaidSubgraph> Children, Direction? Dir)> compositeStack,
		string id)
	{
		if (!nodes.ContainsKey(id))
			RegisterStateNode(nodes, compositeStack, new MermaidNode(id, id, NodeShape.Rounded));
		else if (compositeStack.Count > 0)
		{
			var current = compositeStack.Peek();
			if (!current.NodeIds.Contains(id))
				current.NodeIds.Add(id);
		}
	}
}
