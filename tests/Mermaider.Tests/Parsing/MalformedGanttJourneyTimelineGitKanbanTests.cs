using AwesomeAssertions;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

/// <summary>
/// Gantt, journey, timeline, gitGraph and kanban refuse what Mermaid's own parser refuses (checked against mermaid 12.3),
/// and still accept what it accepts, including its quirks.
/// </summary>
public class MalformedGanttJourneyTimelineGitKanbanTests
{
	[Test]
	// gantt
	[Arguments("gantt\ndateFormat YYYY-MM-DD\nTask\n", 3, "has no start date or duration", ":")]
	[Arguments("gantt\ndateFormat YYYY-MM-DD\nA : a1, 2024-01-01, 1d\nfoo bar\n", 4, "task 'foo bar'", "<task name>")]
	[Arguments("gantt\ndateFormat YYYY-MM-DD\n: a1, 2024-01-01, 1d\n", 3, "task has no name", "task name")]
	[Arguments("gantt\ndateFormat YYYY-MM-DD\nT : crit, active\n", 3, "only tags", "after the tags")]
	[Arguments("gantt\ndateFormat YYYY-MM-DD\nT : milestone\n", 3, "only tags", "after the tags")]
	[Arguments("gantt\ndateFormat YYYY-MM-DD\nT : 2024-01-01, 1d\ntitle\n", 4, "'title' has no value", "text after 'title'")]
	// journey
	[Arguments("journey\nsection A\nMake tea\n", 3, "task 'Make tea' has no score", "<task name>: <score>")]
	[Arguments("journey\nsection A\n: 3: Me\n", 3, "task has no name", "task name")]
	[Arguments("journey\ntitle X\nsection\n", 3, "'section' has no name", "section name")]
	[Arguments("journey\nsection\nTask: 3: Me\n", 2, "'section' has no name", "section name")]
	// timeline
	[Arguments("timeline\n2020 :A\n", 2, "':' must be followed by a space", "<period> : <event>")]
	[Arguments("timeline\n2020:A\n", 2, "':' must be followed by a space", "<period> : <event>")]
	[Arguments("timeline\n2020 : A : : B\n", 2, "event after ':' has no text", "<period> : <event>")]
	[Arguments("timeline\n2020 : A :\n", 2, "event after ':' has no text", "event text")]
	[Arguments("timeline\n: B\n", 2, "no period before it", "a period")]
	[Arguments("timeline\nsection a:b\n2020 : A\n", 2, "section name contains ':'", "without ':'")]
	// gitGraph
	[Arguments("gitGraph\ncommit\nfoo\n", 3, "unrecognized statement 'foo'", "'commit'")]
	[Arguments("gitGraph\ncommit id: \"a\n", 2, "string is never closed", "a closing \"")]
	[Arguments("gitGraph\ncommit id:\n", 2, "'id:' has no quoted value", "a quoted string")]
	[Arguments("gitGraph\ncommit type: HI\n", 2, "'HI' is not a commit type", "NORMAL, REVERSE or HIGHLIGHT")]
	[Arguments("gitGraph\ncommit type: highlight\n", 2, "'highlight' is not a commit type", "NORMAL, REVERSE or HIGHLIGHT")]
	[Arguments("gitGraph\ncommit id: \"a\" foo\n", 2, "unexpected text 'foo'", "id:")]
	[Arguments("gitGraph\nCommit\n", 2, "unrecognized statement 'Commit'", "'commit'")]
	[Arguments("gitGraph\ncommit\nbranch b order:\n", 3, "'order:' has no number", "whole number")]
	[Arguments("gitGraph\ncommit\nbranch b c\n", 3, "unexpected text 'c'", "order")]
	[Arguments("gitGraph\ncommit\nbranch main\n", 3, "branch 'main' already exists", "new branch name")]
	[Arguments("gitGraph\ncommit\ncheckout nope\n", 3, "cannot checkout branch 'nope'", "an existing branch")]
	[Arguments("gitGraph\ncommit\nmerge nope\n", 3, "branch 'nope': it does not exist", "an existing branch")]
	[Arguments("gitGraph\ncommit\nmerge main\n", 3, "into itself", "different branch")]
	[Arguments("gitGraph\nbranch b\nmerge main\n", 3, "cannot merge into branch 'b': it has no commits", "current branch")]
	[Arguments("gitGraph\nbranch b\ncheckout main\ncommit\nmerge b\n", 5, "cannot merge branch 'b': it has no commits", "has commits")]
	[Arguments("gitGraph\ncommit\nbranch b\nmerge main\n", 4, "both are at the same commit", "current branch lacks")]
	[Arguments("gitGraph\ncommit\nbranch b\ncheckout b\ncommit\ncheckout main\nmerge b order: 1\n", 7, "unexpected text 'order:'", "id:")]
	[Arguments("gitGraph\ncommit\nbranch b\ncheckout b\ncommit\ncheckout main\nmerge b id: \"x\"\ncheckout b\ncommit\ncheckout main\nmerge b tag: \"t\" id: \"x\"\n", 11, "'x' already exists", "unique merge id")]
	[Arguments("gitGraph\ncommit\ncherry-pick\n", 3, "no source commit id", "cherry-pick id:")]
	[Arguments("gitGraph\ncommit id: \"a\"\ncherry-pick id: \"zz\"\n", 3, "'zz' does not exist", "cherry-pick id:")]
	[Arguments("gitGraph\ncommit id: \"a\"\ncherry-pick id: \"a\"\n", 3, "already on branch 'main'", "another branch")]
	[Arguments("gitGraph\ncommit id: \"a\"\nbranch b\ncheckout b\ncherry-pick id: \"a\" parent: \"x\"\n", 5, "not an immediate parent", "parents")]
	[Arguments("gitGraph\nbranch b\ncheckout b\ncommit id: \"c\"\ncheckout main\ncherry-pick id: \"c\"\n", 6, "has no commits", "current branch")]
	[Arguments("gitGraph\ncommit;\n", 2, "unexpected text ';'", "id:")]
	// kanban
	[Arguments("kanban\n  Todo\n    task[Safe\n", 3, "never closed", "']'")]
	[Arguments("kanban\n  Todo\n    task[]\n", 3, "label between '[' and ']' is empty", "label text")]
	[Arguments("kanban\n  Todo\n    task[Safe]x\n", 3, "unexpected text 'x'", "'@{")]
	[Arguments("kanban\n  Todo\n    task[a] @{ assigned: 'x' }\n", 3, "unexpected text", "'@{")]
	[Arguments("kanban\n  Todo\n    t[[a]]\n", 3, "second '['", "label text")]
	[Arguments("kanban\n  Todo[\n    task\n", 2, "never closed", "']'")]
	[Arguments("kanban\n  Todo\n    task[a]@{ assigned: 'x'\n", 3, "never closed", "'}'")]
	[Arguments("kanban\n  Todo\n    task[a]@{\n", 3, "never closed", "'}'")]
	[Arguments("kanban\n  Todo\n    task[a]@{ assigned: 'x\n", 3, "never closed", "'}'")]
	[Arguments("kanban\n  Todo\n    task[a]@{ assigned: 'x' } trailing\n", 3, "unexpected text 'trailing'", "end of the line")]
	[Arguments("kanban\n  Todo\n    t[a]@{ assigned: 'x' ticket: 'y' }\n", 3, "separated by ','", "key: value")]
	[Arguments("kanban\n  Todo\n    t[a]@{ assigned: 'x }\n", 3, "string opened with ' in the metadata is never closed", "closing '")]
	[Arguments("kanban\n  Todo\n    a[A]@{ ticket: PRJ-050\n    b[B]@{ ticket: 'y' }\n", 3, "not a valid key: value block", "closed on the same line")]
	[Arguments("kanban\n    a\n  b\n    c\n", 3, "indented less than the first line", "first line")]
	public void Strict_by_default_throws_with_line_and_expectation(string source, int line, string problem, string expected)
	{
		var act = () => MermaidRenderer.RenderSvg(source);

		var ex = act.Should().Throw<MermaidParseException>().Which;
		ex.Line.Should().Be(line);
		ex.Message.Should().Contain($"on line {line}").And.Contain(problem).And.Contain("Expecting").And.Contain(expected);
		ex.SourceLine.Should().NotBeNullOrEmpty();
	}

	[Test]
	public void Message_points_a_caret_at_the_column()
	{
		var act = () => MermaidRenderer.RenderSvg("kanban\n  Todo\n    task[Safe\n");

		act.Should().Throw<MermaidParseException>().Which.Message.Should().Be(
			"Parse error on line 3: label opened with '[' is never closed\n  task[Safe\n      ^\nExpecting ']' to end the label."
		);
	}

	/// <summary>Inputs Mermaid accepts, quirks included. A stricter rule that refused any of them would be wrong.</summary>
	[Test]
	[Arguments("gantt\ndateFormat YYYY-MM-DD\ntitle X\nsection A\nT1 : a1, 2024-01-01, 1d\nT2 : after a1, 2d\nT3 : crit, a3, after a2, 2d\nT4 : milestone, m1, 2024-02-01, 0d\nT5 : done, 2024-01-01, 2024-01-05\n")]
	[Arguments("gantt\ndateFormat YYYY-MM-DD\nincludes 2024-01-01\nexcludes weekends\ntodayMarker off\nweekday monday\ninclusiveEndDates\nT : done,\n")]
	[Arguments("gantt\ndateFormat YYYY-MM-DD\nT : 2024-13-45, 1x\nU : after nobody, 1d\n")]
	[Arguments("journey\ntitle X\nsection A\nTask one: 3: Me, You\nTask two: 5\nTask: abc: Me\nTask: 3:\n")]
	[Arguments("journey\nsection A\nTask: 3: Me\nsection B\nOther: 1: You\n")]
	[Arguments("timeline\ntitle History\nsection Old\n2002 : LinkedIn\n2004 : Facebook : Google\n     : Bing\nsection New\n2010 : http://example.com\n2020\n")]
	[Arguments("timeline\n2020 : A\nsection S\n: B\n")]
	[Arguments("timeline\n2020 :\n2021 : B\n")]
	[Arguments("gitGraph\ncommit id: \"a\" tag: \"v1\" type: HIGHLIGHT\nbranch develop order: 2\ncheckout develop\ncommit id: \"b\"\ncommit\ncheckout main\nmerge develop id: \"m\" tag: \"rel\"\ncherry-pick id: \"b\"\n")]
	[Arguments("gitGraph TB:\ncommit \"msg\"\ncommit id:\"x\"\nbranch \"feat/x\"\ncheckout \"feat/x\"\ncommit\nswitch main\nmerge \"feat/x\"\n")]
	[Arguments("gitGraph\ncommit\nbranch b\ncommit\ncheckout main\ncommit\nmerge b\n")]
	[Arguments("gitGraph\ncommit id: \"a\"\ncommit id: \"a\"\ncheckout main\n")]
	[Arguments("kanban\n  todo[To Do]\n    t1[Write docs]@{ assigned: 'a', ticket: X-1, priority: 'High' }\n    t2[Other]@{ ticket: \"Y\" }\n    t3[A]@{ }\n  doing\n    t4\n    t5 @{ assigned: 'z' }\n")]
	[Arguments("kanban\n  todo[To Do]\n    t1[Multi\n    line]\n    t2[A]@{\n      assigned: 'a'\n    }\n")]
	[Arguments("kanban\ntitle Board\n    a\n    b\n  c\n")]
	[Arguments("kanban\n  Todo\n    a[A]@{ priority: \n    b[B]@{ ticket: 'y' }\n")]
	public void Valid_input_stays_accepted(string source) =>
		MermaidRenderer.RenderSvg(source).Should().StartWith("<svg");

	[Test]
	public void Lenient_parsers_skip_the_statements_strict_refuses()
	{
		GanttParser.Parse(["gantt", "Task", "T : 1d"], strict: false).Sections.Should().ContainSingle();
		JourneyParser.Parse(["journey", "Make tea", "Do: 3: Me"], strict: false).Sections.Should().ContainSingle();
		TimelineParser.Parse(["timeline", "2020 :A", "2021 : B"], strict: false).Sections.Should().ContainSingle();
		GitGraphParser.Parse(["gitGraph", "commit", "checkout nope", "foo"], strict: false).Actions.Should().HaveCount(2);
		KanbanParser.Parse(["kanban", "  Todo", "    task[Safe"], strict: false).Columns.Should().ContainSingle();

		var strict = () => GanttParser.Parse(["gantt", "Task", "T : 1d"]);
		strict.Should().Throw<MermaidParseException>();
	}
}
