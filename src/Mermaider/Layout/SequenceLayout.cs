using Mermaider.Models;
using Mermaider.Rendering;
using Mermaider.Text;

namespace Mermaider.Layout;

internal static class SequenceLayout
{
	private const double Padding = 30;
	private const double ActorGap = 140;
	private const double ActorHeight = 40;
	private const double ActorPadX = 16;
	private const double HeaderGap = 44;
	private const double MessageRowHeight = 50;
	private const double SelfMessageHeight = 30;
	private const double ActivationWidth = 10;
	private const double BlockPadX = 10;
	// room above the first message for the frame's keyword tab / condition and the message's own label pill
	private const double BlockPadTop = 58;
	private const double BlockPadBottom = 8;
	private const double BlockHeaderExtra = 46;
	private const double DividerExtra = 36;
	private const double NoteWidth = 120;
	private const double NoteVPad = 12;
	private const double NoteHPad = 14;
	private const double NoteGap = 10;
	private const double NoteFontSize = 14;
	private const double NestingOffset = 4;
	private const double BoxHeaderRoom = 16;

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
				DesignSystem.Px(TypeRole.Label),
				RenderConstants.FontWeights.NodeLabel);
			// an actor carries a person glyph in front of its name
			if (diagram.Actors[i].Type == SequenceActorType.Actor)
				textW += SequenceSvgRenderer.GlyphWidth;
			actorWidths[i] = Math.Max(textW + (ActorPadX * 2), 80);
		}

		var actorCenterX = new double[diagram.Actors.Count];
		var currentX = Padding + (actorWidths[0] / 2);
		for (var i = 0; i < diagram.Actors.Count; i++)
		{
			if (i > 0)
			{
				var minGap = Math.Max(ActorGap, ((actorWidths[i - 1] + actorWidths[i]) / 2) + 40);
				currentX += minGap;
			}
			actorCenterX[i] = currentX;
		}

		var actorIndex = new Dictionary<string, int>(diagram.Actors.Count);
		for (var i = 0; i < diagram.Actors.Count; i++)
			actorIndex[diagram.Actors[i].Id] = i;

		// boxes put a header strip above the participants
		var actorY = Padding + (diagram.Boxes.Count > 0 ? BoxHeaderRoom : 0);
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

		var createdAt = new Dictionary<string, int>(diagram.Creates.Count);
		foreach (var c in diagram.Creates)
			_ = createdAt.TryAdd(c.ActorId, c.AtMessageIndex);

		var destroyedAt = new Dictionary<string, int>(diagram.Destroys.Count);
		foreach (var d in diagram.Destroys)
			_ = destroyedAt.TryAdd(d.ActorId, d.AtMessageIndex);

		var messageY = actorY + ActorHeight + HeaderGap;
		var messages = new List<PositionedSequenceMessage>(diagram.Messages.Count);

		var extraSpaceBefore = new Dictionary<int, double>();
		foreach (var block in diagram.Blocks)
		{
			_ = extraSpaceBefore.TryGetValue(block.StartIndex, out var prev);
			extraSpaceBefore[block.StartIndex] = Math.Max(prev, BlockHeaderExtra);

			foreach (var div in block.Dividers)
			{
				_ = extraSpaceBefore.TryGetValue(div.Index, out var prevDiv);
				extraSpaceBefore[div.Index] = Math.Max(prevDiv, DividerExtra);
			}
		}

		var notesByAfterIndex = new Dictionary<int, List<int>>();
		for (var ni = 0; ni < diagram.Notes.Count; ni++)
		{
			var afterIdx = diagram.Notes[ni].AfterIndex;
			if (!notesByAfterIndex.TryGetValue(afterIdx, out var list))
			{
				list = [];
				notesByAfterIndex[afterIdx] = list;
			}
			list.Add(ni);
		}

		var activationStacks = new Dictionary<string, Stack<(double StartY, int Depth)>>();
		var activations = new List<Activation>();

		for (var msgIdx = 0; msgIdx < diagram.Messages.Count; msgIdx++)
		{
			var msg = diagram.Messages[msgIdx];
			_ = actorIndex.TryGetValue(msg.From, out var fromIdx);
			_ = actorIndex.TryGetValue(msg.To, out var toIdx);
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
				Bidirectional = msg.Bidirectional,
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
				_ = actorIndex.TryGetValue(msg.From, out var idx);
				activations.Add(new Activation
				{
					ActorId = msg.From,
					X = actorCenterX[idx] - (ActivationWidth / 2) + (depth * NestingOffset),
					TopY = startY,
					BottomY = messageY,
					Width = ActivationWidth,
				});
			}

			messageY += isSelf ? SelfMessageHeight + MessageRowHeight : MessageRowHeight;

			if (notesByAfterIndex.TryGetValue(msgIdx, out var noteIndices))
			{
				// every note gets its own band below the message, so it never sits where the next message's label pill goes
				var noteBand = noteIndices.Count > 0 ? NoteFontSize + (NoteVPad * 2) : 0;
				messageY += noteBand;
			}
		}

		foreach (var (actorId, stack) in activationStacks)
		{
			while (stack.Count > 0)
			{
				var (startY, depth) = stack.Pop();
				_ = actorIndex.TryGetValue(actorId, out var idx);
				activations.Add(new Activation
				{
					ActorId = actorId,
					X = actorCenterX[idx] - (ActivationWidth / 2) + (depth * NestingOffset),
					TopY = startY,
					BottomY = messageY - (MessageRowHeight / 2),
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
					_ = actorIndex.TryGetValue(diagram.Messages[mi].From, out var fi);
					_ = actorIndex.TryGetValue(diagram.Messages[mi].To, out var ti);
					_ = involvedActors.Add(fi);
					_ = involvedActors.Add(ti);
				}
			}
			if (involvedActors.Count == 0)
			{
				for (var ai = 0; ai < diagram.Actors.Count; ai++)
					_ = involvedActors.Add(ai);
			}

			var minIdx = int.MaxValue;
			var maxIdx = int.MinValue;
			foreach (var ai in involvedActors)
			{
				if (ai < minIdx)
					minIdx = ai;
				if (ai > maxIdx)
					maxIdx = ai;
			}

			var blockLeft = actorCenterX[minIdx] - (actorWidths[minIdx] / 2);
			var blockRight = actorCenterX[maxIdx] + (actorWidths[maxIdx] / 2);

			// a frame is at least as wide as its keyword tab plus its condition (and every separator's condition)
			var tagPx = DesignSystem.Px(TypeRole.Tag);
			var keyword = block.Type.ToString().ToUpperInvariant();
			var minWidth = TextMetrics.MeasureTextWidth(keyword, tagPx, 600) + keyword.Length + 20 + 24;
			if (block.Label.Length > 0)
				minWidth += TextMetrics.MeasureTextWidth("[" + block.Label + "]", tagPx, 600) + 10;
			foreach (var d in block.Dividers)
			{
				if (d.Label.Length > 0)
					minWidth = Math.Max(minWidth, TextMetrics.MeasureTextWidth("[" + d.Label + "]", tagPx, 600) + 24);
			}

			blockRight = Math.Max(blockRight, blockLeft + minWidth);

			var dividers = new List<PositionedBlockDivider>(block.Dividers.Count);
			foreach (var d in block.Dividers)
			{
				var dMsg = d.Index < messages.Count ? messages[d.Index] : null;
				var msgY = dMsg?.Y ?? messageY;
				var offset = d.Label.Length > 0 ? 62.0 : 38.0;

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
			var textW = TextMetrics.MeasureTextWidth(
				note.Text,
				NoteFontSize,
				RenderConstants.FontWeights.EdgeLabel) + (NoteHPad * 2);
			var noteW = Math.Max(NoteWidth, textW);
			var noteH = NoteFontSize + (NoteVPad * 2);

			var refMsg = note.AfterIndex >= 0 && note.AfterIndex < messages.Count
				? messages[note.AfterIndex]
				: null;
			var noteY = (refMsg?.Y ?? (actorY + ActorHeight)) + 10;

			_ = actorIndex.TryGetValue(note.ActorIds[0], out var firstActorIdx);
			double noteX;
			if (note.Position == SequenceNotePosition.Left)
			{
				noteX = actorCenterX[firstActorIdx] - (actorWidths[firstActorIdx] / 2) - noteW - NoteGap;
			}
			else if (note.Position == SequenceNotePosition.Right)
			{
				noteX = actorCenterX[firstActorIdx] + (actorWidths[firstActorIdx] / 2) + NoteGap;
			}
			else
			{
				if (note.ActorIds.Count > 1)
				{
					_ = actorIndex.TryGetValue(note.ActorIds[^1], out var lastActorIdx);
					var spanLeft = actorCenterX[firstActorIdx] - (actorWidths[firstActorIdx] / 2);
					var spanRight = actorCenterX[lastActorIdx] + (actorWidths[lastActorIdx] / 2);
					// a spanning note stays inside the frame that wraps the same participants
					noteW = Math.Max(noteW, spanRight - spanLeft - (NoteHPad * 2));
					noteX = ((spanLeft + spanRight) / 2) - (noteW / 2);
				}
				else
				{
					noteX = actorCenterX[firstActorIdx] - (noteW / 2);
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

		var diagramBottom = messageY + HeaderGap + ActorHeight + Padding;
		var globalMinX = Padding;
		var globalMaxX = 0.0;

		foreach (var a in actors)
		{
			globalMinX = Math.Min(globalMinX, a.X - (a.Width / 2));
			globalMaxX = Math.Max(globalMaxX, a.X + (a.Width / 2));
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

		var destroyMarkers = new List<PositionedDestroyMarker>(diagram.Destroys.Count);

		for (var i = 0; i < diagram.Actors.Count; i++)
		{
			var aid = diagram.Actors[i].Id;
			if (createdAt.TryGetValue(aid, out var cIdx) && cIdx < messages.Count)
			{
				// the new participant sits level with the creating message, which ends at the box's edge
				var msg = messages[cIdx];
				actors[i] = actors[i] with { Y = msg.Y - (ActorHeight / 2) };
				if (Math.Abs(msg.X2 - msg.X1) > 1)
				{
					var edge = msg.X2 > msg.X1 ? actors[i].X - (actors[i].Width / 2) : actors[i].X + (actors[i].Width / 2);
					messages[cIdx] = msg with { X2 = edge };
				}
			}
		}

		var lifelines = new Lifeline[diagram.Actors.Count];
		for (var i = 0; i < diagram.Actors.Count; i++)
		{
			var aid = diagram.Actors[i].Id;
			var topY = actors[i].Y + ActorHeight;
			// the lifeline ends at the ghost chip mirrored at the bottom
			var bottomY = diagramBottom - Padding - ActorHeight;

			if (destroyedAt.TryGetValue(aid, out var dIdx) && dIdx < messages.Count)
			{
				bottomY = messages[dIdx].Y;
				destroyMarkers.Add(new PositionedDestroyMarker(actorCenterX[i], bottomY));
			}

			lifelines[i] = new Lifeline(aid, actorCenterX[i], topY, bottomY);
		}

		var positionedBoxes = new List<PositionedSequenceBox>(diagram.Boxes.Count);
		const double boxPadX = 8;
		const double boxPadY = 6;
		const double boxHeaderHeight = DesignSystem.StripHeight;
		var boxRight = 0.0;
		foreach (var box in diagram.Boxes)
		{
			if (box.ActorIds.Count == 0)
				continue;

			var minX = double.MaxValue;
			var maxX = double.MinValue;
			foreach (var aid in box.ActorIds)
			{
				if (!actorIndex.TryGetValue(aid, out var aidx))
					continue;
				var halfW = actorWidths[aidx] / 2;
				minX = Math.Min(minX, actorCenterX[aidx] - halfW);
				maxX = Math.Max(maxX, actorCenterX[aidx] + halfW);
			}

			// a title wider than its participants widens the box (centred, unless that would leave the canvas)
			var boxX = minX - boxPadX;
			var boxW = maxX - minX + (boxPadX * 2);
			if (box.Title.Length > 0)
			{
				var titleW = TextMetrics.MeasureTextWidth(box.Title, DesignSystem.Px(TypeRole.Subheading), 600) + 40;
				if (titleW > boxW)
				{
					boxX = Math.Max(Padding / 2, boxX - ((titleW - boxW) / 2));
					boxW = titleW;
				}
			}

			boxRight = Math.Max(boxRight, boxX + boxW);
			positionedBoxes.Add(new PositionedSequenceBox
			{
				Title = box.Title,
				Color = box.Color,
				X = boxX,
				Y = actorY - boxHeaderHeight - boxPadY,
				Width = boxW,
				Height = diagramBottom - Padding - actorY + boxHeaderHeight + (boxPadY * 2),
			});
		}

		var diagramWidth = Math.Max(globalMaxX + shiftX + Padding, boxRight + Padding);
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
			Boxes = positionedBoxes,
			DestroyMarkers = destroyMarkers,
		};
	}
}
