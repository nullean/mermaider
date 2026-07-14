namespace Mermaider.Gallery;

public enum DiagramCategory { Flowchart, Sequence, State, Class, Er, Pie, Quadrant, Timeline, GitGraph, Radar, Treemap, Venn, Mindmap, Gantt, Journey, C4, Sankey, XyChart, Requirement, Packet, Kanban, Architecture, Block, TreeView, RealWorld }

public sealed record DiagramExample(string Slug, string Title, DiagramCategory Category, string Source, string? Feature = null);

public static partial class DiagramExamples
{
	public static readonly DiagramExample[] All =
	[
		// ── Flowchart ──────────────────────────────────────────────────

		new("flowchart-simple", "Simple Flow", DiagramCategory.Flowchart, """
			graph TD
			  A[Start] --> B{Decision}
			  B -->|Yes| C[OK]
			  B -->|No| D[Cancel]
			  C --> E[End]
			  D --> E
			"""),

		new("flowchart-shapes", "All Shapes", DiagramCategory.Flowchart, """
			graph LR
			  A[Rectangle] --> B(Rounded)
			  B --> C([Stadium])
			  C --> D{Diamond}
			  D --> E((Circle))
			  E --> F>Asymmetric]
			  F --> G{{Hexagon}}
			  G --> H[[Subroutine]]
			"""),

		new("flowchart-edges", "Edge Styles", DiagramCategory.Flowchart, """
			graph LR
			  A -->|solid| B
			  A -.->|dotted| C
			  A ==>|thick| D
			  E --- F
			  G <--> H
			"""),

		new("flowchart-subgraphs", "Subgraphs", DiagramCategory.Flowchart, """
			graph TD
			  subgraph Backend
			    direction LR
			    API[REST API] --> DB[(Database)]
			    API --> Cache[(Redis)]
			  end
			  subgraph Frontend
			    UI[React App] --> State[Redux]
			  end
			  UI --> API
			  State --> API
			"""),

		new("flowchart-chained", "Chained & Parallel", DiagramCategory.Flowchart, """
			graph TD
			  A --> B --> C --> D
			  E & F --> G & H
			"""),

		new("flowchart-styled", "ClassDef Styling", DiagramCategory.Flowchart, """
			graph TD
			  classDef important fill:#f96,color:#fff,stroke:#333,stroke-width:2px
			  classDef muted fill:#eee,color:#999,stroke:#ccc
			  A[Important]:::important --> B[Normal] --> C[Muted]:::muted
			  B --> D[Also Important]:::important
			"""),

		new("flowchart-christmas", "Decision Tree", DiagramCategory.Flowchart, """
			flowchart TD
			  A[Christmas] -->|Get money| B(Go shopping)
			  B --> C{Let me think}
			  C -->|One| D[Laptop]
			  C -->|Two| E[iPhone]
			  C -->|Three| F[Car]
			"""),

		new("flowchart-network", "Nested Subgraphs", DiagramCategory.Flowchart, """
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
			"""),

		new("flowchart-long-edges", "Long & Mixed Edges", DiagramCategory.Flowchart, """
			graph TD
			  A ----> B
			  A ====> C
			  A -...-> D
			  E -->|text| F
			  E -. dotted text .-> G
			  E == thick text ==> H
			"""),

		new("flowchart-styled-sub", "Styled Subgraphs", DiagramCategory.Flowchart, """
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
			"""),

		new("flowchart-invisible", "Invisible Edges", DiagramCategory.Flowchart, """
			graph TD
			  A[Positioned Left] ~~~ B[Positioned Right]
			  A --> C[Connected Below]
			  B --> D[Also Below]
			  C --> E[Merged]
			  D --> E
			""", "invisible edges"),

		new("flowchart-default-class", "Default ClassDef", DiagramCategory.Flowchart, """
			graph TD
			  classDef default fill:#e8f5e9,stroke:#2e7d32,color:#1b5e20
			  A[All nodes] --> B[Get the]
			  B --> C[Default style]
			  C --> D{Unless overridden}
			  classDef special fill:#fff3e0,stroke:#e65100,color:#bf360c
			  D -->|Yes| E[Special]:::special
			  D -->|No| F[Still default]
			""", "default classDef"),

		new("flowchart-markdown", "Markdown in Labels", DiagramCategory.Flowchart, """
			graph TD
			  A["`The **cat** in the hat`"] --> B["`*Italic* emphasis`"]
			  B --> C["`**Bold** and *italic* mixed`"]
			  C --> D[Normal label]
			""", "markdown labels"),

		// ── Sequence ───────────────────────────────────────────────────

		new("sequence-basic", "Basic Messages", DiagramCategory.Sequence, """
			sequenceDiagram
			  participant A as Alice
			  participant B as Bob
			  participant C as Charlie
			  A->>B: Hello Bob!
			  B-->>A: Hi Alice!
			  B->>C: Forward message
			  C-->>B: Got it
			  C->>A: Hey Alice, Charlie here
			"""),

		new("sequence-activation", "Activations", DiagramCategory.Sequence, """
			sequenceDiagram
			  Client->>+Server: POST /login
			  Server->>+DB: SELECT user
			  DB-->>-Server: User row
			  Server->>+Auth: Validate token
			  Auth-->>-Server: OK
			  Server-->>-Client: 200 JWT
			"""),

		new("sequence-blocks", "Alt/Loop Blocks", DiagramCategory.Sequence, """
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
			"""),

		new("sequence-notes", "Notes", DiagramCategory.Sequence, """
			sequenceDiagram
			  participant A as Frontend
			  participant B as Backend
			  A->>B: Login request
			  Note right of B: Validate credentials
			  B-->>A: Auth token
			  Note over A,B: Subsequent requests use JWT
			  A->>B: GET /data
			  Note left of A: Display results
			"""),

		new("sequence-self", "Self Messages", DiagramCategory.Sequence, """
			sequenceDiagram
			  participant A as Service
			  participant B as Queue
			  A->>A: Initialize
			  A->>B: Send message
			  B->>B: Process internally
			  B-->>A: Acknowledgement
			"""),

		new("sequence-par", "Par Block", DiagramCategory.Sequence, """
			sequenceDiagram
			  participant Alice
			  participant Bob
			  participant John
			  par this happens in parallel
			    Alice -->> Bob: Parallel message 1
			  and
			    Alice -->> John: Parallel message 2
			  end
			  Bob -->> Alice: Response 1
			  John -->> Alice: Response 2
			"""),

		new("sequence-full", "Kitchen Sink", DiagramCategory.Sequence, """
			sequenceDiagram
			  actor U as User
			  participant F as Frontend
			  participant B as Backend
			  participant DB as Database
			  U->>F: Click login
			  F->>+B: POST /auth
			  Note right of B: Hash & verify
			  B->>+DB: SELECT user
			  DB-->>-B: Row
			  alt Valid
			    B-->>F: 200 JWT
			    Note over F,B: Session established
			  else Invalid
			    B-->>F: 401 Unauthorized
			  end
			  B-->>-F: Done
			  F-->>U: Show dashboard
			  loop Every 30s
			    F->>B: Heartbeat
			    B-->>F: OK
			  end
			"""),

		new("sequence-autonumber", "Autonumber", DiagramCategory.Sequence, """
			sequenceDiagram
			  autonumber
			  participant Client
			  participant API
			  participant DB
			  Client->>API: Login request
			  API->>DB: Verify user
			  DB-->>API: User found
			  API-->>Client: Auth token
			  Client->>API: GET /profile
			  API->>DB: Fetch profile
			  DB-->>API: Profile data
			  API-->>Client: 200 OK
			""", "autonumber"),

		new("sequence-autonumber-custom", "Autonumber (Custom Start/Step)", DiagramCategory.Sequence, """
			sequenceDiagram
			  autonumber 100 10
			  Alice->>Bob: First message
			  Bob->>Charlie: Second message
			  Charlie-->>Alice: Third message
			""", "autonumber"),

		new("sequence-bidirectional", "Bidirectional Arrows", DiagramCategory.Sequence, """
			sequenceDiagram
			  participant A as Service A
			  participant B as Service B
			  participant C as Service C
			  A<<->>B: Bidirectional sync
			  A<<-->>C: Dashed bidirectional
			  B->>C: Normal one-way
			  C-->>B: Dashed one-way
			""", "bidirectional"),

		new("sequence-box", "Box Grouping", DiagramCategory.Sequence, """
			sequenceDiagram
			  box rgb(200,220,255) Internal Services
			  participant API
			  participant Auth
			  participant DB
			  end
			  box rgb(255,220,200) External
			  participant Client
			  end
			  Client->>API: Request
			  API->>Auth: Validate
			  Auth-->>API: OK
			  API->>DB: Query
			  DB-->>API: Data
			  API-->>Client: Response
			""", "box grouping"),

		new("sequence-create-destroy", "Create & Destroy", DiagramCategory.Sequence, """
			sequenceDiagram
			  participant Alice
			  participant Bob
			  Alice->>Bob: Hello Bob
			  create participant Worker
			  Bob->>Worker: Spawn task
			  Worker->>Worker: Process
			  Worker-->>Bob: Result
			  destroy Worker
			  Bob-xWorker: Terminate
			  Bob-->>Alice: Done
			""", "create/destroy"),

		// ── State ──────────────────────────────────────────────────────

		new("state-simple", "Simple States", DiagramCategory.State, """
			stateDiagram-v2
			  [*] --> Idle
			  Idle --> Processing : submit
			  Processing --> Success : ok
			  Processing --> Failed : error
			  Success --> [*]
			  Failed --> Idle : retry
			"""),

		new("state-multi-end", "Multiple End States", DiagramCategory.State, """
			stateDiagram-v2
			  [*] --> Active
			  Active --> Inactive : disable
			  Active --> Closed : close
			  Inactive --> Active : reactivate
			  Inactive --> Closed : close
			  Closed --> [*]
			"""),

		new("state-linear", "Linear Flow", DiagramCategory.State, """
			stateDiagram-v2
			  [*] --> Draft
			  Draft --> Review : submit
			  Review --> Published : approve
			  Published --> [*]
			"""),

		new("state-choice", "Choice Pseudo-state", DiagramCategory.State, """
			stateDiagram-v2
			  [*] --> Evaluate
			  state checkResult <<choice>>
			  Evaluate --> checkResult
			  checkResult --> Positive : if score > 0
			  checkResult --> Negative : if score < 0
			  checkResult --> Neutral : if score = 0
			  Positive --> [*]
			  Negative --> Retry
			  Neutral --> [*]
			  Retry --> Evaluate
			""", "choice"),

		new("state-fork-join", "Fork & Join", DiagramCategory.State, """
			stateDiagram-v2
			  [*] --> Ready
			  state fork_point <<fork>>
			  state join_point <<join>>
			  Ready --> fork_point
			  fork_point --> TaskA
			  fork_point --> TaskB
			  fork_point --> TaskC
			  TaskA --> join_point
			  TaskB --> join_point
			  TaskC --> join_point
			  join_point --> Complete
			  Complete --> [*]
			""", "fork/join"),

		new("state-notes", "State Notes", DiagramCategory.State, """
			stateDiagram-v2
			  [*] --> Active
			  Active --> Paused : pause
			  Paused --> Active : resume
			  Active --> Done : finish
			  note right of Active : This is the main working state
			  note left of Paused : Temporarily suspended
			  Done --> [*]
			""", "notes"),

		new("state-composite", "Composite States", DiagramCategory.State, """
			stateDiagram-v2
			  [*] --> First
			  state First {
			    [*] --> Inner1
			    Inner1 --> Inner2 : next
			    Inner2 --> Inner3 : next
			    Inner3 --> [*]
			  }
			  First --> Second
			  state Second {
			    [*] --> Inner4
			    Inner4 --> Inner5 : process
			    Inner5 --> [*]
			  }
			  Second --> [*]
			""", "composite"),

		// ── Class ──────────────────────────────────────────────────────

		new("class-basic", "Inheritance", DiagramCategory.Class, """
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
			    +fetch() void
			  }
			  class Cat {
			    +bool indoor
			    +purr() void
			    +scratch() void
			  }
			  Animal <|-- Dog
			  Animal <|-- Cat
			"""),

		new("class-relationships", "All Relationships", DiagramCategory.Class, """
			classDiagram
			  A <|-- B : Inheritance
			  C *-- D : Composition
			  E o-- F : Aggregation
			  G --> H : Association
			  I ..> J : Dependency
			  K ..|> L : Realization
			"""),

		new("class-interface", "Interface & Service", DiagramCategory.Class, """
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
			"""),

		new("class-visibility", "Visibility Modifiers", DiagramCategory.Class, """
			classDiagram
			  class MyService {
			    +String publicField
			    -int privateField
			    #bool protectedField
			    ~float packageField
			    +getStatus() String
			    -validate() bool
			    #reset() void
			    ~notify() void
			  }
			"""),

		new("class-direction", "Direction Override (LR)", DiagramCategory.Class, """
			classDiagram
			  direction LR
			  class Controller {
			    +handle() void
			  }
			  class Service {
			    +process() void
			  }
			  class Repository {
			    +find() Object
			    +save() void
			  }
			  Controller --> Service
			  Service --> Repository
			""", "direction"),

		new("class-lollipop", "Lollipop Interface", DiagramCategory.Class, """
			classDiagram
			  class Shape {
			    <<interface>>
			    +area() double
			  }
			  class Drawable {
			    <<interface>>
			    +draw() void
			  }
			  class Circle {
			    +double radius
			    +area() double
			    +draw() void
			  }
			  Circle ..|> Shape
			  Circle --() Drawable
			""", "lollipop"),

		new("class-notes", "Class Notes", DiagramCategory.Class, """
			classDiagram
			  class UserService {
			    +createUser() User
			    +deleteUser(id) void
			  }
			  class User {
			    +String name
			    +String email
			  }
			  UserService --> User
			  note for UserService "Handles all user CRUD"
			  note for User "Core domain entity"
			""", "notes"),

		new("class-namespace", "Namespace Grouping", DiagramCategory.Class, """
			classDiagram
			  namespace Domain {
			    class Order {
			      +int id
			      +place() void
			    }
			    class Product {
			      +String name
			      +double price
			    }
			  }
			  namespace Infrastructure {
			    class OrderRepo {
			      +save(Order) void
			    }
			    class ProductRepo {
			      +findAll() List
			    }
			  }
			  Order --> Product
			  OrderRepo ..|> Order
			  ProductRepo ..|> Product
			"""),

		// ── ER ─────────────────────────────────────────────────────────

		new("er-basic", "Basic ER", DiagramCategory.Er, """
			erDiagram
			  CUSTOMER ||--o{ ORDER : places
			  ORDER ||--|{ LINE_ITEM : contains
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
			  LINE_ITEM {
			    int id PK
			    int quantity
			    float price
			  }
			"""),

		new("er-complex", "Complex Relations", DiagramCategory.Er, """
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
			"""),

		new("er-cardinalities", "All Cardinalities", DiagramCategory.Er, """
			erDiagram
			  A ||--|| B : one-to-one
			  C ||--o{ D : one-to-zero-or-many
			  E ||--|{ F : one-to-one-or-many
			  G }o--o{ H : zero-or-many-to-zero-or-many
			"""),

		new("er-direction", "Direction Override (TD)", DiagramCategory.Er, """
			erDiagram
			  direction TD
			  CUSTOMER ||--o{ ORDER : places
			  ORDER ||--|{ LINE_ITEM : contains
			  CUSTOMER {
			    string name PK
			    string email
			  }
			  ORDER {
			    int id PK
			    date created
			  }
			  LINE_ITEM {
			    int id PK
			    int quantity
			  }
			""", "direction"),

		new("er-optional-label", "Optional Labels", DiagramCategory.Er, """
			erDiagram
			  PERSON ||--o{ ADDRESS
			  PERSON ||--|{ PHONE
			  PERSON ||--o{ ORDER : places
			  ORDER ||--|{ LINE_ITEM : contains
			""", "optional labels"),

		new("er-aliases", "Entity Aliases", DiagramCategory.Er, """
			erDiagram
			  cust["Customer Account"] {
			    int id PK
			    string name
			    string email UK
			  }
			  ord[Order] {
			    int id PK
			    date created
			    string status
			  }
			  li["Line Item"] {
			    int id PK
			    int qty
			    float price
			  }
			  cust ||--o{ ord : places
			  ord ||--|{ li : contains
			""", "entity aliases"),

		// ── Pie Chart ──────────────────────────────────────────────────

		new("pie-basic", "Basic Pie", DiagramCategory.Pie, """
			pie
			title Pet Adoption
			"Dogs" : 386
			"Cats" : 85
			"Rats" : 15
			"""),

		new("pie-showdata", "Pie with Data Values", DiagramCategory.Pie, """
			pie showData
			title Browser Market Share
			"Chrome" : 65.3
			"Safari" : 18.8
			"Firefox" : 3.2
			"Edge" : 4.7
			"Other" : 8.0
			""", "showData"),

		new("pie-many-slices", "Many Slices", DiagramCategory.Pie, """
			pie
			title Revenue by Region
			"North America" : 42
			"Europe" : 28
			"Asia Pacific" : 18
			"Latin America" : 7
			"Middle East" : 3
			"Africa" : 2
			"""),

		new("pie-inline-title", "Inline Title", DiagramCategory.Pie, """
			pie title Team Lunch Vote
			"Pizza" : 12
			"Sushi" : 8
			"Tacos" : 5
			"Salad" : 3
			"""),

		new("pie-showdata-inline-title", "showData + Inline Title", DiagramCategory.Pie, """
			pie showData title CI Pipeline Outcomes
			"Success" : 187
			"Failed" : 23
			"Cancelled" : 9
			""", "showData"),

		// ── Quadrant Chart ─────────────────────────────────────────────

		new("quadrant-basic", "Priority Matrix", DiagramCategory.Quadrant, """
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
			Feature D: [0.3, 0.8]
			"""),

		new("quadrant-skills", "Skills Assessment", DiagramCategory.Quadrant, """
			quadrantChart
			title Technical Skills Matrix
			x-axis Beginner --> Expert
			y-axis Low Demand --> High Demand
			quadrant-1 Invest
			quadrant-2 Maintain
			quadrant-3 Deprioritize
			quadrant-4 Phase Out
			Kubernetes: [0.4, 0.9]
			React: [0.7, 0.8]
			COBOL: [0.3, 0.1]
			Rust: [0.3, 0.7]
			Python: [0.8, 0.9]
			"""),

		new("quadrant-inline-title", "Inline Title", DiagramCategory.Quadrant, """
			quadrantChart title Feature Prioritization
			x-axis Low Complexity --> High Complexity
			y-axis Low Value --> High Value
			quadrant-1 Quick Wins
			quadrant-2 Big Bets
			quadrant-3 Low Priority
			quadrant-4 Fill-Ins
			Dark Mode: [0.2, 0.8]
			SSO: [0.6, 0.9]
			CSV Export: [0.2, 0.4]
			AI Suggestions: [0.9, 0.7]
			"""),

		// ── Timeline ───────────────────────────────────────────────────

		new("timeline-sections", "Timeline with Sections", DiagramCategory.Timeline, """
			timeline
			title History of Social Media
			section Early Days
			2002 : LinkedIn
			2004 : Facebook : Google
			section Growth
			2006 : Twitter
			2010 : Instagram
			section Modern Era
			2016 : TikTok
			2019 : Threads
			"""),

		new("timeline-simple", "Simple Timeline", DiagramCategory.Timeline, """
			timeline
			title Product Roadmap
			Q1 2025 : Alpha Release
			Q2 2025 : Beta Launch : Partner Onboarding
			Q3 2025 : GA Release
			Q4 2025 : Enterprise Features
			"""),

		new("timeline-inline-title", "Inline Title", DiagramCategory.Timeline, """
			timeline title Programming Language Milestones
			1972 : C
			1983 : C++
			1991 : Python
			1995 : Java : JavaScript : Ruby
			2009 : Go
			2015 : Rust
			2016 : .NET Core
			"""),

		// ── GitGraph ───────────────────────────────────────────────────

		new("gitgraph-basic", "Basic Git Flow", DiagramCategory.GitGraph, """
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
			"""),

		new("gitgraph-feature", "Feature Branches", DiagramCategory.GitGraph, """
			gitGraph
			commit id: "init"
			branch feature-a
			checkout feature-a
			commit id: "a1"
			commit id: "a2"
			checkout main
			branch feature-b
			checkout feature-b
			commit id: "b1"
			checkout main
			merge feature-a id: "merge-a"
			merge feature-b id: "merge-b"
			commit id: "release" tag: "v2.0"
			"""),

		new("gitgraph-hotfix", "Hotfix Branch", DiagramCategory.GitGraph, """
			gitGraph
			commit id: "v1.0" tag: "v1.0"
			branch develop
			checkout develop
			commit id: "feat"
			checkout main
			branch hotfix
			checkout hotfix
			commit id: "fix" type: REVERSE
			checkout main
			merge hotfix id: "patch" tag: "v1.0.1"
			checkout develop
			merge hotfix id: "sync"
			commit id: "more-work"
			"""),

		// ── Radar Chart ────────────────────────────────────────────────

		new("radar-skills", "Skills Comparison", DiagramCategory.Radar, """
			radar-beta
			title Skills Assessment
			axis Design, Frontend, Backend, DevOps, Testing
			curve c1["Team A"]{4, 3, 5, 2, 4}
			curve c2["Team B"]{3, 5, 2, 4, 3}
			max 5
			graticule polygon
			"""),

		new("radar-product", "Product Comparison", DiagramCategory.Radar, """
			radar-beta
			title Product Comparison
			axis Price, Quality, Features, Support, Speed, UX
			curve c1["Product A"]{4, 5, 3, 4, 5, 4}
			curve c2["Product B"]{3, 3, 5, 2, 3, 5}
			max 5
			"""),

		// ── Treemap ────────────────────────────────────────────────────

		new("treemap-flat", "Flat Treemap", DiagramCategory.Treemap, """
			treemap-beta
			"Engineering": 50
			"Marketing": 25
			"Sales": 15
			"Support": 10
			"""),

		new("treemap-nested", "Nested Treemap", DiagramCategory.Treemap,
			"treemap-beta\n  \"Technology\"\n    \"Frontend\": 30\n    \"Backend\": 40\n    \"DevOps\": 15\n  \"Business\"\n    \"Sales\": 25\n    \"Marketing\": 20"),

		// ── Venn Diagram ───────────────────────────────────────────────

		new("venn-two", "Two-Set Venn", DiagramCategory.Venn, """
			venn-beta
			set A["Frontend"]
			set B["Backend"]
			union A, B["Full Stack"]
			"""),

		new("venn-three", "Three-Set Venn", DiagramCategory.Venn, """
			venn-beta
			set A["Design"]
			set B["Engineering"]
			set C["Product"]
			union A, B["Design Systems"]
			union B, C["Technical PM"]
			union A, C["UX Research"]
			"""),

		// ── Mindmap ────────────────────────────────────────────────────

		new("mindmap-project", "Project Mindmap", DiagramCategory.Mindmap,
			"mindmap\n  ((Project))\n    (Planning)\n      Requirements\n      Timeline\n    [Development]\n      Frontend\n      Backend\n    {{Testing}}\n      Unit Tests\n      Integration"),

		new("mindmap-learning", "Learning Path", DiagramCategory.Mindmap,
			"mindmap\n  ((Web Development))\n    (Frontend)\n      HTML\n      CSS\n      JavaScript\n    (Backend)\n      .NET\n      Node.js\n    ))Cloud((\n      AWS\n      Azure"),

		// ── Architecture ───────────────────────────────────────────────

		new("architecture-icon-showcase", "Icon Showcase (all icons)", DiagramCategory.Architecture, """
			architecture-beta
			group defaultPack(cloud)[Default Icons]
			service dCloud(cloud)[cloud] in defaultPack
			service dDatabase(database)[database] in defaultPack
			service dDisk(disk)[disk] in defaultPack
			service dInternet(internet)[internet] in defaultPack
			service dServer(server)[server] in defaultPack
			service dGeneric(generic)[generic] in defaultPack
			dCloud:R -- L:dDatabase
			dCloud:B -- T:dDisk
			dDatabase:B -- T:dInternet
			dDisk:R -- L:dInternet
			dDisk:B -- T:dServer
			dInternet:B -- T:dGeneric
			dServer:R -- L:dGeneric

			group awsPack(aws:compute)[AWS Icons]
			service awsCompute(aws:compute)[aws-compute] in awsPack
			service awsStorage(aws:storage)[aws-storage] in awsPack
			service awsDatabase(aws:database)[aws-database] in awsPack
			service awsNetworking(aws:networking)[aws-networking] in awsPack
			service awsServerless(aws:serverless)[aws-serverless] in awsPack
			service awsLoadBalancer(aws:load-balancer)[aws-load-balancer] in awsPack
			service awsQueue(aws:queue)[aws-queue] in awsPack
			service awsCdn(aws:cdn)[aws-cdn] in awsPack
			service awsCache(aws:cache)[aws-cache] in awsPack
			awsCompute:R -- L:awsStorage
			awsStorage:R -- L:awsDatabase
			awsCompute:B -- T:awsNetworking
			awsStorage:B -- T:awsServerless
			awsDatabase:B -- T:awsLoadBalancer
			awsNetworking:R -- L:awsServerless
			awsServerless:R -- L:awsLoadBalancer
			awsNetworking:B -- T:awsQueue
			awsServerless:B -- T:awsCdn
			awsLoadBalancer:B -- T:awsCache
			awsQueue:R -- L:awsCdn
			awsCdn:R -- L:awsCache

			group azurePack(azure:compute)[Azure Icons]
			service azureCompute(azure:compute)[azure-compute] in azurePack
			service azureStorage(azure:storage)[azure-storage] in azurePack
			service azureDatabase(azure:database)[azure-database] in azurePack
			service azureNetworking(azure:networking)[azure-networking] in azurePack
			service azureServerless(azure:serverless)[azure-serverless] in azurePack
			service azureLoadBalancer(azure:load-balancer)[azure-load-balancer] in azurePack
			service azureQueue(azure:queue)[azure-queue] in azurePack
			service azureCdn(azure:cdn)[azure-cdn] in azurePack
			service azureCache(azure:cache)[azure-cache] in azurePack
			azureCompute:R -- L:azureStorage
			azureStorage:R -- L:azureDatabase
			azureCompute:B -- T:azureNetworking
			azureStorage:B -- T:azureServerless
			azureDatabase:B -- T:azureLoadBalancer
			azureNetworking:R -- L:azureServerless
			azureServerless:R -- L:azureLoadBalancer
			azureNetworking:B -- T:azureQueue
			azureServerless:B -- T:azureCdn
			azureLoadBalancer:B -- T:azureCache
			azureQueue:R -- L:azureCdn
			azureCdn:R -- L:azureCache

			group gcpPack(gcp:compute)[GCP Icons]
			service gcpCompute(gcp:compute)[gcp-compute] in gcpPack
			service gcpStorage(gcp:storage)[gcp-storage] in gcpPack
			service gcpDatabase(gcp:database)[gcp-database] in gcpPack
			service gcpNetworking(gcp:networking)[gcp-networking] in gcpPack
			service gcpServerless(gcp:serverless)[gcp-serverless] in gcpPack
			service gcpLoadBalancer(gcp:load-balancer)[gcp-load-balancer] in gcpPack
			service gcpQueue(gcp:queue)[gcp-queue] in gcpPack
			service gcpCdn(gcp:cdn)[gcp-cdn] in gcpPack
			service gcpCache(gcp:cache)[gcp-cache] in gcpPack
			gcpCompute:R -- L:gcpStorage
			gcpStorage:R -- L:gcpDatabase
			gcpCompute:B -- T:gcpNetworking
			gcpStorage:B -- T:gcpServerless
			gcpDatabase:B -- T:gcpLoadBalancer
			gcpNetworking:R -- L:gcpServerless
			gcpServerless:R -- L:gcpLoadBalancer
			gcpNetworking:B -- T:gcpQueue
			gcpServerless:B -- T:gcpCdn
			gcpLoadBalancer:B -- T:gcpCache
			gcpQueue:R -- L:gcpCdn
			gcpCdn:R -- L:gcpCache

			group elasticPack(elastic:elasticsearch)[Elastic Icons]
			service esElasticsearch(elastic:elasticsearch)[elastic-elasticsearch] in elasticPack
			service esKibana(elastic:kibana)[elastic-kibana] in elasticPack
			service esLogstash(elastic:logstash)[elastic-logstash] in elasticPack
			service esBeats(elastic:beats)[elastic-beats] in elasticPack
			service esFleet(elastic:fleet)[elastic-fleet] in elasticPack
			service esServerless(elastic:serverless)[elastic-serverless] in elasticPack
			service esApm(elastic:apm)[elastic-apm] in elasticPack
			service esSecurity(elastic:security)[elastic-security] in elasticPack
			service esObservability(elastic:observability)[elastic-observability] in elasticPack
			esElasticsearch:R -- L:esKibana
			esKibana:R -- L:esLogstash
			esElasticsearch:B -- T:esBeats
			esKibana:B -- T:esFleet
			esLogstash:B -- T:esServerless
			esBeats:R -- L:esFleet
			esFleet:R -- L:esServerless
			esBeats:B -- T:esApm
			esFleet:B -- T:esSecurity
			esServerless:B -- T:esObservability
			esApm:R -- L:esSecurity
			esSecurity:R -- L:esObservability

			group extPack(ext:api)[Generic (ext:) Icons]
			service extWaf(ext:waf)[ext-waf] in extPack
			service extApiGateway(ext:api-gateway)[ext-api-gateway] in extPack
			service extK8s(ext:k8s)[ext-k8s] in extPack
			service extPod(ext:pod)[ext-pod] in extPack
			service extPool(ext:pool)[ext-pool] in extPack
			service extReverseProxy(ext:reverse-proxy)[ext-reverse-proxy] in extPack
			service extWeb(ext:web)[ext-web] in extPack
			service extApi(ext:api)[ext-api] in extPack
			service extLoadBalancer(ext:load-balancer)[ext-load-balancer] in extPack
			service extQueue(ext:queue)[ext-queue] in extPack
			service extCdn(ext:cdn)[ext-cdn] in extPack
			service extCache(ext:cache)[ext-cache] in extPack
			extWaf:R -- L:extApiGateway
			extApiGateway:R -- L:extK8s
			extWaf:B -- T:extPod
			extApiGateway:B -- T:extPool
			extK8s:B -- T:extReverseProxy
			extPod:R -- L:extPool
			extPool:R -- L:extReverseProxy
			extPod:B -- T:extWeb
			extPool:B -- T:extApi
			extReverseProxy:B -- T:extLoadBalancer
			extWeb:R -- L:extApi
			extApi:R -- L:extLoadBalancer
			extWeb:B -- T:extQueue
			extApi:B -- T:extCdn
			extLoadBalancer:B -- T:extCache
			extQueue:R -- L:extCdn
			extCdn:R -- L:extCache
			""", Feature: "icons"),

		new("architecture-basic", "Cloud API", DiagramCategory.Architecture, """
			architecture-beta
			group api(cloud)[API]
			service db(database)[Database] in api
			service disk1(disk)[Storage] in api
			service server(server)[Server] in api
			server:T -- B:disk1
			server:L -- R:db
			"""),

		new("architecture-junction", "Junction Routing", DiagramCategory.Architecture, """
			architecture-beta
			service a(server)[Gateway]
			service b(server)[ServiceA]
			service c(server)[ServiceB]
			junction j
			a:R -- L:j
			j:R -- L:b
			j:B -- T:c
			"""),

		new("architecture-arrow-directions", "Edge Arrow Directions", DiagramCategory.Architecture, """
			architecture-beta
			service a(server)[Client]
			service b(server)[Server]
			a:R -- L:b

			service c(server)[Sender]
			service d(server)[Receiver]
			c:R --> L:d

			service e(server)[Follower]
			service f(server)[Leader]
			e:R <-- L:f

			service g(server)[Node A]
			service h(server)[Node B]
			g:R <--> L:h
			""", Feature: "arrows"),

		new("architecture-vendor-icons", "Vendor Icon Packs", DiagramCategory.Architecture, """
			architecture-beta
			group awsGroup(cloud)[AWS]
			service awsCompute(aws:compute)[EC2] in awsGroup
			service awsStorage(aws:storage)[S3] in awsGroup

			group azureGroup(cloud)[Azure]
			service azureCompute(azure:compute)[VM] in azureGroup
			service azureStorage(azure:storage)[Blob] in azureGroup

			group gcpGroup(cloud)[GCP]
			service gcpCompute(gcp:compute)[Compute Engine] in gcpGroup
			service gcpStorage(gcp:storage)[Cloud Storage] in gcpGroup

			service search(elastic:elasticsearch)[Elasticsearch]

			awsCompute:R -- L:awsStorage
			azureCompute:R -- L:azureStorage
			gcpCompute:R -- L:gcpStorage
			awsStorage:B -- T:search
			gcpStorage:B -- T:search
			""", Feature: "icons"),

		new("architecture-elastic-stack", "Elastic Stack", DiagramCategory.Architecture, """
			architecture-beta
			group stack(cloud)[Elastic Stack]
			service beats(elastic:beats)[Beats] in stack
			service ls(elastic:logstash)[Logstash] in stack
			service es(elastic:elasticsearch)[Elasticsearch] in stack
			service kbn(elastic:kibana)[Kibana] in stack
			service fleet(elastic:fleet)[Fleet] in stack
			beats:R -- L:ls
			ls:R -- L:es
			es:R -- L:kbn
			fleet:T -- B:beats
			""", Feature: "icons"),

		new("architecture-networking", "Multi-Cloud Networking", DiagramCategory.Architecture, """
			architecture-beta
			group awsNet(cloud)[AWS]
			service awsVpc(aws:networking)[VPC] in awsNet
			service awsCompute(aws:compute)[EC2] in awsNet
			awsVpc:R -- L:awsCompute

			group azureNet(cloud)[Azure]
			service azureVnet(azure:networking)[VNet] in azureNet
			service azureCompute(azure:compute)[VM] in azureNet
			azureVnet:R -- L:azureCompute

			group gcpNet(cloud)[GCP]
			service gcpVpc(gcp:networking)[VPC] in gcpNet
			service gcpCompute(gcp:compute)[Compute Engine] in gcpNet
			gcpVpc:R -- L:gcpCompute
			""", Feature: "icons"),

		new("architecture-observability", "Observability Pipeline (bug report)", DiagramCategory.Architecture, """
			architecture-beta
			group k8s(cloud)[k8s]
			group ech(cloud)[ECH]

			service edot(server)[EDOT] in k8s
			service oteldemo(server)[OtelDemo] in k8s
			service es(server)[Elasticsearch] in ech
			service kbn(server)[Kibana] in ech
			service apm(server)[APM] in ech

			junction otlp

			edot:L -- R:otlp
			otlp:L -- T:apm
			oteldemo:L -- R:edot
			kbn:L -- T:es
			apm:L -- R:es
			"""),

		// ── Real World (RFC diagrams) ─────────────────────────────────

		..CreateNewDiagramTypeExamples(),
		..CreateRequirementExamples(),
		..CreateTreeViewExamples(),
		..CreateRealWorldExamples(),
	];

	public static DiagramExample[] ByCategory(DiagramCategory category) =>
		All.Where(e => e.Category == category).ToArray();

	public static string CategoryLabel(DiagramCategory c) => c switch
	{
		DiagramCategory.Flowchart => "Flowchart",
		DiagramCategory.Sequence => "Sequence",
		DiagramCategory.State => "State",
		DiagramCategory.Class => "Class",
		DiagramCategory.Er => "ER",
		DiagramCategory.Pie => "Pie Chart",
		DiagramCategory.Quadrant => "Quadrant Chart",
		DiagramCategory.Timeline => "Timeline",
		DiagramCategory.GitGraph => "GitGraph",
		DiagramCategory.Radar => "Radar Chart",
		DiagramCategory.Treemap => "Treemap",
		DiagramCategory.Venn => "Venn Diagram",
		DiagramCategory.Mindmap => "Mindmap",
		DiagramCategory.Gantt => "Gantt",
		DiagramCategory.Journey => "User Journey",
		DiagramCategory.C4 => "C4",
		DiagramCategory.Sankey => "Sankey",
		DiagramCategory.XyChart => "XY Chart",
		DiagramCategory.Requirement => "Requirement",
		DiagramCategory.Packet => "Packet",
		DiagramCategory.Kanban => "Kanban",
		DiagramCategory.Architecture => "Architecture",
		DiagramCategory.Block => "Block",
		DiagramCategory.RealWorld => "Real World",
		_ => c.ToString(),
	};

	public static string CategorySlug(DiagramCategory c) => c switch
	{
		DiagramCategory.Flowchart => "flowchart",
		DiagramCategory.Sequence => "sequence",
		DiagramCategory.State => "state",
		DiagramCategory.Class => "class",
		DiagramCategory.Er => "er",
		DiagramCategory.Pie => "pie",
		DiagramCategory.Quadrant => "quadrant",
		DiagramCategory.Timeline => "timeline",
		DiagramCategory.GitGraph => "gitgraph",
		DiagramCategory.Radar => "radar",
		DiagramCategory.Treemap => "treemap",
		DiagramCategory.Venn => "venn",
		DiagramCategory.Mindmap => "mindmap",
		DiagramCategory.Gantt => "gantt",
		DiagramCategory.Journey => "journey",
		DiagramCategory.C4 => "c4",
		DiagramCategory.Sankey => "sankey",
		DiagramCategory.XyChart => "xychart",
		DiagramCategory.Requirement => "requirement",
		DiagramCategory.Packet => "packet",
		DiagramCategory.Kanban => "kanban",
		DiagramCategory.Architecture => "architecture",
		DiagramCategory.Block => "block",
		DiagramCategory.RealWorld => "real-world",
		_ => c.ToString().ToLowerInvariant(),
	};
}
