using VerifyTUnit;

namespace Mermaider.Tests.Snapshots;

public class FlowchartSpecTests
{
	[Test]
	public Task All_node_shapes() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
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
			"""), "svg");

	[Test]
	public Task Direction_LR() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph LR
			Start --> Middle --> End
			"""), "svg");

	[Test]
	public Task Direction_BT() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph BT
			Bottom --> Middle --> Top
			"""), "svg");

	[Test]
	public Task Direction_RL() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph RL
			End --> Middle --> Start
			"""), "svg");

	[Test]
	public Task Bidirectional_arrows() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph LR
			A <--> B
			C <-.-> D
			E <==> F
			"""), "svg");

	[Test]
	public Task Open_ended_edges() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph LR
			A --- B
			C -.- D
			E === F
			"""), "svg");

	[Test]
	public Task Edge_labels_inline_text() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph TD
			A -- text on solid --> B
			C -. text on dotted .-> D
			E == text on thick ==> F
			"""), "svg");

	[Test]
	public Task Edge_labels_pipe_syntax() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph TD
			A -->|Yes| B
			A -->|No| C
			B -->|Done| D
			C -->|Retry| A
			"""), "svg");

	[Test]
	public Task Chained_edges() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph TD
			A --> B --> C --> D --> E
			"""), "svg");

	[Test]
	public Task Parallel_links() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph TD
			A & B --> C & D
			"""), "svg");

	[Test]
	public Task Nested_subgraphs() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
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
			"""), "svg");

	[Test]
	public Task Subgraph_direction_override() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph LR
			subgraph Vertical
			  direction TD
			  V1 --> V2 --> V3
			end
			subgraph Horizontal
			  H1 --> H2 --> H3
			end
			Vertical --> Horizontal
			"""), "svg");

	[Test]
	public Task ClassDef_styling() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph TD
			classDef important fill:#f96,stroke:#333,color:#fff
			classDef secondary fill:#bbf,stroke:#339
			A[Critical]:::important --> B[Normal]
			B --> C[Info]:::secondary
			A --> D[Also Critical]:::important
			"""), "svg");

	[Test]
	public Task Style_directive() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph TD
			A --> B --> C
			style A fill:#f9f,stroke:#333,stroke-width:4px
			style B fill:#bbf,stroke:#f66,stroke-width:2px
			"""), "svg");

	[Test]
	public Task Long_edges() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph TD
			A ----> B
			A ====> C
			A -...-> D
			"""), "svg");

	[Test]
	public Task Complex_decision_flow() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
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
			"""), "svg");

	[Test]
	public Task Network_topology() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
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
			"""), "svg");

	[Test]
	public Task Flowchart_keyword() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			flowchart LR
			A[Input] --> B{Process}
			B --> C[Output]
			"""), "svg");

	[Test]
	public Task Nodes_with_special_characters() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph TD
			A["Node with (parens)"] --> B["Node with {braces}"]
			B --> C["Quoted label"]
			"""), "svg");

	[Test]
	public Task Invisible_edges() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph TD
			A[First] ~~~ B[Second]
			A --> C[Third]
			"""), "svg");

	[Test]
	public Task Default_classDef() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph TD
			A[Node A] --> B[Node B]
			B --> C[Node C]
			classDef default fill:#f9f,stroke:#333,color:#333
			"""), "svg");

	[Test]
	public Task Markdown_in_label() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			graph TD
			A["`The **cat** in the hat`"] --> B["`*Italic* text`"]
			"""), "svg");
}
