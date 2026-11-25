using System.Text;
using AwesomeAssertions;
using CsCheck;
using Mermaider.Models;

namespace Mermaider.Tests.Parsing;

/// <summary>
/// Property-based check (CsCheck) of the parse boundary for every diagram type. A valid seed diagram has one line
/// truncated, stripped of its last character, deleted, or given a stray character. Whatever the edit does, the
/// outcome must be an SVG or a <see cref="MermaidParseException"/>: never a <c>KeyNotFoundException</c>, an
/// <c>IndexOutOfRangeException</c> or any other crash wrapped in <see cref="MermaidRenderException"/>.
/// <see cref="ParseMode.Lenient"/> must hold the same line for the types whose lenient path never throws.
/// </summary>
public class MalformedInputFuzzTests
{
	private static readonly string[] Seeds =
	[
		"flowchart LR\n  A[Start] --> B{Pick}\n  B -->|yes| C(Round)\n  B -->|no| D[(Db)]\n  subgraph one\n    C --> E\n  end\n  classDef hot fill:#f00\n  class A hot\n",
		"stateDiagram-v2\n  [*] --> Idle\n  Idle --> Busy : go\n  state Busy {\n    [*] --> Work\n    Work --> [*]\n  }\n  Busy --> [*]\n",
		"sequenceDiagram\n  participant A\n  actor B\n  A->>+B: Request\n  alt ok\n    B-->>-A: Response\n  else bad\n    B--xA: Fail\n  end\n  Note over A,B: done\n",
		"classDiagram\n  class Animal {\n    +String name\n    +eat() void\n  }\n  Animal <|-- Dog\n  Dog --> Bone : chews\n",
		"erDiagram\n  CUSTOMER ||--o{ ORDER : places\n  CUSTOMER {\n    string name\n    int id PK\n  }\n  ORDER {\n    int id PK\n  }\n",
		"requirementDiagram\n  requirement r1 {\n    id: 1\n    text: the text\n    risk: high\n    verifymethod: test\n  }\n  element e1 {\n    type: sim\n  }\n  e1 - satisfies -> r1\n",
		"architecture-beta\n  group api(cloud)[API]\n  service db(database)[Database] in api\n  service srv(server)[Server] in api\n  db:L -- R:srv\n",
		"gantt\n  title Plan\n  dateFormat YYYY-MM-DD\n  section Build\n  Design : a1, 2026-01-01, 3d\n  Code : after a1, 5d\n",
		"journey\n  title Day\n  section Work\n    Code: 5: Me\n    Meet: 2: Me, You\n",
		"timeline\n  title History\n  2020 : Start\n  2021 : Grow : Scale\n",
		"gitGraph\n  commit\n  branch dev\n  checkout dev\n  commit\n  checkout main\n  merge dev\n",
		"kanban\n  Todo\n    t1[Write]\n  Doing\n    t2[Test]\n",
		"pie title Pets\n  \"Dogs\" : 40\n  \"Cats\" : 60\n",
		"quadrantChart\n  title Reach\n  x-axis Low --> High\n  y-axis Small --> Big\n  A: [0.3, 0.6]\n  B: [0.7, 0.2]\n",
		"xychart-beta\n  title \"Sales\"\n  x-axis [jan, feb, mar]\n  y-axis \"Revenue\" 0 --> 100\n  bar [10, 50, 80]\n  line [20, 40, 60]\n",
		"radar-beta\n  title Skills\n  axis a, b, c\n  curve one{1, 2, 3}\n",
		"venn-beta\n  set A\n  set B\n  union A, B\n",
		"sankey-beta\nA,B,5\nB,C,3\n",
		"packet\n0-7: \"Header\"\n8-15: \"Length\"\n",
		"block-beta\n  columns 2\n  A[\"One\"] B[\"Two\"]\n  A --> B\n",
		"C4Container\n  title Containers\n  Person(u, \"User\")\n  Container_Boundary(c1, \"Bank\") {\n    Container(spa, \"SPA\", \"Angular\", \"UI\")\n  }\n  Rel(u, spa, \"Uses\")\n",
		"mindmap\n  root\n    Child one\n      Leaf\n    Child two\n",
		"treemap-beta\n\"A\"\n  \"B\": 30\n  \"C\": 20\n",
		"treeView-beta\n  root\n    child\n      leaf\n",
	];

	private static string Mutate(string seed, int line, int cut, int kind)
	{
		var lines = seed.TrimEnd('\n').Split('\n');
		var i = 1 + line % (lines.Length - 1); // keep the header
		var text = lines[i];
		switch (kind)
		{
			case 0:
				lines[i] = text[..(cut % (text.Length + 1))];
				break;
			case 1:
				lines[i] = text.Length > 0 ? text[..^1] : text;
				break;
			case 2:
				lines = [.. lines[..i], .. lines[(i + 1)..]];
				break;
			default:
				var at = cut % (text.Length + 1);
				lines[i] = text.Insert(at, "([{\"-:>"[cut % 7].ToString());
				break;
		}
		return string.Join('\n', lines) + "\n";
	}

	private static readonly Gen<string> GenMutant = Gen.Select(
		Gen.Int[0, Seeds.Length - 1],
		Gen.Int[0, 999],
		Gen.Int[0, 999],
		Gen.Int[0, 3],
		(seed, line, cut, kind) => Mutate(Seeds[seed], line, cut, kind)
	);

	private static void AssertRendersOrRefuses(string source, ParseMode mode)
	{
		try
		{
			_ = MermaidRenderer.RenderSvg(source, new RenderOptions { ParseMode = mode });
		}
		catch (MermaidParseException ex)
		{
			ex.Message.Should().NotBeNullOrWhiteSpace();
			if (mode == ParseMode.Strict && ex.SourceLine is not null)
				ex.Message.Should().Contain(ex.SourceLine.Split('\n')[0]);
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"{mode} parse of this input threw {ex.GetType().Name}: {ex.Message}\n---\n{source}", ex);
		}
	}

	[Test]
	public void Seeds_are_valid() =>
		Seeds.Select(s => (Seed: s, Err: Try(s))).Where(x => x.Err is not null).Select(x => $"{x.Seed.Split('\n')[0]}: {x.Err}")
			.Should().BeEmpty();

	private static string? Try(string s)
	{
		try
		{
			_ = MermaidRenderer.RenderSvg(s);
			return null;
		}
		catch (Exception ex)
		{
			return ex.Message.Split('\n')[0];
		}
	}

	[Test]
	public void Strict_mode_renders_or_throws_a_parse_exception() =>
		GenMutant.Sample(source => AssertRendersOrRefuses(source, ParseMode.Strict), iter: 3_000);

	[Test]
	public void Lenient_mode_renders_or_throws_a_parse_exception() =>
		GenMutant.Sample(source => AssertRendersOrRefuses(source, ParseMode.Lenient), iter: 3_000);

	[Test]
	public void Lenient_mode_never_refuses_flowchart_state_or_sequence() =>
		Gen.Select(Gen.Int[0, 2], Gen.Int[0, 999], Gen.Int[0, 999], Gen.Int[0, 3], (seed, line, cut, kind) => Mutate(Seeds[seed], line, cut, kind))
			.Sample(
				source =>
				{
					var act = () => MermaidRenderer.RenderSvg(source, new RenderOptions { ParseMode = ParseMode.Lenient });
					act.Should().NotThrow<MermaidParseException>($"lenient mode skips what it cannot read:\n{source}");
				},
				iter: 2_000
			);
}
