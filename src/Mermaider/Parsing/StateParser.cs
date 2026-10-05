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

	[GeneratedRegex(@"^([\w-]+)\s*:\s*(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex StateDescPattern();

	[GeneratedRegex(@"^state\s+(\w+)\s+<<(choice|fork|join)>>\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex StereotypePattern();

	[GeneratedRegex(@"^note\s+(left|right)\s+of\s+(\w[\w-]*)\s*:\s*(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex NoteInlinePattern();

	[GeneratedRegex(@"^note\s+(left|right)\s+of\s+(\w[\w-]*)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex NoteBlockStartPattern();

	[GeneratedRegex(@"^classDef\s+(\w+)\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassDefPattern();

	[GeneratedRegex(@"^class\s+([\w,-]+)\s+(\w+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassAssignPattern();

	[GeneratedRegex(@"^style\s+([\w,-]+)\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex StylePattern();

	[GeneratedRegex(@"^(\[\*\]|[\w-]+)(?::::([\w][\w-]*))?\s*(-->)\s*(\[\*\]|[\w-]+)(?::::([\w][\w-]*))?(?:\s*:\s*(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex TransitionWithClassPattern();

	[GeneratedRegex(@"^([\w-]+):::([\w][\w-]*)\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassShorthandPattern();

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
		// every [*] in one scope (the diagram or one composite state) is the same start / end node, as in mermaid.js
		var scopeStart = new Dictionary<string, string>(StringComparer.Ordinal);
		var scopeEnd = new Dictionary<string, string>(StringComparer.Ordinal);
		var notes = new List<GraphNote>();
		string? pendingNoteTarget = null;
		var pendingNotePosition = GraphNotePosition.Right;
		var pendingNoteText = new List<string>();
		var classDefs = new Dictionary<string, IReadOnlyDictionary<string, string>>();
		var classAssignments = new Dictionary<string, string>();
		var nodeStyles = new Dictionary<string, Dictionary<string, string>>();

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
						existing = [];
						nodeStyles[id] = existing;
					}
					foreach (var kvp in props)
						existing[kvp.Key] = kvp.Value;
				}
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

			var transitionMatch = TransitionWithClassPattern().Match(line);
			if (transitionMatch.Success)
			{
				var sourceId = transitionMatch.Groups[1].Value;
				var sourceClass = transitionMatch.Groups[2].Success ? transitionMatch.Groups[2].Value : null;
				var targetId = transitionMatch.Groups[4].Value;
				var targetClass = transitionMatch.Groups[5].Success ? transitionMatch.Groups[5].Value : null;
				var rawLabel = transitionMatch.Groups[6].Success ? transitionMatch.Groups[6].Value.Trim() : null;
				var edgeLabel = rawLabel is { Length: > 0 } ? MultilineUtils.NormalizeBrTags(rawLabel) : null;

				if (sourceId == "[*]")
				{
					var scope = compositeStack.Count > 0 ? compositeStack.Peek().Id : "";
					if (!scopeStart.TryGetValue(scope, out var existingStart))
					{
						startCount++;
						existingStart = startCount > 1 ? $"_start{startCount}" : "_start";
						scopeStart[scope] = existingStart;
						RegisterStateNode(nodes, compositeStack, new MermaidNode(existingStart, "", NodeShape.StateStart));
					}

					sourceId = existingStart;
				}
				else if (!compositeStateIds.Contains(sourceId))
				{
					EnsureStateNode(nodes, compositeStack, sourceId);
					if (sourceClass != null)
						classAssignments[sourceId] = sourceClass;
				}

				if (targetId == "[*]")
				{
					var scope = compositeStack.Count > 0 ? compositeStack.Peek().Id : "";
					if (!scopeEnd.TryGetValue(scope, out var existingEnd))
					{
						endCount++;
						existingEnd = endCount > 1 ? $"_end{endCount}" : "_end";
						scopeEnd[scope] = existingEnd;
						RegisterStateNode(nodes, compositeStack, new MermaidNode(existingEnd, "", NodeShape.StateEnd));
					}

					targetId = existingEnd;
				}
				else if (!compositeStateIds.Contains(targetId))
				{
					EnsureStateNode(nodes, compositeStack, targetId);
					if (targetClass != null)
						classAssignments[targetId] = targetClass;
				}

				edges.Add(new MermaidEdge(sourceId, targetId, edgeLabel, EdgeStyle.Solid, false, true));
				continue;
			}

			var classShorthandMatch = ClassShorthandPattern().Match(line);
			if (classShorthandMatch.Success)
			{
				var id = classShorthandMatch.Groups[1].Value;
				var className = classShorthandMatch.Groups[2].Value;
				EnsureStateNode(nodes, compositeStack, id);
				classAssignments[id] = className;
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

		if (classDefs.ContainsKey("default"))
		{
			foreach (var nodeId in nodes.Keys)
			{
				_ = classAssignments.TryAdd(nodeId, "default");
			}
		}

		// Redirect edges that reference composite state IDs to their entry/exit nodes,
		// so the Sugiyama engine can order composites correctly (First above Second, etc.)
		var redirections = new Dictionary<int, (string? SourceSg, string? TargetSg)>();
		if (compositeStateIds.Count > 0)
		{
			var compositeMap = new Dictionary<string, MermaidSubgraph>();
			foreach (var sg in subgraphs)
				compositeMap[sg.Id] = sg;

			for (var i = 0; i < edges.Count; i++)
			{
				var edge = edges[i];
				var src = edge.Source;
				var tgt = edge.Target;

				if (compositeMap.TryGetValue(src, out _))
					src = FindCompositeExitNode(compositeMap[src], nodes) ?? src;

				if (compositeMap.TryGetValue(tgt, out _))
					tgt = FindCompositeEntryNode(compositeMap[tgt], nodes) ?? tgt;

				if (src != edge.Source || tgt != edge.Target)
				{
					redirections[i] = (
						src != edge.Source ? edge.Source : null,
						tgt != edge.Target ? edge.Target : null);
					edges[i] = new MermaidEdge(src, tgt, edge.Label, edge.Style, edge.HasArrowStart, edge.HasArrowEnd);
				}
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
			ClassDefs = classDefs,
			ClassAssignments = classAssignments,
			SubgraphEdgeRedirections = redirections,
			NodeStyles = nodeStyles.ToDictionary(
				kvp => kvp.Key,
				kvp => (IReadOnlyDictionary<string, string>)kvp.Value),
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

	private static string? FindCompositeEntryNode(MermaidSubgraph sg, Dictionary<string, MermaidNode> nodes)
	{
		foreach (var id in sg.NodeIds)
		{
			if (nodes.TryGetValue(id, out var n) && n.Shape == NodeShape.StateStart)
				return id;
		}
		return sg.NodeIds.Count > 0 ? sg.NodeIds[0] : null;
	}

	private static string? FindCompositeExitNode(MermaidSubgraph sg, Dictionary<string, MermaidNode> nodes)
	{
		foreach (var id in sg.NodeIds)
		{
			if (nodes.TryGetValue(id, out var n) && n.Shape == NodeShape.StateEnd)
				return id;
		}
		return sg.NodeIds.Count > 0 ? sg.NodeIds[^1] : null;
	}

	private static IReadOnlyDictionary<string, string> ParseStyleProps(string propsStr)
	{
		var props = new Dictionary<string, string>();
		foreach (var pair in propsStr.Split(','))
		{
			var colonIdx = pair.IndexOf(':');
			if (colonIdx <= 0)
				continue;
			var key = pair[..colonIdx].Trim();
			var val = pair[(colonIdx + 1)..].Trim();
			if (key.Length > 0 && val.Length > 0)
				props[key] = val;
		}
		return props;
	}
}
