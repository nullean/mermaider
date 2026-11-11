using AwesomeAssertions;
using Nullean.Snapshots;

namespace Mermaider.Tests.Snapshots;

public class SnapshotTests
{
	[Test]
	public void Block_grid() =>
		MermaidRenderer
			.RenderSvg("""
			block-beta
			columns 3
			  A["A"] B["B"] C["C"]
			  D["D"] E["E"] F["F"]
			""")
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void C4_context_basic() =>
		MermaidRenderer
			.RenderSvg(
				"""
			C4Context
			title Banking Context
			Person(customer, "Customer")
			System(banking, "Internet Banking")
			System_Ext(mail, "E-mail")
			Rel(customer, banking, "Uses")
			Rel(banking, mail, "Sends e-mails", "SMTP")
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Class_relationships() =>
		MermaidRenderer
			.RenderSvg(
				"""
			classDiagram
			A <|-- B
			A *-- C
			A o-- D
			A --> E
			A ..> F
			A ..|> G
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Class_with_members() =>
		MermaidRenderer
			.RenderSvg(
				"""
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
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Er_basic() =>
		MermaidRenderer
			.RenderSvg(
				"""
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
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Flowchart_all_shapes() =>
		MermaidRenderer
			.RenderSvg(
				"""
			graph LR
			A[Rectangle] --> B(Rounded)
			B --> C([Stadium])
			C --> D{Diamond}
			D --> E((Circle))
			E --> F>Asymmetric]
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Flowchart_edge_styles() =>
		MermaidRenderer
			.RenderSvg("""
			graph LR
			A --> B
			A -.-> C
			A ==> D
			""")
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Flowchart_opaque() =>
		MermaidRenderer
			.RenderSvg("""
			graph TD
			A --> B
			""", new() { Transparent = false })
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Flowchart_simple() =>
		MermaidRenderer
			.RenderSvg("""
			graph TD
			A[Start] --> B{Decision}
			B -->|Yes| C[OK]
			B -->|No| D[Cancel]
			""")
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Flowchart_subgraph() =>
		MermaidRenderer
			.RenderSvg(
				"""
			graph TD
			subgraph Backend
			A[API] --> B[DB]
			end
			subgraph Frontend
			C[UI] --> D[State]
			end
			C --> A
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Flowchart_tokyo_night_theme() =>
		MermaidRenderer
			.RenderSvg(
				"""
			graph TD
			A --> B --> C
			""",
				new() { Bg = "#1a1b26", Fg = "#a9b1d6", Line = "#3d59a1", Accent = "#7aa2f7" }
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Gantt_milestones() =>
		MermaidRenderer
			.RenderSvg(
				"""
			gantt
			title Release train
			dateFormat YYYY-MM-DD
			section Build
			Implement parser :done, p1, 2026-01-06, 3d
			Add tests        :active, p2, after p1, 2d
			section Ship
			RC cut           :milestone, m1, after p2, 0d
			GA               :crit, p3, after m1, 5d
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Gantt_showcase() =>
		MermaidRenderer
			.RenderSvg(
				"""
			gantt
			title Shipping this file
			dateFormat  YYYY-MM-DD
			section Render
			Spike the renderer :done, a1, 2026-07-07, 1d
			Print this page    :active, a2, after a1, 1d
			section Polish
			Update tests       :crit, after a2, 12h
			Update docs        : 6h
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void GitGraph_branching() =>
		MermaidRenderer
			.RenderSvg(
				"""
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
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Journey_working_day() =>
		MermaidRenderer
			.RenderSvg(
				"""
			journey
			title My working day
			section Go to work
			Make tea: 5: Me
			Go upstairs: 3: Me
			Do work: 1: Me, Cat
			section Go home
			Go downstairs: 5: Me
			Sit down: 5: Me
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Mindmap_basic() =>
		MermaidRenderer
			.RenderSvg(
				"""
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
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Packet_udp_header() =>
		MermaidRenderer
			.RenderSvg(
				"""
			packet-beta
			title UDP Header
			0-15: "Source Port"
			16-31: "Destination Port"
			32-47: "Length"
			48-63: "Checksum"
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Pie_basic() =>
		MermaidRenderer
			.RenderSvg("""
			pie
			title Pet Adoption
			"Dogs" : 386
			"Cats" : 85
			"Rats" : 15
			""")
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Pie_showData() =>
		MermaidRenderer
			.RenderSvg(
				"""
			pie showData
			title Browser Market Share
			"Chrome" : 65.3
			"Safari" : 18.8
			"Firefox" : 3.2
			"Edge" : 4.7
			"Other" : 8.0
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Quadrant_basic() =>
		MermaidRenderer
			.RenderSvg(
				"""
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
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Radar_basic() =>
		MermaidRenderer
			.RenderSvg(
				"""
			radar-beta
			title Skills Assessment
			axis Design, Frontend, Backend, DevOps, Testing
			curve c1["Team A"]{4, 3, 5, 2, 4}
			curve c2["Team B"]{3, 5, 2, 4, 3}
			max 5
			graticule polygon
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Requirement_basic() =>
		MermaidRenderer
			.RenderSvg(
				"""
			requirementDiagram

			requirement test_req {
			id: 1
			text: the test text.
			risk: high
			verifymethod: test
			}

			element test_entity {
			type: simulation
			}

			test_entity - satisfies -> test_req
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Sankey_basic() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sankey-beta
			Electricity grid,Over generation / exports,104.453
			Electricity grid,Heating and cooling - homes,113.726
			Electricity grid,Industry,342.165
			Bio-conversion,Losses,26.862
			Bio-conversion,Solid,280.322
			Bio-conversion,Gas,81.144
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Sequence_basic() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			participant A as Alice
			participant B as Bob
			A->>B: Hello
			B-->>A: Hi
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Sequence_with_activation() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			Client->>+Server: Request
			Server->>+DB: Query
			DB-->>-Server: Result
			Server-->>-Client: Response
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Sequence_with_blocks() =>
		MermaidRenderer
			.RenderSvg(
				"""
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
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Sequence_with_notes() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			participant A
			participant B
			A->>B: Hello
			Note right of B: Important
			Note over A,B: Shared note
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void State_diagram() =>
		MermaidRenderer
			.RenderSvg(
				"""
			stateDiagram-v2
			[*] --> Idle
			Idle --> Processing : start
			Processing --> Done : complete
			Done --> [*]
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Timeline_with_sections() =>
		MermaidRenderer
			.RenderSvg(
				"""
			timeline
			title History of Social Media
			section Early Days
			2002 : LinkedIn
			2004 : Facebook : Google
			section Modern Era
			2010 : Instagram
			2019 : TikTok
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Treemap_basic() =>
		MermaidRenderer
			.RenderSvg(
				"""
			treemap-beta
			"Engineering": 50
			"Marketing": 25
			"Sales": 15
			"Support": 10
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Venn_two_sets() =>
		MermaidRenderer
			.RenderSvg("""
			venn-beta
			set A["Frontend"]
			set B["Backend"]
			union A, B["Full Stack"]
			""")
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void XyChart_basic() =>
		MermaidRenderer
			.RenderSvg(
				"""
			xychart-beta
			title "Sales Revenue"
			x-axis [jan, feb, mar, apr, may, jun]
			y-axis "Revenue (in $)" 4000 --> 11000
			bar [5000, 6000, 7500, 8200, 9500, 10500]
			line [5000, 6000, 7500, 8200, 9500, 10500]
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void XyChart_horizontal() =>
		MermaidRenderer
			.RenderSvg(
				"""
			xychart-beta horizontal
			title "Throughput"
			x-axis [alpha, beta, gamma]
			y-axis "req/s" 0 --> 100
			bar [30, 60, 90]
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void XyChart_multi_series_legend() =>
		MermaidRenderer
			.RenderSvg(
				"""
			xychart-beta
			title "Latency"
			x-axis [p50, p90, p99]
			y-axis "ms" 0 --> 200
			bar "api" [40, 80, 150]
			bar "db" [20, 45, 90]
			line "slo" [100, 100, 100]
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Architecture_basic() =>
		MermaidRenderer
			.RenderSvg(
				"""
			architecture-beta
			group api(cloud)[API]
			service db(database)[Database] in api
			service server(server)[Server] in api
			server:R -- L:db
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);
}
