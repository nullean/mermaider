using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;
using Mermaider.Text;

namespace Mermaider.Parsing;

internal static partial class SequenceParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^(participant|actor)\s+(\S+?)(?:\s+as\s+(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ActorPattern();

	[GeneratedRegex(@"^Note\s+(left of|right of|over)\s+([^:]+):\s*(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex NotePattern();

	[GeneratedRegex(@"^(loop|alt|opt|par|critical|break|rect)\s*(.*)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex BlockStartPattern();

	[GeneratedRegex(@"^(else|and)\s*(.*)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex DividerPattern();

	[GeneratedRegex(@"^(\S+?)\s*(->>|-->>|-\)|--\)|-x|--x|->|-->)\s*([+-]?)(\S+?)\s*:\s*(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex MessagePattern();

	[GeneratedRegex(@"^autonumber(?:\s+(\d+))?(?:\s+(\d+))?\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex AutonumberPattern();

	[GeneratedRegex(@"^(\S+?)\s*<<(->>|-->>)\s*(\S+?)\s*:\s*(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex BidirectionalMessagePattern();

	[GeneratedRegex(@"^box(?:\s+(\S+))?(?:\s+(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex BoxPattern();

	[GeneratedRegex(@"^create\s+(participant|actor)\s+(\S+?)(?:\s+as\s+(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex CreatePattern();

	[GeneratedRegex(@"^destroy\s+(\S+)\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex DestroyPattern();

	internal static SequenceDiagram Parse(string[] lines)
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

	private static SequenceDiagram ParseCore(string[] lines)
	{
		var actors = new List<SequenceActor>();
		var messages = new List<SequenceMessage>();
		var blocks = new List<SequenceBlock>();
		var notes = new List<SequenceNote>();
		var actorIds = new HashSet<string>();
		var blockStack = new Stack<(SequenceBlockType Type, string Label, int StartIndex, List<SequenceBlockDivider> Dividers)>();
		var boxes = new List<SequenceBox>();
		var boxStack = new Stack<(string? Color, string Title, List<string> ActorIds)>();
		var autoNumber = -1;
		var autoStep = 1;
		var creates = new List<SequenceCreate>();
		var destroys = new List<SequenceDestroy>();
		string? pendingCreate = null;
		string? pendingDestroy = null;

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			var autoMatch = AutonumberPattern().Match(line);
			if (autoMatch.Success)
			{
				autoNumber = autoMatch.Groups[1].Success ? int.Parse(autoMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 1;
				if (autoMatch.Groups[2].Success)
					autoStep = int.Parse(autoMatch.Groups[2].Value, CultureInfo.InvariantCulture);
				continue;
			}

			var boxMatch = BoxPattern().Match(line);
			if (boxMatch.Success)
			{
				var color = boxMatch.Groups[1].Success ? boxMatch.Groups[1].Value : null;
				var title = boxMatch.Groups[2].Success ? boxMatch.Groups[2].Value.Trim() : "";
				boxStack.Push((color, title, []));
				continue;
			}

			var createMatch = CreatePattern().Match(line);
			if (createMatch.Success)
			{
				var type = createMatch.Groups[1].Value == "actor" ? SequenceActorType.Actor : SequenceActorType.Participant;
				var id = createMatch.Groups[2].Value;
				var rawLabel = createMatch.Groups[3].Success ? createMatch.Groups[3].Value.Trim() : id;
				var label = MultilineUtils.NormalizeBrTags(rawLabel);
				if (actorIds.Add(id))
					actors.Add(new SequenceActor(id, label, type));
				pendingCreate = id;
				continue;
			}

			var destroyMatch = DestroyPattern().Match(line);
			if (destroyMatch.Success)
			{
				pendingDestroy = destroyMatch.Groups[1].Value;
				continue;
			}

			var actorMatch = ActorPattern().Match(line);
			if (actorMatch.Success)
			{
				var type = actorMatch.Groups[1].Value == "actor" ? SequenceActorType.Actor : SequenceActorType.Participant;
				var id = actorMatch.Groups[2].Value;
				var rawLabel = actorMatch.Groups[3].Success ? actorMatch.Groups[3].Value.Trim() : id;
				var label = MultilineUtils.NormalizeBrTags(rawLabel);
				if (actorIds.Add(id))
					actors.Add(new SequenceActor(id, label, type));
				if (boxStack.Count > 0)
					boxStack.Peek().ActorIds.Add(id);
				continue;
			}

			var noteMatch = NotePattern().Match(line);
			if (noteMatch.Success)
			{
				var posStr = noteMatch.Groups[1].Value.ToLowerInvariant();
				var actorsStr = noteMatch.Groups[2].Value.Trim();
				var text = MultilineUtils.NormalizeBrTags(noteMatch.Groups[3].Value.Trim());
				var noteActorIds = actorsStr.Split(',', StringSplitOptions.TrimEntries);

				foreach (var aid in noteActorIds)
					EnsureActor(actors, actorIds, aid);

				var position = posStr switch
				{
					"left of" => SequenceNotePosition.Left,
					"right of" => SequenceNotePosition.Right,
					_ => SequenceNotePosition.Over,
				};

				notes.Add(new SequenceNote(noteActorIds, text, position, messages.Count - 1));
				continue;
			}

			var blockMatch = BlockStartPattern().Match(line);
			if (blockMatch.Success)
			{
				var blockType = Enum.Parse<SequenceBlockType>(blockMatch.Groups[1].Value, ignoreCase: true);
				var rawLabel = blockMatch.Groups[2].Success ? blockMatch.Groups[2].Value.Trim() : "";
				var label = rawLabel.Length > 0 ? MultilineUtils.NormalizeBrTags(rawLabel) : "";
				blockStack.Push((blockType, label, messages.Count, []));
				continue;
			}

			var dividerMatch = DividerPattern().Match(line);
			if (dividerMatch.Success && blockStack.Count > 0)
			{
				var rawLabel = dividerMatch.Groups[2].Success ? dividerMatch.Groups[2].Value.Trim() : "";
				var label = rawLabel.Length > 0 ? MultilineUtils.NormalizeBrTags(rawLabel) : "";
				var current = blockStack.Peek();
				current.Dividers.Add(new SequenceBlockDivider(messages.Count, label));
				continue;
			}

			if (line == "end" && blockStack.Count > 0)
			{
				var completed = blockStack.Pop();
				blocks.Add(new SequenceBlock
				{
					Type = completed.Type,
					Label = completed.Label,
					StartIndex = completed.StartIndex,
					EndIndex = Math.Max(messages.Count - 1, completed.StartIndex),
					Dividers = completed.Dividers,
				});
				continue;
			}

			if (line == "end" && blockStack.Count == 0 && boxStack.Count > 0)
			{
				var completed = boxStack.Pop();
				boxes.Add(new SequenceBox(completed.Color, completed.Title, completed.ActorIds));
				continue;
			}

			var biMatch = BidirectionalMessagePattern().Match(line);
			if (biMatch.Success)
			{
				var from = biMatch.Groups[1].Value;
				var arrow = biMatch.Groups[2].Value;
				var to = biMatch.Groups[3].Value;
				var label = MultilineUtils.NormalizeBrTags(biMatch.Groups[4].Value.Trim());

				EnsureActor(actors, actorIds, from);
				EnsureActor(actors, actorIds, to);

				var lineStyle = arrow.StartsWith("--", StringComparison.OrdinalIgnoreCase) ? SequenceLineStyle.Dashed : SequenceLineStyle.Solid;

				if (autoNumber >= 0)
				{
					label = $"{autoNumber}. {label}";
					autoNumber += autoStep;
				}

				messages.Add(new SequenceMessage(from, to, label, lineStyle, SequenceArrowHead.Filled, Bidirectional: true));

				if (pendingCreate != null)
				{
					creates.Add(new SequenceCreate(pendingCreate, messages.Count - 1));
					pendingCreate = null;
				}
				if (pendingDestroy != null)
				{
					destroys.Add(new SequenceDestroy(pendingDestroy, messages.Count - 1));
					pendingDestroy = null;
				}

				continue;
			}

			var msgMatch = MessagePattern().Match(line);
			if (msgMatch.Success)
			{
				var from = msgMatch.Groups[1].Value;
				var arrow = msgMatch.Groups[2].Value;
				var activationMark = msgMatch.Groups[3].Value;
				var to = msgMatch.Groups[4].Value;
				var label = MultilineUtils.NormalizeBrTags(msgMatch.Groups[5].Value.Trim());

				if (autoNumber >= 0)
				{
					label = $"{autoNumber}. {label}";
					autoNumber += autoStep;
				}

				EnsureActor(actors, actorIds, from);
				EnsureActor(actors, actorIds, to);

				var lineStyle = arrow.StartsWith("--", StringComparison.OrdinalIgnoreCase) ? SequenceLineStyle.Dashed : SequenceLineStyle.Solid;
				var arrowHead = arrow.Contains(">>") || arrow.Contains('x') ? SequenceArrowHead.Filled : SequenceArrowHead.Open;

				messages.Add(new SequenceMessage(
					from, to, label, lineStyle, arrowHead,
					Activate: activationMark == "+",
					Deactivate: activationMark == "-"));

				if (pendingCreate != null)
				{
					creates.Add(new SequenceCreate(pendingCreate, messages.Count - 1));
					pendingCreate = null;
				}
				if (pendingDestroy != null)
				{
					destroys.Add(new SequenceDestroy(pendingDestroy, messages.Count - 1));
					pendingDestroy = null;
				}
			}
		}

		return new SequenceDiagram
		{
			Actors = actors,
			Messages = messages,
			Blocks = blocks,
			Notes = notes,
			Boxes = boxes,
			Creates = creates,
			Destroys = destroys,
		};
	}

	private static void EnsureActor(List<SequenceActor> actors, HashSet<string> actorIds, string id)
	{
		if (actorIds.Add(id))
			actors.Add(new SequenceActor(id, id, SequenceActorType.Participant));
	}
}
