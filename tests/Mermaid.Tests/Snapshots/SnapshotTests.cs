using VerifyTUnit;

namespace Mermaid.Tests.Snapshots;

public class SnapshotTests
{
	[Test]
	public Task Flowchart_simple() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph TD
			A[Start] --> B{Decision}
			B -->|Yes| C[OK]
			B -->|No| D[Cancel]
			"""), "svg");

	[Test]
	public Task Flowchart_all_shapes() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph LR
			A[Rectangle] --> B(Rounded)
			B --> C([Stadium])
			C --> D{Diamond}
			D --> E((Circle))
			E --> F>Asymmetric]
			"""), "svg");

	[Test]
	public Task Flowchart_subgraph() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph TD
			subgraph Backend
			A[API] --> B[DB]
			end
			subgraph Frontend
			C[UI] --> D[State]
			end
			C --> A
			"""), "svg");

	[Test]
	public Task Flowchart_edge_styles() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph LR
			A --> B
			A -.-> C
			A ==> D
			"""), "svg");

	[Test]
	public Task State_diagram() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			stateDiagram-v2
			[*] --> Idle
			Idle --> Processing : start
			Processing --> Done : complete
			Done --> [*]
			"""), "svg");

	[Test]
	public Task Sequence_basic() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			sequenceDiagram
			participant A as Alice
			participant B as Bob
			A->>B: Hello
			B-->>A: Hi
			"""), "svg");

	[Test]
	public Task Sequence_with_activation() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			sequenceDiagram
			Client->>+Server: Request
			Server->>+DB: Query
			DB-->>-Server: Result
			Server-->>-Client: Response
			"""), "svg");

	[Test]
	public Task Sequence_with_blocks() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			sequenceDiagram
			participant A
			participant B
			alt Success
			A->>B: OK
			else Failure
			A->>B: Error
			end
			loop Retry
			A->>B: Ping
			end
			"""), "svg");

	[Test]
	public Task Sequence_with_notes() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			sequenceDiagram
			participant A
			participant B
			A->>B: Hello
			Note right of B: Important
			Note over A,B: Shared note
			"""), "svg");

	[Test]
	public Task Class_with_members() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			classDiagram
			class Animal {
			<<abstract>>
			+String name
			+int age
			+eat() void
			+sleep() void
			}
			class Dog {
			+String breed
			+bark() void
			}
			Animal <|-- Dog
			"""), "svg");

	[Test]
	public Task Class_relationships() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			classDiagram
			A <|-- B
			A *-- C
			A o-- D
			A --> E
			A ..> F
			A ..|> G
			"""), "svg");

	[Test]
	public Task Er_basic() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			erDiagram
			CUSTOMER ||--o{ ORDER : places
			ORDER ||--|{ LINE_ITEM : contains
			CUSTOMER {
			string name PK
			string email UK
			}
			ORDER {
			int id PK
			date created
			}
			"""), "svg");

	[Test]
	public Task Flowchart_tokyo_night_theme() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph TD
			A --> B --> C
			""",
			new() { Bg = "#1a1b26", Fg = "#a9b1d6", Line = "#3d59a1", Accent = "#7aa2f7" }),
			"svg");

	[Test]
	public Task Flowchart_transparent() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph TD
			A --> B
			""",
			new() { Transparent = true }),
			"svg");
}
