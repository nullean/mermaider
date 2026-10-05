using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class SequenceSvgRenderer
{
	private static readonly string NodeLabelTextAttrs =
		RenderConstants.TextAttrs.SeqNodeLabelFill + "var(--_text)\"";

	private static readonly string MessageLabelCenterAttrs =
		RenderConstants.TextAttrs.SeqMessageLabelCenterFill + "var(--_text)\"";

	private static readonly string NoteLabelAttrs =
		RenderConstants.TextAttrs.SeqNoteCenterFill + "var(--_accent-text)\"";

	private static readonly string ConditionAttrs =
		RenderConstants.TextAttrs.EdgeLabelCenterFill + "var(--_text)\"";


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
		StyleBlock.AppendStyleBlock(sb, context.Styles.Font, context.Styles.Strict, context.Styles.FontScale, context.Styles.MonoFont);
		AppendArrowDefs(sb);

		// Same language as the other diagrams: participants that talk to each other share a cluster colour, boxes and
		// frames are tinted groups with the title in the border colour.
		var palette = ClusterPalette.Build(
			diagram.Actors.Select(a => new ClusterBox(a.Id, a.X - (a.Width / 2), a.Y, a.Width, a.Height)).ToList(),
			diagram.Messages.Select(m => (m.From, m.To)),
			diagram.Boxes.Select((b, i) => new ClusterGroup("box" + i, b.X, b.Y, b.Width, b.Height, [])).ToList(),
			context.Styles.Colors);
		var autoPalette = context.Styles.Colors.AutoPalette();
		var clusterCount = diagram.Actors.Select(a => palette.Has(a.Id) ? palette.NodeStroke(a.Id) : a.Id).Distinct().Count();

		for (var i = 0; i < diagram.Boxes.Count; i++)
			AppendBox(sb, diagram.Boxes[i], palette, "box" + i);

		for (var i = 0; i < diagram.Blocks.Count; i++)
			AppendBlock(sb, diagram.Blocks[i], autoPalette[(clusterCount + i) % autoPalette.Length]);

		foreach (var lifeline in diagram.Lifelines)
			AppendLifeline(sb, lifeline);

		foreach (var activation in diagram.Activations)
			AppendActivation(sb, activation, palette);

		foreach (var message in diagram.Messages)
			AppendMessage(sb, message);

		foreach (var note in diagram.Notes)
			AppendNote(sb, note);

		foreach (var actor in diagram.Actors)
			AppendActor(sb, actor, palette);

		if (diagram.Actors.Count > 0)
		{
			var actorH = diagram.Actors[0].Height;
			var bottomActorY = diagram.Height - actorH - 30;
			foreach (var actor in diagram.Actors)
				AppendActor(sb, actor with { Y = bottomActorY }, palette);
		}

		foreach (var dm in diagram.DestroyMarkers)
			AppendDestroyMarker(sb, dm);

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static void AppendArrowDefs(StringBuilder sb)
	{
		var s = RenderConstants.ArrowHead.Size;
		var w = s;
		var h = s;
		var halfH = h / 2.0;

		_ = sb.Append("\n<defs>\n");

		_ = sb.Append("  <marker id=\"seq-arrow\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"").Append(w)
			.Append("\" refY=\"").Append(halfH)
			.Append("\" orient=\"auto-start-reverse\">\n");
		_ = sb.Append("    <polygon points=\"0 0, ").Append(w).Append(' ').Append(halfH)
			.Append(", 0 ").Append(h)
			.Append("\" fill=\"var(--_line)\" />\n");
		_ = sb.Append("  </marker>\n");

		_ = sb.Append("  <marker id=\"seq-arrow-open\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"").Append(w)
			.Append("\" refY=\"").Append(halfH)
			.Append("\" orient=\"auto-start-reverse\">\n");
		_ = sb.Append("    <polyline points=\"0 0, ").Append(w).Append(' ').Append(halfH)
			.Append(", 0 ").Append(h)
			.Append("\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"2\" />\n");
		_ = sb.Append("  </marker>\n");

		_ = sb.Append("</defs>\n");
	}

	private static void AppendActor(StringBuilder sb, PositionedSequenceActor actor, ClusterPalette palette)
	{
		var hasColour = palette.Has(actor.Id);
		var border = hasColour ? palette.NodeStroke(actor.Id) : "var(--_line)";
		var fill = hasColour ? palette.NodeFill(actor.Id) : "var(--_node-fill)";
		_ = sb.Append("\n<g class=\"actor\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, actor.Id.AsSpan());
		_ = sb.Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, actor.Label.AsSpan());
		_ = sb.Append("\" data-type=\"").Append(actor.Type == SequenceActorType.Actor ? "actor" : "participant")
			.Append("\">\n");

		if (actor.Type == SequenceActorType.Actor)
		{
			var s = actor.Height / 24 * 0.9;
			var tx = actor.X - (12 * s);
			var ty = actor.Y + ((actor.Height - (24 * s)) / 2);
			var sw = RenderConstants.StrokeWidths.OuterBox / s;

			_ = sb.Append("  <g transform=\"translate(").Append(tx).Append(',').Append(ty)
				.Append(") scale(").Append(s).Append(")\">\n");
			_ = sb.Append("    <path d=\"M21 12C21 16.9706 16.9706 21 12 21C7.02944 21 3 16.9706 3 12C3 7.02944 7.02944 3 12 3C16.9706 3 21 7.02944 21 12Z\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"")
				.Append(sw).Append("\" />\n");
			_ = sb.Append("    <path d=\"M15 10C15 11.6569 13.6569 13 12 13C10.3431 13 9 11.6569 9 10C9 8.34315 10.3431 7 12 7C13.6569 7 15 8.34315 15 10Z\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"")
				.Append(sw).Append("\" />\n");
			_ = sb.Append("    <path d=\"M5.62842 18.3563C7.08963 17.0398 9.39997 16 12 16C14.6 16 16.9104 17.0398 18.3716 18.3563\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"")
				.Append(sw).Append("\" />\n");
			_ = sb.Append("  </g>\n  ");

			MultilineUtils.AppendMultilineText(
				sb, actor.Label, actor.X, actor.Y + actor.Height + 14,
				RenderConstants.FontSizes.NodeLabel,
				NodeLabelTextAttrs);
			_ = sb.Append('\n');
		}
		else
		{
			var boxX = actor.X - (actor.Width / 2);
			_ = sb.Append("  <rect x=\"").Append(boxX).Append("\" y=\"").Append(actor.Y)
				.Append("\" width=\"").Append(actor.Width).Append("\" height=\"").Append(actor.Height)
				.Append("\" rx=\"").Append(RenderConstants.Radii.Rectangle)
				.Append("\" ry=\"").Append(RenderConstants.Radii.Rectangle)
				.Append("\" fill=\"").Append(fill).Append("\" stroke=\"").Append(border).Append("\" stroke-width=\"")
				.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n  ");

			MultilineUtils.AppendMultilineText(
				sb, actor.Label, actor.X, actor.Y + (actor.Height / 2),
				RenderConstants.FontSizes.NodeLabel,
				NodeLabelTextAttrs);
			_ = sb.Append('\n');
		}

		_ = sb.Append("</g>");
	}

	private static void AppendLifeline(StringBuilder sb, Lifeline lifeline)
	{
		_ = sb.Append("\n<line class=\"lifeline\" data-actor=\"");
		MultilineUtils.AppendEscapedAttr(sb, lifeline.ActorId.AsSpan());
		_ = sb.Append("\" x1=\"").Append(lifeline.X)
			.Append("\" y1=\"").Append(lifeline.TopY)
			.Append("\" x2=\"").Append(lifeline.X)
			.Append("\" y2=\"").Append(lifeline.BottomY)
			.Append("\" stroke=\"var(--_line)\" stroke-width=\"0.75\" stroke-dasharray=\"6 4\" />");
	}

	private static void AppendActivation(StringBuilder sb, Activation activation, ClusterPalette palette)
	{
		var hasColour = palette.Has(activation.ActorId);
		_ = sb.Append("\n<rect class=\"activation\" data-actor=\"");
		MultilineUtils.AppendEscapedAttr(sb, activation.ActorId.AsSpan());
		_ = sb.Append("\" x=\"").Append(activation.X)
			.Append("\" y=\"").Append(activation.TopY)
			.Append("\" width=\"").Append(activation.Width)
			.Append("\" height=\"").Append(activation.BottomY - activation.TopY)
			.Append("\" rx=\"4\" ry=\"4\"")
			.Append(" fill=\"").Append(hasColour ? palette.NodeFill(activation.ActorId) : "var(--_node-fill)")
			.Append("\" stroke=\"").Append(hasColour ? palette.NodeStroke(activation.ActorId) : "var(--_node-stroke)")
			.Append("\" stroke-width=\"").Append(RenderConstants.StrokeWidths.InnerBox).Append("\" />");
	}

	private static void AppendMessage(StringBuilder sb, PositionedSequenceMessage msg)
	{
		var dashArray = msg.LineStyle == SequenceLineStyle.Dashed ? " stroke-dasharray=\"6 4\"" : "";
		var markerId = msg.ArrowHead == SequenceArrowHead.Filled ? "seq-arrow" : "seq-arrow-open";

		_ = sb.Append("\n<g class=\"message\" data-from=\"");
		MultilineUtils.AppendEscapedAttr(sb, msg.From.AsSpan());
		_ = sb.Append("\" data-to=\"");
		MultilineUtils.AppendEscapedAttr(sb, msg.To.AsSpan());
		_ = sb.Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, msg.Label.AsSpan());
		_ = sb.Append("\" data-line-style=\"").Append(msg.LineStyle == SequenceLineStyle.Dashed ? "dashed" : "solid");
		_ = sb.Append("\" data-arrow-head=\"").Append(msg.ArrowHead == SequenceArrowHead.Filled ? "filled" : "open");
		_ = sb.Append("\" data-self=\"").Append(msg.IsSelf ? "true" : "false");
		_ = sb.Append("\">\n");

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
				.Append("\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"")
				.Append(RenderConstants.StrokeWidths.Connector).Append('"').Append(dashArray)
				.Append(" marker-end=\"url(#").Append(markerId).Append(")\" />\n  ");

			if (msg.Label.Length > 0)
			{
				var selfMetrics = TextMetrics.MeasureMultiline(msg.Label.AsSpan(), RenderConstants.FontSizes.EdgeLabel, RenderConstants.FontWeights.EdgeLabel);
				var pillCx = msg.X1 + loopW + labelPadding + (ErSvgRenderer.LabelBoxWidth(selfMetrics.Width) / 2);
				VisualLanguage.AppendLabelPill(sb, pillCx, msg.Y + (loopH / 2), selfMetrics.Width, selfMetrics.Height);
				_ = sb.Append("\n  ");
				MultilineUtils.AppendMultilineText(
					sb, msg.Label, pillCx, msg.Y + (loopH / 2),
					RenderConstants.FontSizes.SeqMessageLabel,
					MessageLabelCenterAttrs);
			}

			_ = sb.Append('\n');
		}
		else
		{
			var markerStart = msg.Bidirectional ? $" marker-start=\"url(#{markerId})\"" : "";
			_ = sb.Append("  <line x1=\"").Append(msg.X1).Append("\" y1=\"").Append(msg.Y)
				.Append("\" x2=\"").Append(msg.X2).Append("\" y2=\"").Append(msg.Y)
				.Append("\" stroke=\"var(--_line)\" stroke-width=\"")
				.Append(RenderConstants.StrokeWidths.Connector).Append('"').Append(dashArray)
				.Append(" marker-end=\"url(#").Append(markerId).Append(")\"").Append(markerStart).Append(" />\n  ");

			var midX = (msg.X1 + msg.X2) / 2;
			var label = msg.Label;
			AppendAutoNumberBadge(sb, ref label, msg);
			if (label.Length > 0)
			{
				// the pill sits just above its line, like the labels of every other diagram type
				var metrics = TextMetrics.MeasureMultiline(label.AsSpan(), RenderConstants.FontSizes.EdgeLabel, RenderConstants.FontWeights.EdgeLabel);
				var pillY = msg.Y - 18;
				VisualLanguage.AppendLabelPill(sb, midX, pillY, metrics.Width, metrics.Height);
				_ = sb.Append("\n  ");
				MultilineUtils.AppendMultilineText(
					sb, label, midX, pillY,
					RenderConstants.FontSizes.SeqMessageLabel,
					MessageLabelCenterAttrs);
			}

			_ = sb.Append('\n');
		}

		_ = sb.Append("</g>");
	}

	private static void AppendAutoNumberBadge(StringBuilder sb, ref string label, PositionedSequenceMessage msg)
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

		var badgeX = msg.X1;
		const double badgeR = 11;
		const string badgeFontSize = RenderConstants.FsVar.Xs;

		_ = sb.Append("<circle cx=\"").Append(badgeX).Append("\" cy=\"").Append(msg.Y)
			.Append("\" r=\"").Append(badgeR)
			.Append("\" fill=\"var(--_text)\" />");
		_ = sb.Append("<text x=\"").Append(badgeX).Append("\" y=\"").Append(msg.Y)
			.Append("\" text-anchor=\"middle\" dy=\"0.35em\" font-size=\"").Append(badgeFontSize)
			.Append("\" font-weight=\"700\" fill=\"var(--bg)\">")
			.Append(numStr).Append("</text>\n  ");
	}

	private static void AppendBlock(StringBuilder sb, PositionedSequenceBlock block, string colour)
	{
		var isRect = block.Type == SequenceBlockType.Rect;
		var border = VisualLanguage.GroupBorder(colour);
		var titleAttrs = RenderConstants.TextAttrs.SeqBlockTabFill + border + "\"";

		_ = sb.Append("\n<g class=\"block\" data-type=\"").Append(block.Type.ToLower()).Append('"');
		if (block.Label.Length > 0)
		{
			_ = sb.Append(" data-label=\"");
			MultilineUtils.AppendEscapedAttr(sb, block.Label.AsSpan());
			_ = sb.Append('"');
		}
		_ = sb.Append(">\n");

		if (isRect)
		{
			// `rect rgb(…)` is a highlight region: a tint of its own colour when the label is one, else the frame colour; no tab
			var tint = block.Label.Length > 0 && SvgValueAllowlist.IsAllowedColor(block.Label)
				? $"color-mix(in srgb, {MultilineUtils.EscapeAttr(block.Label.Trim())} 18%, var(--bg))"
				: VisualLanguage.GroupFill(colour, 0);
			_ = sb.Append("  <rect x=\"").Append(block.X).Append("\" y=\"").Append(block.Y)
				.Append("\" width=\"").Append(block.Width).Append("\" height=\"").Append(block.Height)
				.Append("\" rx=\"").Append(RenderConstants.Radii.Group).Append("\" ry=\"").Append(RenderConstants.Radii.Group)
				.Append("\" fill=\"").Append(tint).Append("\" stroke=\"none\" />\n");
			_ = sb.Append("</g>");
			return;
		}

		_ = sb.Append("  <rect x=\"").Append(block.X).Append("\" y=\"").Append(block.Y)
			.Append("\" width=\"").Append(block.Width).Append("\" height=\"").Append(block.Height)
			.Append("\" rx=\"").Append(RenderConstants.Radii.Group)
			.Append("\" ry=\"").Append(RenderConstants.Radii.Group)
			.Append("\" fill=\"").Append(VisualLanguage.GroupFill(colour, 0)).Append("\" stroke=\"").Append(border).Append("\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n");

		var typeName = block.Type.ToLower();
		var tabWidth = TextMetrics.MeasureTextWidth(
			typeName,
			RenderConstants.FontSizes.EdgeLabel,
			RenderConstants.FontWeights.GroupHeader) + 16;
		const double tabHeight = 18;

		// the keyword tab: tinted like an entity header, keyword in the border colour
		_ = sb.Append("  <rect x=\"").Append(block.X).Append("\" y=\"").Append(block.Y)
			.Append("\" width=\"").Append(tabWidth).Append("\" height=\"").Append(tabHeight)
			.Append("\" rx=\"6\" ry=\"6\"")
			.Append(" fill=\"").Append(VisualLanguage.Tint(colour, VisualLanguage.HeaderTint)).Append("\" stroke=\"").Append(border).Append("\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n  ");

		MultilineUtils.AppendMultilineText(
			sb, typeName,
			block.X + 6, block.Y + (tabHeight / 2),
			RenderConstants.FontSizes.EdgeLabel,
			titleAttrs);
		_ = sb.Append('\n');

		if (block.Label.Length > 0)
		{
			// Place the condition right of the type tab so it never overlaps it.
			var labelW = ErSvgRenderer.LabelBoxWidth(TextMetrics.MeasureTextWidth(
				block.Label,
				RenderConstants.FontSizes.EdgeLabel,
				RenderConstants.FontWeights.EdgeLabel));
			var badgeCx = block.X + tabWidth + 8 + (labelW / 2);
			AppendConditionPill(sb, block.Label, badgeCx, block.Y + (tabHeight / 2));
		}

		foreach (var divider in block.Dividers)
		{
			_ = sb.Append("  <line x1=\"").Append(block.X).Append("\" y1=\"").Append(divider.Y)
				.Append("\" x2=\"").Append(block.X + block.Width).Append("\" y2=\"").Append(divider.Y)
				.Append("\" stroke=\"").Append(border).Append("\" stroke-width=\"")
				.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" stroke-dasharray=\"4 4\" />\n");

			if (divider.Label.Length > 0)
				AppendConditionPill(sb, divider.Label, block.X + (block.Width / 2), divider.Y + 14);
		}

		_ = sb.Append("</g>");
	}

	// The condition of a frame ("[ok]", "[retry]") is a label pill like every other label.
	private static void AppendConditionPill(StringBuilder sb, string label, double cx, double cy)
	{
		var metrics = TextMetrics.MeasureMultiline(label.AsSpan(), RenderConstants.FontSizes.EdgeLabel, RenderConstants.FontWeights.EdgeLabel);
		_ = sb.Append("  ");
		VisualLanguage.AppendLabelPill(sb, cx, cy, metrics.Width, metrics.Height);
		_ = sb.Append("\n  ");
		MultilineUtils.AppendMultilineText(sb, label, cx, cy, RenderConstants.FontSizes.EdgeLabel, ConditionAttrs);
		_ = sb.Append('\n');
	}

	private static void AppendNote(StringBuilder sb, PositionedSequenceNote note)
	{
		_ = sb.Append("\n<g class=\"note\"");
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
		_ = sb.Append(">\n");

		_ = sb.Append("  <rect x=\"").Append(note.X).Append("\" y=\"").Append(note.Y)
			.Append("\" width=\"").Append(note.Width).Append("\" height=\"").Append(note.Height)
			.Append("\" rx=\"6\" ry=\"6\"")
			.Append(" fill=\"var(--_accent-fill)\" stroke=\"var(--_accent-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.InnerBox).Append("\" />\n  ");

		const double asymmetry = 6.0;
		var textX = note.X + (note.Width / 2);
		if (note.Position == SequenceNotePosition.Right)
			textX -= asymmetry;
		else if (note.Position == SequenceNotePosition.Left)
			textX += asymmetry;

		MultilineUtils.AppendMultilineText(
			sb, note.Text,
			textX, note.Y + (note.Height / 2),
			RenderConstants.FontSizes.EdgeLabel,
			NoteLabelAttrs);
		_ = sb.Append('\n');

		_ = sb.Append("</g>");
	}

	private static void AppendDestroyMarker(StringBuilder sb, PositionedDestroyMarker dm)
	{
		const double size = 12;
		_ = sb.Append("\n<g class=\"destroy\">\n");
		_ = sb.Append("  <line x1=\"").Append(dm.X - size).Append("\" y1=\"").Append(dm.Y - size)
			.Append("\" x2=\"").Append(dm.X + size).Append("\" y2=\"").Append(dm.Y + size)
			.Append("\" stroke=\"var(--_line)\" stroke-width=\"2.5\" />\n");
		_ = sb.Append("  <line x1=\"").Append(dm.X + size).Append("\" y1=\"").Append(dm.Y - size)
			.Append("\" x2=\"").Append(dm.X - size).Append("\" y2=\"").Append(dm.Y + size)
			.Append("\" stroke=\"var(--_line)\" stroke-width=\"2.5\" />\n");
		_ = sb.Append("</g>");
	}

	private static void AppendBox(StringBuilder sb, PositionedSequenceBox box, ClusterPalette palette, string groupId)
	{
		_ = sb.Append("\n<g class=\"box\"");
		if (box.Title.Length > 0)
		{
			_ = sb.Append(" data-label=\"");
			MultilineUtils.AppendEscapedAttr(sb, box.Title.AsSpan());
			_ = sb.Append('"');
		}
		_ = sb.Append(">\n");

		// `box Aqua Team`: the colour names the hue of the box; without one the group takes its alternating palette colour.
		// box.Color is a free-form \S+ token from source, so it is only used when it is an allowed colour and is escaped.
		string fill;
		string stroke;
		string title;
		if (box.Color is { } boxColor && SvgValueAllowlist.IsAllowedColor(boxColor))
		{
			var safe = MultilineUtils.EscapeAttr(boxColor.Trim());
			fill = $"color-mix(in srgb, {safe} {VisualLanguage.GroupTintBase + 10}%, var(--bg))";
			var isHex = ColorUtils.TryHueSaturation(boxColor, out _, out _);
			stroke = isHex ? VisualLanguage.GroupBorder(boxColor.Trim()) : safe;
			// a named colour cannot be darkened numerically, so its title is mixed towards the text colour
			title = isHex ? stroke : $"color-mix(in srgb, {safe} 80%, var(--fg))";
		}
		else
		{
			fill = palette.GroupFill(groupId, 0);
			stroke = palette.GroupStroke(groupId);
			title = stroke;
		}

		_ = sb.Append("  <rect x=\"").Append(box.X).Append("\" y=\"").Append(box.Y)
			.Append("\" width=\"").Append(box.Width).Append("\" height=\"").Append(box.Height)
			.Append("\" rx=\"").Append(RenderConstants.Radii.Group)
			.Append("\" ry=\"").Append(RenderConstants.Radii.Group)
			.Append("\" fill=\"").Append(fill)
			.Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n");

		if (box.Title.Length > 0)
		{
			_ = sb.Append("  ");
			MultilineUtils.AppendMultilineText(
				sb, box.Title,
				box.X + (box.Width / 2), box.Y + 10,
				RenderConstants.FontSizes.EdgeLabel,
				"text-anchor=\"middle\" " + RenderConstants.TextAttrs.SeqBlockTabFill + title + "\"");
			_ = sb.Append('\n');
		}

		_ = sb.Append("</g>");
	}
}
