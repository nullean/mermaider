using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class GitGraphParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^commit(?:\s+(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex CommitPattern();

	[GeneratedRegex(@"^branch\s+""?([^""\s]+)""?(?:\s+order:\s*(\d+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex BranchPattern();

	[GeneratedRegex(@"^(?:checkout|switch)\s+""?([^""]+)""?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex CheckoutPattern();

	[GeneratedRegex(@"^merge\s+""?([^""]+?)""?(?:\s+(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex MergePattern();

	[GeneratedRegex(@"^cherry-pick\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex CherryPickPattern();

	[GeneratedRegex(@"id:\s*""([^""]+)""", RegexOptions.None, TimeoutMs)]
	private static partial Regex IdAttr();

	[GeneratedRegex(@"tag:\s*""([^""]+)""", RegexOptions.None, TimeoutMs)]
	private static partial Regex TagAttr();

	[GeneratedRegex(@"type:\s*(NORMAL|HIGHLIGHT|REVERSE)", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex TypeAttr();

	[GeneratedRegex(@"parent:\s*""([^""]+)""", RegexOptions.None, TimeoutMs)]
	private static partial Regex ParentAttr();

	internal static GitGraph Parse(string[] lines)
	{
		try
		{
			return ParseCore(lines);
		}
		catch (RegexMatchTimeoutException ex)
		{
			throw new MermaidParseException(
				$"Parsing timed out after {ex.MatchTimeout.TotalSeconds}s — input may contain pathological patterns.",
				ex);
		}
	}

	private static GitGraph ParseCore(string[] lines)
	{
		var actions = new List<GitAction>();
		var orientation = GitGraphOrientation.LR;

		var firstLine = lines[0];
		if (firstLine.Contains("TB:", StringComparison.OrdinalIgnoreCase))
			orientation = GitGraphOrientation.TB;
		else if (firstLine.Contains("BT:", StringComparison.OrdinalIgnoreCase))
			orientation = GitGraphOrientation.BT;

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			var branchMatch = BranchPattern().Match(line);
			if (branchMatch.Success)
			{
				int? order = branchMatch.Groups[2].Success ? int.Parse(branchMatch.Groups[2].Value, CultureInfo.InvariantCulture) : null;
				actions.Add(new GitBranchAction(branchMatch.Groups[1].Value, order));
				continue;
			}

			var checkoutMatch = CheckoutPattern().Match(line);
			if (checkoutMatch.Success)
			{
				actions.Add(new GitCheckoutAction(checkoutMatch.Groups[1].Value));
				continue;
			}

			var mergeMatch = MergePattern().Match(line);
			if (mergeMatch.Success)
			{
				var rest = mergeMatch.Groups[2].Success ? mergeMatch.Groups[2].Value : "";
				actions.Add(new GitMergeAction(
					mergeMatch.Groups[1].Value,
					ExtractAttr(IdAttr(), rest),
					ExtractAttr(TagAttr(), rest),
					ExtractType(rest)));
				continue;
			}

			var cherryMatch = CherryPickPattern().Match(line);
			if (cherryMatch.Success)
			{
				var rest = cherryMatch.Groups[1].Value;
				actions.Add(new GitCherryPickAction(
					ExtractAttr(IdAttr(), rest) ?? "",
					ExtractAttr(ParentAttr(), rest)));
				continue;
			}

			var commitMatch = CommitPattern().Match(line);
			if (commitMatch.Success)
			{
				var rest = commitMatch.Groups[1].Success ? commitMatch.Groups[1].Value : "";
				actions.Add(new GitCommitAction
				{
					Id = ExtractAttr(IdAttr(), rest),
					Tag = ExtractAttr(TagAttr(), rest),
					Type = ExtractType(rest),
				});
			}
		}

		return new GitGraph { Actions = actions, Orientation = orientation };
	}

	private static string? ExtractAttr(Regex pattern, string text)
	{
		var match = pattern.Match(text);
		return match.Success ? match.Groups[1].Value : null;
	}

	private static GitCommitType ExtractType(string text)
	{
		var match = TypeAttr().Match(text);
		if (!match.Success)
			return GitCommitType.Normal;
		return match.Groups[1].Value.ToUpperInvariant() switch
		{
			"HIGHLIGHT" => GitCommitType.Highlight,
			"REVERSE" => GitCommitType.Reverse,
			_ => GitCommitType.Normal,
		};
	}
}
