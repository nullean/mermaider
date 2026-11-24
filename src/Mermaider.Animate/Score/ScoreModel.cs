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
	public override string ToString() => IsGroup ? $"group {Id}" : Id;
}

/// <summary>An edge named by its endpoints; <see cref="Index"/> picks one of several parallel edges (1-based).</summary>
internal sealed record EdgeRef(string From, string To, int? Index, int Line)
{
	public override string ToString() => Index is { } i ? $"{From} -> {To} #{i}" : $"{From} -> {To}";
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

internal sealed record StreamStatement(IReadOnlyList<string> Path, StreamRate Rate, bool Always, int Line) : Statement(Line);

internal sealed record TourStatement(string? Token, IReadOnlyList<string> Path, string? Label, Role? Role, int Line) : Statement(Line);

internal sealed record RunStatement(IReadOnlyList<string> Path, int Line) : Statement(Line);

internal sealed record TryStatement(string From, string To, string? Reason, int Line) : Statement(Line);

internal sealed record PingStatement(string From, string To, string? Request, string? Reply, int Line) : Statement(Line);

internal sealed record CaptionStatement(string Text, int Line) : Statement(Line);

internal sealed record BuildStatement(int Line) : Statement(Line);

/// <summary>Plays sequence messages; null bounds mean from the first or to the last.</summary>
internal sealed record PlayStatement(int? First, int? Last, int Line) : Statement(Line);

internal sealed record TokenDeclaration(string Name, string Label, Role? Role, int Line);

internal sealed record ScoreStep(string? Caption, IReadOnlyList<Statement> Statements, int Line);

internal sealed record ScoreDocument(
	PlaybackMode? Mode,
	MotionPreset? Motion,
	IReadOnlyList<ScoreStep> Steps,
	IReadOnlyDictionary<string, TokenDeclaration> Tokens,
	int Line
)
{
	/// <summary>A bare <c>animate</c>: the diagram type's default animation plays.</summary>
	public bool IsDefault => Steps.Count == 0;
}
