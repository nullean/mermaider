using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;
using Microsoft.Extensions.ObjectPool;

namespace Mermaider.Rendering;

internal static class SequenceSvgRenderer
{
	private static readonly ObjectPool<StringBuilder> s_sbPool =
		new DefaultObjectPoolProvider().CreateStringBuilderPool(initialCapacity: 4096, maximumRetainedCapacity: 64 * 1024);

	internal static string Render(PositionedSequenceDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null)
	{
		var sb = s_sbPool.Get();
		try
		{
			StyleBlock.AppendSvgOpenTag(sb, diagram.Width, diagram.Height, colors, transparent);
			StyleBlock.AppendStyleBlock(sb, font, strict);
			AppendArrowDefs(sb);

			foreach (var block in diagram.Blocks)
				AppendBlock(sb, block);

			foreach (var lifeline in diagram.Lifelines)
				AppendLifeline(sb, lifeline);

			foreach (var activation in diagram.Activations)
				AppendActivation(sb, activation);

			foreach (var message in diagram.Messages)
				AppendMessage(sb, message);

			foreach (var note in diagram.Notes)
				AppendNote(sb, note);

			foreach (var actor in diagram.Actors)
				AppendActor(sb, actor);

			sb.Append("\n</svg>");
			return sb.ToString();
		}
		finally
		{
			sb.Clear();
			s_sbPool.Return(sb);
		}
	}

	private static void AppendArrowDefs(StringBuilder sb)
	{
		var s = RenderConstants.ArrowHead.Size;
		var w = s;
		var h = s;
		var halfH = h / 2.0;

		sb.Append("\n<defs>\n");

		sb.Append("  <marker id=\"seq-arrow\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"").Append(w)
			.Append("\" refY=\"").Append(halfH)
			.Append("\" orient=\"auto-start-reverse\">\n");
		sb.Append("    <polygon points=\"0 0, ").Append(w).Append(' ').Append(halfH)
			.Append(", 0 ").Append(h)
			.Append("\" fill=\"var(--_arrow)\" />\n");
		sb.Append("  </marker>\n");

		sb.Append("  <marker id=\"seq-arrow-open\" markerUnits=\"userSpaceOnUse\" markerWidth=\"").Append(w)
			.Append("\" markerHeight=\"").Append(h)
			.Append("\" refX=\"").Append(w)
			.Append("\" refY=\"").Append(halfH)
			.Append("\" orient=\"auto-start-reverse\">\n");
		sb.Append("    <polyline points=\"0 0, ").Append(w).Append(' ').Append(halfH)
			.Append(", 0 ").Append(h)
			.Append("\" fill=\"none\" stroke=\"var(--_arrow)\" stroke-width=\"1.5\" />\n");
		sb.Append("  </marker>\n");

		sb.Append("</defs>\n");
	}

	private static void AppendActor(StringBuilder sb, PositionedSequenceActor actor)
	{
		sb.Append("\n<g class=\"actor\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, actor.Id.AsSpan());
		sb.Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, actor.Label.AsSpan());
		sb.Append("\" data-type=\"").Append(actor.Type == SequenceActorType.Actor ? "actor" : "participant")
			.Append("\">\n");

		if (actor.Type == SequenceActorType.Actor)
		{
			var s = (actor.Height / 24) * 0.9;
			var tx = actor.X - 12 * s;
			var ty = actor.Y + (actor.Height - 24 * s) / 2;
			var sw = RenderConstants.StrokeWidths.OuterBox / s;

			sb.Append("  <g transform=\"translate(").Append(tx).Append(',').Append(ty)
				.Append(") scale(").Append(s).Append(")\">\n");
			sb.Append("    <path d=\"M21 12C21 16.9706 16.9706 21 12 21C7.02944 21 3 16.9706 3 12C3 7.02944 7.02944 3 12 3C16.9706 3 21 7.02944 21 12Z\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"")
				.Append(sw).Append("\" />\n");
			sb.Append("    <path d=\"M15 10C15 11.6569 13.6569 13 12 13C10.3431 13 9 11.6569 9 10C9 8.34315 10.3431 7 12 7C13.6569 7 15 8.34315 15 10Z\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"")
				.Append(sw).Append("\" />\n");
			sb.Append("    <path d=\"M5.62842 18.3563C7.08963 17.0398 9.39997 16 12 16C14.6 16 16.9104 17.0398 18.3716 18.3563\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"")
				.Append(sw).Append("\" />\n");
			sb.Append("  </g>\n  ");

			MultilineUtils.AppendMultilineText(
				sb, actor.Label, actor.X, actor.Y + actor.Height + 14,
				RenderConstants.FontSizes.NodeLabel,
				$"font-size=\"{RenderConstants.FontSizes.NodeLabel}\" text-anchor=\"middle\" font-weight=\"{RenderConstants.FontWeights.NodeLabel}\" fill=\"var(--_text)\"");
			sb.Append('\n');
		}
		else
		{
			var boxX = actor.X - actor.Width / 2;
			sb.Append("  <rect x=\"").Append(boxX).Append("\" y=\"").Append(actor.Y)
				.Append("\" width=\"").Append(actor.Width).Append("\" height=\"").Append(actor.Height)
				.Append("\" rx=\"").Append(RenderConstants.Radii.Rectangle)
				.Append("\" ry=\"").Append(RenderConstants.Radii.Rectangle)
				.Append("\" fill=\"var(--_node-fill)\" stroke=\"var(--_node-stroke)\" stroke-width=\"")
				.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n  ");

			MultilineUtils.AppendMultilineText(
				sb, actor.Label, actor.X, actor.Y + actor.Height / 2,
				RenderConstants.FontSizes.NodeLabel,
				$"font-size=\"{RenderConstants.FontSizes.NodeLabel}\" text-anchor=\"middle\" font-weight=\"{RenderConstants.FontWeights.NodeLabel}\" fill=\"var(--_text)\"");
			sb.Append('\n');
		}

		sb.Append("</g>");
	}

	private static void AppendLifeline(StringBuilder sb, Lifeline lifeline)
	{
		sb.Append("\n<line class=\"lifeline\" data-actor=\"");
		MultilineUtils.AppendEscapedAttr(sb, lifeline.ActorId.AsSpan());
		sb.Append("\" x1=\"").Append(lifeline.X)
			.Append("\" y1=\"").Append(lifeline.TopY)
			.Append("\" x2=\"").Append(lifeline.X)
			.Append("\" y2=\"").Append(lifeline.BottomY)
			.Append("\" stroke=\"var(--_line)\" stroke-width=\"0.75\" stroke-dasharray=\"6 4\" />");
	}

	private static void AppendActivation(StringBuilder sb, Activation activation)
	{
		sb.Append("\n<rect class=\"activation\" data-actor=\"");
		MultilineUtils.AppendEscapedAttr(sb, activation.ActorId.AsSpan());
		sb.Append("\" x=\"").Append(activation.X)
			.Append("\" y=\"").Append(activation.TopY)
			.Append("\" width=\"").Append(activation.Width)
			.Append("\" height=\"").Append(activation.BottomY - activation.TopY)
			.Append("\" rx=\"4\" ry=\"4\"")
			.Append(" fill=\"var(--_node-fill)\" stroke=\"var(--_node-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.InnerBox).Append("\" />");
	}

	private static void AppendMessage(StringBuilder sb, PositionedSequenceMessage msg)
	{
		var dashArray = msg.LineStyle == SequenceLineStyle.Dashed ? " stroke-dasharray=\"6 4\"" : "";
		var markerId = msg.ArrowHead == SequenceArrowHead.Filled ? "seq-arrow" : "seq-arrow-open";

		sb.Append("\n<g class=\"message\" data-from=\"");
		MultilineUtils.AppendEscapedAttr(sb, msg.From.AsSpan());
		sb.Append("\" data-to=\"");
		MultilineUtils.AppendEscapedAttr(sb, msg.To.AsSpan());
		sb.Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, msg.Label.AsSpan());
		sb.Append("\" data-line-style=\"").Append(msg.LineStyle == SequenceLineStyle.Dashed ? "dashed" : "solid");
		sb.Append("\" data-arrow-head=\"").Append(msg.ArrowHead == SequenceArrowHead.Filled ? "filled" : "open");
		sb.Append("\" data-self=\"").Append(msg.IsSelf ? "true" : "false");
		sb.Append("\">\n");

		if (msg.IsSelf)
		{
			const double loopW = 30;
			const double loopH = 20;
			const double labelPadding = 8;

			sb.Append("  <polyline points=\"")
				.Append(msg.X1).Append(',').Append(msg.Y).Append(' ')
				.Append(msg.X1 + loopW).Append(',').Append(msg.Y).Append(' ')
				.Append(msg.X1 + loopW).Append(',').Append(msg.Y + loopH).Append(' ')
				.Append(msg.X2).Append(',').Append(msg.Y + loopH)
				.Append("\" fill=\"none\" stroke=\"var(--_line)\" stroke-width=\"")
				.Append(RenderConstants.StrokeWidths.Connector).Append('"').Append(dashArray)
				.Append(" marker-end=\"url(#").Append(markerId).Append(")\" />\n  ");

			MultilineUtils.AppendMultilineText(
				sb, msg.Label, msg.X1 + loopW + labelPadding, msg.Y + loopH / 2,
				RenderConstants.FontSizes.EdgeLabel,
				$"font-size=\"{RenderConstants.FontSizes.EdgeLabel}\" text-anchor=\"start\" font-weight=\"{RenderConstants.FontWeights.EdgeLabel}\" fill=\"var(--_text-muted)\"");
			sb.Append('\n');
		}
		else
		{
			sb.Append("  <line x1=\"").Append(msg.X1).Append("\" y1=\"").Append(msg.Y)
				.Append("\" x2=\"").Append(msg.X2).Append("\" y2=\"").Append(msg.Y)
				.Append("\" stroke=\"var(--_line)\" stroke-width=\"")
				.Append(RenderConstants.StrokeWidths.Connector).Append('"').Append(dashArray)
				.Append(" marker-end=\"url(#").Append(markerId).Append(")\" />\n  ");

			var midX = (msg.X1 + msg.X2) / 2;
			MultilineUtils.AppendMultilineText(
				sb, msg.Label, midX, msg.Y - 10,
				RenderConstants.FontSizes.EdgeLabel,
				$"font-size=\"{RenderConstants.FontSizes.EdgeLabel}\" text-anchor=\"middle\" font-weight=\"{RenderConstants.FontWeights.EdgeLabel}\" fill=\"var(--_text-muted)\"");
			sb.Append('\n');
		}

		sb.Append("</g>");
	}

	private static void AppendBlock(StringBuilder sb, PositionedSequenceBlock block)
	{
		sb.Append("\n<g class=\"block\" data-type=\"").Append(block.Type.ToString().ToLowerInvariant()).Append('"');
		if (block.Label.Length > 0)
		{
			sb.Append(" data-label=\"");
			MultilineUtils.AppendEscapedAttr(sb, block.Label.AsSpan());
			sb.Append('"');
		}
		sb.Append(">\n");

		sb.Append("  <rect x=\"").Append(block.X).Append("\" y=\"").Append(block.Y)
			.Append("\" width=\"").Append(block.Width).Append("\" height=\"").Append(block.Height)
			.Append("\" rx=\"").Append(RenderConstants.Radii.Group)
			.Append("\" ry=\"").Append(RenderConstants.Radii.Group)
			.Append("\" fill=\"none\" stroke=\"var(--_group-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n");

		var labelText = block.Label.Length > 0
			? $"{block.Type.ToString().ToLowerInvariant()} [{block.Label}]"
			: block.Type.ToString().ToLowerInvariant();
		var firstLine = labelText.Split('\n')[0];
		var tabWidth = TextMetrics.MeasureTextWidth(
			firstLine,
			RenderConstants.FontSizes.EdgeLabel,
			RenderConstants.FontWeights.GroupHeader) + 16;
		const double tabHeight = 18;

		sb.Append("  <rect x=\"").Append(block.X).Append("\" y=\"").Append(block.Y)
			.Append("\" width=\"").Append(tabWidth).Append("\" height=\"").Append(tabHeight)
			.Append("\" rx=\"6\" ry=\"6\"")
			.Append(" fill=\"var(--_group-hdr)\" stroke=\"var(--_group-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.OuterBox).Append("\" />\n  ");

		MultilineUtils.AppendMultilineText(
			sb, labelText,
			block.X + 6, block.Y + tabHeight / 2,
			RenderConstants.FontSizes.EdgeLabel,
			$"font-size=\"{RenderConstants.FontSizes.EdgeLabel}\" font-weight=\"{RenderConstants.FontWeights.GroupHeader}\" fill=\"var(--_text-sec)\"");
		sb.Append('\n');

		foreach (var divider in block.Dividers)
		{
			sb.Append("  <line x1=\"").Append(block.X).Append("\" y1=\"").Append(divider.Y)
				.Append("\" x2=\"").Append(block.X + block.Width).Append("\" y2=\"").Append(divider.Y)
				.Append("\" stroke=\"var(--_line)\" stroke-width=\"0.75\" stroke-dasharray=\"6 4\" />\n");

			if (divider.Label.Length > 0)
			{
				sb.Append("  ");
				MultilineUtils.AppendMultilineText(
					sb, $"[{divider.Label}]",
					block.X + 8, divider.Y + 14,
					RenderConstants.FontSizes.EdgeLabel,
					$"font-size=\"{RenderConstants.FontSizes.EdgeLabel}\" text-anchor=\"start\" font-weight=\"{RenderConstants.FontWeights.EdgeLabel}\" fill=\"var(--_text-muted)\"");
				sb.Append('\n');
			}
		}

		sb.Append("</g>");
	}

	private static void AppendNote(StringBuilder sb, PositionedSequenceNote note)
	{
		sb.Append("\n<g class=\"note\"");
		if (note.Position.HasValue)
			sb.Append(" data-position=\"").Append(note.Position.Value.ToString().ToLowerInvariant()).Append('"');
		if (note.Actors is { Count: > 0 })
		{
			sb.Append(" data-actors=\"");
			for (var i = 0; i < note.Actors.Count; i++)
			{
				if (i > 0) sb.Append(',');
				MultilineUtils.AppendEscapedAttr(sb, note.Actors[i].AsSpan());
			}
			sb.Append('"');
		}
		sb.Append(">\n");

		sb.Append("  <rect x=\"").Append(note.X).Append("\" y=\"").Append(note.Y)
			.Append("\" width=\"").Append(note.Width).Append("\" height=\"").Append(note.Height)
			.Append("\" rx=\"6\" ry=\"6\"")
			.Append(" fill=\"var(--_group-hdr)\" stroke=\"var(--_node-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.InnerBox).Append("\" />\n  ");

		const double asymmetry = 6.0;
		var textX = note.X + note.Width / 2;
		if (note.Position == SequenceNotePosition.Right)
			textX -= asymmetry;
		else if (note.Position == SequenceNotePosition.Left)
			textX += asymmetry;

		MultilineUtils.AppendMultilineText(
			sb, note.Text,
			textX, note.Y + note.Height / 2,
			RenderConstants.FontSizes.EdgeLabel,
			$"font-size=\"{RenderConstants.FontSizes.EdgeLabel}\" text-anchor=\"middle\" font-weight=\"{RenderConstants.FontWeights.EdgeLabel}\" fill=\"var(--_text-muted)\"");
		sb.Append('\n');

		sb.Append("</g>");
	}
}
