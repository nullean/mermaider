using Mermaider.Models;

namespace Mermaider.Rendering;

internal static class EnumNames
{
	internal static string ToLower(this EdgeStyle style) => style switch
	{
		EdgeStyle.Solid => "solid",
		EdgeStyle.Dotted => "dotted",
		EdgeStyle.Thick => "thick",
		EdgeStyle.Invisible => "invisible",
		_ => style.ToString().ToLowerInvariant(),
	};

	internal static string ToLower(this NodeShape shape) => shape switch
	{
		NodeShape.Rectangle => "rectangle",
		NodeShape.Rounded => "rounded",
		NodeShape.Stadium => "stadium",
		NodeShape.Diamond => "diamond",
		NodeShape.Circle => "circle",
		NodeShape.DoubleCircle => "doublecircle",
		NodeShape.Subroutine => "subroutine",
		NodeShape.Hexagon => "hexagon",
		NodeShape.Cylinder => "cylinder",
		NodeShape.Asymmetric => "asymmetric",
		NodeShape.Trapezoid => "trapezoid",
		NodeShape.TrapezoidAlt => "trapezoidalt",
		NodeShape.StateStart => "statestart",
		NodeShape.StateEnd => "stateend",
		NodeShape.ForkJoin => "forkjoin",
		_ => shape.ToString().ToLowerInvariant(),
	};

	internal static string ToLower(this ErCardinality c) => c switch
	{
		ErCardinality.One => "one",
		ErCardinality.Many => "many",
		ErCardinality.ZeroOne => "zeroone",
		ErCardinality.ZeroMany => "zeromany",
		_ => c.ToString().ToLowerInvariant(),
	};

	internal static string ToLower(this ClassRelationType t) => t switch
	{
		ClassRelationType.Inheritance => "inheritance",
		ClassRelationType.Composition => "composition",
		ClassRelationType.Aggregation => "aggregation",
		ClassRelationType.Association => "association",
		ClassRelationType.Dependency => "dependency",
		ClassRelationType.Realization => "realization",
		ClassRelationType.Lollipop => "lollipop",
		_ => t.ToString().ToLowerInvariant(),
	};

	internal static string ToLower(this SequenceBlockType t) => t switch
	{
		SequenceBlockType.Loop => "loop",
		SequenceBlockType.Alt => "alt",
		SequenceBlockType.Opt => "opt",
		SequenceBlockType.Par => "par",
		SequenceBlockType.Critical => "critical",
		SequenceBlockType.Break => "break",
		_ => t.ToString().ToLowerInvariant(),
	};

	internal static string ToLower(this SequenceNotePosition p) => p switch
	{
		SequenceNotePosition.Left => "left",
		SequenceNotePosition.Right => "right",
		SequenceNotePosition.Over => "over",
		_ => p.ToString().ToLowerInvariant(),
	};
}
