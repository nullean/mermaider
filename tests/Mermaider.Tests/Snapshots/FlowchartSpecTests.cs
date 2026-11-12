using AwesomeAssertions;
using Nullean.Snapshots;

namespace Mermaider.Tests.Snapshots;

public class FlowchartSpecTests
{
	[Test]
	public void All_node_shapes() =>
		MermaidRenderer
			.RenderSvg(
				"""
			graph LR
			A[Rectangle] --> B(Rounded)
			B --> C([Stadium])
			C --> D{Diamond}
			D --> E((Circle))
			E --> F>Asymmetric]
			F --> G{{Hexagon}}
			G --> H[[Subroutine]]
			H --> I[(Cylinder)]
			I --> J[/Trapezoid\]
			J --> K[\Trapezoid Alt/]
			K --> L(((Double Circle)))
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Direction_LR() =>
		MermaidRenderer.RenderSvg("""
			graph LR
			Start --> Middle --> End
			""").Should().MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Direction_BT() =>
		MermaidRenderer.RenderSvg("""
			graph BT
			Bottom --> Middle --> Top
			""").Should().MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Direction_RL() =>
		MermaidRenderer.RenderSvg("""
			graph RL
			End --> Middle --> Start
			""").Should().MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Bidirectional_arrows() =>
		MermaidRenderer
			.RenderSvg("""
			graph LR
			A <--> B
			C <-.-> D
			E <==> F
			""")
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Open_ended_edges() =>
		MermaidRenderer
			.RenderSvg("""
			graph LR
			A --- B
			C -.- D
			E === F
			""")
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Edge_labels_inline_text() =>
		MermaidRenderer
			.RenderSvg(
				"""
			graph TD
			A -- text on solid --> B
			C -. text on dotted .-> D
			E == text on thick ==> F
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Edge_labels_pipe_syntax() =>
		MermaidRenderer
			.RenderSvg("""
			graph TD
			A -->|Yes| B
			A -->|No| C
			B -->|Done| D
			C -->|Retry| A
			""")
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Chained_edges() =>
		MermaidRenderer.RenderSvg("""
			graph TD
			A --> B --> C --> D --> E
			""").Should().MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Parallel_links() =>
		MermaidRenderer.RenderSvg("""
			graph TD
			A & B --> C & D
			""").Should().MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Nested_subgraphs() =>
		MermaidRenderer
			.RenderSvg(
				"""
			flowchart TD
			subgraph Outer[Deployment]
			  subgraph Inner1[Cluster A]
			    A1[Pod 1] --> A2[Pod 2]
			  end
			  subgraph Inner2[Cluster B]
			    B1[Pod 1] --> B2[Pod 2]
			  end
			end
			LB[Load Balancer] --> Inner1
			LB --> Inner2
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Subgraph_direction_override() =>
		MermaidRenderer
			.RenderSvg(
				"""
			graph LR
			subgraph Vertical
			  direction TD
			  V1 --> V2 --> V3
			end
			subgraph Horizontal
			  H1 --> H2 --> H3
			end
			Vertical --> Horizontal
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void ClassDef_styling() =>
		MermaidRenderer
			.RenderSvg(
				"""
			graph TD
			classDef important fill:#f96,stroke:#333,color:#fff
			classDef secondary fill:#bbf,stroke:#339
			A[Critical]:::important --> B[Normal]
			B --> C[Info]:::secondary
			A --> D[Also Critical]:::important
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Style_directive() =>
		MermaidRenderer
			.RenderSvg(
				"""
			graph TD
			A --> B --> C
			style A fill:#f9f,stroke:#333,stroke-width:4px
			style B fill:#bbf,stroke:#f66,stroke-width:2px
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Long_edges() =>
		MermaidRenderer
			.RenderSvg("""
			graph TD
			A ----> B
			A ====> C
			A -...-> D
			""")
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Complex_decision_flow() =>
		MermaidRenderer
			.RenderSvg(
				"""
			flowchart TD
			A[Start] --> B{Is it raining?}
			B -->|Yes| C[Take umbrella]
			B -->|No| D{Is it cold?}
			D -->|Yes| E[Wear jacket]
			D -->|No| F[Dress light]
			C --> G[Go outside]
			E --> G
			F --> G
			G --> H{Enjoy?}
			H -->|Yes| I((Done))
			H -->|No| A
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Network_topology() =>
		MermaidRenderer
			.RenderSvg(
				"""
			flowchart TB
			internet((Internet))
			subgraph cloud[Cloud VPC]
			  subgraph pub[Public Subnet]
			    lb([Load Balancer])
			    nat([NAT Gateway])
			  end
			  subgraph priv[Private Subnet]
			    app1[App Server 1]
			    app2[App Server 2]
			    db[(Database)]
			  end
			end
			internet --> lb
			lb --> app1 & app2
			app1 & app2 --> db
			app1 & app2 --> nat
			nat --> internet
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Flowchart_keyword() =>
		MermaidRenderer
			.RenderSvg("""
			flowchart LR
			A[Input] --> B{Process}
			B --> C[Output]
			""")
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Nodes_with_special_characters() =>
		MermaidRenderer
			.RenderSvg("""
			graph TD
			A["Node with (parens)"] --> B["Node with {braces}"]
			B --> C["Quoted label"]
			""")
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Invisible_edges() =>
		MermaidRenderer
			.RenderSvg("""
			graph TD
			A[First] ~~~ B[Second]
			A --> C[Third]
			""")
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Default_classDef() =>
		MermaidRenderer
			.RenderSvg(
				"""
			graph TD
			A[Node A] --> B[Node B]
			B --> C[Node C]
			classDef default fill:#f9f,stroke:#333,color:#333
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Markdown_in_label() =>
		MermaidRenderer
			.RenderSvg("""
			graph TD
			A["`The **cat** in the hat`"] --> B["`*Italic* text`"]
			""")
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);
}
