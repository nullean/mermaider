using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class GitGraphParserTests
{
	[Test]
	public void Parses_simple_commits()
	{
		var lines = new[]
		{
			"gitGraph",
			"commit",
			"commit",
			"commit",
		};

		var graph = GitGraphParser.Parse(lines);

		graph.Actions.Should().HaveCount(3);
		graph.Actions[0].Should().BeOfType<GitCommitAction>();
	}

	[Test]
	public void Parses_commit_with_id()
	{
		var lines = new[]
		{
			"gitGraph",
			"commit id: \"abc123\"",
		};

		var graph = GitGraphParser.Parse(lines);

		var commit = (GitCommitAction)graph.Actions[0];
		commit.Id.Should().Be("abc123");
	}

	[Test]
	public void Parses_commit_with_tag()
	{
		var lines = new[]
		{
			"gitGraph",
			"commit tag: \"v1.0\"",
		};

		var graph = GitGraphParser.Parse(lines);

		var commit = (GitCommitAction)graph.Actions[0];
		commit.Tag.Should().Be("v1.0");
	}

	[Test]
	public void Parses_commit_type()
	{
		var lines = new[]
		{
			"gitGraph",
			"commit type: HIGHLIGHT",
			"commit type: REVERSE",
		};

		var graph = GitGraphParser.Parse(lines);

		((GitCommitAction)graph.Actions[0]).Type.Should().Be(GitCommitType.Highlight);
		((GitCommitAction)graph.Actions[1]).Type.Should().Be(GitCommitType.Reverse);
	}

	[Test]
	public void Parses_branch_and_checkout()
	{
		var lines = new[]
		{
			"gitGraph",
			"commit",
			"branch develop",
			"commit",
			"checkout main",
			"commit",
		};

		var graph = GitGraphParser.Parse(lines);

		graph.Actions.Should().HaveCount(5);
		((GitBranchAction)graph.Actions[1]).Name.Should().Be("develop");
		((GitCheckoutAction)graph.Actions[3]).Name.Should().Be("main");
	}

	[Test]
	public void Parses_merge()
	{
		var lines = new[]
		{
			"gitGraph",
			"commit",
			"branch develop",
			"commit",
			"checkout main",
			"merge develop",
		};

		var graph = GitGraphParser.Parse(lines);

		var merge = (GitMergeAction)graph.Actions[4];
		merge.Name.Should().Be("develop");
	}

	[Test]
	public void Parses_merge_with_attributes()
	{
		var lines = new[]
		{
			"gitGraph",
			"commit",
			"branch develop",
			"commit",
			"checkout main",
			"merge develop id: \"m1\" tag: \"release\" type: HIGHLIGHT",
		};

		var graph = GitGraphParser.Parse(lines);

		var merge = (GitMergeAction)graph.Actions[4];
		merge.Id.Should().Be("m1");
		merge.Tag.Should().Be("release");
		merge.Type.Should().Be(GitCommitType.Highlight);
	}

	[Test]
	public void Parses_cherry_pick()
	{
		var lines = new[]
		{
			"gitGraph",
			"commit id: \"abc\"",
			"branch develop",
			"cherry-pick id: \"abc\"",
		};

		var graph = GitGraphParser.Parse(lines);

		var cp = (GitCherryPickAction)graph.Actions[2];
		cp.Id.Should().Be("abc");
	}

	[Test]
	public void Parses_TB_orientation()
	{
		var lines = new[]
		{
			"gitGraph TB:",
			"commit",
		};

		var graph = GitGraphParser.Parse(lines);

		graph.Orientation.Should().Be(GitGraphOrientation.TB);
	}

	[Test]
	public void Parses_branch_with_order()
	{
		var lines = new[]
		{
			"gitGraph",
			"branch develop order: 2",
		};

		var graph = GitGraphParser.Parse(lines);

		var branch = (GitBranchAction)graph.Actions[0];
		branch.Name.Should().Be("develop");
		branch.Order.Should().Be(2);
	}

	[Test]
	public void Parses_switch_as_checkout()
	{
		var lines = new[]
		{
			"gitGraph",
			"switch main",
		};

		var graph = GitGraphParser.Parse(lines);

		((GitCheckoutAction)graph.Actions[0]).Name.Should().Be("main");
	}

	[Test]
	public void Default_orientation_is_LR()
	{
		var lines = new[]
		{
			"gitGraph",
			"commit",
		};

		var graph = GitGraphParser.Parse(lines);

		graph.Orientation.Should().Be(GitGraphOrientation.LR);
	}
}
