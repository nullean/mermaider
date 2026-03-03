namespace Mermaider.Gallery;

public enum DiagramCategory { Flowchart, Sequence, State, Class, Er }

public sealed record DiagramExample(string Slug, string Title, DiagramCategory Category, string Source, string? Feature = null);

public static class DiagramExamples
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
		_ => c.ToString(),
	};

	public static string CategorySlug(DiagramCategory c) => c switch
	{
		DiagramCategory.Flowchart => "flowchart",
		DiagramCategory.Sequence => "sequence",
		DiagramCategory.State => "state",
		DiagramCategory.Class => "class",
		DiagramCategory.Er => "er",
		_ => c.ToString().ToLowerInvariant(),
	};
}
