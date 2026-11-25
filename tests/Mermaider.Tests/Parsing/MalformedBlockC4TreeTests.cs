using AwesomeAssertions;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

/// <summary>
/// Strict parsing of block, C4, treemap, mindmap and treeView: input that Mermaid's own parser refuses is refused with a
/// located message, and skipped under lenient parsing. Each refused case was checked against mermaid 12.3's parser.
/// </summary>
public class MalformedBlockC4TreeTests
{
	[Test]
	// block
	[Arguments("block-beta\n  columns 2\n  A[\"x\n", 3, "quoted label is never closed", "closing")]
	[Arguments("block-beta\n  A[\"x\"\n", 2, "opened with '['", "']'")]
	[Arguments("block-beta\n  A(\"x\"\n  B[\"y\"]\n", 2, "opened with '('", "')'")]
	[Arguments("block-beta\n  A[\"x\"]\n  A -->\n", 3, "arrow has no target block", "a block id after the arrow")]
	[Arguments("block-beta\n  A -\n  B\n", 2, "arrow is incomplete", "'-->'")]
	[Arguments("block-beta\n  A\n  --> B\n", 3, "has no source block", "a block id before the arrow")]
	[Arguments("block-beta\n  A:\n", 2, "':' is not followed by a column span", "a number after ':'")]
	[Arguments("block-beta\n  block:g\n    A\n", 2, "'block:g' group is never closed", "'end'")]
	[Arguments("block-beta\n  A\n  end\n", 3, "'end' has no matching 'block'", "'block'")]
	// mindmap
	[Arguments("mindmap\n    Topic A\n    Topic B\n", 3, "second root", "an indent deeper than the root's")]
	[Arguments("mindmap\n  root\n    child\n  other\n", 4, "second root", "an indent deeper")]
	[Arguments("mindmap\n  root[Square\n", 2, "never closed", "']'")]
	[Arguments("mindmap\n  root((Circ\n", 2, "never closed", "')'")]
	[Arguments("mindmap\n  root\n    a{{hex}\n", 3, "never closed", "'}}'")]
	[Arguments("mindmap\n  root\n    a)bang\n", 3, "never closed", "'('")]
	[Arguments("mindmap\n  root\n    a[]\n", 3, "empty label", "a label")]
	[Arguments("mindmap\n  root\n    a (b) c\n", 3, "never closed, or has text after it", "')'")]
	[Arguments("mindmap\n  root\n    ::icon(fa\n", 3, "icon decoration is never closed", "')'")]
	// treemap
	[Arguments("treemap-beta\n\"Engineering\": 50\n\"Market\n\"Sales\": 15\n", 3, "quoted name is never closed", "closing")]
	[Arguments("treemap-beta\n\"A\":\n", 2, "value after ':' is missing", "a non-negative number")]
	[Arguments("treemap-beta\n\"A\": x\n", 2, "'x' is not a number", "a non-negative number")]
	[Arguments("treemap-beta\n\"A\": -1\n", 2, "'-1' is not a number", "a non-negative number")]
	[Arguments("treemap-beta\n\"A\" 1\n", 2, "unexpected text '1'", "': value'")]
	[Arguments("treemap-beta\n\"A\": 1 junk\n", 2, "unexpected text 'junk'", "':::class'")]
	// treeView
	[Arguments("treeView-beta\n    app.ts icon(file:code\n", 2, "icon( is never closed", "')'")]
	[Arguments("treeView-beta\n    app.ts icon(foo bar)\n", 2, "icon name 'foo bar' is not valid", "icon(pack:name)")]
	[Arguments("treeView-beta\n    app.ts icon(file:)\n", 2, "icon name 'file:' is not valid", "icon(name)")]
	[Arguments("treeView-beta\n    \"a b\n", 2, "quoted name is never closed", "closing")]
	[Arguments("treeView-beta\n    ## just a note\n", 2, "node has no name", "a file or directory name")]
	[Arguments("treeView-beta\n    icon(file:code)\n", 2, "node has no name", "a file or directory name")]
	[Arguments("treeView-beta\n├──\n└── main.ts\n", 2, "no name after the branch", "a file or directory name")]
	// C4
	[Arguments("C4Context\nPerson(a, \"A\"\n", 2, "Person( is never closed", "')'")]
	[Arguments("C4Context\nPerson(a, \"A\")\nRel(a, b, \"x\"\n", 3, "Rel( is never closed", "')'")]
	[Arguments("C4Context\nPerson(a, \"A\")\nRel(a, b)\n", 3, "Rel() has no label", "Rel(from, to, \"Label\")")]
	[Arguments("C4Context\nPerson(a, \"A\")\nRel(a)\n", 3, "Rel() has no target", "Rel(from, to, \"Label\")")]
	[Arguments("C4Context\nPerson(a)\n", 2, "Person() has no label", "Person(alias, \"Label\")")]
	[Arguments("C4Context\nPerson(a, \"A\")\nRel(a, b, x)\n", 3, "argument 'x' is not in double quotes", "quoted")]
	[Arguments("C4Context\nFoo(a, \"A\")\n", 2, "unknown statement 'Foo'", "a C4 statement")]
	[Arguments("C4Context\nPer\n", 2, "unknown statement 'Per'", "a C4 statement")]
	[Arguments("C4Context\nperson(a, \"A\")\n", 2, "unknown statement 'person'", "a C4 statement")]
	[Arguments("C4Context\nPerson (a, \"A\")\n", 2, "Person is not followed by '('", "'(' straight after Person")]
	[Arguments("C4Context\nPerson(a, \"A\") junk\n", 2, "unexpected text 'junk'", "the end of the statement")]
	[Arguments("C4Context\nPerson(a, \"A\")\n}\n", 3, "'}' has no matching '{'", "Boundary")]
	[Arguments("C4Context\nBoundary(b, \"B\") {\n  Person(a, \"A\")\n", 2, "boundary is never closed", "'}'")]
	[Arguments("C4Context\nBoundary(b, \"B\")\nPerson(a, \"A\")\n", 2, "Boundary() has no body", "'{'")]
	[Arguments("C4Context\nBoundary(b, \"B\") {\n}\n", 2, "boundary has no elements", "at least one element")]
	[Arguments("C4Context\nPerson(a, \"A\", $tags=)\n", 2, "argument '$tags=' has no value", "$name=")]
	[Arguments("C4Context\nUpdateRelStyle(a, b)\n", 2, "UpdateRelStyle() has no label", "UpdateRelStyle")]
	public void Strict_by_default_throws_with_line_and_expectation(string source, int line, string problem, string expected)
	{
		var act = () => MermaidRenderer.RenderSvg(source);

		var ex = act.Should().Throw<MermaidParseException>().Which;
		ex.Line.Should().Be(line);
		ex.Message.Should().Contain($"on line {line}").And.Contain(problem).And.Contain("Expecting").And.Contain(expected);
		ex.SourceLine.Should().NotBeNullOrEmpty();
		ex.Column.Should().BeGreaterThan(0);
	}

	[Test]
	public void Message_points_a_caret_at_the_column()
	{
		var act = () => MermaidRenderer.RenderSvg("mindmap\n  root\n    child[open\n");

		act
			.Should()
			.Throw<MermaidParseException>()
			.Which
			.Message
			.Should()
			.Be(
				"Parse error on line 3: node shape '[' ... ']' is never closed, or has text after it\n  child[open\n       ^\nExpecting ']' at the end of the node."
			);
	}

	// Each of these is accepted by mermaid 12.3, so strict parsing must accept it too.
	[Test]
	[Arguments("block-beta\n  columns 2\n  A[\"x\"] B[\"y\"]\n  A --> B\n")]
	[Arguments("block-beta\n  A[\"first\nsecond\"] B\n")]
	[Arguments("block-beta\n  A -->\n  B\n")]
	[Arguments("block-beta\n  columns 3\n  A:2 B\n  space:2 C\n")]
	[Arguments("block-beta\n  block:g\n    A\n  end\n  B\n")]
	[Arguments("block-beta\n  A -->|label| B\n")]
	[Arguments("mindmap\n  root((Central))\n    Topic A\n      Leaf\n    Topic B\n")]
	[Arguments("mindmap\n  root\n    a[first\n    last]\n")]
	[Arguments("mindmap\n  root\n    a)bang(\n    b))cloud((\n    c{{hex}}\n")]
	[Arguments("mindmap\n  root\n    a b]\n    ::icon(fa fa-book)\n    :::cls\n")]
	[Arguments("treemap-beta\n\"A\": 1.5\n\"B\": 1,000\n\"C\": 5:::cls\n\"D\"\n  \"E\": 2\n")]
	[Arguments("treemap-beta\n'A': 1\n\"B\" \"C\"\n")]
	[Arguments("treeView-beta\n    src/ icon(folder-open)\n        app.ts icon(file:code) ## main entry :::hot\n        \"my file.md\"\n")]
	[Arguments("treeView-beta\n├── a\n│\n│   └── b icon(none)\n└── c\n")]
	[Arguments("C4Context\ntitle T\nPerson(a, \"A\")\nPerson_Ext(b, \"B\", $tags=\"x\")\nRel(a, b, \"Uses\", \"HTTPS\")\nBoundary(c, \"C\") {\n  System(s, \"S\")\n}\nUpdateLayoutConfig($c4ShapeInRow=\"3\")\n")]
	[Arguments("C4Container\nContainer_Boundary(b, \"B\")\n{\n  Container(c, \"C\", \"tech\", \"descr\")\n}\n")]
	[Arguments("C4Context\nPerson(a, \"A\"\nPerson(b, \"B\")\n")]
	public void Strict_accepts_what_mermaid_accepts(string source)
	{
		var act = () => MermaidRenderer.RenderSvg(source);

		act.Should().NotThrow();
	}

	[Test]
	[Arguments("block-beta\n  A[\"x\"\n  B[\"y\"]")]
	[Arguments("block-beta\n  A -\n  B")]
	[Arguments("block-beta\n  block:g\n    A")]
	[Arguments("block-beta\n  A:\n  B")]
	public void Lenient_block_skips_what_strict_refuses(string source)
	{
		var lines = source.Split('\n').Select(l => l.Trim()).ToArray();

		var strict = () => BlockParser.Parse(lines);
		var lenient = () => BlockParser.Parse(lines, strict: false);

		strict.Should().Throw<MermaidParseException>();
		lenient.Should().NotThrow();
	}

	[Test]
	[Arguments("C4Context\nPerson(a, \"A\"")]
	[Arguments("C4Context\nPerson(a, \"A\")\nRel(a, b)")]
	[Arguments("C4Context\nFoo(a, \"A\")\nPerson(a, \"A\")")]
	[Arguments("C4Context\nBoundary(b, \"B\") {\nPerson(a, \"A\")")]
	[Arguments("C4Context\nPerson(a, \"A\")\n}")]
	public void Lenient_c4_skips_what_strict_refuses(string source)
	{
		var lines = source.Split('\n').Select(l => l.Trim()).ToArray();

		var strict = () => C4Parser.Parse(lines);
		var lenient = () => C4Parser.Parse(lines, strict: false);

		strict.Should().Throw<MermaidParseException>();
		lenient.Should().NotThrow();
	}

	[Test]
	[Arguments("treemap-beta\n\"A\n")]
	[Arguments("treemap-beta\n\"A\": x\n")]
	[Arguments("treemap-beta\n\"A\" 1\n")]
	public void Lenient_treemap_skips_what_strict_refuses(string source)
	{
		var lines = source.TrimEnd('\n').Split('\n');

		var strict = () => TreemapParser.Parse(lines);
		var lenient = () => TreemapParser.Parse(lines, strict: false);

		strict.Should().Throw<MermaidParseException>();
		lenient.Should().NotThrow();
	}

	[Test]
	[Arguments("mindmap\n    Topic A\n    Topic B")]
	[Arguments("mindmap\n  root[Square")]
	[Arguments("mindmap\n  root\n    a[]")]
	public void Lenient_mindmap_skips_what_strict_refuses(string source)
	{
		var lines = source.Split('\n');

		var strict = () => MindmapParser.Parse(lines);
		var lenient = () => MindmapParser.Parse(lines, strict: false);

		strict.Should().Throw<MermaidParseException>();
		lenient.Should().NotThrow();
	}

	[Test]
	[Arguments("treeView-beta\n    app.ts icon(file:code")]
	[Arguments("treeView-beta\n    \"a b")]
	[Arguments("treeView-beta\n├──\n└── b")]
	[Arguments("treeView-beta\n    ## note")]
	public void Lenient_treeView_skips_what_strict_refuses(string source)
	{
		var lines = source.Split('\n');

		var strict = () => TreeViewParser.Parse(lines);
		var lenient = () => TreeViewParser.Parse(lines, strict: false);

		strict.Should().Throw<MermaidParseException>();
		lenient.Should().NotThrow();
	}
}
