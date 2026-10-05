using Mermaider.Models;

namespace Mermaider.Theming;

/// <summary>How a box (node, card, cell) is filled.</summary>
internal enum FillKind
{
	/// <summary>Family top → bottom gradient (flat stage when gradients are off).</summary>
	Gradient,

	/// <summary>The page background, so the outline carries the shape (transparent-safe).</summary>
	Knockout,

	/// <summary>The family's soft stage, no outline.</summary>
	Soft,
}

/// <summary>How a container (subgraph, namespace, frame, column, section, boundary) is drawn.</summary>
internal enum ContainerKind
{
	/// <summary>Tinted body (a lighter shade of the container's colour into its tint) and a 28px header strip with the title in the family ink.</summary>
	Strip,

	/// <summary>No fill, dashed outline, a caps tab knocked out of the top border.</summary>
	Tab,

	/// <summary>Soft filled block without border; the title is a chip with an accent dot.</summary>
	Chip,
}

/// <summary>How an entity / class / requirement table is drawn.</summary>
internal enum EntityKind
{
	/// <summary>Tinted header band, hairline rows, centred name.</summary>
	Band,

	/// <summary>Accent rule on top, left-aligned name, hairlines, no fill.</summary>
	Plain,

	/// <summary>Filled card, soft header, zebra rows, no outline.</summary>
	Card,
}

/// <summary>How an edge label is drawn.</summary>
internal enum LabelKind
{
	/// <summary>Outlined pill on the page background.</summary>
	Pill,

	/// <summary>Plain text with a page-background halo.</summary>
	Halo,

	/// <summary>Filled badge chip, no border.</summary>
	Chip,
}

/// <summary>How a note is drawn.</summary>
internal enum NoteKind
{
	/// <summary>Accent-tint card with a 3px accent rail on the left.</summary>
	Rail,

	/// <summary>Four accent registration ticks at the corners, no fill.</summary>
	Ticks,

	/// <summary>Accent-band sticker with a quote mark.</summary>
	Sticker,
}

/// <summary>How a small badge (key, count, tag) is drawn.</summary>
internal enum BadgeKind
{
	/// <summary>Family band chip with ink text.</summary>
	Band,

	/// <summary><c>[bracketed]</c> ink text, no chip.</summary>
	Bracket,

	/// <summary>Solid soft chip.</summary>
	Chip,
}

/// <summary>The accent mark in front of a diagram title.</summary>
internal enum TitleMark
{
	Pip,
	Rule,
	Disc,
}

/// <summary>Shape of terminals (state start/end, data points, legend swatches).</summary>
internal enum TerminalKind
{
	Dot,
	Square,
	Disc,
}

/// <summary>Arrowhead and marker drawing style.</summary>
internal enum MarkerKind
{
	/// <summary>Standard filled triangle in the line colour.</summary>
	Filled,

	/// <summary>Narrow open heads drawn in the accent.</summary>
	Thin,

	/// <summary>Chunky, round-joined heads in the line colour.</summary>
	Chunky,
}

/// <summary>How chart marks (bars, slices, series) are drawn.</summary>
internal enum ChartMarkKind
{
	/// <summary>Solid palette fills, gradient bars.</summary>
	Solid,

	/// <summary>Area tint plus palette outline, square markers.</summary>
	Outline,

	/// <summary>Fat marks: donut pie, wide rounded bars, smooth thick lines.</summary>
	Fat,
}

/// <summary>
/// Every knob a renderer may consult. Three presets (<see cref="Quiet"/>, <see cref="Blueprint"/>, <see cref="Tonal"/>)
/// share geometry, so layout never depends on the style; only paint does.
/// </summary>
internal sealed record StyleSpec
{
	internal required DiagramStyle Style { get; init; }
	internal required double NodeRadius { get; init; }
	internal required double ContainerRadius { get; init; }

	/// <summary>Node outline width; 0 means no outline (tone carries the edge).</summary>
	internal required double OutlineWidth { get; init; }

	internal required FillKind Fill { get; init; }
	internal required ContainerKind Container { get; init; }
	internal required EntityKind Entity { get; init; }
	internal required double EdgeWidth { get; init; }

	/// <summary>Radius of rounded bends on orthogonal edges (0 = square corners).</summary>
	internal required double EdgeBendRadius { get; init; }

	internal required MarkerKind Marker { get; init; }
	internal required LabelKind Label { get; init; }
	internal required NoteKind Note { get; init; }
	internal required BadgeKind Badge { get; init; }
	internal required TitleMark TitleMark { get; init; }
	internal required TerminalKind Terminal { get; init; }
	internal required ChartMarkKind ChartMarks { get; init; }

	/// <summary>Set caption / tag / meta text (the xs tier) in the mono font.</summary>
	internal required bool MonoXs { get; init; }

	internal required int LabelWeight { get; init; }
	internal required int HeadingWeight { get; init; }

	/// <summary>Whether nodes / containers cast shadows at all (the elevation input scales them).</summary>
	internal required bool NodeShadow { get; init; }

	internal required bool ContainerShadow { get; init; }

	/// <summary>Containers use dashed outlines (Blueprint tab).</summary>
	internal bool DashedContainers => Container == ContainerKind.Tab;

	internal static readonly StyleSpec Quiet = new()
	{
		Style = DiagramStyle.Quiet,
		NodeRadius = 8,
		ContainerRadius = 12,
		OutlineWidth = 1.25,
		Fill = FillKind.Gradient,
		Container = ContainerKind.Strip,
		Entity = EntityKind.Band,
		EdgeWidth = 1.5,
		EdgeBendRadius = 8,
		Marker = MarkerKind.Filled,
		Label = LabelKind.Pill,
		Note = NoteKind.Rail,
		Badge = BadgeKind.Band,
		TitleMark = TitleMark.Pip,
		Terminal = TerminalKind.Dot,
		ChartMarks = ChartMarkKind.Solid,
		MonoXs = false,
		LabelWeight = 500,
		HeadingWeight = 600,
		NodeShadow = true,
		ContainerShadow = true,
	};

	internal static readonly StyleSpec Blueprint = new()
	{
		Style = DiagramStyle.Blueprint,
		NodeRadius = 2,
		ContainerRadius = 4,
		OutlineWidth = 1.25,
		Fill = FillKind.Knockout,
		Container = ContainerKind.Tab,
		Entity = EntityKind.Plain,
		EdgeWidth = 1,
		EdgeBendRadius = 0,
		Marker = MarkerKind.Thin,
		Label = LabelKind.Halo,
		Note = NoteKind.Ticks,
		Badge = BadgeKind.Bracket,
		TitleMark = TitleMark.Rule,
		Terminal = TerminalKind.Square,
		ChartMarks = ChartMarkKind.Outline,
		MonoXs = true,
		LabelWeight = 500,
		HeadingWeight = 600,
		NodeShadow = false,
		ContainerShadow = false,
	};

	internal static readonly StyleSpec Tonal = new()
	{
		Style = DiagramStyle.Tonal,
		NodeRadius = 14,
		ContainerRadius = 22,
		OutlineWidth = 0,
		Fill = FillKind.Soft,
		Container = ContainerKind.Chip,
		Entity = EntityKind.Card,
		EdgeWidth = 2,
		EdgeBendRadius = 16,
		Marker = MarkerKind.Chunky,
		Label = LabelKind.Chip,
		Note = NoteKind.Sticker,
		Badge = BadgeKind.Chip,
		TitleMark = TitleMark.Disc,
		Terminal = TerminalKind.Disc,
		ChartMarks = ChartMarkKind.Fat,
		MonoXs = false,
		LabelWeight = 600,
		HeadingWeight = 700,
		NodeShadow = true,
		ContainerShadow = false,
	};

	internal static StyleSpec For(DiagramStyle style) => style switch
	{
		DiagramStyle.Blueprint => Blueprint,
		DiagramStyle.Tonal => Tonal,
		_ => Quiet,
	};
}

/// <summary>The design inputs that are not colours: the preset plus the gradient, tint and elevation knobs.</summary>
internal sealed record DesignInputs(StyleSpec Spec, bool Gradient, double Tint, int Elevation)
{
	internal static readonly DesignInputs Default = new(StyleSpec.Quiet, Gradient: true, Tint: 1.0, Elevation: 1);

	internal static DesignInputs From(RenderOptions? options)
	{
		if (options is null)
			return Default;

		var tint = options.Tint is { } t && double.IsFinite(t) ? Math.Clamp(t, 0.5, 1.5) : 1.0;
		var elevation = Math.Clamp(options.Elevation ?? 1, 0, 2);
		return new DesignInputs(StyleSpec.For(options.Style), options.Gradient, tint, elevation);
	}
}
