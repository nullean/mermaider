using System.Globalization;
using System.Text;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>Named text roles over the four size tiers; every piece of text in every diagram is one of these.</summary>
internal enum TypeRole
{
	/// <summary>Diagram title: l · 700 · text.</summary>
	Title,

	/// <summary>Entity name, venn set, C4 name, tree level 0: m · heading weight · text.</summary>
	Heading,

	/// <summary>Container title, tree level 1, treemap tile name: s · 600 · family ink.</summary>
	Subheading,

	/// <summary>Node label, actor, legend, axis name, card: s · label weight · text.</summary>
	Label,

	/// <summary>Members, task names, descriptions: s · 400 · text.</summary>
	Body,

	/// <summary>Edge and message labels, pills: xs · 500 · secondary.</summary>
	Caption,

	/// <summary>Badges, keyword tabs, counts, chips, shares: xs · 600 · family ink.</summary>
	Tag,

	/// <summary>Types, ticks, subtitles, units: xs · 400 · muted.</summary>
	Meta,

	/// <summary>Quadrant labels, gantt section names: xs · 600 caps +.08em · muted.</summary>
	Eyebrow,
}

/// <summary>Edge end markers shared by every diagram type.</summary>
internal enum MarkerShape
{
	/// <summary>Filled arrowhead (flow, message, dependency).</summary>
	Arrow,

	/// <summary>Open V arrowhead (association, async reply).</summary>
	Open,

	/// <summary>Hollow triangle (inheritance, realization).</summary>
	Triangle,

	/// <summary>Filled diamond (composition).</summary>
	Diamond,

	/// <summary>Hollow diamond (aggregation).</summary>
	DiamondHollow,

	/// <summary>Hollow circle (lollipop / contains).</summary>
	Circle,
}

/// <summary>
/// The design system for one render: the style preset, the gradient / tint / elevation knobs, the colour families, and the
/// shared components (node, container, edge, marker, label, note, badge, title, terminal) every renderer draws with.
/// Gradients and markers a diagram uses are collected and written once by <see cref="AppendDefs"/>.
/// </summary>
internal sealed partial class DesignSystem
{
	internal StyleSpec Spec { get; }
	internal bool Gradient { get; }
	internal double TintStrength { get; }
	internal int Elevation { get; }
	internal DiagramColors Colors { get; }

	private readonly string[] _autoPalette;
	private readonly Dictionary<string, string> _gradients = new(StringComparer.Ordinal);
	private readonly SortedDictionary<string, string> _markers = new(StringComparer.Ordinal);

	// Gradient / marker ids are document-global when several SVGs are inlined in one page, and their colours resolve
	// in the defining SVG. Prefixing every id with a hash of the colour and design inputs keeps two differently themed
	// diagrams apart, while identical themes share identical (harmless) definitions.
	private readonly string _idPrefix;

	private static string IdPrefix(DiagramColors colors, DesignInputs inputs)
	{
		var key = string.Join('|', colors.Bg, colors.Fg, colors.Accent, colors.Line, colors.Muted, colors.Surface, colors.Border,
			colors.DataPalette is null ? "" : string.Join(',', colors.DataPalette), colors.Default, colors.Success, colors.Failure,
			colors.Warning, colors.Info, inputs.Spec.Style, inputs.Gradient, inputs.Tint, inputs.Elevation);
		var hash = 2166136261u;
		foreach (var c in key)
			hash = (hash ^ c) * 16777619u;
		return "m" + hash.ToString("x8", CultureInfo.InvariantCulture) + "-";
	}

	internal DesignSystem(DiagramColors colors, DesignInputs inputs)
	{
		Colors = colors;
		Spec = inputs.Spec;
		Gradient = inputs.Gradient;
		TintStrength = inputs.Tint;
		Elevation = inputs.Elevation;
		_autoPalette = colors.AutoPalette();
		_idPrefix = IdPrefix(colors, inputs);
		Accent = ColorFamily.Accent(TintStrength);
		Neutral = ColorFamily.Neutral(TintStrength);
	}

	internal static DesignSystem For(SvgRenderContext context) => new(context.Styles.Colors, context.Styles.Design);

	// ====================================================================
	// Families
	// ====================================================================

	internal ColorFamily Accent { get; }
	internal ColorFamily Neutral { get; }

	/// <summary>Number of distinct cluster families before the auto palette wraps.</summary>
	internal int ClusterCount => _autoPalette.Length;

	/// <summary>Cluster family <c>p&lt;i&gt;</c> from the auto palette (role hues skipped, default colour first).</summary>
	internal ColorFamily Cluster(int i)
	{
		var slot = ((i % _autoPalette.Length) + _autoPalette.Length) % _autoPalette.Length;
		return new ColorFamily("p" + slot.ToString(CultureInfo.InvariantCulture), _autoPalette[slot], TintStrength);
	}

	/// <summary>Chart series family from the full data palette (charts may use role hues; entity diagrams may not).</summary>
	internal ColorFamily Series(int i)
	{
		var color = Colors.PaletteAt(i);
		var palette = Colors.DataPalette ?? CategoricalPalette.Colors;
		var slot = i % palette.Length;
		return new ColorFamily("d" + slot.ToString(CultureInfo.InvariantCulture), color, TintStrength);
	}

	internal ColorFamily Role(ColorRole role) => new(role switch
	{
		ColorRole.Success => "s",
		ColorRole.Failure => "f",
		ColorRole.Warning => "w",
		ColorRole.Info => "i",
		_ => "p0",
	}, Colors.RoleColor(role), TintStrength);

	/// <summary>The family of a role class name (<c>success</c>, <c>failure</c>, <c>warning</c>, <c>info</c>), else null.</summary>
	internal ColorFamily? RoleByName(string? name) => name switch
	{
		"success" => Role(ColorRole.Success),
		"failure" => Role(ColorRole.Failure),
		"warning" => Role(ColorRole.Warning),
		"info" => Role(ColorRole.Info),
		_ => null,
	};

	// ====================================================================
	// Boxes
	// ====================================================================

	/// <summary>Fill of a node / card / cell in <paramref name="family"/>: gradient, flat, page knock-out or soft per preset.</summary>
	internal string NodeFill(ColorFamily family) => Spec.Fill switch
	{
		FillKind.Knockout => "var(--bg)",
		FillKind.Soft => family.Soft,
		_ => Gradient ? VerticalGradient("ng-" + family.Key, family.Top, family.Bot) : family.Flat,
	};

	/// <summary>Outline colour of a node; <c>none</c> when the preset has no outlines.</summary>
	internal string NodeStroke(ColorFamily family) => Spec.OutlineWidth > 0 ? family.Stroke : "none";

	internal string NodeStrokeWidth => Num(Spec.OutlineWidth);

	/// <summary>Terminals (stadium) read as stronger: a slightly heavier outline.</summary>
	internal string TerminalStrokeWidth => Num(Spec.OutlineWidth > 0 ? Spec.OutlineWidth + 0.5 : 0);

	/// <summary>Appends a node rectangle in <paramref name="family"/> with the preset radius.</summary>
	internal void AppendBox(StringBuilder sb, double x, double y, double w, double h, ColorFamily family, double? radius = null)
	{
		var r = Num(radius ?? Spec.NodeRadius);
		_ = sb.Append("<rect x=\"").Append(x).Append("\" y=\"").Append(y)
			.Append("\" width=\"").Append(w).Append("\" height=\"").Append(h)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"").Append(NodeFill(family))
			.Append("\" stroke=\"").Append(NodeStroke(family))
			.Append("\" stroke-width=\"").Append(NodeStrokeWidth).Append("\" />");
	}

	// ====================================================================
	// Containers
	// ====================================================================

	/// <summary>Height of a container header strip / tab band.</summary>
	internal const double StripHeight = 28;

	/// <summary>Inset between a container border and its content.</summary>
	internal const double ContainerInset = 12;

	/// <summary>Body fill of a container at nesting <paramref name="depth"/>.</summary>
	internal string ContainerFill(ColorFamily family, int depth) => Spec.Container switch
	{
		ContainerKind.Tab => "none",
		_ when Gradient => DiagonalGradient(
			"cw-" + family.Key + "-" + depth.ToString(CultureInfo.InvariantCulture),
			family.Mix((ColorFamily.TintRatio + (ColorFamily.TintStep * depth)) * 0.35 * TintStrength), family.Tint(depth)),
		_ => family.Tint(depth),
	};

	/// <summary>Container outlines are as heavy as node outlines, so every box in a diagram reads at one weight.</summary>
	internal string ContainerStrokeWidth => Num(Math.Max(1, Spec.OutlineWidth));

	internal string ContainerStroke(ColorFamily family) => Spec.Container == ContainerKind.Chip ? "none" : family.Edge;

	/// <summary>Container border for the Tab preset is the family stroke (it is the only thing drawn).</summary>
	private string ContainerOutline(ColorFamily family) => Spec.Container switch
	{
		ContainerKind.Tab => family.Stroke,
		ContainerKind.Chip => "none",
		_ => family.Edge,
	};

	/// <summary>
	/// The container body: everything drawn underneath the edges. Call <see cref="AppendContainerHeader"/> after the edges so
	/// titles stay on top. <paramref name="cssClass"/> keeps the elevation rule (<c>subgraph</c>, <c>kanban-column</c>, …).
	/// </summary>
	internal void AppendContainerBody(StringBuilder sb, double x, double y, double w, double h, ColorFamily family, int depth, string cssClass = "subgraph", string? dataAttrs = null)
	{
		var r = Num(Spec.ContainerRadius);
		_ = sb.Append("\n<g class=\"").Append(cssClass).Append('"');
		if (dataAttrs is not null)
			_ = sb.Append(' ').Append(dataAttrs);
		_ = sb.Append(">\n  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
			.Append("\" width=\"").Append(w).Append("\" height=\"").Append(h)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"").Append(ContainerFill(family, depth))
			.Append("\" stroke=\"").Append(ContainerOutline(family))
			.Append("\" stroke-width=\"").Append(ContainerStrokeWidth).Append('"');
		if (Spec.DashedContainers)
			_ = sb.Append(" stroke-dasharray=\"3 4\"");
		_ = sb.Append(" />");

		if (Spec.Container == ContainerKind.Strip)
		{
			// header strip: rounded top, square bottom, in the family band, with a hairline under it
			_ = sb.Append("\n  ");
			AppendTopRoundedRect(sb, x, y, w, StripHeight, Spec.ContainerRadius, family.Band);
			_ = sb.Append("\n  <line x1=\"").Append(x).Append("\" y1=\"").Append(y + StripHeight)
				.Append("\" x2=\"").Append(x + w).Append("\" y2=\"").Append(y + StripHeight)
				.Append("\" stroke=\"").Append(family.Edge).Append("\" stroke-width=\"").Append(ContainerStrokeWidth).Append("\" />");
		}

		_ = sb.Append("\n</g>");
	}

	/// <summary>
	/// The container title (and optional count) in the preset's header language: strip title with an accent mark, a caps tab
	/// knocked out of a dashed border, or a chip with an accent dot.
	/// </summary>
	internal void AppendContainerHeader(StringBuilder sb, double x, double y, double w, string title, ColorFamily family, string? count = null)
	{
		var midY = y + (StripHeight / 2);
		switch (Spec.Container)
		{
			case ContainerKind.Tab:
				{
					var caps = title.ToUpperInvariant();
					var textW = XsWidth(caps, 600, letterSpacingPerChar: 1.0);
					var tabX = x + 12;
					// knock the border out behind the tab, then tick + caps title
					_ = sb.Append("\n<rect x=\"").Append(tabX - 4).Append("\" y=\"").Append(y - 1)
						.Append("\" width=\"").Append(textW + 20).Append("\" height=\"2\" fill=\"var(--bg)\" />");
					_ = sb.Append("\n<rect x=\"").Append(tabX).Append("\" y=\"").Append(y - 5)
						.Append("\" width=\"2\" height=\"10\" fill=\"").Append(family.Stroke).Append("\" />");
					_ = sb.Append('\n');
					AppendText(sb, caps, tabX + 8, y, TypeRole.Eyebrow, family.Ink, anchor: "start");
					if (count is not null)
					{
						_ = sb.Append('\n');
						AppendText(sb, "[" + count + "]", x + w - 12, y, TypeRole.Tag, family.Ink, anchor: "end");
					}

					break;
				}
			case ContainerKind.Chip:
				{
					var textW = TextMetrics.MeasureTextWidth(title, Px(TypeRole.Subheading), 700);
					var chipW = textW + 34;
					_ = sb.Append("\n<rect x=\"").Append(x + 10).Append("\" y=\"").Append(y + 8)
						.Append("\" width=\"").Append(chipW).Append("\" height=\"22\" rx=\"11\" ry=\"11\" fill=\"").Append(family.Band).Append("\" />");
					_ = sb.Append("\n<circle cx=\"").Append(x + 22).Append("\" cy=\"").Append(y + 19)
						.Append("\" r=\"3.5\" fill=\"").Append(family.Stroke).Append("\" />");
					_ = sb.Append('\n');
					AppendText(sb, title, x + 30, y + 19, TypeRole.Subheading, family.Ink, anchor: "start", weight: 700);
					if (count is not null)
					{
						_ = sb.Append('\n');
						_ = AppendBadge(sb, x + w - 12, y + 19, count, family, anchor: "end");
					}

					break;
				}
			default:
				{
					// ink title in the strip
					_ = sb.Append('\n');
					AppendText(sb, title, x + 12, midY, TypeRole.Subheading, family.Ink, anchor: "start");
					if (count is not null)
					{
						_ = sb.Append('\n');
						_ = AppendBadge(sb, x + w - 10, midY, count, family, anchor: "end");
					}

					break;
				}
		}
	}

	/// <summary>
	/// Space reserved between a container's top edge and its first content row. The same in every preset (the tallest
	/// header, the Tonal chip, plus breathing room), so layout never depends on the style.
	/// </summary>
	internal const double ContainerContentTop = 44;

	/// <summary>Vertical space a container header paints (strip / tab / chip). Paint only — lay out with <see cref="ContainerContentTop"/>.</summary>
	internal double ContainerHeaderHeight => Spec.Container switch
	{
		ContainerKind.Tab => 16,
		ContainerKind.Chip => 36,
		_ => StripHeight,
	};

	// ====================================================================
	// Edges, dash language, markers
	// ====================================================================

	internal const string EdgeColor = "var(--_line)";

	internal string EdgeWidth => Num(Spec.EdgeWidth);

	internal string ThickEdgeWidth => Num(Spec.EdgeWidth * 2);

	/// <summary>Dashed: dependency, return, realize.</summary>
	internal const string DashArray = "5 4";

	/// <summary>Dotted: derived, trace, copy (drawn with round caps).</summary>
	internal const string DotArray = "1.5 4.5";

	/// <summary>Returns <c>url(#…)</c> for a shared marker, registering its definition.</summary>
	/// <param name="shape">The marker shape.</param>
	/// <param name="atStart">Mirror the marker for <c>marker-start</c>.</param>
	/// <param name="accent">Draw it in the accent (story lines, highlighted edges) instead of the preset's marker colour.</param>
	internal string Marker(MarkerShape shape, bool atStart = false, bool accent = false)
	{
		var id = _idPrefix + "mk-" + shape.ToString().ToLowerInvariant() + (atStart ? "-s" : "") + (accent ? "-a" : "");
		if (!_markers.ContainsKey(id))
			_markers[id] = BuildMarker(id, shape, atStart, accent ? ColorFamily.AccentBase : MarkerColor);
		return "url(#" + id + ")";
	}

	private string MarkerColor => Spec.Marker == MarkerKind.Thin ? ColorFamily.AccentBase : EdgeColor;

	private string BuildMarker(string id, MarkerShape shape, bool atStart, string color)
	{
		// geometry in a 12 × 12 box pointing right; start markers are mirrored
		var (size, half) = Spec.Marker switch
		{
			MarkerKind.Thin => (10.0, 3.5),
			MarkerKind.Chunky => (11.0, 5.5),
			_ => (10.0, 4.5),
		};
		var join = Spec.Marker == MarkerKind.Chunky ? "round" : "miter";
		var sw = Spec.Marker == MarkerKind.Chunky ? 1.5 : 1;
		var tip = atStart ? 0 : size;
		var back = atStart ? size : 0;
		var cy = 6.0;

		static string Pt(double x, double y) => Num(x) + "," + Num(y);

		var body = shape switch
		{
			MarkerShape.Arrow => Spec.Marker == MarkerKind.Thin
				? $"<path d=\"M{Pt(back, cy - half)} L{Pt(tip, cy)} L{Pt(back, cy + half)}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1.25\" stroke-linejoin=\"{join}\" stroke-linecap=\"round\" />"
				: $"<polygon points=\"{Pt(back, cy - half)} {Pt(tip, cy)} {Pt(back, cy + half)}\" fill=\"{color}\" stroke=\"{color}\" stroke-width=\"{Num(sw)}\" stroke-linejoin=\"{join}\" />",
			MarkerShape.Open =>
				$"<path d=\"M{Pt(back, cy - half)} L{Pt(tip, cy)} L{Pt(back, cy + half)}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{Num(sw + 0.25)}\" stroke-linejoin=\"{join}\" stroke-linecap=\"round\" />",
			MarkerShape.Triangle =>
				$"<polygon points=\"{Pt(back, cy - half - 1)} {Pt(tip, cy)} {Pt(back, cy + half + 1)}\" fill=\"var(--bg)\" stroke=\"{color}\" stroke-width=\"{Num(sw + 0.25)}\" stroke-linejoin=\"{join}\" />",
			MarkerShape.Diamond =>
				$"<polygon points=\"{Pt(0, cy)} {Pt((size / 2) + 1, cy - half)} {Pt(size + 2, cy)} {Pt((size / 2) + 1, cy + half)}\" fill=\"{color}\" stroke=\"{color}\" stroke-width=\"{Num(sw)}\" stroke-linejoin=\"{join}\" />",
			MarkerShape.DiamondHollow =>
				$"<polygon points=\"{Pt(0, cy)} {Pt((size / 2) + 1, cy - half)} {Pt(size + 2, cy)} {Pt((size / 2) + 1, cy + half)}\" fill=\"var(--bg)\" stroke=\"{color}\" stroke-width=\"{Num(sw + 0.25)}\" stroke-linejoin=\"{join}\" />",
			_ =>
				$"<circle cx=\"{Num((size / 2) + 1)}\" cy=\"{Num(cy)}\" r=\"{Num(half)}\" fill=\"var(--bg)\" stroke=\"{color}\" stroke-width=\"{Num(sw + 0.25)}\" />",
		};

		var w = shape is MarkerShape.Diamond or MarkerShape.DiamondHollow or MarkerShape.Circle ? size + 2 : size;
		var refX = shape is MarkerShape.Diamond or MarkerShape.DiamondHollow or MarkerShape.Circle
			? (atStart ? 0 : w)
			: tip;
		return $"  <marker id=\"{id}\" markerUnits=\"userSpaceOnUse\" markerWidth=\"{Num(w + 1)}\" markerHeight=\"12\" refX=\"{Num(refX)}\" refY=\"{Num(cy)}\" orient=\"auto\">\n    {body}\n  </marker>";
	}

	// ====================================================================
	// Labels, badges, notes, titles, terminals
	// ====================================================================

	/// <summary>Page-background halo behind text (paint-order stroke), so text stays legible where it crosses a line.</summary>
	internal const string HaloAttributes = " stroke=\"var(--bg)\" stroke-width=\"4\" stroke-linejoin=\"round\" paint-order=\"stroke\"";

	/// <summary>Height of an edge-label pill / chip (xs text + 4px each side).</summary>
	internal const double PillHeight = 20;

	/// <summary>Width of the edge label box for a measured text width.</summary>
	internal static double LabelBoxWidth(double textWidth) => textWidth + 16;

	/// <summary>
	/// An edge / message label centred at (<paramref name="cx"/>, <paramref name="cy"/>) in the preset's label language:
	/// outlined pill, halo text, or filled chip. Multi-line labels grow the box.
	/// </summary>
	internal void AppendEdgeLabel(StringBuilder sb, double cx, double cy, string text, string? colorOverride = null)
	{
		var px = Px(TypeRole.Caption);
		var metrics = TextMetrics.MeasureMultiline(text.AsSpan(), px, 500);
		var w = LabelBoxWidth(metrics.Width);
		var h = Math.Max(PillHeight, metrics.Height + 6);
		var r = Spec.Label == LabelKind.Chip ? Math.Min(6, h / 2) : h / 2;
		switch (Spec.Label)
		{
			case LabelKind.Pill:
				_ = sb.Append("<rect x=\"").Append(cx - (w / 2)).Append("\" y=\"").Append(cy - (h / 2))
					.Append("\" width=\"").Append(w).Append("\" height=\"").Append(h)
					.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
					.Append("\" fill=\"var(--bg)\" stroke=\"").Append(EdgeColor).Append("\" stroke-width=\"").Append(EdgeWidth).Append("\" />");
				break;
			case LabelKind.Halo:
				// a borderless page-coloured knock-out: the halo alone lets the line show through the gaps between words
				_ = sb.Append("<rect x=\"").Append(cx - (metrics.Width / 2) - 3).Append("\" y=\"").Append(cy - (metrics.Height / 2) - 1)
					.Append("\" width=\"").Append(metrics.Width + 6).Append("\" height=\"").Append(metrics.Height + 2)
					.Append("\" fill=\"var(--bg)\" stroke=\"none\" />");
				break;
			case LabelKind.Chip:
				_ = sb.Append("<rect x=\"").Append(cx - (w / 2)).Append("\" y=\"").Append(cy - (h / 2))
					.Append("\" width=\"").Append(w).Append("\" height=\"").Append(h)
					.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
					.Append("\" fill=\"var(--_key-badge)\" stroke=\"none\" />");
				break;
		}

		_ = sb.Append(' ');
		var attrs = TextAttributes(TypeRole.Caption, colorOverride, anchor: "middle", weight: Spec.Label == LabelKind.Chip ? 600 : null)
			+ (Spec.Label == LabelKind.Halo ? HaloAttributes : "");
		MultilineUtils.AppendMultilineText(sb, text, cx, cy, px, attrs);
	}

	/// <summary>A small badge (key, count, tag) anchored at <paramref name="x"/>, vertically centred on <paramref name="cy"/>. Returns its width.</summary>
	internal double AppendBadge(StringBuilder sb, double x, double cy, string text, ColorFamily family, string anchor = "start")
	{
		var px = Px(TypeRole.Tag);
		var shown = Spec.Badge == BadgeKind.Bracket ? "[" + text + "]" : text;
		var textW = IsMono(TypeRole.Tag) ? XsWidth(shown, 600) : TextMetrics.MeasureTextWidth(shown, px, 600);
		var w = Spec.Badge == BadgeKind.Bracket ? textW : textW + 12;
		var left = anchor switch
		{
			"end" => x - w,
			"middle" => x - (w / 2),
			_ => x,
		};
		if (Spec.Badge != BadgeKind.Bracket)
		{
			_ = sb.Append("<rect x=\"").Append(left).Append("\" y=\"").Append(cy - 8)
				.Append("\" width=\"").Append(w).Append("\" height=\"16\" rx=\"")
				.Append(Spec.Badge == BadgeKind.Chip ? "8" : "4").Append("\" ry=\"").Append(Spec.Badge == BadgeKind.Chip ? "8" : "4")
				.Append("\" fill=\"").Append(Spec.Badge == BadgeKind.Chip ? family.Soft : family.Band).Append("\" />");
		}

		AppendText(sb, shown, left + (w / 2), cy, TypeRole.Tag, family.Ink, anchor: "middle");
		return w;
	}

	/// <summary>A note box (and its dotted link) in the preset's note language: accent rail, registration ticks, or sticker.</summary>
	internal void AppendNote(StringBuilder sb, double x, double y, double w, double h, string text, Models.Point? linkFrom = null, Models.Point? linkTo = null)
	{
		_ = sb.Append("\n<g class=\"note\">");
		if (linkFrom is { } lf && linkTo is { } lt)
		{
			_ = sb.Append("\n  <line x1=\"").Append(lf.X).Append("\" y1=\"").Append(lf.Y)
				.Append("\" x2=\"").Append(lt.X).Append("\" y2=\"").Append(lt.Y)
				.Append("\" stroke=\"").Append(Accent.Edge).Append("\" stroke-width=\"1.25\" stroke-dasharray=\"").Append(DotArray)
				.Append("\" stroke-linecap=\"round\" />");
		}

		_ = sb.Append("\n  ");
		switch (Spec.Note)
		{
			case NoteKind.Ticks:
				{
					const double t = 6;
					var a = ColorFamily.AccentBase;
					_ = sb.Append("<path d=\"M").Append(x).Append(',').Append(y + t).Append(" V").Append(y).Append(" H").Append(x + t)
						.Append(" M").Append(x + w - t).Append(',').Append(y).Append(" H").Append(x + w).Append(" V").Append(y + t)
						.Append(" M").Append(x + w).Append(',').Append(y + h - t).Append(" V").Append(y + h).Append(" H").Append(x + w - t)
						.Append(" M").Append(x + t).Append(',').Append(y + h).Append(" H").Append(x).Append(" V").Append(y + h - t)
						.Append("\" fill=\"none\" stroke=\"").Append(a).Append("\" stroke-width=\"1.5\" />");
					break;
				}
			case NoteKind.Sticker:
				_ = sb.Append("<rect x=\"").Append(x).Append("\" y=\"").Append(y)
					.Append("\" width=\"").Append(w).Append("\" height=\"").Append(h)
					.Append("\" rx=\"10\" ry=\"10\" fill=\"").Append(Accent.Band).Append("\" stroke=\"none\" />");
				_ = sb.Append("\n  ");
				AppendText(sb, "“", x + 9, y + 11, TypeRole.Heading, ColorFamily.AccentBase, anchor: "middle", weight: 700);
				break;
			default:
				_ = sb.Append("<rect x=\"").Append(x).Append("\" y=\"").Append(y)
					.Append("\" width=\"").Append(w).Append("\" height=\"").Append(h)
					.Append("\" rx=\"4\" ry=\"4\" fill=\"").Append(Accent.Tint(1)).Append("\" stroke=\"").Append(Accent.Edge)
					.Append("\" stroke-width=\"1\" />");
				_ = sb.Append("\n  <rect x=\"").Append(x).Append("\" y=\"").Append(y)
					.Append("\" width=\"3\" height=\"").Append(h).Append("\" fill=\"").Append(ColorFamily.AccentBase).Append("\" />");
				break;
		}

		_ = sb.Append("\n  ");
		// layouts size notes at the caption tier, so the text is set at that tier too
		var px = Px(TypeRole.Caption);
		MultilineUtils.AppendMultilineText(sb, text, x + (w / 2) + (Spec.Note == NoteKind.Rail ? 1.5 : 0), y + (h / 2), px,
			TextAttributes(TypeRole.Caption, Accent.Ink, anchor: "middle"));
		_ = sb.Append("\n</g>");
	}

	/// <summary>Left offset of a title's text from its mark.</summary>
	internal const double TitleIndent = 16;

	/// <summary>The diagram title at (<paramref name="x"/>, <paramref name="cy"/>) with the preset's accent mark, left aligned.</summary>
	internal void AppendTitle(StringBuilder sb, double x, double cy, string title)
	{
		var a = ColorFamily.AccentBase;
		_ = sb.Append("\n<g class=\"diagram-title\">\n  ");
		_ = Spec.TitleMark switch
		{
			TitleMark.Rule => sb.Append("<rect x=\"").Append(x).Append("\" y=\"").Append(cy - 9)
								.Append("\" width=\"3\" height=\"18\" fill=\"").Append(a).Append("\" />"),
			TitleMark.Disc => sb.Append("<circle cx=\"").Append(x + 5).Append("\" cy=\"").Append(cy)
								.Append("\" r=\"5\" fill=\"").Append(a).Append("\" />"),
			_ => sb.Append("<rect x=\"").Append(x).Append("\" y=\"").Append(cy - 4.5)
								.Append("\" width=\"9\" height=\"9\" rx=\"2\" ry=\"2\" fill=\"").Append(a).Append("\" />"),
		};
		_ = sb.Append("\n  ");
		AppendText(sb, title, x + TitleIndent, cy, TypeRole.Title, null, anchor: "start");
		_ = sb.Append("\n</g>");
	}

	/// <summary>A start (filled) or end (ring + core) terminal in the accent: dot, square or disc per preset.</summary>
	internal void AppendTerminal(StringBuilder sb, double cx, double cy, double r, bool end)
	{
		var a = ColorFamily.AccentBase;
		if (Spec.Terminal == TerminalKind.Square)
		{
			var s = r * 1.6;
			if (end)
			{
				_ = sb.Append("<rect x=\"").Append(cx - (s / 2)).Append("\" y=\"").Append(cy - (s / 2))
					.Append("\" width=\"").Append(s).Append("\" height=\"").Append(s)
					.Append("\" fill=\"none\" stroke=\"").Append(a).Append("\" stroke-width=\"1.5\" />\n");
				var inner = s * 0.5;
				_ = sb.Append("<rect x=\"").Append(cx - (inner / 2)).Append("\" y=\"").Append(cy - (inner / 2))
					.Append("\" width=\"").Append(inner).Append("\" height=\"").Append(inner).Append("\" fill=\"").Append(a).Append("\" />");
			}
			else
			{
				_ = sb.Append("<rect x=\"").Append(cx - (s / 2)).Append("\" y=\"").Append(cy - (s / 2))
					.Append("\" width=\"").Append(s).Append("\" height=\"").Append(s).Append("\" fill=\"").Append(a).Append("\" />");
			}

			return;
		}

		var rr = Spec.Terminal == TerminalKind.Disc ? Math.Min(r, 8) : r;
		if (end)
		{
			_ = sb.Append("<circle cx=\"").Append(cx).Append("\" cy=\"").Append(cy).Append("\" r=\"").Append(rr)
				.Append("\" fill=\"none\" stroke=\"").Append(a).Append("\" stroke-width=\"2\" />\n");
			_ = sb.Append("<circle cx=\"").Append(cx).Append("\" cy=\"").Append(cy).Append("\" r=\"").Append(Math.Max(2, rr - 4))
				.Append("\" fill=\"").Append(a).Append("\" />");
		}
		else
		{
			_ = sb.Append("<circle cx=\"").Append(cx).Append("\" cy=\"").Append(cy).Append("\" r=\"").Append(rr)
				.Append("\" fill=\"").Append(a).Append("\" />");
		}
	}

	// ====================================================================
	// Text
	// ====================================================================

	/// <summary>Default px of a role's tier (what layout measures with).</summary>
	internal static double Px(TypeRole role) => role switch
	{
		TypeRole.Title => 18,
		TypeRole.Heading => 16,
		TypeRole.Caption or TypeRole.Tag or TypeRole.Meta or TypeRole.Eyebrow => 12,
		_ => 14,
	};

	internal static string FsVar(TypeRole role) => role switch
	{
		TypeRole.Title => RenderConstants.FsVar.L,
		TypeRole.Heading => RenderConstants.FsVar.M,
		TypeRole.Caption or TypeRole.Tag or TypeRole.Meta or TypeRole.Eyebrow => RenderConstants.FsVar.Xs,
		_ => RenderConstants.FsVar.S,
	};

	internal int Weight(TypeRole role) => role switch
	{
		TypeRole.Title => 700,
		TypeRole.Heading => Spec.HeadingWeight,
		TypeRole.Subheading => 600,
		TypeRole.Label => Spec.LabelWeight,
		TypeRole.Body or TypeRole.Meta => 400,
		TypeRole.Caption => 500,
		_ => 600,
	};

	private static string DefaultColor(TypeRole role) => role switch
	{
		TypeRole.Caption => "var(--_text-sec)",
		TypeRole.Meta or TypeRole.Eyebrow => "var(--_text-muted)",
		_ => "var(--_text)",
	};

	/// <summary>True when the role renders in the mono font under this preset.</summary>
	internal bool IsMono(TypeRole role) =>
		Spec.MonoXs && role is TypeRole.Caption or TypeRole.Tag or TypeRole.Meta or TypeRole.Eyebrow;

	/// <summary>Attribute string for a text element in <paramref name="role"/> (size, weight, colour, anchor, mono, tracking).</summary>
	internal string TextAttributes(TypeRole role, string? color = null, string anchor = "middle", int? weight = null)
	{
		var sb = new StringBuilder(160);
		if (IsMono(role))
			_ = sb.Append("class=\"mono\" ");
		_ = sb.Append("text-anchor=\"").Append(anchor).Append("\" font-size=\"").Append(FsVar(role))
			.Append("\" font-weight=\"").Append(weight ?? Weight(role)).Append('"');
		if (role == TypeRole.Eyebrow)
			_ = sb.Append(" letter-spacing=\"0.08em\"");
		_ = sb.Append(" fill=\"").Append(color ?? DefaultColor(role)).Append('"');
		return sb.ToString();
	}

	/// <summary>Appends a (possibly multi-line) text centred vertically on <paramref name="cy"/>.</summary>
	internal void AppendText(StringBuilder sb, string text, double x, double cy, TypeRole role, string? color = null, string anchor = "middle", int? weight = null) =>
		MultilineUtils.AppendMultilineText(sb, text, x, cy, Px(role), TextAttributes(role, color, anchor, weight));

	// ====================================================================
	// Gradients and defs
	// ====================================================================

	/// <summary>Registers a top → bottom gradient and returns its <c>url(#id)</c>.</summary>
	internal string VerticalGradient(string id, string top, string bottom) =>
		RegisterGradient(id, "x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\"", [(0, top, null), (1, bottom, null)]);

	/// <summary>Registers a diagonal (top-left → bottom-right) wash gradient and returns its <c>url(#id)</c>.</summary>
	internal string DiagonalGradient(string id, string start, string end) =>
		RegisterGradient(id, "x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"", [(0, start, null), (0.55, end, null), (1, end, null)]);

	/// <summary>Registers a gradient in user space between two points (sankey ribbons) and returns its <c>url(#id)</c>.</summary>
	internal string SpanGradient(string id, double x1, double y1, double x2, double y2, string from, string to, double opacity) =>
		RegisterGradient(id,
			$"gradientUnits=\"userSpaceOnUse\" x1=\"{Num(x1)}\" y1=\"{Num(y1)}\" x2=\"{Num(x2)}\" y2=\"{Num(y2)}\"",
			[(0, from, opacity), (1, to, opacity)]);

	/// <summary>Fill for a bar / column in a series colour: gradient (base → lighter) when gradients are on, else solid.</summary>
	internal string BarFill(ColorFamily family) => Gradient && Spec.ChartMarks != ChartMarkKind.Outline
		? VerticalGradient("bg-" + family.Key, family.Base, ColorFamily.Mix(family.Base, 62, "var(--bg)"))
		: Spec.ChartMarks == ChartMarkKind.Outline ? family.Mix(ColorFamily.AreaOpacity * 100) : family.Base;

	private string RegisterGradient(string id, string geometry, (double Offset, string Color, double? Opacity)[] stops)
	{
		id = _idPrefix + id;
		if (!_gradients.ContainsKey(id))
		{
			var sb = new StringBuilder(256);
			_ = sb.Append("  <linearGradient id=\"").Append(id).Append("\" ").Append(geometry).Append('>');
			foreach (var (offset, color, opacity) in stops)
			{
				_ = sb.Append("<stop offset=\"").Append(Num(offset)).Append("\" stop-color=\"").Append(color).Append('"');
				if (opacity is { } o)
					_ = sb.Append(" stop-opacity=\"").Append(Num(o)).Append('"');
				_ = sb.Append(" />");
			}

			_ = sb.Append("</linearGradient>");
			_gradients[id] = sb.ToString();
		}

		return "url(#" + id + ")";
	}

	/// <summary>Writes one <c>&lt;defs&gt;</c> with every gradient and marker this render used (nothing when none).</summary>
	internal void AppendDefs(StringBuilder sb)
	{
		if (_gradients.Count == 0 && _markers.Count == 0)
			return;

		_ = sb.Append("\n<defs>\n");
		foreach (var id in _gradients.Keys.Order(StringComparer.Ordinal))
			_ = sb.Append(_gradients[id]).Append('\n');
		foreach (var marker in _markers.Values)
			_ = sb.Append(marker).Append('\n');
		_ = sb.Append("</defs>");
	}

	/// <summary>Writes the collected defs and closes the document.</summary>
	internal void Close(StringBuilder sb)
	{
		AppendDefs(sb);
		_ = sb.Append("\n</svg>");
	}

	// ====================================================================
	// Helpers
	// ====================================================================

	/// <summary>A path with rounded top corners and a square bottom (header bands, strips).</summary>
	internal static void AppendTopRoundedRect(StringBuilder sb, double x, double y, double w, double h, double radius, string fill)
	{
		var r = Math.Min(radius, Math.Min(w / 2, h));
		_ = sb.Append("<path d=\"M").Append(x).Append(',').Append(y + h)
			.Append(" L").Append(x).Append(',').Append(y + r)
			.Append(" Q").Append(x).Append(',').Append(y).Append(' ').Append(x + r).Append(',').Append(y)
			.Append(" L").Append(x + w - r).Append(',').Append(y)
			.Append(" Q").Append(x + w).Append(',').Append(y).Append(' ').Append(x + w).Append(',').Append(y + r)
			.Append(" L").Append(x + w).Append(',').Append(y + h)
			.Append(" Z\" fill=\"").Append(fill).Append("\" />");
	}

	internal static string Num(double v) => Math.Round(v, 3).ToString(CultureInfo.InvariantCulture);
}
