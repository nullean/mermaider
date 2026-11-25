using AwesomeAssertions;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

/// <summary>
/// Class, ER, requirement and architecture diagrams that Mermaid itself refuses are refused with a located message by default;
/// the parsers still skip them with <c>strict: false</c>. Every case here was checked against mermaid.parse (12.3).
/// </summary>
public class MalformedClassErReqArchTests
{
	private static string[] Lines(string source) => source.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();

	[Test]
	// class
	[Arguments("classDiagram\n    class Animal\n    Animal <|--\n", 3, "has no target class", "a class name after the arrow")]
	[Arguments("classDiagram\n    class Dog\n    <|-- Dog\n", 3, "has no source class", "a class name before the arrow")]
	[Arguments("classDiagram\n    class A\n    class B\n    A <|- B\n", 4, "arrow '<|-' is not valid", "an arrow such as")]
	[Arguments("classDiagram\n    class A\n    class B\n    A ---> B\n", 4, "arrow '--->' is not valid", "an arrow such as")]
	[Arguments("classDiagram\n    A --> B --> C\n", 2, "a second arrow '-->'", "one relationship per line")]
	[Arguments("classDiagram\n    Customer \"1\" --> \"0.\n", 2, "quoted cardinality is not closed", "a closing")]
	[Arguments("classDiagram\n    Customer \"1\"\n", 2, "a cardinality needs an arrow", "an arrow after the cardinality")]
	[Arguments("classDiagram\n    class Animal {\n        +String name\n", 2, "class 'Animal' body is never closed", "'}' to close the class")]
	[Arguments("classDiagram\n    class A {\n        +x\n    class B {\n        +y\n    }\n", 2, "class 'A' is never closed before the next class starts", "'}' to close the class")]
	[Arguments("classDiagram\n    }\n    class A\n", 2, "'}' closes no class or namespace", "a class or namespace block")]
	[Arguments("classDiagram\n    class A {\n        +x {readonly}\n    }\n", 3, "unexpected '{' inside the body of class 'A'", "a member without braces")]
	[Arguments("classDiagram\n    class\n    class A\n", 2, "class has no name", "a class name after 'class'")]
	[Arguments("classDiagram\n    class A~T\n", 2, "generic type is not closed", "a closing '~'")]
	[Arguments("classDiagram\n    note for Animal \"Can be wild\n", 2, "note text is not closed", "'note \"text\"'")]
	[Arguments("classDiagram\n    namespace Domain {\n        class A\n", 2, "namespace 'Domain' is never closed", "'}' to close the namespace")]
	[Arguments("classDiagram\n    namespace Domain {\n    }\n", 3, "namespace 'Domain' is empty", "at least one class")]
	[Arguments("classDiagram\n    namespace Domain {\n        class A\n        class B\n        A --> B\n    }\n", 5, "relationship cannot be declared inside a namespace", "relationships after its closing")]
	// er
	[Arguments("erDiagram\n    CUSTOMER ||--o{ ORDER : places\n    ORDER ||\n", 3, "no line between its cardinalities", "'--' (identifying)")]
	[Arguments("erDiagram\n    A ||--o{\n", 2, "relationship has no target entity", "an entity name after the cardinalities")]
	[Arguments("erDiagram\n    A |--| B : x\n", 2, "cardinality '|' is not valid", "a cardinality marker")]
	[Arguments("erDiagram\n    A {|--|{ B : x\n", 2, "cardinality '{|' is not valid", "a cardinality marker")]
	[Arguments("erDiagram\n    A ||==o{ B : x\n", 2, "no line between its cardinalities", "'--' (identifying)")]
	[Arguments("erDiagram\n    A ||--o{ B : \"unclosed\n", 2, "label has an unclosed quote", "closing")]
	[Arguments("erDiagram\n    A ||--o{ B :\n", 2, "label is empty", "label text")]
	[Arguments("erDiagram\n    A ||--o{ B C : x\n", 2, "unexpected text 'C : x'", "': label'")]
	[Arguments("erDiagram\n    USER {\n        int id PK\n", 2, "entity 'USER' block is never closed", "'}' to close the entity block")]
	[Arguments("erDiagram\n    USER {\n        int\n    }\n", 3, "attribute 'int' has no name", "'type name'")]
	[Arguments("erDiagram\n    USER {\n        int id BOGUS\n    }\n", 3, "'BOGUS' is not a key list", "PK, FK or UK")]
	[Arguments("erDiagram\n    USER {\n        int id PK FK\n    }\n", 3, "'PK FK' is not a key list", "comma separated")]
	[Arguments("erDiagram\n    USER {\n        string name \"unclosed\n    }\n", 3, "attribute comment has an unclosed quote", "a single")]
	[Arguments("erDiagram\n    USER {\n        string name \"a\" PK\n    }\n", 3, "attribute comment must be one quoted string at the end", "a single")]
	[Arguments("erDiagram\n    USER {\n        1int id\n    }\n", 3, "attribute type '1int' is not valid", "a type such as")]
	[Arguments("erDiagram\n    A ||--o{ B : x\n    }\n", 3, "'}' closes no entity block", "an entity block")]
	[Arguments("erDiagram\n    A[\"Alias\" {\n        int id\n    }\n", 2, "entity alias is malformed", "an alias in square brackets")]
	// requirement
	[Arguments("requirementDiagram\n    requirement r {\n        id: 1\n", 2, "block 'requirement r' is never closed", "'}' to close the block")]
	[Arguments("requirementDiagram\n    element e {\n        type: x\n", 2, "block 'element e' is never closed", "'}' to close the block")]
	[Arguments("requirementDiagram\n    requirement r {\n        risk: hig\n    }\n", 3, "risk 'hig' is not a known level", "low, medium or high")]
	[Arguments("requirementDiagram\n    requirement r {\n        verifymethod: inspect\n    }\n", 3, "verifymethod 'inspect' is not a known method", "analysis, demonstration, inspection or test")]
	[Arguments("requirementDiagram\n    requirement r {\n        id\n    }\n", 3, "'id' is not a requirement property", "'key: value'")]
	[Arguments("requirementDiagram\n    requirement r {\n        id:\n    }\n", 3, "is not a requirement property", "'key: value'")]
	[Arguments("requirementDiagram\n    requirement r {\n        foo: bar\n    }\n", 3, "unknown requirement property 'foo'", "id, text, risk or verifymethod")]
	[Arguments("requirementDiagram\n    element e {\n        id: x\n    }\n", 3, "unknown element property 'id'", "type or docref")]
	[Arguments("requirementDiagram\n    requirement r {\n        text: \"unclosed\n    }\n", 3, "quoted value for 'text' is malformed", "matching double quotes")]
	[Arguments("requirementDiagram\n    direction XX\n", 2, "direction is not TB, BT, LR or RL", "TB, BT, LR or RL")]
	[Arguments("requirementDiagram\n    a - bogus -> b\n", 2, "relationship is incomplete or has an unknown type", "contains, copies, derives")]
	[Arguments("requirementDiagram\n    a - verifies ->\n", 2, "relationship is incomplete or has an unknown type", "<source> - <type> -> <target>")]
	[Arguments("requirementDiagram\n    }\n    requirement r {\n    id: 1\n    }\n", 2, "unexpected text '}'", "a requirement or element block")]
	[Arguments("requirementDiagram\n    element\n", 2, "unexpected text 'element'", "a requirement or element block")]
	// architecture
	[Arguments("architecture-beta\n    service a(server)[A]\n    service b(server)[B]\n    a:R -- L:c\n", 4, "edge refers to 'c', which is not declared", "the id of a declared group")]
	[Arguments("architecture-beta\n    service a(server)[A]\n    service b(server)[B]\n    a -- b\n", 4, "edge has no side on 'a'", "a:R")]
	[Arguments("architecture-beta\n    service a(server)[A]\n    service b(server)[B]\n    a:R -- b\n", 4, "edge has no side on 'b'", "L:b")]
	[Arguments("architecture-beta\n    service a(server)[A]\n    service b(server)[B]\n    a:X -- L:b\n", 4, "side 'X' is not valid", "L, R, T or B")]
	[Arguments("architecture-beta\n    service a(server)[A]\n    service b(server)[B]\n    a:R - L:b\n", 4, "edge arrow '-' is not valid", "'--', '-->'")]
	[Arguments("architecture-beta\n    service a(server)[A]\n    service b(server)[B]\n    a:R --\n", 4, "edge has no target", "a side and a target id")]
	[Arguments("architecture-beta\n    service a(server)[A]\n    service b(server)[B]\n    a:R -- L:\n", 4, "edge has no target", "a side and a target id")]
	[Arguments("architecture-beta\n    service a(server)[A]\n    service b(server)[B]\n    a:R -- L:b extra\n", 4, "unexpected text 'extra' after the edge", "the end of the edge")]
	[Arguments("architecture-beta\n    service a(server)[A] in g\n", 2, "is in group 'g', which is not declared", "a declared group")]
	[Arguments("architecture-beta\n    service a(server)[A]\n    service b(server)[B] in a\n", 3, "which is a service and not a group", "the id of a group")]
	[Arguments("architecture-beta\n    group g(cloud)[G]\n    service g(server)[A]\n", 3, "service id 'g' is already used by a group", "a unique id")]
	[Arguments("architecture-beta\n    group g(cloud)[G] in g\n", 2, "cannot be placed in itself", "a different group")]
	[Arguments("architecture-beta\n    service a(server)[A] in\n", 2, "service has 'in' but no group name", "service id(icon)[Title]")]
	[Arguments("architecture-beta\n    service a(server)[]\n", 2, "service has an empty title", "a title between the brackets")]
	[Arguments("architecture-beta\n    service a()[A]\n", 2, "service has an empty icon", "an icon name")]
	[Arguments("architecture-beta\n    service a(server)[A] garbage\n", 2, "service declaration is malformed", "service id(icon)[Title]")]
	[Arguments("architecture-beta\n    group g(cloud)[G] garbage\n", 2, "group declaration is malformed", "group id(icon)[Title]")]
	[Arguments("architecture-beta\n    service a(server)[A]\n    junction\n", 3, "junction declaration is malformed", "junction id")]
	[Arguments("architecture-beta\n    garbage\n", 2, "unexpected text 'garbage'", "a 'group', 'service' or 'junction' declaration")]
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
		var act = () => MermaidRenderer.RenderSvg("erDiagram\n    A ||==o{ B : x\n");

		act
			.Should()
			.Throw<MermaidParseException>()
			.Which
			.Message
			.Should()
			.Be(
				"Parse error on line 2: relationship has no line between its cardinalities\n  A ||==o{ B : x\n      ^\nExpecting '--' (identifying) or '..' (non-identifying) between the cardinalities, e.g. 'A ||--o{ B : places'."
			);
	}

	[Test]
	[Arguments("classDiagram\nclass Animal\nAnimal <|--\n", "class")]
	[Arguments("classDiagram\nclass A {\n+x\n", "class")]
	[Arguments("classDiagram\nnamespace N {\nclass A\n", "class")]
	[Arguments("erDiagram\nA ||--o{\n", "er")]
	[Arguments("erDiagram\nUSER {\nint id BOGUS\n}\n", "er")]
	[Arguments("requirementDiagram\nrequirement r {\nid: 1\n", "requirement")]
	[Arguments("requirementDiagram\nrequirement r {\nrisk: hig\n}\n", "requirement")]
	[Arguments("architecture-beta\nservice a(server)[A]\nservice b(server)[B]\na:R -- L:c\n", "architecture")]
	[Arguments("architecture-beta\nservice a(server)[A]\nservice b(server)[B]\na -- b\n", "architecture")]
	public void Lenient_parsers_skip_what_they_cannot_read(string source, string kind)
	{
		var lines = Lines(source);

		switch (kind)
		{
			case "class":
				ClassParser.Parse(lines, strict: false).Should().NotBeNull();
				break;
			case "er":
				ErParser.Parse(lines, strict: false).Should().NotBeNull();
				break;
			case "requirement":
				RequirementParser.Parse(lines, strict: false).Should().NotBeNull();
				break;
			default:
				ArchitectureParser.Parse(lines, strict: false).Should().NotBeNull();
				break;
		}

		var strict = () =>
		{
			switch (kind)
			{
				case "class":
					ClassParser.Parse(lines);
					break;
				case "er":
					ErParser.Parse(lines);
					break;
				case "requirement":
					RequirementParser.Parse(lines);
					break;
				default:
					ArchitectureParser.Parse(lines);
					break;
			}
		};
		strict.Should().Throw<MermaidParseException>();
	}

	[Test]
	[Arguments("classDiagram\n    class A\n    class B\n    A <|-- B\n    A *-- B : has\n    A \"1\" --> \"*\" B : owns\n    A <|--- B\n    A --() B\n    A <|--|> B\n    A -- B : x -- y\n")]
	[Arguments("classDiagram\n    class Animal {\n        <<abstract>>\n        +String name\n        +eat() void\n    }\n    class Dog[\"The dog\"] {\n        +bark() void\n    }\n    Animal <|-- Dog %% inherits\n")]
	[Arguments("classDiagram\n    namespace Zoo {\n        class Animal\n        class Keeper\n    }\n    Keeper --> Animal\n")]
	[Arguments("classDiagram\n    class A\n    note for A \"hello\"\n    note \"free\"\n    A : +x int\n    A : +y()\n")]
	[Arguments("classDiagram\n    class List~T~ {\n        +add(T item) void\n    }\n    List~T~ --> Item\n")]
	[Arguments("erDiagram\n    A ||--o{ B : places\n    B }|..|{ C : \"has many\"\n    C |o--o| D : x y z\n    A ||.-o{ E : mixed\n    D }o--|| A : back\n")]
	[Arguments("erDiagram\n    USER {\n        int id PK, FK \"the key\"\n        string name UK\n        varchar(255) email\n    }\n    \"Order Item\" {\n        int id\n    }\n    ITEM[\"Line item\"] {\n        int id\n    }\n")]
	[Arguments("erDiagram\n    A {\n        int id\n    } \n    B ||--o{ A : x\n")]
	[Arguments("requirementDiagram\n    requirement r {\n        id: 1\n        text: \"the text\"\n        risk: High\n        verifymethod: Test\n    }\n    functionalRequirement f {\n        id: 2\n    }\n    element e {\n        type: simulation\n        docRef: a/b\n    }\n    e - satisfies -> r\n    f <- derives - r\n")]
	[Arguments("requirementDiagram\n    direction LR\n    requirement \"my req\" { %% a comment\n        id: 1\n    }\n    a-verifies->b\n    classDef c fill:#f00\n")]
	[Arguments("architecture-beta\n    group g(cloud)[G]\n    group h(cloud)[H] in g\n    service a(server)[A] in h\n    service b(server)[B]\n    junction j\n    a:R -- L:j\n    j:R --> L:b %% note\n    a :B <-- T : b\n")]
	[Arguments("architecture-beta\n    service a[A]\n    service b(server)\n    service c\n    a:R -- L:b\n    b:R -- L:c\n")]
	public void Valid_input_stays_accepted(string source) => MermaidRenderer.RenderSvg(source).Should().StartWith("<svg");

	[Test]
	public void ER_relationship_without_a_label_is_still_rendered() =>
		MermaidRenderer.RenderSvg("erDiagram\n    CUSTOMER ||--o{ ORDER\n").Should().StartWith("<svg");

	[Test]
	public void Class_label_and_er_dash_dot_arrows_are_read()
	{
		var diagram = ClassParser.Parse(Lines("classDiagram\nclass Dog[\"The dog\"]\nclass Cat[\"The cat\"] {\n+meow() void\n}\n"));
		diagram.Classes.Select(c => c.Label).Should().Equal("The dog", "The cat");

		var er = ErParser.Parse(Lines("erDiagram\nA ||.-o{ B : x\nA ||-.o{ C : y\nA ||--o{ D : z\nA ||..o{ E : w\n"));
		er.Relationships.Select(r => r.Identifying).Should().Equal(false, false, true, false);
	}

	[Test]
	public void Architecture_target_side_after_the_id_is_read_as_a_side()
	{
		var diagram = ArchitectureParser.Parse(Lines("architecture-beta\nservice a(server)[A]\nservice b(server)[B]\na:R --> b:L\n"));
		diagram.Edges.Should().ContainSingle().Which.TargetId.Should().Be("b");
	}
}
