using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class GitGraphSvgRenderer
{
	private const double CommitSpacing = 60;
	private const double LaneSpacing = 40;
	private const double CommitRadius = 8;
	private const double LabelOffsetY = 22;
	private const double TagOffsetY = -16;
	private const double BranchLabelWidth = 80;
	private const double LeftPad = 100;
	private const double TopPad = 40;
	private const string LabelFontSize = RenderConstants.FsVar.S;
	private const string TagFontSize = RenderConstants.FsVar.S;
	private const double TagFontSizePx = 11;
	private const string BranchFontSize = RenderConstants.FsVar.S;
	private const double BranchFontSizePx = 12;
	private const double LinkStrokeWidth = 3;


	internal static string Render(GitGraph graph, DiagramColors colors, string font, string? monoFont = null, bool transparent = false, StrictStylingOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = RenderToBuilder(graph, colors, font, monoFont, transparent, strict, accessibility, diagramType);
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

	internal static StringBuilder RenderToBuilder(GitGraph graph, DiagramColors colors, string font, string? monoFont = null, bool transparent = false, StrictStylingOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		var simulation = Simulate(graph, colors);
		if (simulation.Commits.Count == 0)
		{
			StyleBlock.AppendSvgOpenTag(sb, 200, 100, colors, transparent, accessibility, diagramType);
			StyleBlock.AppendStyleBlock(sb, font, strict, monoFont: monoFont);
			_ = sb.Append("\n</svg>");
			return sb;
		}

		var maxLane = 0;
		foreach (var c in simulation.Commits)
		{
			if (c.Lane > maxLane)
				maxLane = c.Lane;
		}

		var width = LeftPad + (simulation.Commits.Count * CommitSpacing) + 60;
		var height = TopPad + ((maxLane + 1) * LaneSpacing) + 60;

		StyleBlock.AppendSvgOpenTag(sb, width, height, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict, monoFont: monoFont);
		_ = sb.Append("\n<defs>\n</defs>\n");

		foreach (var branch in simulation.Branches)
			AppendBranchLabel(sb, branch);

		foreach (var link in simulation.Links)
			AppendLink(sb, link, simulation);

		foreach (var commit in simulation.Commits)
			AppendCommit(sb, commit);

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static void AppendBranchLabel(StringBuilder sb, BranchInfo branch)
	{
		var y = TopPad + (branch.Lane * LaneSpacing);
		var labelW = TextMetrics.MeasureTextWidth(branch.Name, BranchFontSizePx, 600) + 12;
		var pillH = BranchFontSizePx + 8;
		_ = sb.Append("\n<rect x=\"6\" y=\"").Append((y - (pillH / 2)).SvgFormat())
			.Append("\" width=\"").Append(labelW.SvgFormat()).Append("\" height=\"").Append(pillH.SvgFormat())
			.Append("\" rx=\"").Append((pillH / 2).SvgFormat()).Append("\" ry=\"").Append((pillH / 2).SvgFormat())
			.Append("\" fill=\"").Append(branch.Color).Append("\" opacity=\"0.15\" />");
		_ = sb.Append("\n<text x=\"12\" y=\"").Append(y.SvgFormat())
			.Append("\" dy=\"0.35em\" font-size=\"").Append(BranchFontSize)
			.Append("\" font-weight=\"600\" fill=\"").Append(branch.Color).Append("\">");
		MultilineUtils.AppendEscapedXml(sb, branch.Name.AsSpan());
		_ = sb.Append("</text>");
	}

	private static void AppendLink(StringBuilder sb, CommitLink link, SimulationResult sim)
	{
		var from = sim.Commits[link.FromIndex];
		var to = sim.Commits[link.ToIndex];
		var x1 = LeftPad + (from.Position * CommitSpacing);
		var y1 = TopPad + (from.Lane * LaneSpacing);
		var x2 = LeftPad + (to.Position * CommitSpacing);
		var y2 = TopPad + (to.Lane * LaneSpacing);

		if (Math.Abs(y1 - y2) < 0.01)
		{
			_ = sb.Append("\n<line x1=\"").Append(x1.SvgFormat()).Append("\" y1=\"").Append(y1.SvgFormat())
				.Append("\" x2=\"").Append(x2.SvgFormat()).Append("\" y2=\"").Append(y2.SvgFormat())
				.Append("\" stroke=\"").Append(link.Color)
				.Append("\" stroke-width=\"").Append(LinkStrokeWidth).Append("\" />");
		}
		else
		{
			var midX = (x1 + x2) / 2;
			_ = sb.Append("\n<path d=\"M ").Append(x1.SvgFormat()).Append(' ').Append(y1.SvgFormat())
				.Append(" C ").Append(midX.SvgFormat()).Append(' ').Append(y1.SvgFormat())
				.Append(' ').Append(midX.SvgFormat()).Append(' ').Append(y2.SvgFormat())
				.Append(' ').Append(x2.SvgFormat()).Append(' ').Append(y2.SvgFormat())
				.Append("\" fill=\"none\" stroke=\"").Append(link.Color)
				.Append("\" stroke-width=\"").Append(LinkStrokeWidth).Append("\" />");
		}
	}

	private static void AppendCommit(StringBuilder sb, CommitInfo commit)
	{
		var cx = LeftPad + (commit.Position * CommitSpacing);
		var cy = TopPad + (commit.Lane * LaneSpacing);

		switch (commit.Type)
		{
			case GitCommitType.Highlight:
				_ = sb.Append("\n<rect x=\"").Append((cx - 8).SvgFormat()).Append("\" y=\"").Append((cy - 8).SvgFormat())
					.Append("\" width=\"16\" height=\"16\" rx=\"3\" ry=\"3\" fill=\"")
					.Append(commit.Color).Append("\" stroke=\"var(--bg)\" stroke-width=\"2\" />");
				break;
			case GitCommitType.Reverse:
				_ = sb.Append("\n<circle cx=\"").Append(cx.SvgFormat()).Append("\" cy=\"").Append(cy.SvgFormat())
					.Append("\" r=\"").Append(CommitRadius)
					.Append("\" fill=\"var(--bg)\" stroke=\"").Append(commit.Color).Append("\" stroke-width=\"2\" />");
				_ = sb.Append("\n<line x1=\"").Append((cx - 5).SvgFormat()).Append("\" y1=\"").Append((cy - 5).SvgFormat())
					.Append("\" x2=\"").Append((cx + 5).SvgFormat()).Append("\" y2=\"").Append((cy + 5).SvgFormat())
					.Append("\" stroke=\"").Append(commit.Color).Append("\" stroke-width=\"2\" />");
				_ = sb.Append("\n<line x1=\"").Append((cx + 5).SvgFormat()).Append("\" y1=\"").Append((cy - 5).SvgFormat())
					.Append("\" x2=\"").Append((cx - 5).SvgFormat()).Append("\" y2=\"").Append((cy + 5).SvgFormat())
					.Append("\" stroke=\"").Append(commit.Color).Append("\" stroke-width=\"2\" />");
				break;
			default:
				if (commit.IsMerge)
				{
					_ = sb.Append("\n<circle cx=\"").Append(cx.SvgFormat()).Append("\" cy=\"").Append(cy.SvgFormat())
						.Append("\" r=\"").Append(CommitRadius + 2)
						.Append("\" fill=\"").Append(commit.Color).Append("\" stroke=\"var(--bg)\" stroke-width=\"2\" />");
					_ = sb.Append("\n<circle cx=\"").Append(cx.SvgFormat()).Append("\" cy=\"").Append(cy.SvgFormat())
						.Append("\" r=\"").Append(CommitRadius - 2)
						.Append("\" fill=\"").Append(commit.Color).Append("\" />");
				}
				else
				{
					_ = sb.Append("\n<circle cx=\"").Append(cx.SvgFormat()).Append("\" cy=\"").Append(cy.SvgFormat())
						.Append("\" r=\"").Append(CommitRadius)
						.Append("\" fill=\"").Append(commit.Color).Append("\" stroke=\"var(--bg)\" stroke-width=\"2\" />");
				}
				break;
		}

		if (commit.Label is { Length: > 0 })
		{
			_ = sb.Append("\n<text x=\"").Append(cx.SvgFormat()).Append("\" y=\"").Append((cy + LabelOffsetY).SvgFormat())
				.Append("\" text-anchor=\"middle\" font-size=\"").Append(LabelFontSize)
				.Append("\" fill=\"var(--_text-sec)\">");
			MultilineUtils.AppendEscapedXml(sb, commit.Label.AsSpan());
			_ = sb.Append("</text>");
		}

		if (commit.Tag is { Length: > 0 })
		{
			var tagW = TextMetrics.MeasureTextWidth(commit.Tag, TagFontSizePx, 500) + 10;
			_ = sb.Append("\n<rect x=\"").Append((cx - (tagW / 2)).SvgFormat()).Append("\" y=\"").Append((cy + TagOffsetY - 8).SvgFormat())
				.Append("\" width=\"").Append(tagW.SvgFormat()).Append("\" height=\"16\" rx=\"8\" ry=\"8\" fill=\"")
				.Append(commit.Color).Append("\" opacity=\"0.2\" />");
			_ = sb.Append("\n<text x=\"").Append(cx.SvgFormat()).Append("\" y=\"").Append((cy + TagOffsetY).SvgFormat())
				.Append("\" text-anchor=\"middle\" dy=\"0.35em\" font-size=\"").Append(TagFontSize)
				.Append("\" font-weight=\"500\" fill=\"").Append(commit.Color).Append("\">");
			MultilineUtils.AppendEscapedXml(sb, commit.Tag.AsSpan());
			_ = sb.Append("</text>");
		}
	}

	private sealed record CommitInfo(int Position, int Lane, string Color, string? Label, string? Tag, GitCommitType Type, bool IsMerge);
	private sealed record CommitLink(int FromIndex, int ToIndex, string Color);
	private sealed record BranchInfo(string Name, int Lane, string Color);
	private sealed record SimulationResult(List<CommitInfo> Commits, List<CommitLink> Links, List<BranchInfo> Branches);

	private static SimulationResult Simulate(GitGraph graph, DiagramColors colors)
	{
		var commits = new List<CommitInfo>();
		var links = new List<CommitLink>();
		var branches = new Dictionary<string, int>();
		var branchColors = new Dictionary<string, string>();
		var branchHeads = new Dictionary<string, int>();
		var branchList = new List<BranchInfo>();
		var nextLane = 0;
		var position = 0;
		var currentBranch = "main";

		branches["main"] = nextLane++;
		branchColors["main"] = colors.PaletteAt(0);
		branchList.Add(new BranchInfo("main", 0, colors.PaletteAt(0)));

		var commitCounter = 0;

		foreach (var action in graph.Actions)
		{
			if (action is GitBranchAction branch)
			{
				if (!branches.ContainsKey(branch.Name))
				{
					var lane = nextLane++;
					branches[branch.Name] = lane;
					var color = colors.PaletteAt(lane);
					branchColors[branch.Name] = color;
					branchList.Add(new BranchInfo(branch.Name, lane, color));
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
				var color = branchColors.GetValueOrDefault(currentBranch, colors.PaletteAt(0));
				var label = commit.Id ?? commitCounter.ToString(CultureInfo.InvariantCulture);
				commitCounter++;

				var idx = commits.Count;
				commits.Add(new CommitInfo(position, lane, color, label, commit.Tag, commit.Type, false));

				if (branchHeads.TryGetValue(currentBranch, out var prevIdx))
					links.Add(new CommitLink(prevIdx, idx, color));

				branchHeads[currentBranch] = idx;
				position++;
			}
			else if (action is GitMergeAction merge)
			{
				var lane = branches.GetValueOrDefault(currentBranch, 0);
				var color = branchColors.GetValueOrDefault(currentBranch, colors.PaletteAt(0));
				commitCounter++;

				var idx = commits.Count;
				commits.Add(new CommitInfo(position, lane, color, merge.Id, merge.Tag, merge.Type, true));

				if (branchHeads.TryGetValue(currentBranch, out var prevIdx))
					links.Add(new CommitLink(prevIdx, idx, color));

				if (branchHeads.TryGetValue(merge.Name, out var mergeFromIdx))
				{
					var mergeColor = branchColors.GetValueOrDefault(merge.Name, color);
					links.Add(new CommitLink(mergeFromIdx, idx, mergeColor));
				}

				branchHeads[currentBranch] = idx;
				position++;
			}
			else if (action is GitCherryPickAction cherryPick)
			{
				var lane = branches.GetValueOrDefault(currentBranch, 0);
				var color = branchColors.GetValueOrDefault(currentBranch, colors.PaletteAt(0));
				commitCounter++;

				var idx = commits.Count;
				commits.Add(new CommitInfo(position, lane, color, cherryPick.Id, null, GitCommitType.Normal, false));

				if (branchHeads.TryGetValue(currentBranch, out var prevIdx))
					links.Add(new CommitLink(prevIdx, idx, color));

				branchHeads[currentBranch] = idx;
				position++;
			}
		}

		return new SimulationResult(commits, links, branchList);
	}

}
