using AwesomeAssertions;
using Mermaider;

namespace Mermaider.Tests;

/// <summary>
/// Compatibility tests derived from the mermaid-js/mermaid test suite.
/// Each test feeds a real Mermaid diagram text (from the upstream project's parser
/// and Cypress rendering specs) into our renderer and asserts it does not throw
/// and produces a valid SVG.
///
/// Source: https://github.com/mermaid-js/mermaid (MIT licensed)
/// Files: packages/mermaid/src/diagrams/*/parser/*.spec.js
///        cypress/integration/rendering/*.spec.js
/// </summary>
public class MermaidCompatTests
{
	// ====================================================================
	// Flowchart — Parser compatibility
	// ====================================================================

	[Test]
	[MethodDataSource(nameof(FlowchartDiagrams))]
	public void Flowchart_renders(string name, string source)
	{
		var svg = MermaidRenderer.RenderSvg(source);
		AssertValidSvg(svg, name);
	}

	public static IEnumerable<(string Name, string Source)> FlowchartDiagrams()
	{
		yield return ("trailing whitespace", """
			graph TD
			  A-->B
			  B-->C
			""");
		yield return ("node name with end substring", """
			graph TD
			  endpoint --> sender
			""");
		yield return ("keywords between dashes", """
			graph TD
			  a-end-node --> b
			""");
		yield return ("default in node name", """
			graph TD
			  default --> monograph
			""");
		yield return ("single node", """
			graph TD
			  A
			""");
		yield return ("single square node", """
			graph TD
			  a[A]
			""");
		yield return ("single circle node", """
			graph TD
			  a((A))
			""");
		yield return ("single round node", """
			graph TD
			  a(A)
			""");
		yield return ("single odd node", """
			graph TD
			  a>A]
			""");
		yield return ("single diamond node", """
			graph TD
			  a{A}
			""");
		yield return ("single hexagon node", """
			graph TD
			  a{{A}}
			""");
		yield return ("single double circle node", """
			graph TD
			  a(((A)))
			""");
		yield return ("alphanumeric id starting with number", """
			graph TD
			  1id --> A
			""");
		yield return ("id with minus", """
			graph TD
			  i-d --> A
			""");
		yield return ("id with underscore", """
			graph TD
			  i_d --> A
			""");

		// Edges
		yield return ("solid arrow", """
			graph TD
			  A --> B
			""");
		yield return ("dotted arrow", """
			graph TD
			  A -.-> B
			""");
		yield return ("thick arrow", """
			graph TD
			  A ==> B
			""");
		yield return ("open ended", """
			graph TD
			  A --- B
			""");
		yield return ("solid with text", """
			graph TD
			  A -->|text| B
			""");
		yield return ("dotted with text", """
			graph TD
			  A -. text .-> B
			""");
		yield return ("thick with text", """
			graph TD
			  A == text ==> B
			""");
		yield return ("bidirectional", """
			graph TD
			  A <--> B
			""");
		yield return ("multiple edges", """
			graph TD
			  A---|This is text|B
			  A---|Second edge|B
			""");
		yield return ("long edges", """
			graph TD
			  A ----> B
			  A ====> C
			  A -...-> D
			""");

		// Directions
		yield return ("direction TD", """
			graph TD
			  A --> B
			""");
		yield return ("direction LR", """
			graph LR
			  A --> B
			""");
		yield return ("direction BT", """
			graph BT
			  A --> B
			""");
		yield return ("direction RL", """
			graph RL
			  A --> B
			""");
		yield return ("flowchart keyword", """
			flowchart TD
			  A --> B
			""");
		yield return ("flowchart LR", """
			flowchart LR
			  A --> B
			""");

		// Chaining and parallel
		yield return ("chained edges", """
			graph TD
			  A --> B --> C --> D
			""");
		yield return ("parallel links", """
			graph TD
			  A & B --> C & D
			""");
		yield return ("chained + parallel", """
			graph TD
			  A --> B --> C
			  E & F --> G & H
			""");

		// Shapes with labels
		yield return ("all shapes", """
			graph LR
			  A[Rectangle] --> B(Rounded)
			  B --> C([Stadium])
			  C --> D{Diamond}
			  D --> E((Circle))
			  E --> F>Asymmetric]
			  F --> G{{Hexagon}}
			  G --> H[[Subroutine]]
			""");

		// ClassDef and Style
		yield return ("classDef", """
			graph TD
			  classDef exClass fill:#f96,color:#fff,stroke:#333
			  A[Important]:::exClass --> B
			""");
		yield return ("style directive", """
			graph TD
			  A --> B
			  style A fill:#f9f,stroke:#333,stroke-width:4px
			""");
		yield return ("class assignment", """
			graph TD
			  classDef myClass fill:#bbb
			  A --> B
			  class A,B myClass
			""");

		// Subgraphs
		yield return ("simple subgraph", """
			graph TD
			  A-->B
			  subgraph myTitle
			    c-->d
			  end
			""");
		yield return ("subgraph with bracket id", """
			graph TD
			  subgraph uid1[text of doom]
			    c-->d
			  end
			""");
		yield return ("nested subgraphs", """
			graph TD
			  A-->B
			  subgraph outer
			    c-->d
			    subgraph inner
			      e-->f
			    end
			  end
			""");
		yield return ("subgraph direction override", """
			graph LR
			  subgraph WithTD
			    direction TD
			    A1 --> A2
			  end
			""");
		yield return ("subgraph with edges out", """
			flowchart TD
			  subgraph S1
			    sub1 --> sub2
			  end
			  subgraph S2
			    sub4
			  end
			  S1 --> S2
			  sub1 --> sub4
			""");

		// Complex real-world from Cypress rendering tests
		yield return ("christmas shopping", """
			flowchart TD
			  A[Christmas] -->|Get money| B(Go shopping)
			  B --> C{Let me think}
			  C -->|One| D[Laptop]
			  C -->|Two| E[iPhone]
			  C -->|Three| F[Car]
			""");
		yield return ("network topology", """
			flowchart TB
			  internet
			  nat
			  router
			  subgraph project
			    router
			    nat
			    subgraph subnet1
			      compute1
			      lb1
			    end
			    subgraph subnet2
			      compute2
			      lb2
			    end
			  end
			  internet --> router
			  router --> subnet1 & subnet2
			  subnet1 & subnet2 --> nat --> internet
			""");
		yield return ("styled subgraphs", """
			flowchart TB
			  A
			  B
			  subgraph foo[Foo SubGraph]
			    C
			    D
			  end
			  subgraph bar[Bar SubGraph]
			    E
			    F
			  end
			  A-->B
			  B-->C
			  C-->D
			  B-->D
			  D-->E
			  E-->A
			""");
	}

	// ====================================================================
	// Sequence — Rendering compatibility
	// ====================================================================

	[Test]
	[MethodDataSource(nameof(SequenceDiagrams))]
	public void Sequence_renders(string name, string source)
	{
		var svg = MermaidRenderer.RenderSvg(source);
		AssertValidSvg(svg, name);
	}

	public static IEnumerable<(string Name, string Source)> SequenceDiagrams()
	{
		yield return ("basic", """
			sequenceDiagram
			  participant Alice
			  participant Bob
			  Alice ->> Bob: Hello Bob, how are you?
			  Bob-->>Alice: Hi Alice!
			""");
		yield return ("three participants", """
			sequenceDiagram
			  participant Alice
			  participant Bob
			  participant John as John Second Line
			  Alice ->> Bob: Hello Bob, how are you?
			  Bob-->>John: How about you John?
			  Bob-->>Alice: I am good thanks!
			""");
		yield return ("alt/else blocks", """
			sequenceDiagram
			  Alice->>John: Hello John
			  alt either this
			    Alice->>John: Yes
			  else or this
			    Alice->>John: No
			  end
			""");
		yield return ("loop block", """
			sequenceDiagram
			  Alice->>John: Hello John, how are you?
			  loop Healthcheck
			    John->>John: Fight against hypochondria
			  end
			  John-->>Alice: Great!
			""");
		yield return ("activation", """
			sequenceDiagram
			  Client->>+Server: POST /login
			  Server->>+DB: SELECT user
			  DB-->>-Server: User row
			  Server-->>-Client: 200 JWT
			""");
		yield return ("notes", """
			sequenceDiagram
			  Alice->>Bob: Hello
			  Note right of Bob: Bob thinks
			  Bob-->>Alice: Fine!
			""");
		yield return ("note over two actors", """
			sequenceDiagram
			  Alice->>Bob: Hello
			  Note over Alice,Bob: Both engaged
			  Bob-->>Alice: Reply
			""");
		yield return ("self message", """
			sequenceDiagram
			  participant Service
			  participant Queue
			  Service->>Service: Initialize
			  Service->>Queue: Send
			  Queue->>Queue: Process internally
			  Queue-->>Service: Ack
			""");
		yield return ("par block", """
			sequenceDiagram
			  par this happens in parallel
			    Alice -->> Bob: Parallel message 1
			  and
			    Alice -->> John: Parallel message 2
			  end
			""");
		yield return ("nested alt loop", """
			sequenceDiagram
			  participant Client
			  participant API
			  participant DB
			  Client->>API: Request
			  alt Cache Hit
			    API-->>Client: Cached Response
			  else Cache Miss
			    API->>DB: Query
			    DB-->>API: Result
			    API-->>Client: Fresh Response
			  end
			  loop Health Check
			    Client->>API: Ping
			    API-->>Client: Pong
			  end
			""");
	}

	// ====================================================================
	// State — Rendering compatibility
	// ====================================================================

	[Test]
	[MethodDataSource(nameof(StateDiagrams))]
	public void State_renders(string name, string source)
	{
		var svg = MermaidRenderer.RenderSvg(source);
		AssertValidSvg(svg, name);
	}

	public static IEnumerable<(string Name, string Source)> StateDiagrams()
	{
		yield return ("basic", """
			stateDiagram-v2
			  [*] --> Idle
			  Idle --> Processing : submit
			  Processing --> Done : ok
			  Done --> [*]
			""");
		yield return ("with failure retry", """
			stateDiagram-v2
			  [*] --> Idle
			  Idle --> Processing : submit
			  Processing --> Success : ok
			  Processing --> Failed : error
			  Success --> [*]
			  Failed --> Idle : retry
			""");
		yield return ("multiple end states", """
			stateDiagram-v2
			  [*] --> Active
			  Active --> Inactive : disable
			  Active --> Closed : close
			  Inactive --> Active : reactivate
			  Inactive --> Closed : close
			  Closed --> [*]
			""");
		yield return ("linear flow", """
			stateDiagram-v2
			  [*] --> Draft
			  Draft --> Review : submit
			  Review --> Published : approve
			  Published --> [*]
			""");
	}

	// ====================================================================
	// Class — Rendering compatibility
	// ====================================================================

	[Test]
	[MethodDataSource(nameof(ClassDiagrams))]
	public void Class_renders(string name, string source)
	{
		var svg = MermaidRenderer.RenderSvg(source);
		AssertValidSvg(svg, name);
	}

	public static IEnumerable<(string Name, string Source)> ClassDiagrams()
	{
		yield return ("inheritance", """
			classDiagram
			  Animal <|-- Duck
			  Animal <|-- Fish
			  Animal : +int age
			  Animal : +String gender
			  Animal : +isMammal() bool
			  Duck : +String beakColor
			  Duck : +swim()
			  Duck : +quack()
			""");
		yield return ("all relationship types", """
			classDiagram
			  A <|-- B : Inheritance
			  C *-- D : Composition
			  E o-- F : Aggregation
			  G --> H : Association
			  I ..> J : Dependency
			  K ..|> L : Realization
			""");
		yield return ("interface and implementation", """
			classDiagram
			  class ILogger {
			    <<interface>>
			    +Log(string message) void
			    +LogError(Exception ex) void
			  }
			  class ConsoleLogger {
			    -bool _verbose
			    +Log(string message) void
			    +LogError(Exception ex) void
			  }
			  class FileLogger {
			    -string _path
			    +Log(string message) void
			    +LogError(Exception ex) void
			    +Flush() void
			  }
			  ILogger <|.. ConsoleLogger
			  ILogger <|.. FileLogger
			""");
		yield return ("abstract class", """
			classDiagram
			  class Animal {
			    <<abstract>>
			    +String name
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
			""");
		yield return ("class with visibility modifiers", """
			classDiagram
			  class MyClass {
			    +publicMethod() void
			    -privateMethod() void
			    #protectedMethod() void
			    ~packageMethod() void
			    +String publicAttr
			    -int privateAttr
			  }
			""");
	}

	// ====================================================================
	// ER — Rendering compatibility
	// ====================================================================

	[Test]
	[MethodDataSource(nameof(ErDiagrams))]
	public void Er_renders(string name, string source)
	{
		var svg = MermaidRenderer.RenderSvg(source);
		AssertValidSvg(svg, name);
	}

	public static IEnumerable<(string Name, string Source)> ErDiagrams()
	{
		yield return ("basic cardinalities", """
			erDiagram
			  CUSTOMER ||--o{ ORDER : places
			  ORDER ||--|{ LINE_ITEM : contains
			""");
		yield return ("with attributes", """
			erDiagram
			  CUSTOMER ||--o{ ORDER : places
			  CUSTOMER {
			    string name PK
			    string email UK
			    date joined
			  }
			  ORDER {
			    int id PK
			    date created
			    string status
			  }
			""");
		yield return ("complex blog schema", """
			erDiagram
			  USER ||--o{ POST : writes
			  USER ||--o{ COMMENT : writes
			  POST ||--o{ COMMENT : has
			  POST }o--o{ TAG : tagged
			  USER {
			    int id PK
			    string username UK
			    string email
			  }
			  POST {
			    int id PK
			    string title
			    text body
			    date published
			  }
			  COMMENT {
			    int id PK
			    text content
			    date created
			  }
			  TAG {
			    int id PK
			    string name UK
			  }
			""");
		yield return ("all cardinality types", """
			erDiagram
			  A ||--|| B : one-to-one
			  C ||--o{ D : one-to-zero-or-many
			  E ||--|{ F : one-to-one-or-many
			  G }o--o{ H : zero-or-many-to-zero-or-many
			""");
		yield return ("identifying relationships", """
			erDiagram
			  PARENT ||--|{ CHILD : has
			  CHILD }|--|| DETAIL : contains
			""");
	}

	// ====================================================================
	// Cross-cutting: diagram detection
	// ====================================================================

	[Test]
	[MethodDataSource(nameof(DetectionDiagrams))]
	public void Detects_and_renders_all_types(string name, string source)
	{
		var svg = MermaidRenderer.RenderSvg(source);
		AssertValidSvg(svg, name);
	}

	public static IEnumerable<(string Name, string Source)> DetectionDiagrams()
	{
		yield return ("graph TD", """
			graph TD
			  A-->B
			""");
		yield return ("graph LR", """
			graph LR
			  A-->B
			""");
		yield return ("flowchart TD", """
			flowchart TD
			  A-->B
			""");
		yield return ("flowchart LR", """
			flowchart LR
			  A-->B
			""");
		yield return ("stateDiagram-v2", """
			stateDiagram-v2
			  [*] --> A
			  A --> [*]
			""");
		yield return ("sequenceDiagram", """
			sequenceDiagram
			  Alice->>Bob: Hi
			""");
		yield return ("classDiagram", """
			classDiagram
			  A <|-- B
			""");
		yield return ("erDiagram", """
			erDiagram
			  A ||--o{ B : rel
			""");
	}

	// ====================================================================
	// Helpers
	// ====================================================================

	private static void AssertValidSvg(string svg, string name)
	{
		svg.Should().NotBeNullOrEmpty($"[{name}] SVG was null or empty");
		svg.Should().Contain("<svg", $"[{name}] Missing <svg> root element");
		svg.Should().Contain("</svg>", $"[{name}] Missing </svg> closing tag");
	}
}
