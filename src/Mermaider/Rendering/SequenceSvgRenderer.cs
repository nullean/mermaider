using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Sequence diagrams on the shared design system: participants are nodes in their cluster family (mirrored as ghost chips
/// at the bottom), lifelines are soft dashed rules, messages are edges with plain caption labels, activation bars are the
/// accent, frames are containers with a keyword tab and notes are the shared note.
/// </summary>
internal static class SequenceSvgRenderer
{
	/// <summary>Space between a message line and the centre of its caption.</summary>
	private const double LabelLift = 12;

	/// <summary>Width the person glyph and its gap take in front of an actor's name (also reserved by the layout).</summary>
	internal const double GlyphWidth = 22;

	internal static string Render(PositionedSequenceDiagram diagram, SvgRenderContext context)
	{
		var sb = RenderToBuilder(diagram, context);
		try
		{
			return sb.ToString();
		}
		finally
		{
			_ = sb.Clear();
			SharedStringBuilderPool.Instance.Return(sb);
		}
	}

	internal static StringBuilder RenderToBuilder(PositionedSequenceDiagram diagram, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		StyleBlock.AppendSvgOpenTag(sb, diagram.Width, diagram.Height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles);
		var ds = DesignSystem.For(context);
		var strict = context.Styles.Strict is not null;

		// participants that talk to each other share a cluster family; boxes and frames take the next palette slots
		var palette = ClusterPalette.Build(
			diagram.Actors.Select(a => new ClusterBox(a.Id, a.X - (a.Width / 2), a.Y, a.Width, a.Height)).ToList(),
			diagram.Messages.Select(m => (m.From, m.To)),
			diagram.Boxes.Select((b, i) => new ClusterGroup("box" + i, b.X, b.Y, b.Width, b.Height, [])).ToList(),
			context.Styles.Colors).WithTint(ds.TintStrength);
		var clusterCount = diagram.Actors.Select(a => palette.Has(a.Id) ? palette.Family(a.Id).Key : a.Id).Distinct().Count();

		for (var i = 0; i < diagram.Boxes.Count; i++)
			AppendBox(sb, ds, diagram.Boxes[i], palette.GroupFamily("box" + i), i, strict);

		// outer frames first, so a nested frame is never painted over by the frame around it
		foreach (var i in Enumerable.Range(0, diagram.Blocks.Count).OrderByDescending(i => diagram.Blocks[i].Width * diagram.Blocks[i].Height))
			AppendBlock(sb, ds, diagram.Blocks[i], ds.Cluster(clusterCount + i), strict);

		foreach (var lifeline in diagram.Lifelines)
			AppendLifeline(sb, lifeline);

		foreach (var activation in diagram.Activations)
			AppendActivation(sb, ds, activation);

		foreach (var message in diagram.Messages)
			AppendMessage(sb, ds, message);

		foreach (var note in diagram.Notes)
			AppendNote(sb, ds, note);

		foreach (var actor in diagram.Actors)
			AppendActor(sb, ds, actor, palette.Has(actor.Id) ? palette.Family(actor.Id) : ds.Neutral);

		if (diagram.Actors.Count > 0)
		{
			var actorH = diagram.Actors[0].Height;
			var bottomActorY = diagram.Height - actorH - 30;
			foreach (var actor in diagram.Actors)
				AppendGhost(sb, ds, actor with { Y = bottomActorY });
		}

		foreach (var dm in diagram.DestroyMarkers)
			AppendDestroyMarker(sb, dm);

		ds.Close(sb);
		return sb;
	}

	private static void AppendActorAttrs(StringBuilder sb, PositionedSequenceActor actor)
	{
		_ = sb.Append(" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, actor.Id.AsSpan());
		_ = sb.Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, actor.Label.AsSpan());
		_ = sb.Append("\" data-type=\"").Append(actor.Type == SequenceActorType.Actor ? "actor" : "participant").Append('"');
	}

	private static void AppendActor(StringBuilder sb, DesignSystem ds, PositionedSequenceActor actor, ColorFamily family)
	{
		_ = sb.Append("\n<g class=\"actor\"");
		AppendActorAttrs(sb, actor);
		_ = sb.Append(">\n  ");

		ds.AppendBox(sb, actor.X - (actor.Width / 2), actor.Y, actor.Width, actor.Height, family);
		_ = sb.Append("\n  ");
		var cy = actor.Y + (actor.Height / 2);
		if (actor.Type == SequenceActorType.Actor)
		{
			// an actor is the same chip with a person glyph in the family ink in front of its name
			var textW = TextMetrics.MeasureMultiline(actor.Label.AsSpan(), DesignSystem.Px(TypeRole.Label), ds.Weight(TypeRole.Label)).Width;
			var left = actor.X - ((GlyphWidth + textW) / 2);
			DesignSystem.AppendGlyph(sb, DesignSystem.Glyph.Person, left + 8, cy, family.Ink);
			_ = sb.Append("\n  ");
			ds.AppendText(sb, actor.Label, left + GlyphWidth, cy, TypeRole.Label, anchor: "start");
		}
		else
		{
			ds.AppendText(sb, actor.Label, actor.X, cy, TypeRole.Label);
		}

		_ = sb.Append("\n</g>");
	}

	private static void AppendGhost(StringBuilder sb, DesignSystem ds, PositionedSequenceActor actor)
	{
		_ = sb.Append("\n<g class=\"actor-ghost\"");
		AppendActorAttrs(sb, actor);
		_ = sb.Append(">\n  ");
		ds.AppendGhostChip(sb, actor.X - (actor.Width / 2), actor.Y, actor.Width, actor.Height, actor.Label);
		_ = sb.Append("\n</g>");
	}

	private static void AppendLifeline(StringBuilder sb, Lifeline lifeline)
	{
		_ = sb.Append("\n<line class=\"lifeline\" data-actor=\"");
		MultilineUtils.AppendEscapedAttr(sb, lifeline.ActorId.AsSpan());
		_ = sb.Append("\" x1=\"").Append(lifeline.X)
			.Append("\" y1=\"").Append(lifeline.TopY)
			.Append("\" x2=\"").Append(lifeline.X)
			.Append("\" y2=\"").Append(lifeline.BottomY)
			.Append("\" stroke=\"var(--_line-soft)\" stroke-width=\"1.25\" stroke-dasharray=\"4 4\" />");
	}

	private static void AppendActivation(StringBuilder sb, DesignSystem ds, Activation activation)
	{
		var r = DesignSystem.Num(Math.Min(3, ds.Spec.NodeRadius));
		_ = sb.Append("\n<rect class=\"activation\" data-actor=\"");
		MultilineUtils.AppendEscapedAttr(sb, activation.ActorId.AsSpan());
		_ = sb.Append("\" x=\"").Append(activation.X)
			.Append("\" y=\"").Append(activation.TopY)
			.Append("\" width=\"").Append(activation.Width)
			.Append("\" height=\"").Append(activation.BottomY - activation.TopY)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"").Append(ds.Accent.Top)
			.Append("\" stroke=\"").Append(ds.Accent.Stroke)
			.Append("\" stroke-width=\"1.25\" />");
	}

	private static void AppendMessage(StringBuilder sb, DesignSystem ds, PositionedSequenceMessage msg)
	{
		var dashed = msg.LineStyle == SequenceLineStyle.Dashed;
		var dashArray = dashed ? $" stroke-dasharray=\"{DesignSystem.DashArray}\"" : "";
		var shape = msg.ArrowHead == SequenceArrowHead.Filled ? MarkerShape.Arrow : MarkerShape.Open;

		_ = sb.Append("\n<g class=\"message\" data-from=\"");
		MultilineUtils.AppendEscapedAttr(sb, msg.From.AsSpan());
		_ = sb.Append("\" data-to=\"");
		MultilineUtils.AppendEscapedAttr(sb, msg.To.AsSpan());
		_ = sb.Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, msg.Label.AsSpan());
		_ = sb.Append("\" data-line-style=\"").Append(dashed ? "dashed" : "solid");
		_ = sb.Append("\" data-arrow-head=\"").Append(msg.ArrowHead == SequenceArrowHead.Filled ? "filled" : "open");
		_ = sb.Append("\" data-self=\"").Append(msg.IsSelf ? "true" : "false");
		_ = sb.Append("\">\n");

		var label = msg.Label;
		if (msg.IsSelf)
		{
			const double loopW = 30;
			const double loopH = 20;
			const double labelPadding = 8;

			_ = sb.Append("  <polyline points=\"")
				.Append(msg.X1).Append(',').Append(msg.Y).Append(' ')
				.Append(msg.X1 + loopW).Append(',').Append(msg.Y).Append(' ')
				.Append(msg.X1 + loopW).Append(',').Append(msg.Y + loopH).Append(' ')
				.Append(msg.X2).Append(',').Append(msg.Y + loopH)
				.Append("\" fill=\"none\" stroke=\"").Append(DesignSystem.EdgeColor).Append("\" stroke-width=\"").Append(ds.EdgeWidth)
				.Append("\" stroke-linejoin=\"round\"").Append(dashArray)
				.Append(" marker-end=\"").Append(ds.Marker(shape)).Append("\" />\n  ");

			AppendAutoNumberBadge(sb, ds, ref label, msg.X1, msg.Y);
			if (label.Length > 0)
				ds.AppendHaloCaption(sb, label, msg.X1 + loopW + labelPadding, msg.Y + (loopH / 2), anchor: "start");

			_ = sb.Append('\n');
		}
		else
		{
			var markerStart = msg.Bidirectional ? $" marker-start=\"{ds.Marker(shape, atStart: true)}\"" : "";
			_ = sb.Append("  <line x1=\"").Append(msg.X1).Append("\" y1=\"").Append(msg.Y)
				.Append("\" x2=\"").Append(msg.X2).Append("\" y2=\"").Append(msg.Y)
				.Append("\" stroke=\"").Append(DesignSystem.EdgeColor).Append("\" stroke-width=\"").Append(ds.EdgeWidth).Append('"').Append(dashArray)
				.Append(" marker-end=\"").Append(ds.Marker(shape)).Append('"').Append(markerStart).Append(" />\n  ");

			AppendAutoNumberBadge(sb, ds, ref label, msg.X1, msg.Y);
			if (label.Length > 0)
			{
				// plain caption just above its line; the halo only shows where it crosses a lifeline
				ds.AppendHaloCaption(sb, label, (msg.X1 + msg.X2) / 2, msg.Y - LabelLift);
			}

			_ = sb.Append('\n');
		}

		_ = sb.Append("</g>");
	}

	private static void AppendAutoNumberBadge(StringBuilder sb, DesignSystem ds, ref string label, double x, double y)
	{
		var dotIdx = label.IndexOf(". ", StringComparison.Ordinal);
		if (dotIdx is <= 0 or > 4)
			return;

		var numStr = label[..dotIdx];
		foreach (var c in numStr)
		{
			if (!char.IsDigit(c))
				return;
		}

		label = label[(dotIdx + 2)..];

		// an accent badge on the message's start, knocked out of the line so the number stays readable
		_ = sb.Append("<g class=\"autonumber\">");
		if (ds.Spec.Badge == BadgeKind.Bracket)
		{
			var w = TextMetrics.MeasureTextWidth("[" + numStr + "]", DesignSystem.Px(TypeRole.Tag), 600) + 4;
			_ = sb.Append("<rect x=\"").Append(x - (w / 2)).Append("\" y=\"").Append(y - 8)
				.Append("\" width=\"").Append(w).Append("\" height=\"16\" fill=\"var(--bg)\" />");
		}

		_ = ds.AppendBadge(sb, x, y, numStr, ds.Accent, anchor: "middle");
		_ = sb.Append("</g>\n  ");
	}

	private static void AppendBlock(StringBuilder sb, DesignSystem ds, PositionedSequenceBlock block, ColorFamily family, bool strict)
	{
		_ = sb.Append("\n<g class=\"block\" data-type=\"").Append(block.Type.ToLower()).Append('"');
		if (block.Label.Length > 0)
		{
			_ = sb.Append(" data-label=\"");
			MultilineUtils.AppendEscapedAttr(sb, block.Label.AsSpan());
			_ = sb.Append('"');
		}
		_ = sb.Append(">\n");

		if (block.Type == SequenceBlockType.Rect)
		{
			// `rect rgb(…)` is a highlight region: a tint of its own colour (non-strict) when the label is one, else the family tint
			var tint = !strict && block.Label.Length > 0 && SvgValueAllowlist.IsAllowedColor(block.Label)
				? new ColorFamily("rc", MultilineUtils.EscapeAttr(block.Label.Trim()), ds.TintStrength).Band
				: family.Tint(0);
			var r = DesignSystem.Num(ds.Spec.NodeRadius + 2);
			_ = sb.Append("  <rect x=\"").Append(block.X).Append("\" y=\"").Append(block.Y)
				.Append("\" width=\"").Append(block.Width).Append("\" height=\"").Append(block.Height)
				.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
				.Append("\" fill=\"").Append(tint).Append("\" stroke=\"none\" />\n");
			_ = sb.Append("</g>");
			return;
		}

		var condition = block.Label.Length > 0 ? "[" + block.Label + "]" : null;
		ds.AppendFrame(sb, block.X, block.Y, block.Width, block.Height, family, block.Type.ToLower(), condition);

		foreach (var divider in block.Dividers)
			ds.AppendFrameDivider(sb, block.X, block.X + block.Width, divider.Y, family, divider.Label.Length > 0 ? "[" + divider.Label + "]" : null);

		_ = sb.Append("</g>");
	}

	private static void AppendNote(StringBuilder sb, DesignSystem ds, PositionedSequenceNote note)
	{
		_ = sb.Append("\n<g class=\"sequence-note\"");
		if (note.Position.HasValue)
			_ = sb.Append(" data-position=\"").Append(note.Position.Value.ToLower()).Append('"');
		if (note.Actors is { Count: > 0 })
		{
			_ = sb.Append(" data-actors=\"");
			for (var i = 0; i < note.Actors.Count; i++)
			{
				if (i > 0)
					_ = sb.Append(',');
				MultilineUtils.AppendEscapedAttr(sb, note.Actors[i].AsSpan());
			}
			_ = sb.Append('"');
		}
		_ = sb.Append('>');
		ds.AppendNote(sb, note.X, note.Y, note.Width, note.Height, note.Text);
		_ = sb.Append("\n</g>");
	}

	private static void AppendDestroyMarker(StringBuilder sb, PositionedDestroyMarker dm)
	{
		const double size = 9;
		_ = sb.Append("\n<g class=\"destroy\">\n");
		_ = sb.Append("  <path d=\"M").Append(dm.X - size).Append(',').Append(dm.Y - size)
			.Append(" L").Append(dm.X + size).Append(',').Append(dm.Y + size)
			.Append(" M").Append(dm.X + size).Append(',').Append(dm.Y - size)
			.Append(" L").Append(dm.X - size).Append(',').Append(dm.Y + size)
			.Append("\" fill=\"none\" stroke=\"var(--_line-strong)\" stroke-width=\"2.5\" stroke-linecap=\"round\" />\n");
		_ = sb.Append("</g>");
	}

	private static void AppendBox(StringBuilder sb, DesignSystem ds, PositionedSequenceBox box, ColorFamily family, int index, bool strict)
	{
		// `box Aqua Team`: the colour names the hue of the box (non-strict only); otherwise the group takes its palette family.
		// box.Color is a free-form \S+ token from source, so it is only used when it is an allowed colour and is escaped.
		if (!strict && box.Color is { } boxColor && SvgValueAllowlist.IsAllowedColor(boxColor))
			family = new ColorFamily("bx" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), MultilineUtils.EscapeAttr(boxColor.Trim()), ds.TintStrength);

		string? dataAttrs = null;
		if (box.Title.Length > 0)
		{
			var attrs = new StringBuilder("data-label=\"");
			MultilineUtils.AppendEscapedAttr(attrs, box.Title.AsSpan());
			dataAttrs = attrs.Append('"').ToString();
		}

		ds.AppendContainerBody(sb, box.X, box.Y, box.Width, box.Height, family, 0, "box", dataAttrs);
		if (box.Title.Length > 0)
			ds.AppendContainerHeader(sb, box.X, box.Y, box.Width, box.Title, family);
	}
}
