namespace Mermaider.Models;

public sealed record GitGraph
{
	public required IReadOnlyList<GitAction> Actions { get; init; }
	public GitGraphOrientation Orientation { get; init; } = GitGraphOrientation.LR;
}

public enum GitGraphOrientation { LR, TB, BT }

public abstract record GitAction;

public sealed record GitCommitAction : GitAction
{
	public string? Id { get; init; }
	public GitCommitType Type { get; init; } = GitCommitType.Normal;
	public string? Tag { get; init; }
}

public enum GitCommitType { Normal, Highlight, Reverse }

public sealed record GitBranchAction(string Name, int? Order = null) : GitAction;
public sealed record GitCheckoutAction(string Name) : GitAction;
public sealed record GitMergeAction(string Name, string? Id = null, string? Tag = null, GitCommitType Type = GitCommitType.Normal) : GitAction;
public sealed record GitCherryPickAction(string Id, string? Parent = null) : GitAction;
