namespace Mermaider.Models;

public sealed record TreemapDiagram
{
	public required IReadOnlyList<TreemapNode> Roots { get; init; }
}

public sealed record TreemapNode
{
	public required string Label { get; init; }
	public double? Value { get; init; }
	public required IReadOnlyList<TreemapNode> Children { get; init; }

	public double ComputedValue => Value ?? SumChildren();

	private double SumChildren()
	{
		var total = 0.0;
		foreach (var child in Children)
			total += child.ComputedValue;
		return total;
	}
}
