namespace Mermaider.Models;

/// <summary>Parsed sequence diagram — logical structure from mermaid text.</summary>
public sealed record SequenceDiagram
{
	public required IReadOnlyList<SequenceActor> Actors { get; init; }
	public required IReadOnlyList<SequenceMessage> Messages { get; init; }
	public required IReadOnlyList<SequenceBlock> Blocks { get; init; }
	public required IReadOnlyList<SequenceNote> Notes { get; init; }
	public IReadOnlyList<SequenceBox> Boxes { get; init; } = [];
	public IReadOnlyList<SequenceCreate> Creates { get; init; } = [];
	public IReadOnlyList<SequenceDestroy> Destroys { get; init; } = [];
}

public readonly record struct SequenceActor(string Id, string Label, SequenceActorType Type);

public enum SequenceActorType { Participant, Actor }

public sealed record SequenceMessage(
	string From,
	string To,
	string Label,
	SequenceLineStyle LineStyle,
	SequenceArrowHead ArrowHead,
	bool Activate = false,
	bool Deactivate = false,
	bool Bidirectional = false
);

public enum SequenceLineStyle { Solid, Dashed }
public enum SequenceArrowHead { Filled, Open }

public sealed record SequenceBlock
{
	public required SequenceBlockType Type { get; init; }
	public required string Label { get; init; }
	public required int StartIndex { get; init; }
	public required int EndIndex { get; init; }
	public required IReadOnlyList<SequenceBlockDivider> Dividers { get; init; }
}

public enum SequenceBlockType { Loop, Alt, Opt, Par, Critical, Break, Rect }

public readonly record struct SequenceBlockDivider(int Index, string Label);

public sealed record SequenceNote(
	IReadOnlyList<string> ActorIds,
	string Text,
	SequenceNotePosition Position,
	int AfterIndex
);

public enum SequenceNotePosition { Left, Right, Over }

public sealed record SequenceBox(string? Color, string Title, IReadOnlyList<string> ActorIds);

public sealed record SequenceCreate(string ActorId, int AtMessageIndex);

public sealed record SequenceDestroy(string ActorId, int AtMessageIndex);
