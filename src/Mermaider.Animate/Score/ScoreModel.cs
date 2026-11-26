namespace Mermaider.Animate.Score;

/// <summary>A semantic colour role, resolved through the diagram's role colours.</summary>
internal enum Role
{
	Ok,
	Fail,
	Warn,
	Info,
}

internal enum Speed
{
	Normal,
	Slow,
	Fast,
}

/// <summary>What happens to a token before it arrives.</summary>
internal enum Fate
{
	Arrives,
	Lost,
	Stalls,
}

internal enum StreamRate
{
	Trickle,
	Steady,
	Busy,
}

/// <summary>A node, service, actor or (with <see cref="IsGroup"/>) a group, named by its diagram id.</summary>
internal sealed record TargetRef(string Id, bool IsGroup, int Line)
{
	/// <summary>Named by its label in quotes (<c>"Code"</c>), for elements whose source gives them no id.</summary>
	public bool ByLabel { get; init; }

	public override string ToString() => IsGroup ? $"group {Id}" : ByLabel ? $"\"{Id}\"" : Id;
}

/// <summary>
/// An edge named by its endpoints; <see cref="Index"/> picks one of several parallel edges (1-based). A sequence message
/// is named by its number alone (<c>#3</c>): <see cref="IsMessage"/>, with the number in <see cref="Index"/>.
/// </summary>
internal sealed record EdgeRef(string From, string To, int? Index, int Line)
{
	public bool IsMessage => From == "#";

	public static EdgeRef Message(int number, int line) => new("#", "#", number, line);

	public override string ToString() => IsMessage ? $"#{Index}" : Index is { } i ? $"{From} -> {To} #{i}" : $"{From} -> {To}";
}

internal abstract record Statement(int Line)
{
	/// <summary>Starts together with the statement before it instead of after it.</summary>
	public bool With { get; init; }
}

internal sealed record ShowStatement(IReadOnlyList<TargetRef> Targets, IReadOnlyList<EdgeRef> Edges, bool Each, int Line) : Statement(Line);

internal sealed record HideStatement(IReadOnlyList<TargetRef> Targets, IReadOnlyList<EdgeRef> Edges, bool Each, int Line) : Statement(Line);

internal sealed record FocusStatement(IReadOnlyList<TargetRef> Targets, bool Close, bool All, int Line) : Statement(Line);

internal sealed record PulseStatement(IReadOnlyList<TargetRef> Targets, bool Each, int Line) : Statement(Line);

internal sealed record MarkStatement(IReadOnlyList<TargetRef> Targets, IReadOnlyList<EdgeRef> Edges, Role? Role, int Line) : Statement(Line);

internal sealed record DrawStatement(IReadOnlyList<EdgeRef> Edges, bool Each, int Line) : Statement(Line);

internal sealed record EraseStatement(IReadOnlyList<EdgeRef> Edges, bool Each, int Line) : Statement(Line);

internal sealed record CutStatement(IReadOnlyList<EdgeRef> Edges, int Line) : Statement(Line);

internal sealed record RestoreStatement(IReadOnlyList<EdgeRef> Edges, int Line) : Statement(Line);

internal sealed record SendStatement(
	string? Token,
	string From,
	IReadOnlyList<string> To,
	string? Label,
	Role? Role,
	string? Becomes,
	Speed Speed,
	Fate Fate,
	bool Each,
	int Line
) : Statement(Line);

/// <summary>What a highlight emphasises.</summary>
internal enum HighlightKind
{
	/// <summary>Nodes: a soft ring that breathes, in the role's colour.</summary>
	Node,

	/// <summary>A route that stays lit, a sheen running along it.</summary>
	Path,

	/// <summary>A route whose edges flow in the direction of travel.</summary>
	Flow,
}

/// <summary>
/// A standing emphasis for the whole animation, with or without steps: <c>highlight api as fail</c>,
/// <c>highlight path a -> b -> c as ok</c>, <c>highlight flow a -> b -> c</c>.
/// </summary>
internal sealed record HighlightStatement(HighlightKind Kind, IReadOnlyList<TargetRef> Targets, IReadOnlyList<string> Route, Role? Role, int Line) : Statement(
	Line
)
{
	/// <summary>A mark the diagram picks by its data: <c>biggest</c> or <c>lowest</c>.</summary>
	public string? Pick { get; init; }
}

internal sealed record StreamStatement(IReadOnlyList<string> Path, StreamRate Rate, bool Always, int Line) : Statement(Line);

internal sealed record TourStatement(string? Token, IReadOnlyList<string> Path, string? Label, Role? Role, int Line) : Statement(Line);

internal sealed record RunStatement(IReadOnlyList<string> Path, int Line) : Statement(Line);

internal sealed record TryStatement(string From, string To, string? Reason, int Line) : Statement(Line);

internal sealed record PingStatement(string From, string To, string? Request, string? Reply, int Line) : Statement(Line);

internal sealed record CaptionStatement(string Text, int Line) : Statement(Line);

/// <summary>A queue or buffer filling: <see cref="Count"/> of <see cref="Capacity"/> slots (capacity from an earlier fill when null).</summary>
internal sealed record FillStatement(TargetRef Target, int Count, int? Capacity, int Line) : Statement(Line);

/// <summary>A short label pinned above a node until cleared (<see cref="Text"/> null).</summary>
/// <summary>A number on a node rolls from one value to another: <c>count api 38 -&gt; 1200 "ms"</c>.</summary>
internal sealed record CountStatement(TargetRef Target, double? From, double To, string? Unit, Role? Role, int Line) : Statement(Line);

internal sealed record BadgeStatement(IReadOnlyList<TargetRef> Targets, string? Text, Role? Role, int Line) : Statement(Line);

/// <summary>A node waiting with no answer: the wait itself is shown.</summary>
internal sealed record HangStatement(IReadOnlyList<TargetRef> Targets, int Line) : Statement(Line);

/// <summary>A bracket beside a run of sequence messages, with a label: what that span costs or means.</summary>
internal sealed record NoteStatement(int First, int Last, string Text, Role? Role, int Line) : Statement(Line);

internal sealed record BuildStatement(int Line) : Statement(Line);

/// <summary>Cards slide into another kanban column; the cards below them in their old column close the gap.</summary>
internal sealed record MoveStatement(IReadOnlyList<TargetRef> Targets, string Column, bool Each, int Line) : Statement(Line);

/// <summary>A bar takes a new value.</summary>
internal sealed record SetStatement(TargetRef Target, double Value, int Line) : Statement(Line);

/// <summary>Every flow between the nodes of a path is lit.</summary>
internal sealed record TraceStatement(IReadOnlyList<string> Path, int Line) : Statement(Line);

/// <summary>A tree node's subtree grows out of it, level by level.</summary>
internal sealed record ExpandStatement(IReadOnlyList<TargetRef> Targets, int Line) : Statement(Line);

/// <summary>A gantt chart's playhead moves on, to a date or to the end, growing every task it passes.</summary>
internal sealed record SweepStatement(DateTime? To, int Line) : Statement(Line);

/// <summary>Plays sequence messages; null bounds mean from the first or to the last.</summary>
internal sealed record PlayStatement(int? First, int? Last, int Line) : Statement(Line);

internal sealed record TokenDeclaration(string Name, string Label, Role? Role, int Line);

internal sealed record ScoreStep(string? Caption, IReadOnlyList<Statement> Statements, int Line);

/// <summary>One ending of a score: its own steps, played after the steps every ending shares.</summary>
internal sealed record ScoreScenario(string Name, IReadOnlyList<ScoreStep> Steps, int Line)
{
	/// <summary>How the ending turns out (<c>as fail</c>), shown in its colour where the player offers it.</summary>
	public Role? Role { get; init; }
}

internal sealed record ScoreDocument(
	PlaybackMode? Mode,
	MotionPreset? Motion,
	IReadOnlyList<ScoreStep> Steps,
	IReadOnlyDictionary<string, TokenDeclaration> Tokens,
	int Line
)
{
	/// <summary>
	/// The score's endings, in order; empty for a score with one story. <see cref="Steps"/> are then the steps every
	/// ending begins with.
	/// </summary>
	public IReadOnlyList<ScoreScenario> Scenarios { get; init; } = [];

	/// <summary>Whether the player lists the steps, when the header says (<c>contents</c>, <c>compact</c>).</summary>
	public PlayerContents? Contents { get; init; }

	/// <summary>Standing emphases for the whole animation, in the order written.</summary>
	public IReadOnlyList<HighlightStatement> Highlights { get; init; } = [];

	/// <summary>A bare <c>animate</c>: the diagram type's default animation plays.</summary>
	public bool IsDefault => Steps.Count == 0 && Scenarios.Count == 0 && Highlights.Count == 0;

	/// <summary>The score of one ending: the shared steps, then its own, as a score with one story.</summary>
	public ScoreDocument For(ScoreScenario scenario) => this with { Steps = [.. Steps, .. scenario.Steps], Scenarios = [] };
}
