using BenchmarkDotNet.Attributes;
using Mermaid;
using Mermaid.Layout.Msagl;
using Mermaid.Models;
using Mermaid.Parsing;
using Mermaid.Rendering;
using Mermaid.Theming;
using ML = Sugiyama;

namespace Mermaid.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class RenderBenchmarks
{
	private const string SimpleFlowchart = """
		graph TD
		A[Start] --> B{Decision}
		B -->|Yes| C[OK]
		B -->|No| D[Cancel]
		C --> E[End]
		D --> E
		""";

	private const string LargeFlowchart = """
		graph TD
		A[Entry] --> B[Validate]
		B --> C{Valid?}
		C -->|Yes| D[Process]
		C -->|No| E[Reject]
		D --> F[Transform]
		F --> G[Enrich]
		G --> H{Quality?}
		H -->|Pass| I[Store]
		H -->|Fail| J[Retry]
		J --> D
		I --> K[Index]
		K --> L[Notify]
		L --> M[Complete]
		E --> N[Log Error]
		N --> O[Alert]
		O --> P[End]
		M --> P
		""";

	private const string SequenceDiagram = """
		sequenceDiagram
		participant C as Client
		participant S as Server
		participant D as Database
		C->>S: POST /api/data
		S->>+D: INSERT INTO table
		D-->>-S: OK
		S-->>C: 201 Created
		C->>S: GET /api/data/1
		S->>+D: SELECT * FROM table
		D-->>-S: Row
		S-->>C: 200 OK
		""";

	private const string ClassDiagram = """
		classDiagram
		class Animal {
		+String name
		+int age
		+eat() void
		+sleep() void
		}
		class Dog {
		+String breed
		+bark() void
		}
		class Cat {
		+String color
		+meow() void
		}
		Animal <|-- Dog
		Animal <|-- Cat
		""";

	private const string StateDiagram = """
		stateDiagram-v2
		[*] --> Idle
		Idle --> Processing : submit
		Processing --> Success : ok
		Processing --> Failed : error
		Success --> [*]
		Failed --> Idle : retry
		""";

	private const string ErDiagram = """
		erDiagram
		CUSTOMER ||--o{ ORDER : places
		ORDER ||--|{ LINE_ITEM : contains
		CUSTOMER {
		string name PK
		string email UK
		int age
		}
		ORDER {
		int id PK
		date created
		string status
		}
		LINE_ITEM {
		int id PK
		int quantity
		float price
		}
		""";

	[Benchmark(Baseline = true)]
	public string Flowchart_Simple() => MermaidRenderer.RenderSvg(SimpleFlowchart);

	[Benchmark]
	public string Flowchart_Large() => MermaidRenderer.RenderSvg(LargeFlowchart);

	[Benchmark]
	public string Sequence() => MermaidRenderer.RenderSvg(SequenceDiagram);

	[Benchmark]
	public string State() => MermaidRenderer.RenderSvg(StateDiagram);

	[Benchmark]
	public string Class() => MermaidRenderer.RenderSvg(ClassDiagram);

	[Benchmark]
	public string Er() => MermaidRenderer.RenderSvg(ErDiagram);
}

/// <summary>
/// Isolates Parse, Layout, and Render phases for the simple flowchart
/// to identify where allocations come from.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class PhaseBenchmarks
{
	private static readonly MsaglLayoutProvider MsaglProvider = new();

	private static readonly string[] Lines = SimpleFlowchart
		.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("%%")).ToArray();

	private static readonly MermaidGraph ParsedGraph = FlowchartParser.Parse(Lines);
	private static readonly PositionedGraph MsaglLayoutResult = MsaglProvider.LayoutFlowchart(ParsedGraph);
	private static readonly DiagramColors Colors = new() { Bg = "#FFFFFF", Fg = "#27272A" };

	private const string SimpleFlowchart = """
		graph TD
		A[Start] --> B{Decision}
		B -->|Yes| C[OK]
		B -->|No| D[Cancel]
		C --> E[End]
		D --> E
		""";

	private static readonly ML.LayoutGraph LightweightInput = BuildLightweightInput();

	private static ML.LayoutGraph BuildLightweightInput()
	{
		var nodes = new List<ML.LayoutNode>();
		foreach (var (id, node) in ParsedGraph.Nodes)
		{
			var (w, h) = NodeSizing.Estimate(node.Label, node.Shape);
			nodes.Add(new ML.LayoutNode(id, w, h));
		}
		var edges = ParsedGraph.Edges.Select(e => new ML.LayoutEdge(e.Source, e.Target)).ToList();
		return new ML.LayoutGraph(ML.LayoutDirection.TD, nodes, edges, []);
	}

	[Benchmark]
	public MermaidGraph Parse() => FlowchartParser.Parse(Lines);

	[Benchmark]
	public PositionedGraph Layout_Msagl() => MsaglProvider.LayoutFlowchart(ParsedGraph);

	[Benchmark]
	public ML.LayoutResult Layout_Lightweight() => ML.SugiyamaLayout.Compute(LightweightInput);

	[Benchmark]
	public string Render() => SvgRenderer.Render(MsaglLayoutResult, Colors, "Inter", false);

	[Benchmark]
	public string EndToEnd_Msagl() => MermaidRenderer.RenderSvg(SimpleFlowchart,
		new RenderOptions { LayoutProvider = MsaglProvider });

	[Benchmark]
	public string EndToEnd_Lightweight() => MermaidRenderer.RenderSvg(SimpleFlowchart);
}
