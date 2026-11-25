using AwesomeAssertions;
using Mermaider.Animate.Score;

namespace Mermaider.Animate.Tests;

public class ScoreTests
{
	private static (ScoreDocument Score, List<AnimationDiagnostic> Diagnostics) Parse(string source)
	{
		var lines = source.Replace("\r\n", "\n").Split('\n');
		var diagnostics = new List<AnimationDiagnostic>();
		var start = ScoreParser.FindStart(lines);
		start.Should().BeGreaterThanOrEqualTo(0);
		return (ScoreParser.Parse(lines, start, diagnostics), diagnostics);
	}

	[Test]
	[Arguments("a <- b", new[] { "a", "<-", "b" })]
	[Arguments("a<-b", new[] { "a<-b" })]
	[Arguments("<-", new[] { "<-" })]
	[Arguments(".)<-@", new[] { ".)<-@" })]
	[Arguments("a <-> b", new[] { "a", "<->", "b" })]
	[Arguments("a<->b->c", new[] { "a", "<->", "b", "->", "c" })]
	public void A_lone_left_arrow_is_part_of_a_word(string line, string[] expected)
	{
		// "<-" once ended a word before its first character without being a token, and the tokenizer never moved on
		var tokenizing = Task.Run(() => ScoreParser.Tokenize(line, 1, []));
		tokenizing.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("tokenizing a line always ends");
		tokenizing.Result.Select(w => w.Text).Should().Equal(expected);
	}

	[Test]
	public void A_left_arrow_in_a_score_is_reported_not_hung_on()
	{
		var compiling = Task.Run(() => MermaidAnimator.Compile("flowchart LR\n  a --> b\nanimate\n  send a <- b\n"));
		Action wait = () => compiling.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("compiling a score always ends");
		wait.Should().Throw<AggregateException>().WithInnerException<AnimationException>().Which.Diagnostics.Should().ContainSingle(d => d.Line == 4);
	}

	[Test]
	public void With_outside_a_step_joins_the_statement_before_it()
	{
		// a statement outside a step is a step of its own; "with" on the next line once crashed adding to it
		var (score, diagnostics) = Parse("flowchart LR\n  a --> b\nanimate\n  show a\n  with show b\n  show b\n");
		diagnostics.Should().BeEmpty();
		score.Steps.Select(s => s.Statements.Count).Should().Equal(2, 1);
		MermaidAnimator.Compile("flowchart LR\n  a --> b\nanimate\n  show a\n  with show b\n").Steps.Should().ContainSingle();
	}

	[Test]
	public void Finds_the_block_only_at_the_start_of_a_line()
	{
		ScoreParser.FindStart(["flowchart LR", "  animate --> b"]).Should().Be(-1);
		ScoreParser.FindStart(["flowchart LR", "  a --> b", "animate"]).Should().Be(2);
		ScoreParser.FindStart(["flowchart LR", "animated --> b"]).Should().Be(-1);
	}

	[Test]
	public void Header_takes_a_mode_and_a_preset()
	{
		var (score, diagnostics) = Parse("flowchart LR\n  a --> b\nanimate once crisp\n");
		diagnostics.Should().BeEmpty();
		score.Mode.Should().Be(PlaybackMode.Once);
		score.Motion.Should().Be(MotionPreset.Crisp);
		score.IsDefault.Should().BeTrue();
	}

	[Test]
	public void Statements_belong_to_the_step_above_them_by_indentation()
	{
		var (score, diagnostics) = Parse(
			"""
			flowchart LR
			  a --> b --> c
			animate
			  build
			  step "First"
			    send a -> b "hello" as ok slow
			    with pulse c
			  step "Second"
			    focus b close
			  draw a -> b
			"""
		);
		diagnostics.Should().BeEmpty();
		score.Steps.Should().HaveCount(4);
		score.Steps[0].Statements.Single().Should().BeOfType<BuildStatement>();
		score.Steps[1].Caption.Should().Be("First");
		var send = score.Steps[1].Statements[0].Should().BeOfType<SendStatement>().Subject;
		send.From.Should().Be("a");
		send.To.Should().Equal("b");
		send.Label.Should().Be("hello");
		send.Role.Should().Be(Role.Ok);
		send.Speed.Should().Be(Speed.Slow);
		score.Steps[1].Statements[1].With.Should().BeTrue();
		score.Steps[2].Statements.Single().Should().BeOfType<FocusStatement>().Which.Close.Should().BeTrue();
		score.Steps[3].Caption.Should().BeNull();
	}

	[Test]
	public void A_send_can_name_a_declared_token_and_fan_out()
	{
		var (score, diagnostics) = Parse(
			"""
			flowchart LR
			  t --> a & b
			animate
			  token o = "order #42" as warn
			  send o t -> a, b each becomes "done" lost
			"""
		);
		diagnostics.Should().BeEmpty();
		score.Tokens["o"].Label.Should().Be("order #42");
		var send = score.Steps.Single().Statements.Single().Should().BeOfType<SendStatement>().Subject;
		send.Token.Should().Be("o");
		send.To.Should().Equal("a", "b");
		send.Each.Should().BeTrue();
		send.Becomes.Should().Be("done");
		send.Fate.Should().Be(Fate.Lost);
	}

	[Test]
	public void Edge_chains_name_every_edge_and_take_an_index()
	{
		var (score, diagnostics) = Parse("flowchart LR\n  a --> b\nanimate\n  draw a -> b -> c, d -> e #2 each\n");
		diagnostics.Should().BeEmpty();
		var draw = score.Steps.Single().Statements.Single().Should().BeOfType<DrawStatement>().Subject;
		draw.Edges.Select(e => e.ToString()).Should().Equal("a -> b", "b -> c", "d -> e #2");
		draw.Each.Should().BeTrue();
	}

	[Test]
	public void Every_problem_is_reported_with_its_line()
	{
		var (_, diagnostics) = Parse(
			"""
			flowchart LR
			  a --> b
			animate sideways
			  wiggle a
			  mark a purple
			  caption "never closed
			  step "Empty"
			"""
		);
		diagnostics.Select(d => d.Line).Should().Equal(3, 4, 5, 6, 7);
		diagnostics[0].Message.Should().Contain("sideways");
		diagnostics[1].Message.Should().Contain("Unknown verb 'wiggle'");
		diagnostics[2].Message.Should().Contain("Unknown role 'purple'");
		diagnostics[3].Message.Should().Contain("not closed");
		diagnostics[4].Message.Should().Contain("empty");
	}

	[Test]
	public void Verbs_reject_the_wrong_kind_of_target()
	{
		var (_, diagnostics) = Parse("flowchart LR\n  a --> b\nanimate\n  draw a\n  pulse a -> b\n");
		diagnostics.Should().HaveCount(2);
		diagnostics[0].Message.Should().Contain("takes edges");
		diagnostics[1].Message.Should().Contain("takes nodes");
	}

	[Test]
	public void Play_takes_a_message_range()
	{
		var (score, diagnostics) = Parse("sequenceDiagram\n  a->>b: hi\nanimate\n  play #2..#4\n  play #3\n");
		diagnostics.Should().BeEmpty();
		score.Steps[0].Statements.Single().Should().BeEquivalentTo(new PlayStatement(2, 4, 4));
		score.Steps[1].Statements.Single().Should().BeEquivalentTo(new PlayStatement(3, 3, 5));
	}
}
