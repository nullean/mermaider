using System.Text;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Parts shared by the "systems" diagram types (sequence, architecture, C4): ghost participant chips, keyword-tab frames,
/// halo captions, dashed boundaries, small line glyphs and word wrapping for fixed-width cards.
/// </summary>
internal sealed partial class DesignSystem
{
	/// <summary>Height of a sequence frame keyword tab.</summary>
	internal const double FrameTabHeight = 22;

	/// <summary>Dash of a C4 boundary / dashed container outline.</summary>
	internal const string BoundaryDash = "3 4";

	/// <summary>
	/// A lower-emphasis mirror of a node: page-coloured chip with a soft outline and muted label (sequence footer row).
	/// </summary>
	internal void AppendGhostChip(StringBuilder sb, double x, double y, double w, double h, string label)
	{
		var r = Num(Spec.NodeRadius);
		_ = sb.Append("<rect x=\"").Append(x).Append("\" y=\"").Append(y)
			.Append("\" width=\"").Append(w).Append("\" height=\"").Append(h)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"var(--bg)\" stroke=\"var(--_line-soft)\" stroke-width=\"1.25\" />\n  ");
		AppendText(sb, label, x + (w / 2), y + (h / 2), TypeRole.Label, "var(--_text-muted)");
	}

	/// <summary>
	/// Caption text (message / relation label) with a page-coloured halo so it stays legible where it crosses a line.
	/// Multi-line captions keep their last line at <paramref name="baselineCy"/> and grow upwards.
	/// </summary>
	internal void AppendHaloCaption(StringBuilder sb, string text, double cx, double baselineCy, string anchor = "middle")
	{
		var px = Px(TypeRole.Caption);
		var lines = text.Split('\n').Length;
		var cy = baselineCy - ((lines - 1) * px * TextMetrics.LineHeightRatio / 2);
		var attrs = TextAttributes(TypeRole.Caption, anchor: anchor) + HaloAttributes;
		MultilineUtils.AppendMultilineText(sb, text, cx, cy, px, attrs);
	}


	/// <summary>
	/// A sequence frame (alt / loop / opt / par / critical / break) in the preset's container language: tinted body with a
	/// family-band keyword tab (Quiet), dashed outline with a caps keyword (Blueprint), or a soft block with a keyword chip
	/// (Tonal). The condition sits next to the keyword as caption text.
	/// </summary>
	internal void AppendFrame(StringBuilder sb, double x, double y, double w, double h, ColorFamily family, string keyword, string? condition)
	{
		var tagPx = Px(TypeRole.Tag);
		double conditionX;
		double midY;
		switch (Spec.Container)
		{
			case ContainerKind.Tab:
				{
					_ = sb.Append("  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
						.Append("\" width=\"").Append(w).Append("\" height=\"").Append(h)
						.Append("\" rx=\"").Append(Num(Spec.NodeRadius)).Append("\" ry=\"").Append(Num(Spec.NodeRadius))
						.Append("\" fill=\"none\" stroke=\"").Append(family.Edge).Append("\" stroke-width=\"1\" stroke-dasharray=\"").Append(BoundaryDash).Append("\" />\n  ");
					var caps = keyword.ToUpperInvariant();
					midY = y + 12;
					AppendText(sb, caps, x + 12, midY, TypeRole.Eyebrow, family.Ink, anchor: "start");
					conditionX = x + 12 + TextMetrics.MeasureTextWidth(caps, tagPx, 600) + (caps.Length * 1.0) + 8;
					break;
				}
			case ContainerKind.Chip:
				{
					_ = sb.Append("  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
						.Append("\" width=\"").Append(w).Append("\" height=\"").Append(h)
						.Append("\" rx=\"").Append(Num(Spec.NodeRadius + 2)).Append("\" ry=\"").Append(Num(Spec.NodeRadius + 2))
						.Append("\" fill=\"").Append(family.Tint(1)).Append("\" stroke=\"none\" />\n  ");
					var chipW = TextMetrics.MeasureTextWidth(keyword, tagPx, 600) + 16;
					midY = y + 18;
					_ = sb.Append("<rect x=\"").Append(x + 8).Append("\" y=\"").Append(midY - 10)
						.Append("\" width=\"").Append(chipW).Append("\" height=\"20\" rx=\"10\" ry=\"10\" fill=\"").Append(family.Band).Append("\" />\n  ");
					AppendText(sb, keyword, x + 8 + (chipW / 2), midY, TypeRole.Tag, family.Ink);
					conditionX = x + 8 + chipW + 10;
					break;
				}
			default:
				{
					var r = Num(Spec.NodeRadius + 2);
					_ = sb.Append("  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
						.Append("\" width=\"").Append(w).Append("\" height=\"").Append(h)
						.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
						.Append("\" fill=\"").Append(family.Tint(0)).Append("\" stroke=\"").Append(family.Edge).Append("\" stroke-width=\"1.25\" />\n  ");
					var tabW = TextMetrics.MeasureTextWidth(keyword, tagPx, 600) + 20;
					midY = y + (FrameTabHeight / 2);
					// tab: rounded top-left corner only, in the family band
					var tr = Math.Min(Spec.NodeRadius + 2, FrameTabHeight / 2);
					_ = sb.Append("<path d=\"M").Append(x).Append(',').Append(y + FrameTabHeight)
						.Append(" L").Append(x).Append(',').Append(y + tr)
						.Append(" Q").Append(x).Append(',').Append(y).Append(' ').Append(x + tr).Append(',').Append(y)
						.Append(" L").Append(x + tabW).Append(',').Append(y)
						.Append(" L").Append(x + tabW).Append(',').Append(y + FrameTabHeight)
						.Append(" Z\" fill=\"").Append(family.Band).Append("\" stroke=\"").Append(family.Edge).Append("\" stroke-width=\"1.25\" />\n  ");
					AppendText(sb, keyword, x + (tabW / 2), midY, TypeRole.Tag, family.Ink);
					conditionX = x + tabW + 10;
					break;
				}
		}

		if (condition is { Length: > 0 })
		{
			_ = sb.Append("\n  ");
			AppendText(sb, condition, conditionX, midY, TypeRole.Caption, anchor: "start");
		}

		_ = sb.Append('\n');
	}

	/// <summary>An <c>else</c> / <c>and</c> separator inside a frame: dashed family edge, condition as caption text under it.</summary>
	internal void AppendFrameDivider(StringBuilder sb, double x1, double x2, double y, ColorFamily family, string? condition)
	{
		_ = sb.Append("  <line x1=\"").Append(x1).Append("\" y1=\"").Append(y)
			.Append("\" x2=\"").Append(x2).Append("\" y2=\"").Append(y)
			.Append("\" stroke=\"").Append(family.Edge).Append("\" stroke-width=\"1.25\" stroke-dasharray=\"").Append(DashArray).Append("\" />\n");
		if (condition is { Length: > 0 })
		{
			_ = sb.Append("  ");
			AppendText(sb, condition, x1 + 12, y + 14, TypeRole.Caption, anchor: "start");
			_ = sb.Append('\n');
		}
	}

	/// <summary>
	/// A container body whose outline is dashed in every preset (C4 boundary): the preset's container fill and header band,
	/// with a dashed family outline. Draw the title with <see cref="AppendContainerHeader"/>.
	/// </summary>
	internal void AppendDashedContainerBody(StringBuilder sb, double x, double y, double w, double h, ColorFamily family, int depth, string cssClass, string? dataAttrs = null)
	{
		var r = Num(Spec.ContainerRadius);
		_ = sb.Append("\n<g class=\"").Append(cssClass).Append('"');
		if (dataAttrs is not null)
			_ = sb.Append(' ').Append(dataAttrs);
		_ = sb.Append(">\n  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
			.Append("\" width=\"").Append(w).Append("\" height=\"").Append(h)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"").Append(ContainerFill(family, depth)).Append("\" stroke=\"none\" />");
		if (Spec.Container == ContainerKind.Strip)
		{
			_ = sb.Append("\n  ");
			AppendTopRoundedRect(sb, x, y, w, StripHeight, Spec.ContainerRadius, family.Band);
			_ = sb.Append("\n  <line x1=\"").Append(x).Append("\" y1=\"").Append(y + StripHeight)
				.Append("\" x2=\"").Append(x + w).Append("\" y2=\"").Append(y + StripHeight)
				.Append("\" stroke=\"").Append(family.Edge).Append("\" stroke-width=\"1\" />");
		}

		_ = sb.Append("\n  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
			.Append("\" width=\"").Append(w).Append("\" height=\"").Append(h)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"none\" stroke=\"").Append(Spec.Container == ContainerKind.Tab ? family.Stroke : family.Edge)
			.Append("\" stroke-width=\"1.25\" stroke-dasharray=\"").Append(BoundaryDash).Append("\" />\n</g>");
	}

	/// <summary>Small line glyphs drawn in a family ink (person, cloud, database, disk, server, internet, generic).</summary>
	internal enum Glyph
	{
		Person,
		Cloud,
		Database,
		Disk,
		Server,
		Internet,
		Generic,
	}

	/// <summary>Draws <paramref name="glyph"/> centred at (<paramref name="cx"/>, <paramref name="cy"/>) in <paramref name="ink"/>, about 20px across × <paramref name="scale"/>.</summary>
	internal static void AppendGlyph(StringBuilder sb, Glyph glyph, double cx, double cy, string ink, double scale = 1.0)
	{
		_ = sb.Append("<g transform=\"translate(").Append(Num(cx)).Append(' ').Append(Num(cy)).Append(')');
		if (Math.Abs(scale - 1) > 0.001)
			_ = sb.Append(" scale(").Append(Num(scale)).Append(')');
		_ = sb.Append("\" fill=\"none\" stroke=\"").Append(ink).Append("\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\">");
		_ = glyph switch
		{
			Glyph.Person => sb.Append("<circle cx=\"0\" cy=\"-4.5\" r=\"3.6\" /><path d=\"M-7 8 C-7 1.5 7 1.5 7 8\" />"),
			Glyph.Cloud => sb.Append("<path d=\"M-7 4.5 C-11 4.5 -11 -1.5 -6 -1.5 C-6 -6.5 3 -7.5 4 -1.5 C10 -2.5 11 4.5 6 4.5Z\" />"),
			Glyph.Database => sb.Append("<ellipse cx=\"0\" cy=\"-8\" rx=\"9\" ry=\"3.5\" /><path d=\"M-9 -8 V8 C-9 10 9 10 9 8 V-8 M-9 0 C-9 2 9 2 9 0\" />"),
			Glyph.Disk => sb.Append("<path d=\"M-9 -8 H9 L7 9 H-7Z\" /><path d=\"M-9 -8 C-9 -11 9 -11 9 -8\" />"),
			Glyph.Server => sb.Append("<rect x=\"-10\" y=\"-9\" width=\"20\" height=\"7\" rx=\"2\" /><rect x=\"-10\" y=\"2\" width=\"20\" height=\"7\" rx=\"2\" /><path d=\"M-5 -5.5 H-4 M-5 5.5 H-4\" />"),
			Glyph.Internet => sb.Append("<circle cx=\"0\" cy=\"0\" r=\"9\" /><path d=\"M-9 0 H9 M0 -9 C5 -4 5 4 0 9 M0 -9 C-5 -4 -5 4 0 9\" />"),
			_ => sb.Append("<rect x=\"-8\" y=\"-8\" width=\"16\" height=\"16\" rx=\"3\" /><circle cx=\"0\" cy=\"0\" r=\"1.5\" />"),
		};
		_ = sb.Append("</g>");
	}

	/// <summary>
	/// Greedy word wrap of <paramref name="text"/> to <paramref name="maxWidth"/>; explicit newlines are kept and a word longer
	/// than the width is broken by characters, so nothing is ever clipped or cut with an ellipsis.
	/// </summary>
	internal static List<string> WrapWords(string text, double maxWidth, double fontPx, int weight)
	{
		var result = new List<string>();
		foreach (var paragraph in text.Split('\n'))
		{
			var current = "";
			foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
			{
				var candidate = current.Length == 0 ? word : current + " " + word;
				if (TextMetrics.MeasureTextWidth(candidate, fontPx, weight) <= maxWidth)
				{
					current = candidate;
					continue;
				}

				if (current.Length > 0)
					result.Add(current);
				current = word;
				while (current.Length > 1 && TextMetrics.MeasureTextWidth(current, fontPx, weight) > maxWidth)
				{
					var cut = current.Length - 1;
					while (cut > 1 && TextMetrics.MeasureTextWidth(current.AsSpan(0, cut), fontPx, weight) > maxWidth)
						cut--;
					result.Add(current[..cut]);
					current = current[cut..];
				}
			}

			result.Add(current);
		}

		return result;
	}

	/// <summary>Size of the icon slot in a container header.</summary>
	internal const double HeaderIconSize = 18;

	/// <summary>
	/// <see cref="AppendContainerHeader"/> with an icon slot in front of the title: the preset's header language (strip with
	/// accent mark, caps tab knocked out of the border, chip with accent dot) and <paramref name="drawIcon"/> called with the
	/// centre of an <see cref="HeaderIconSize"/> slot.
	/// </summary>
	internal void AppendContainerHeaderWithIcon(StringBuilder sb, double x, double y, string title, ColorFamily family, Action<StringBuilder, double, double> drawIcon)
	{
		var slot = HeaderIconSize + 6;
		switch (Spec.Container)
		{
			case ContainerKind.Tab:
				{
					var caps = title.ToUpperInvariant();
					var textW = TextMetrics.MeasureTextWidth(caps, Px(TypeRole.Eyebrow), 600) + (caps.Length * 1.0);
					var tabX = x + 12;
					_ = sb.Append("\n<rect x=\"").Append(tabX - 4).Append("\" y=\"").Append(y - (HeaderIconSize / 2))
						.Append("\" width=\"").Append(textW + slot + 20).Append("\" height=\"").Append(HeaderIconSize).Append("\" fill=\"var(--bg)\" />");
					_ = sb.Append("\n<rect x=\"").Append(tabX).Append("\" y=\"").Append(y - 5)
						.Append("\" width=\"2\" height=\"10\" fill=\"").Append(ColorFamily.AccentBase).Append("\" />\n");
					drawIcon(sb, tabX + 8 + (HeaderIconSize / 2), y);
					_ = sb.Append('\n');
					AppendText(sb, caps, tabX + 8 + slot, y, TypeRole.Eyebrow, family.Ink, anchor: "start");
					break;
				}
			case ContainerKind.Chip:
				{
					var textW = TextMetrics.MeasureTextWidth(title, Px(TypeRole.Subheading), 700);
					var chipW = textW + 34 + slot;
					_ = sb.Append("\n<rect x=\"").Append(x + 10).Append("\" y=\"").Append(y + 7)
						.Append("\" width=\"").Append(chipW).Append("\" height=\"24\" rx=\"12\" ry=\"12\" fill=\"").Append(family.Band).Append("\" />");
					_ = sb.Append("\n<circle cx=\"").Append(x + 22).Append("\" cy=\"").Append(y + 19)
						.Append("\" r=\"3.5\" fill=\"").Append(ColorFamily.AccentBase).Append("\" />\n");
					drawIcon(sb, x + 30 + (HeaderIconSize / 2), y + 19);
					_ = sb.Append('\n');
					AppendText(sb, title, x + 30 + slot, y + 19, TypeRole.Subheading, family.Ink, anchor: "start", weight: 700);
					break;
				}
			default:
				{
					var midY = y + (StripHeight / 2);
					_ = sb.Append("\n<rect x=\"").Append(x + 10).Append("\" y=\"").Append(midY - 6)
						.Append("\" width=\"3\" height=\"12\" rx=\"1.5\" ry=\"1.5\" fill=\"").Append(ColorFamily.AccentBase).Append("\" />\n");
					drawIcon(sb, x + 20 + (HeaderIconSize / 2), midY);
					_ = sb.Append('\n');
					AppendText(sb, title, x + 20 + slot, midY, TypeRole.Subheading, family.Ink, anchor: "start");
					break;
				}
		}
	}

}
