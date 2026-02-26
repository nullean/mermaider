namespace Mermaid.Gallery;

public static class DiagramExamples
{
	public static readonly (string Slug, string Title, string Source)[] All =
	[
		("flowchart-simple", "Flowchart — Simple", """
			graph TD
			  A[Start] --> B{Decision}
			  B -->|Yes| C[OK]
			  B -->|No| D[Cancel]
			  C --> E[End]
			  D --> E
			"""),

		("flowchart-shapes", "Flowchart — All Shapes", """
			graph LR
			  A[Rectangle] --> B(Rounded)
			  B --> C([Stadium])
			  C --> D{Diamond}
			  D --> E((Circle))
			  E --> F>Asymmetric]
			  F --> G{{Hexagon}}
			  G --> H[[Subroutine]]
			"""),

		("flowchart-edges", "Flowchart — Edge Styles", """
			graph LR
			  A -->|solid| B
			  A -.->|dotted| C
			  A ==>|thick| D
			  E --- F
			  G <--> H
			"""),

		("flowchart-subgraphs", "Flowchart — Subgraphs", """
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

		("flowchart-chained", "Flowchart — Chained & Parallel", """
			graph TD
			  A --> B --> C --> D
			  E & F --> G & H
			"""),

		("flowchart-styled", "Flowchart — ClassDef Styling", """
			graph TD
			  classDef important fill:#f96,color:#fff,stroke:#333,stroke-width:2px
			  classDef muted fill:#eee,color:#999,stroke:#ccc
			  A[Important]:::important --> B[Normal] --> C[Muted]:::muted
			  B --> D[Also Important]:::important
			"""),

		("state-simple", "State — Simple", """
			stateDiagram-v2
			  [*] --> Idle
			  Idle --> Processing : submit
			  Processing --> Success : ok
			  Processing --> Failed : error
			  Success --> [*]
			  Failed --> Idle : retry
			"""),

		("sequence-basic", "Sequence — Basic", """
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

		("sequence-activation", "Sequence — Activation", """
			sequenceDiagram
			  Client->>+Server: POST /login
			  Server->>+DB: SELECT user
			  DB-->>-Server: User row
			  Server->>+Auth: Validate token
			  Auth-->>-Server: OK
			  Server-->>-Client: 200 JWT
			"""),

		("sequence-blocks", "Sequence — Alt/Loop Blocks", """
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

		("sequence-notes", "Sequence — Notes", """
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

		("sequence-self", "Sequence — Self Messages", """
			sequenceDiagram
			  participant A as Service
			  participant B as Queue
			  A->>A: Initialize
			  A->>B: Send message
			  B->>B: Process internally
			  B-->>A: Acknowledgement
			"""),

		("class-basic", "Class — Inheritance", """
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

		("class-relationships", "Class — All Relationships", """
			classDiagram
			  A <|-- B : Inheritance
			  C *-- D : Composition
			  E o-- F : Aggregation
			  G --> H : Association
			  I ..> J : Dependency
			  K ..|> L : Realization
			"""),

		("class-interface", "Class — Interface & Service", """
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

		("er-basic", "ER — Basic", """
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

		("er-complex", "ER — Complex Relations", """
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
	];
}
