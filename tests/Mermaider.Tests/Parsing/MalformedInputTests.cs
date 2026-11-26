using AwesomeAssertions;
using Mermaider.Models;

namespace Mermaider.Tests.Parsing;

/// <summary>Malformed input is refused with a located message by default, and skipped under <see cref="ParseMode.Lenient"/> (nullean/mermaider#121).</summary>
public class MalformedInputTests
{
	private static readonly RenderOptions Lenient = new() { ParseMode = ParseMode.Lenient };

	[Test]
	[Arguments("flowchart LR\n    A[Unclosed --> B\n", 2, "never closed", "']'")]
	[Arguments("flowchart LR\n    A[Start] --> B(Unclosed\n    B --> C\n", 2, "never closed", "')'")]
	[Arguments("flowchart LR\n    A -->\n", 2, "has no target node", "a node id")]
	[Arguments("flowchart LR\n    A --> B garbage\n", 2, "unexpected text 'garbage'", "a link")]
	[Arguments("sequenceDiagram\n    Alice->>\n", 2, "has no target participant", "a participant")]
	[Arguments("sequenceDiagram\n    Alice->>: Hello\n", 2, "has no target participant", "a participant")]
	[Arguments("sequenceDiagram\n    Alice->>Bob\n", 2, "has no text", "message text")]
	[Arguments("sequenceDiagram\n    Alice->>Bob: Hi\n    alt open\n    Bob->>Alice: Yo\n", 3, "'alt' block is never closed", "'end'")]
	[Arguments("stateDiagram-v2\n    [*] -->\n", 2, "has no target state", "a state id")]
	[Arguments("stateDiagram-v2\n    --> B\n", 2, "has no source state", "a state id")]
	[Arguments("flowchart LR\n    subgraph one\n    A --> B\n", 2, "subgraph 'one' is never closed", "'end'")]
	[Arguments("flowchart LR\n    A & --> B\n", 2, "'&' has no node", "a node id")]
	[Arguments("flowchart LR\n    A --> b((B)\n", 2, "never closed", "')'")]
	[Arguments("stateDiagram-v2\n    [*] --> A\n    A -\n", 3, "arrow is incomplete", "'-->'")]
	[Arguments("stateDiagram-v2\n    state First {\n    [*] --> A\n", 2, "composite state 'First' is never closed", "'}'")]
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
		var act = () => MermaidRenderer.RenderSvg("flowchart LR\n    A[Unclosed --> B\n");

		act
			.Should()
			.Throw<MermaidParseException>()
			.Which
			.Message
			.Should()
			.Be("Parse error on line 2: node label opened with '[' is never closed\n  A[Unclosed --> B\n   ^\nExpecting ']' to end the label.");
	}

	[Test]
	[Arguments("flowchart LR\n    A[Unclosed --> B\n")]
	[Arguments("flowchart LR\n    A -->\n")]
	[Arguments("sequenceDiagram\n    Alice->>\n")]
	[Arguments("sequenceDiagram\n    Alice->>Bob: Hi\n    alt open\n    Bob->>Alice: Yo\n")]
	[Arguments("stateDiagram-v2\n    [*] -->\n")]
	public void Lenient_renders_what_it_can(string source) => MermaidRenderer.RenderSvg(source, Lenient).Should().StartWith("<svg");

	[Test]
	public void Parse_api_honours_the_mode()
	{
		var act = () => MermaidRenderer.Parse("flowchart LR\n    A -->\n");
		act.Should().Throw<MermaidParseException>().Which.Line.Should().Be(2);

		MermaidRenderer.Parse("flowchart LR\n    A -->\n", ParseMode.Lenient).Nodes.Should().ContainKey("A");
	}

	[Test]
	public void Critical_option_is_a_divider_not_an_opt_block() =>
		MermaidRenderer
			.RenderSvg("sequenceDiagram\n    critical Connect\n    A->>B: Go\n    option Timeout\n    A->>A: Retry\n    end\n")
			.Should()
			.Contain("Timeout")
			.And
			.NotContain(">ion Timeout<");

	[Test]
	[Arguments("flowchart LR\n    A[Start] --> B(Round) --> C{Pick};\n    A & B --> D\n    A -- text --> D\n    A -->|lbl| E[\"multi\nline\"]\n")]
	[Arguments("sequenceDiagram\n    Alice->>+Bob: Hi\n    Bob-->>-Alice: Yo\n    loop every day\n    Alice->>Bob: Again\n    end\n")]
	[Arguments("flowchart LR\n    A -->\n    B\n    E-\n")]
	[Arguments("stateDiagram-v2\n    [*] --> A\n    A --> B: \n")]
	[Arguments("stateDiagram-v2\n    [*] --> Still\n    Still --> [*]\n    Still --> Moving : go\n")]
	public void Valid_input_stays_accepted(string source) => MermaidRenderer.RenderSvg(source).Should().StartWith("<svg");

	/// <summary>Every diagram type reads <see cref="RenderOptions.ParseMode"/>: strict refuses, lenient renders.</summary>
	[Test]
	[Arguments("pie\n    \"Dogs\" :\n    \"Cats\" : 5\n")]
	[Arguments("classDiagram\n    class Animal {\n    +name\n")]
	[Arguments("erDiagram\n    CUSTOMER ||--o{\n    CUSTOMER { string name }\n")]
	[Arguments("journey\n    title Day\n    Code 5 Me\n    Meet: 3: Me\n")]
	[Arguments("packet\n0-7: \"A\"\n9-15: \"B\"\n")]
	[Arguments("sankey-beta\nA,B\nA,C,3\n")]
	[Arguments("mindmap\n  root\n  second root\n")]
	[Arguments("gantt\n    title Plan\n    dateFormat YYYY-MM-DD\n    a task with no colon\n    Real : a1, 2026-01-01, 1d\n")]
	[Arguments("kanban\n  Todo\n    t1[Unclosed\n")]
	[Arguments("quadrantChart\n    A: [0.5]\n    B: [0.2, 0.3]\n")]
	[Arguments("requirementDiagram\n    element e {\n    type: sim\n")]
	[Arguments("architecture-beta\n    service a(server)[A]\n    service b(server)[B]\n    a -- b\n    a:R -- L:b\n")]
	public void Every_diagram_type_honours_the_parse_mode(string source)
	{
		var strict = () => MermaidRenderer.RenderSvg(source);
		strict.Should().Throw<MermaidParseException>();

		var lenient = () => MermaidRenderer.RenderSvg(source, Lenient);
		lenient.Should().NotThrow();
	}
}
