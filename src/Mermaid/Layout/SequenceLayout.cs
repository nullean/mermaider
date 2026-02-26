using Mermaid.Models;
using Mermaid.Rendering;
using Mermaid.Text;

namespace Mermaid.Layout;

internal static class SequenceLayout
{
	private const double Padding = 30;
	private const double ActorGap = 140;
	private const double ActorHeight = 40;
	private const double ActorPadX = 16;
	private const double HeaderGap = 20;
	private const double MessageRowHeight = 40;
	private const double SelfMessageHeight = 30;
	private const double ActivationWidth = 10;
	private const double BlockPadX = 10;
	private const double BlockPadTop = 40;
	private const double BlockPadBottom = 8;
	private const double BlockHeaderExtra = 28;
	private const double DividerExtra = 24;
	private const double NoteWidth = 120;
	private const double NotePadding = 8;
	private const double NoteGap = 10;
	private const double NestingOffset = 4;

	internal static PositionedSequenceDiagram Layout(SequenceDiagram diagram)
	{
		if (diagram.Actors.Count == 0)
		{
			return new PositionedSequenceDiagram
			{
				Width = 0,
				Height = 0,
				Actors = [],
				Lifelines = [],
				Messages = [],
				Activations = [],
				Blocks = [],
				Notes = [],
			};
		}

		var actorWidths = new double[diagram.Actors.Count];
		for (var i = 0; i < diagram.Actors.Count; i++)
		{
			var textW = TextMetrics.MeasureTextWidth(
				diagram.Actors[i].Label,
				RenderConstants.FontSizes.NodeLabel,
				RenderConstants.FontWeights.NodeLabel);
			actorWidths[i] = Math.Max(textW + ActorPadX * 2, 80);
		}

		var actorCenterX = new double[diagram.Actors.Count];
		var currentX = Padding + actorWidths[0] / 2;
		for (var i = 0; i < diagram.Actors.Count; i++)
		{
			if (i > 0)
			{
				var minGap = Math.Max(ActorGap, (actorWidths[i - 1] + actorWidths[i]) / 2 + 40);
				currentX += minGap;
			}
			actorCenterX[i] = currentX;
		}

		var actorIndex = new Dictionary<string, int>(diagram.Actors.Count);
		for (var i = 0; i < diagram.Actors.Count; i++)
			actorIndex[diagram.Actors[i].Id] = i;

		var actorY = Padding;
		var actors = new PositionedSequenceActor[diagram.Actors.Count];
		for (var i = 0; i < diagram.Actors.Count; i++)
		{
			actors[i] = new PositionedSequenceActor
			{
				Id = diagram.Actors[i].Id,
				Label = diagram.Actors[i].Label,
				Type = diagram.Actors[i].Type,
				X = actorCenterX[i],
				Y = actorY,
				Width = actorWidths[i],
				Height = ActorHeight,
			};
		}

		var messageY = actorY + ActorHeight + HeaderGap;
		var messages = new List<PositionedSequenceMessage>(diagram.Messages.Count);

		var extraSpaceBefore = new Dictionary<int, double>();
		foreach (var block in diagram.Blocks)
		{
			extraSpaceBefore.TryGetValue(block.StartIndex, out var prev);
			extraSpaceBefore[block.StartIndex] = Math.Max(prev, BlockHeaderExtra);

			foreach (var div in block.Dividers)
			{
				extraSpaceBefore.TryGetValue(div.Index, out var prevDiv);
				extraSpaceBefore[div.Index] = Math.Max(prevDiv, DividerExtra);
			}
		}

		var activationStacks = new Dictionary<string, Stack<(double StartY, int Depth)>>();
		var activations = new List<Activation>();

		for (var msgIdx = 0; msgIdx < diagram.Messages.Count; msgIdx++)
		{
			var msg = diagram.Messages[msgIdx];
			actorIndex.TryGetValue(msg.From, out var fromIdx);
			actorIndex.TryGetValue(msg.To, out var toIdx);
			var isSelf = msg.From == msg.To;

			if (extraSpaceBefore.TryGetValue(msgIdx, out var extra) && extra > 0)
				messageY += extra;

			messages.Add(new PositionedSequenceMessage
			{
				From = msg.From,
				To = msg.To,
				Label = msg.Label,
				LineStyle = msg.LineStyle,
				ArrowHead = msg.ArrowHead,
				X1 = actorCenterX[fromIdx],
				X2 = actorCenterX[toIdx],
				Y = messageY,
				IsSelf = isSelf,
			});

			if (msg.Activate)
			{
				if (!activationStacks.TryGetValue(msg.To, out var stack))
				{
					stack = new Stack<(double, int)>();
					activationStacks[msg.To] = stack;
				}
				stack.Push((messageY, stack.Count));
			}

			if (msg.Deactivate && activationStacks.TryGetValue(msg.From, out var deactStack) && deactStack.Count > 0)
			{
				var (startY, depth) = deactStack.Pop();
				actorIndex.TryGetValue(msg.From, out var idx);
				activations.Add(new Activation
				{
					ActorId = msg.From,
					X = actorCenterX[idx] - ActivationWidth / 2 + depth * NestingOffset,
					TopY = startY,
					BottomY = messageY,
					Width = ActivationWidth,
				});
			}

			messageY += isSelf ? SelfMessageHeight + MessageRowHeight : MessageRowHeight;
		}

		foreach (var (actorId, stack) in activationStacks)
		{
			while (stack.Count > 0)
			{
				var (startY, depth) = stack.Pop();
				actorIndex.TryGetValue(actorId, out var idx);
				activations.Add(new Activation
				{
					ActorId = actorId,
					X = actorCenterX[idx] - ActivationWidth / 2 + depth * NestingOffset,
					TopY = startY,
					BottomY = messageY - MessageRowHeight / 2,
					Width = ActivationWidth,
				});
			}
		}

		var blocks = new List<PositionedSequenceBlock>(diagram.Blocks.Count);
		foreach (var block in diagram.Blocks)
		{
			var startMsg = block.StartIndex < messages.Count ? messages[block.StartIndex] : null;
			var endMsg = block.EndIndex < messages.Count ? messages[block.EndIndex] : null;
			var blockTop = (startMsg?.Y ?? messageY) - BlockPadTop;
			var blockBottom = (endMsg?.Y ?? messageY) + BlockPadBottom + 12;

			var involvedActors = new HashSet<int>();
			for (var mi = block.StartIndex; mi <= block.EndIndex; mi++)
			{
				if (mi < diagram.Messages.Count)
				{
					actorIndex.TryGetValue(diagram.Messages[mi].From, out var fi);
					actorIndex.TryGetValue(diagram.Messages[mi].To, out var ti);
					involvedActors.Add(fi);
					involvedActors.Add(ti);
				}
			}
			if (involvedActors.Count == 0)
			{
				for (var ai = 0; ai < diagram.Actors.Count; ai++)
					involvedActors.Add(ai);
			}

			var minIdx = int.MaxValue;
			var maxIdx = int.MinValue;
			foreach (var ai in involvedActors)
			{
				if (ai < minIdx) minIdx = ai;
				if (ai > maxIdx) maxIdx = ai;
			}

			var blockLeft = actorCenterX[minIdx] - actorWidths[minIdx] / 2 - BlockPadX;
			var blockRight = actorCenterX[maxIdx] + actorWidths[maxIdx] / 2 + BlockPadX;

			var dividers = new List<PositionedBlockDivider>(block.Dividers.Count);
			foreach (var d in block.Dividers)
			{
				var dMsg = d.Index < messages.Count ? messages[d.Index] : null;
				var msgY = dMsg?.Y ?? messageY;
				var offset = 28.0;

				if (d.Label.Length > 0 && dMsg?.Label.Length > 0)
				{
					var divLabelW = TextMetrics.MeasureTextWidth(
						$"[{d.Label}]",
						RenderConstants.FontSizes.EdgeLabel,
						RenderConstants.FontWeights.EdgeLabel);
					var divLabelLeft = blockLeft + 8;
					var divLabelRight = divLabelLeft + divLabelW;

					var msgLabelW = TextMetrics.MeasureTextWidth(
						dMsg.Label,
						RenderConstants.FontSizes.EdgeLabel,
						RenderConstants.FontWeights.EdgeLabel);
					var msgLabelLeft = dMsg.IsSelf
						? dMsg.X1 + 36
						: (dMsg.X1 + dMsg.X2) / 2 - msgLabelW / 2;
					var msgLabelRight = msgLabelLeft + msgLabelW;

					if (divLabelRight > msgLabelLeft && divLabelLeft < msgLabelRight)
						offset = 36;
				}

				dividers.Add(new PositionedBlockDivider(msgY - offset, d.Label));
			}

			blocks.Add(new PositionedSequenceBlock
			{
				Type = block.Type,
				Label = block.Label,
				X = blockLeft,
				Y = blockTop,
				Width = blockRight - blockLeft,
				Height = blockBottom - blockTop,
				Dividers = dividers,
			});
		}

		var notes = new List<PositionedSequenceNote>(diagram.Notes.Count);
		foreach (var note in diagram.Notes)
		{
			var noteW = Math.Max(
				NoteWidth,
				TextMetrics.MeasureTextWidth(
					note.Text,
					RenderConstants.FontSizes.EdgeLabel,
					RenderConstants.FontWeights.EdgeLabel) + NotePadding * 2);
			var noteH = RenderConstants.FontSizes.EdgeLabel + NotePadding * 2;

			var refMsg = note.AfterIndex >= 0 && note.AfterIndex < messages.Count
				? messages[note.AfterIndex]
				: null;
			var noteY = (refMsg?.Y ?? actorY + ActorHeight) + 4;

			actorIndex.TryGetValue(note.ActorIds[0], out var firstActorIdx);
			double noteX;
			if (note.Position == SequenceNotePosition.Left)
			{
				noteX = actorCenterX[firstActorIdx] - actorWidths[firstActorIdx] / 2 - noteW - NoteGap;
			}
			else if (note.Position == SequenceNotePosition.Right)
			{
				noteX = actorCenterX[firstActorIdx] + actorWidths[firstActorIdx] / 2 + NoteGap;
			}
			else
			{
				if (note.ActorIds.Count > 1)
				{
					actorIndex.TryGetValue(note.ActorIds[^1], out var lastActorIdx);
					noteX = (actorCenterX[firstActorIdx] + actorCenterX[lastActorIdx]) / 2 - noteW / 2;
				}
				else
				{
					noteX = actorCenterX[firstActorIdx] - noteW / 2;
				}
			}

			notes.Add(new PositionedSequenceNote
			{
				Text = note.Text,
				X = noteX,
				Y = noteY,
				Width = noteW,
				Height = noteH,
				Position = note.Position,
				Actors = note.ActorIds,
			});
		}

		var diagramBottom = messageY + Padding;
		var globalMinX = Padding;
		var globalMaxX = 0.0;

		foreach (var a in actors)
		{
			globalMinX = Math.Min(globalMinX, a.X - a.Width / 2);
			globalMaxX = Math.Max(globalMaxX, a.X + a.Width / 2);
		}
		foreach (var b in blocks)
		{
			globalMinX = Math.Min(globalMinX, b.X);
			globalMaxX = Math.Max(globalMaxX, b.X + b.Width);
		}
		foreach (var n in notes)
		{
			globalMinX = Math.Min(globalMinX, n.X);
			globalMaxX = Math.Max(globalMaxX, n.X + n.Width);
		}
		foreach (var m in messages)
		{
			if (m.IsSelf && m.Label.Length > 0)
			{
				const double loopW = 30;
				const double labelPadding = 8;
				var labelLeft = m.X1 + loopW + labelPadding;
				var labelWidth = TextMetrics.MeasureTextWidth(
					m.Label,
					RenderConstants.FontSizes.EdgeLabel,
					RenderConstants.FontWeights.EdgeLabel);
				globalMaxX = Math.Max(globalMaxX, labelLeft + labelWidth + 8);
			}
		}

		var shiftX = globalMinX < Padding ? Padding - globalMinX : 0;
		if (shiftX > 0)
		{
			for (var i = 0; i < actors.Length; i++)
				actors[i] = actors[i] with { X = actors[i].X + shiftX };

			for (var i = 0; i < messages.Count; i++)
				messages[i] = messages[i] with { X1 = messages[i].X1 + shiftX, X2 = messages[i].X2 + shiftX };

			for (var i = 0; i < activations.Count; i++)
				activations[i] = activations[i] with { X = activations[i].X + shiftX };

			for (var i = 0; i < blocks.Count; i++)
				blocks[i] = blocks[i] with { X = blocks[i].X + shiftX };

			for (var i = 0; i < notes.Count; i++)
				notes[i] = notes[i] with { X = notes[i].X + shiftX };

			for (var i = 0; i < actorCenterX.Length; i++)
				actorCenterX[i] += shiftX;
		}

		var lifelines = new Lifeline[diagram.Actors.Count];
		for (var i = 0; i < diagram.Actors.Count; i++)
		{
			lifelines[i] = new Lifeline(
				diagram.Actors[i].Id,
				actorCenterX[i],
				actorY + ActorHeight,
				diagramBottom - Padding);
		}

		var diagramWidth = globalMaxX + shiftX + Padding;
		var diagramHeight = diagramBottom;

		return new PositionedSequenceDiagram
		{
			Width = Math.Max(diagramWidth, 200),
			Height = Math.Max(diagramHeight, 100),
			Actors = actors,
			Lifelines = lifelines,
			Messages = messages,
			Activations = activations,
			Blocks = blocks,
			Notes = notes,
		};
	}
}
