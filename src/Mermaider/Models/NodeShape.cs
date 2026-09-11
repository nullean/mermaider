namespace Mermaider.Models;

/// <summary>Visual shape of a node in a Mermaid diagram.</summary>
public enum NodeShape
{
	Rectangle,
	Rounded,
	Diamond,
	Stadium,
	Circle,
	Subroutine,
	DoubleCircle,
	Hexagon,
	Cylinder,
	Asymmetric,
	Trapezoid,
	TrapezoidAlt,

	/// <summary><c>[/text/]</c>, leaning right.</summary>
	Parallelogram,

	/// <summary><c>[\text\]</c>, leaning left.</summary>
	ParallelogramAlt,
	StateStart,
	StateEnd,
	ForkJoin
}
