using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Git graph on the design system: each branch lane is a cluster family on the shared edge language (lane line in the
/// family stroke at the preset edge width; forks and merges as orthogonal connectors with the preset bend radius).
/// Commits are points, merges rings; highlight commits and the checked-out HEAD use the accent. Branch names are family
/// badges, tags accent badges, commit ids meta text.
/// </summary>
internal static class GitGraphSvgRenderer
{
	private const double Pad = 24;
	private const double MinCommitSpacing = 64;
	private const double MaxCommitSpacing = 140;
	private const double LaneSpacing = 56;
	private const double LabelColumnGap = 32;
	private const double TagSpace = 28;
	private const double CommitR = 4.5;
	private const double MergeR = 5.5;
	private const double HighlightR = 6;
	private const double LabelOffsetY = 18;
	private const double TagOffsetY = -20;

	internal static string Render(GitGraph graph, SvgRenderContext context)
	{
		var sb = RenderToBuilder(graph, context);
		try
		{
			return sb.ToString();
		}
		finally
		{
			_ = sb.Clear();
			SharedStringBuilderPool.Instance.Return(sb);
		}
	}

	internal static StringBuilder RenderToBuilder(GitGraph graph, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var ds = DesignSystem.For(context);

		var simulation = Simulate(graph);
		if (simulation.Commits.Count == 0)
		{
			StyleBlock.AppendSvgOpenTag(sb, 200, 100, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
			StyleBlock.AppendStyleBlock(sb, context.Styles);
			ds.Close(sb);
			return sb;
		}

		// geometry (preset-independent: xs text measured at its widest, mono or proportional)
		var maxLane = 0;
		var maxLabel = 0.0;
		var hasTags = false;
		foreach (var c in simulation.Commits)
		{
			maxLane = Math.Max(maxLane, c.Lane);
			if (c.Label is { Length: > 0 } label)
				maxLabel = Math.Max(maxLabel, DesignSystem.XsWidth(label, 400));
			if (c.Tag is { Length: > 0 } tag)
			{
				hasTags = true;
				maxLabel = Math.Max(maxLabel, DesignSystem.XsWidth("[" + tag + "]", 600) + 12);
			}
		}

		var labelColumn = 0.0;
		foreach (var b in simulation.Branches)
			labelColumn = Math.Max(labelColumn, DesignSystem.XsWidth("[" + b.Name + "]", 600) + 12);

		var spacing = Math.Clamp(maxLabel + 16, MinCommitSpacing, MaxCommitSpacing);
		var geo = new Geometry(Pad + labelColumn + LabelColumnGap, Pad + (hasTags ? TagSpace : 12), spacing);
		var lastX = geo.X(simulation.Commits.Count - 1);
		var width = lastX + Math.Max(spacing / 2, (maxLabel / 2) + 4) + Pad;
		var height = geo.Y(maxLane) + LabelOffsetY + 12 + Pad;

		StyleBlock.AppendSvgOpenTag(sb, width, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles);

		foreach (var branch in simulation.Branches)
		{
			_ = sb.Append("\n<g class=\"gitgraph-branch\" data-branch=\"");
			Text.MultilineUtils.AppendEscapedAttr(sb, branch.Name.AsSpan());
			_ = sb.Append("\">\n  ");
			_ = ds.AppendBadge(sb, Pad, geo.Y(branch.Lane), branch.Name, ds.Cluster(branch.Lane));
			_ = sb.Append("\n</g>");
		}

		// fork / merge connectors under the lane lines, so a lane reads as one continuous line
		foreach (var link in simulation.Links)
		{
			if (simulation.Commits[link.FromIndex].Lane != simulation.Commits[link.ToIndex].Lane)
				AppendLink(sb, ds, link, simulation, geo, context.EdgeRadius);
		}

		foreach (var link in simulation.Links)
		{
			if (simulation.Commits[link.FromIndex].Lane == simulation.Commits[link.ToIndex].Lane)
				AppendLink(sb, ds, link, simulation, geo, context.EdgeRadius);
		}

		for (var i = 0; i < simulation.Commits.Count; i++)
			AppendCommit(sb, ds, simulation.Commits[i], geo, i == simulation.HeadIndex);

		ds.Close(sb);
		return sb;
	}

	private readonly record struct Geometry(double Left, double Top, double Spacing)
	{
		internal double X(int position) => Left + (position * Spacing);

		internal double Y(int lane) => Top + (lane * LaneSpacing);
	}

	private static void AppendLink(StringBuilder sb, DesignSystem ds, CommitLink link, SimulationResult sim, Geometry geo, double radius)
	{
		var from = sim.Commits[link.FromIndex];
		var to = sim.Commits[link.ToIndex];
		var x1 = geo.X(from.Position);
		var y1 = geo.Y(from.Lane);
		var x2 = geo.X(to.Position);
		var y2 = geo.Y(to.Lane);
		var family = ds.Cluster(link.Lane);

		_ = sb.Append("\n<path d=\"");
		if (Math.Abs(y1 - y2) < 0.01)
		{
			_ = sb.Append('M').Append(DesignSystem.Num(x1)).Append(',').Append(DesignSystem.Num(y1))
				.Append(" L").Append(DesignSystem.Num(x2)).Append(',').Append(DesignSystem.Num(y2));
		}
		else
		{
			// forks and merges both turn half a step before the commit they reach, so a branch visibly drops off its parent
			// lane right where its first commit is (and never shares a vertical with a sibling branch forked earlier)
			var turnX = Math.Max(x1, x2 - (geo.Spacing / 2));
			Point[] points = [new(x1, y1), new(turnX, y1), new(turnX, y2), new(x2, y2)];
			SvgRenderer.BuildOrthogonalPath(sb, points, radius);
		}

		_ = sb.Append("\" class=\"gitgraph-link\" data-kind=\"").Append(link.Kind.ToString().ToLowerInvariant())
			.Append("\" fill=\"none\" stroke=\"").Append(family.Stroke)
			.Append("\" stroke-width=\"").Append(ds.EdgeWidth).Append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\" />");
	}

	private static void AppendCommit(StringBuilder sb, DesignSystem ds, CommitInfo commit, Geometry geo, bool isHead)
	{
		var cx = geo.X(commit.Position);
		var cy = geo.Y(commit.Lane);
		var family = ds.Cluster(commit.Lane);
		var color = family.Stroke;
		var square = ds.Spec.Terminal == TerminalKind.Square;

		_ = sb.Append("\n<g class=\"commit\" data-type=\"").Append(commit.Type.ToString().ToLowerInvariant())
			.Append(commit.IsMerge ? "\" data-merge=\"true" : "")
			.Append(isHead ? "\" data-head=\"true" : "")
			.Append("\">\n  ");

		switch (commit.Type)
		{
			case GitCommitType.Highlight:
				ds.AppendPoint(sb, cx, cy, ColorFamily.AccentBase, HighlightR);
				break;
			case GitCommitType.Reverse:
				AppendRing(sb, cx, cy, MergeR + 1, color, square);
				const double k = 3;
				_ = sb.Append("\n  <line x1=\"").Append(DesignSystem.Num(cx - k)).Append("\" y1=\"").Append(DesignSystem.Num(cy - k))
					.Append("\" x2=\"").Append(DesignSystem.Num(cx + k)).Append("\" y2=\"").Append(DesignSystem.Num(cy + k))
					.Append("\" stroke=\"").Append(color).Append("\" stroke-width=\"1.5\" stroke-linecap=\"round\" />");
				_ = sb.Append("\n  <line x1=\"").Append(DesignSystem.Num(cx + k)).Append("\" y1=\"").Append(DesignSystem.Num(cy - k))
					.Append("\" x2=\"").Append(DesignSystem.Num(cx - k)).Append("\" y2=\"").Append(DesignSystem.Num(cy + k))
					.Append("\" stroke=\"").Append(color).Append("\" stroke-width=\"1.5\" stroke-linecap=\"round\" />");
				break;
			default:
				if (commit.IsMerge)
					AppendRing(sb, cx, cy, MergeR, color, square);
				else
					ds.AppendPoint(sb, cx, cy, color, CommitR);
				break;
		}

		if (isHead)
		{
			// the checked-out HEAD: an accent ring, the "look here" mark
			const double hr = 10;
			_ = sb.Append("\n  ");
			_ = square
				? sb.Append("<rect x=\"").Append(DesignSystem.Num(cx - hr)).Append("\" y=\"").Append(DesignSystem.Num(cy - hr))
					.Append("\" width=\"").Append(DesignSystem.Num(hr * 2)).Append("\" height=\"").Append(DesignSystem.Num(hr * 2))
					.Append("\" fill=\"none\" stroke=\"").Append(ColorFamily.AccentBase).Append("\" stroke-width=\"").Append(DesignSystem.Num(DesignSystem.RingWidth)).Append("\" />")
				: sb.Append("<circle cx=\"").Append(DesignSystem.Num(cx)).Append("\" cy=\"").Append(DesignSystem.Num(cy))
					.Append("\" r=\"").Append(DesignSystem.Num(hr)).Append("\" fill=\"none\" stroke=\"").Append(ColorFamily.AccentBase)
					.Append("\" stroke-width=\"").Append(DesignSystem.Num(DesignSystem.RingWidth)).Append("\" />");
		}

		if (commit.Label is { Length: > 0 })
		{
			_ = sb.Append("\n  ");
			ds.AppendText(sb, commit.Label, cx, cy + LabelOffsetY, TypeRole.Meta, anchor: "middle");
		}

		if (commit.Tag is { Length: > 0 })
		{
			_ = sb.Append("\n  ");
			_ = ds.AppendBadge(sb, cx, cy + TagOffsetY, commit.Tag, ds.Accent, anchor: "middle");
		}

		_ = sb.Append("\n</g>");
	}

	/// <summary>A hollow commit (merge / revert): page-coloured core inside a family ring; square in the Blueprint preset.</summary>
	private static void AppendRing(StringBuilder sb, double cx, double cy, double r, string color, bool square) => _ = square
			? sb.Append("<rect x=\"").Append(DesignSystem.Num(cx - r)).Append("\" y=\"").Append(DesignSystem.Num(cy - r))
				.Append("\" width=\"").Append(DesignSystem.Num(r * 2)).Append("\" height=\"").Append(DesignSystem.Num(r * 2))
				.Append("\" fill=\"var(--bg)\" stroke=\"").Append(color).Append("\" stroke-width=\"2\" />")
			: sb.Append("<circle cx=\"").Append(DesignSystem.Num(cx)).Append("\" cy=\"").Append(DesignSystem.Num(cy))
				.Append("\" r=\"").Append(DesignSystem.Num(r)).Append("\" fill=\"var(--bg)\" stroke=\"").Append(color).Append("\" stroke-width=\"2.5\" />");

	private enum LinkKind
	{
		Lane,
		Fork,
		Merge,
	}

	private sealed record CommitInfo(int Position, int Lane, string? Label, string? Tag, GitCommitType Type, bool IsMerge);

	/// <summary>A connector between two commits, painted in the family of <paramref name="Lane"/>.</summary>
	private sealed record CommitLink(int FromIndex, int ToIndex, int Lane, LinkKind Kind);

	private sealed record BranchInfo(string Name, int Lane);

	private sealed record SimulationResult(List<CommitInfo> Commits, List<CommitLink> Links, List<BranchInfo> Branches, int HeadIndex);

	private static SimulationResult Simulate(GitGraph graph)
	{
		var commits = new List<CommitInfo>();
		var links = new List<CommitLink>();
		var branches = new Dictionary<string, int>();
		var branchHeads = new Dictionary<string, int>();
		var forkPoints = new Dictionary<string, int>();
		var branchList = new List<BranchInfo>();
		var nextLane = 0;
		var position = 0;
		var currentBranch = "main";

		branches["main"] = nextLane++;
		branchList.Add(new BranchInfo("main", 0));

		var commitCounter = 0;

		// links the new commit at idx to its parent on the current branch: the branch head, or, for a branch's first commit,
		// the commit it was branched from (a fork connector in the branch's family)
		void LinkToParent(int idx, int lane)
		{
			if (branchHeads.TryGetValue(currentBranch, out var prevIdx))
				links.Add(new CommitLink(prevIdx, idx, lane, LinkKind.Lane));
			else if (forkPoints.TryGetValue(currentBranch, out var forkIdx))
				links.Add(new CommitLink(forkIdx, idx, lane, commits[forkIdx].Lane == lane ? LinkKind.Lane : LinkKind.Fork));
		}

		foreach (var action in graph.Actions)
		{
			if (action is GitBranchAction branch)
			{
				if (!branches.ContainsKey(branch.Name))
				{
					var lane = nextLane++;
					branches[branch.Name] = lane;
					branchList.Add(new BranchInfo(branch.Name, lane));
					if (branchHeads.TryGetValue(currentBranch, out var forkIdx))
						forkPoints[branch.Name] = forkIdx;
				}
				currentBranch = branch.Name;
			}
			else if (action is GitCheckoutAction checkout)
			{
				currentBranch = checkout.Name;
			}
			else if (action is GitCommitAction commit)
			{
				var lane = branches.GetValueOrDefault(currentBranch, 0);
				var label = commit.Id ?? commitCounter.ToString(CultureInfo.InvariantCulture);
				commitCounter++;

				var idx = commits.Count;
				commits.Add(new CommitInfo(position, lane, label, commit.Tag, commit.Type, false));
				LinkToParent(idx, lane);
				branchHeads[currentBranch] = idx;
				position++;
			}
			else if (action is GitMergeAction merge)
			{
				var lane = branches.GetValueOrDefault(currentBranch, 0);
				commitCounter++;

				var idx = commits.Count;
				commits.Add(new CommitInfo(position, lane, merge.Id, merge.Tag, merge.Type, true));
				LinkToParent(idx, lane);

				if (branchHeads.TryGetValue(merge.Name, out var mergeFromIdx))
					links.Add(new CommitLink(mergeFromIdx, idx, commits[mergeFromIdx].Lane, LinkKind.Merge));

				branchHeads[currentBranch] = idx;
				position++;
			}
			else if (action is GitCherryPickAction cherryPick)
			{
				var lane = branches.GetValueOrDefault(currentBranch, 0);
				commitCounter++;

				var idx = commits.Count;
				commits.Add(new CommitInfo(position, lane, cherryPick.Id, null, GitCommitType.Normal, false));
				LinkToParent(idx, lane);
				branchHeads[currentBranch] = idx;
				position++;
			}
		}

		var head = branchHeads.GetValueOrDefault(currentBranch, -1);
		return new SimulationResult(commits, links, branchList, head);
	}
}
