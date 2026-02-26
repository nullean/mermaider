namespace Mermaid.Models;

/// <summary>Fully positioned sequence diagram ready for SVG rendering.</summary>
public sealed record PositionedSequenceDiagram
{
	public required double Width { get; init; }
	public required double Height { get; init; }
	public required IReadOnlyList<PositionedSequenceActor> Actors { get; init; }
	public required IReadOnlyList<Lifeline> Lifelines { get; init; }
	public required IReadOnlyList<PositionedSequenceMessage> Messages { get; init; }
	public required IReadOnlyList<Activation> Activations { get; init; }
	public required IReadOnlyList<PositionedSequenceBlock> Blocks { get; init; }
	public required IReadOnlyList<PositionedSequenceNote> Notes { get; init; }
}

public sealed record PositionedSequenceActor
{
	public required string Id { get; init; }
	public required string Label { get; init; }
	public required SequenceActorType Type { get; init; }
	public required double X { get; init; }
	public required double Y { get; init; }
	public required double Width { get; init; }
	public required double Height { get; init; }
}

public readonly record struct Lifeline(string ActorId, double X, double TopY, double BottomY);

public sealed record PositionedSequenceMessage
{
	public required string From { get; init; }
	public required string To { get; init; }
	public required string Label { get; init; }
	public required SequenceLineStyle LineStyle { get; init; }
	public required SequenceArrowHead ArrowHead { get; init; }
	public required double X1 { get; init; }
	public required double X2 { get; init; }
	public required double Y { get; init; }
	public required bool IsSelf { get; init; }
}

public sealed record Activation
{
	public required string ActorId { get; init; }
	public required double X { get; init; }
	public required double TopY { get; init; }
	public required double BottomY { get; init; }
	public required double Width { get; init; }
}

public sealed record PositionedSequenceBlock
{
	public required SequenceBlockType Type { get; init; }
	public required string Label { get; init; }
	public required double X { get; init; }
	public required double Y { get; init; }
	public required double Width { get; init; }
	public required double Height { get; init; }
	public required IReadOnlyList<PositionedBlockDivider> Dividers { get; init; }
}

public readonly record struct PositionedBlockDivider(double Y, string Label);

public sealed record PositionedSequenceNote
{
	public required string Text { get; init; }
	public required double X { get; init; }
	public required double Y { get; init; }
	public required double Width { get; init; }
	public required double Height { get; init; }
	public SequenceNotePosition? Position { get; init; }
	public IReadOnlyList<string>? Actors { get; init; }
}
