using VerifyTUnit;

namespace Mermaider.Tests.Snapshots;

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
	public Task Flowchart_opaque() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph TD
			A --> B
			""",
			new() { Transparent = false }),
			"svg");

	[Test]
	public Task Pie_basic() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			pie
			title Pet Adoption
			"Dogs" : 386
			"Cats" : 85
			"Rats" : 15
			"""), "svg");

	[Test]
	public Task Pie_showData() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			pie showData
			title Browser Market Share
			"Chrome" : 65.3
			"Safari" : 18.8
			"Firefox" : 3.2
			"Edge" : 4.7
			"Other" : 8.0
			"""), "svg");

	[Test]
	public Task Quadrant_basic() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			quadrantChart
			title Priority Matrix
			x-axis Low Effort --> High Effort
			y-axis Low Impact --> High Impact
			quadrant-1 Do First
			quadrant-2 Schedule
			quadrant-3 Delegate
			quadrant-4 Eliminate
			Feature A: [0.8, 0.9]
			Feature B: [0.2, 0.3]
			Feature C: [0.6, 0.4]
			"""), "svg");

	[Test]
	public Task Timeline_with_sections() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			timeline
			title History of Social Media
			section Early Days
			2002 : LinkedIn
			2004 : Facebook : Google
			section Modern Era
			2010 : Instagram
			2019 : TikTok
			"""), "svg");

	[Test]
	public Task GitGraph_branching() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			gitGraph
			commit id: "init"
			commit id: "feat-1"
			branch develop
			checkout develop
			commit id: "dev-1"
			commit id: "dev-2" tag: "v0.1"
			checkout main
			merge develop id: "merge-1"
			commit id: "release" type: HIGHLIGHT tag: "v1.0"
			"""), "svg");

	[Test]
	public Task Radar_basic() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			radar-beta
			title Skills Assessment
			axis Design, Frontend, Backend, DevOps, Testing
			curve c1["Team A"]{4, 3, 5, 2, 4}
			curve c2["Team B"]{3, 5, 2, 4, 3}
			max 5
			graticule polygon
			"""), "svg");

	[Test]
	public Task Treemap_basic() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			treemap-beta
			"Engineering": 50
			"Marketing": 25
			"Sales": 15
			"Support": 10
			"""), "svg");

	[Test]
	public Task Venn_two_sets() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			venn-beta
			set A["Frontend"]
			set B["Backend"]
			union A, B["Full Stack"]
			"""), "svg");

	[Test]
	public Task Mindmap_basic() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			mindmap
			  ((Project))
			    (Planning)
			      Requirements
			      Timeline
			    [Development]
			      Frontend
			      Backend
			    {{Testing}}
			      Unit Tests
			      Integration
			"""), "svg");
}
