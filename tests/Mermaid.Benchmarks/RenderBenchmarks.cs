using BenchmarkDotNet.Attributes;
using Mermaid;

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
	public string Class() => MermaidRenderer.RenderSvg(ClassDiagram);

	[Benchmark]
	public string Er() => MermaidRenderer.RenderSvg(ErDiagram);
}
