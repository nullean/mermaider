namespace Mermaider.Examples;

public static partial class DiagramExamples
{
	private static DiagramExample[] CreateNewDiagramTypeExamples() =>
	[
		new("gantt-shipping", "Shipping Schedule", DiagramCategory.Gantt, """
			gantt
			  title Shipping this file
			  dateFormat  YYYY-MM-DD
			  section Render
			  Spike the renderer :done, a1, 2026-07-07, 1d
			  Print this page    :active, a2, after a1, 1d
			  section Polish
			  Update tests       :crit, after a2, 12h
			  Update docs        : 6h
			"""),

		new("gantt-milestones", "Milestones", DiagramCategory.Gantt, """
			gantt
			  title Release train
			  dateFormat YYYY-MM-DD
			  section Build
			  Implement parser :done, p1, 2026-01-06, 3d
			  Add tests        :active, p2, after p1, 2d
			  section Ship
			  RC cut           :milestone, m1, after p2, 0d
			  GA               :crit, p3, after m1, 5d
			"""),

		new("gantt-multi-after", "Multi-dependency", DiagramCategory.Gantt, """
			gantt
			  title Multi-dependency demo
			  dateFormat YYYY-MM-DD
			  section Backend
			  API design    :done, b1, 2026-01-05, 2d
			  Implement     :done, b2, after b1, 3d
			  section Frontend
			  UI design     :done, f1, 2026-01-05, 2d
			  Build         :active, f2, after f1, 3d
			  section Release
			  Integration   :crit, r1, after b2 f2, 2d
			  QA            :crit, r2, after r1, 1d
			  Launch        :milestone, m1, after r2, 0d
			"""),

		new("journey-workday", "Working Day", DiagramCategory.Journey, """
			journey
			  title My working day
			  section Go to work
			    Make tea: 5: Me
			    Go upstairs: 3: Me
			    Do work: 1: Me, Cat
			  section Go home
			    Go downstairs: 5: Me
			    Sit down: 5: Me
			"""),

		new("journey-onboarding", "App Onboarding", DiagramCategory.Journey, """
			journey
			  title First-time login
			  section Discover
			    Open app: 4: User
			    See welcome: 5: User
			  section Authenticate
			    Enter email: 3: User
			    Confirm MFA: 2: User, System
			  section Success
			    Reach dashboard: 5: User
			"""),

		new("c4-context", "System Context", DiagramCategory.C4, """
			C4Context
			title System Context diagram for Internet Banking System
			Person(customer, "Banking Customer", "A customer of the bank, with personal bank accounts.")
			System(banking, "Internet Banking System", "Allows customers to view accounts and make payments.")
			System_Ext(mail, "E-mail System", "The internal Microsoft Exchange e-mail system.")
			SystemDb_Ext(mainframe, "Mainframe Banking System", "Stores core banking information.")
			Rel(customer, banking, "Uses")
			Rel(banking, mail, "Sends e-mails", "SMTP")
			Rel(banking, mainframe, "Uses")
			"""),

		new("c4-container", "Container Diagram", DiagramCategory.C4, """
			C4Container
			title Container diagram for Internet Banking System
			Person(customer, "Customer", "A customer of the bank")
			Container_Boundary(c1, "Internet Banking") {
			  Container(spa, "Single-Page App", "JavaScript, Angular", "Provides banking UI")
			  Container(api, "API Application", "Java, Spring", "Provides banking functionality via API")
			  ContainerDb(db, "Database", "SQL Database", "Stores user registration info")
			}
			System_Ext(mail, "E-mail System", "Microsoft Exchange")
			Rel(customer, spa, "Uses", "HTTPS")
			Rel(spa, api, "Uses", "JSON/HTTPS")
			Rel(api, db, "Reads from and writes to", "JDBC")
			Rel(api, mail, "Sends e-mails", "SMTP")
			"""),

		new("sankey-energy", "Energy Flow", DiagramCategory.Sankey, """
			sankey-beta
			Electricity grid,Over generation / exports,104.453
			Electricity grid,Heating and cooling - homes,113.726
			Electricity grid,Industry,342.165
			Electricity grid,Losses,56.691
			Thermal generation,Electricity grid,525.531
			Nuclear,Thermal generation,839.978
			Wind,Electricity grid,289.366
			"""),

		new("sankey-funnel", "Funnel", DiagramCategory.Sankey, """
			sankey-beta
			Visitors,Signups,1000
			Signups,Trials,400
			Trials,Paid,120
			Trials,Churned,280
			"""),

		new("sankey-quoted", "Quoted Names", DiagramCategory.Sankey, """
			sankey-beta
			"Pumped heat","Heating and cooling, homes",193.026
			"Pumped heat","Heating and cooling, commercial",70.672
			"Solar thermal","Heating and cooling, homes",11.606
			"Solar thermal","Heating and cooling, commercial",3.3
			"""),

		new("xychart-sales", "Sales Revenue", DiagramCategory.XyChart, """
			xychart-beta
			title "Sales Revenue"
			x-axis [jan, feb, mar, apr, may, jun, jul, aug, sep, oct, nov, dec]
			y-axis "Revenue (in $)" 4000 --> 11000
			bar [5000, 6000, 7500, 8200, 9500, 10500, 11000, 10200, 9200, 8500, 7000, 6000]
			line [5000, 6000, 7500, 8200, 9500, 10500, 11000, 10200, 9200, 8500, 7000, 6000]
			"""),

		new("xychart-latency", "Latency Lines", DiagramCategory.XyChart, """
			xychart-beta
			title "An Example Chart"
			x-axis ["90d", "60d", "30d", "7d", "1d", "Current"]
			y-axis "Seconds" 0 --> 200
			line "avg" [48.1, 41.5, 45.7, 72.8, 67.7, 59.9]
			line "p50" [38.2, 36.8, 39.7, 54.5, 49.0, 38.4]
			line "p95" [112.2, 75.3, 103.0, 177.0, 180.2, 109.4]
			"""),

		new("packet-udp", "UDP Header", DiagramCategory.Packet, """
			packet-beta
			title UDP Header
			0-15: "Source Port"
			16-31: "Destination Port"
			32-47: "Length"
			48-63: "Checksum"
			"""),

		new("packet-tcp-flags", "Bit-count Form", DiagramCategory.Packet, """
			packet
			title TCP Segment (partial)
			+16: "Source Port"
			+16: "Dest Port"
			+32: "Sequence Number"
			+32: "Ack Number"
			"""),

		new("kanban-sprint", "Sprint Board", DiagramCategory.Kanban, """
			kanban
			  Todo
			    Task1
			    Task2
			  In Progress
			    Task3
			  Done
			    Task4
			"""),

		new("kanban-metadata", "Tasks with Metadata", DiagramCategory.Kanban, """
			kanban
			  todo[To Do]
			    docs[Create Documentation]
			    id8[Design grammar]@{ assigned: 'knsv' }
			  wip[In Progress]
			    id4[Create parsing tests]@{ ticket: MC-2038, assigned: 'K.Sveidqvist', priority: 'High' }
			  done[Done]
			    id5[define getData]
			"""),

		new("kanban-priorities", "Priority Levels", DiagramCategory.Kanban, """
			kanban
			  backlog[Backlog]
			    p1[Security audit]@{ priority: 'Very High' }
			    p2[Perf regression]@{ priority: 'High' }
			    p3[Docs update]@{ priority: 'Low' }
			    p4[Typo fix]@{ priority: 'Very Low' }
			  review[Review]
			    p5[API redesign]@{ assigned: 'alice', priority: 'High' }
			    p6[Changelog]@{ assigned: 'bob', ticket: CH-99, priority: 'Very Low' }
			"""),

		new("kanban-bare-columns", "Bare Column Titles", DiagramCategory.Kanban, """
			kanban
			  [To Do]
			    t1[Implement feature]
			    t2[Write unit tests]
			  [In Review]
			    t3[PR for auth flow]
			  [Done]
			    t4[Deploy to staging]
			"""),

		new("block-grid", "3×2 Grid", DiagramCategory.Block, """
			block-beta
			columns 3
			  A["A"] B["B"] C["C"]
			  D["D"] E["E"] F["F"]
			"""),

		new("block-pipeline", "Pipeline with Edges", DiagramCategory.Block, """
			block-beta
			columns 4
			  In["Input"] Process["Process"] Out["Output"] Store["Store"]
			  In --> Process
			  Process --> Out
			  Out --> Store
			"""),

		new("block-spans", "Column Spans", DiagramCategory.Block, """
			block-beta
			columns 3
			  Header["Header spans all three"]:3
			  Nav["Nav"] Content["Content"]:2
			  Sidebar:2 Aside
			  Footer["Footer"]:3
			"""),

		new("block-labelled-edges", "Labelled Edges", DiagramCategory.Block, """
			block-beta
			columns 3
			  Client["Client"] space API["API"]
			  space space space
			  Queue["Queue"] Worker["Worker"] DB["Database"]
			  Client -- "request" --> API
			  API -- "enqueue" --> Queue
			  Queue -- "pull" --> Worker
			  Worker -- "write" --> DB
			"""),

		new("block-routing", "Routing Around Blocks", DiagramCategory.Block, """
			block-beta
			columns 3
			  A["Source"] B["Middle"] C["Target"]
			  D["Left"] E["Centre"] F["Right"]
			  G["Bottom left"] H["Bottom"] I["Bottom right"]
			  A --> C
			  B --> E
			  E --> H
			  D --> F
			  C --> G
			"""),

		new("block-title-rounded", "Title, Spaces and Rounded Blocks", DiagramCategory.Block, """
			block-beta
			title Release train
			columns 4
			  Plan(Plan) Build(Build) space Ship(Ship)
			  space Test(Test) Review(Review) space
			  Plan --> Build
			  Build --> Test
			  Test --> Review
			  Review --> Ship
			"""),

		new("block-layers", "Layered Architecture", DiagramCategory.Block, """
			block-beta
			columns 4
			  UI["User interface"]:4
			  Auth["Auth"] Billing["Billing"] Search["Search"] Reports["Reports"]
			  Core["Core platform"]:4
			  Postgres["Postgres"]:2 Cache["Cache"] Storage["Object storage"]
			  UI --> Auth
			  UI --> Reports
			  Auth --> Core
			  Billing --> Core
			  Search --> Core
			  Reports --> Core
			"""),

		new("block-long-labels", "Long Labels", DiagramCategory.Block, """
			block-beta
			columns 3
			  A["A block with a rather long label"] B["Short"] C["Another long label here"]
			  D["Short"] E["Medium length label"] F["F"]
			  A --> B
			  B --> C
			  C --> F
			  D --> E
			"""),

		new("kanban-release-board", "Release Board", DiagramCategory.Kanban, """
			kanban
			  backlog[Backlog]
			    a1[Customer export]@{ ticket: PRJ-101, priority: 'Low' }
			    a2[Dark mode polish]@{ ticket: PRJ-117, priority: 'Very Low' }
			    a3[Audit log retention]@{ ticket: PRJ-124, assigned: 'maria', priority: 'High' }
			  progress[In Progress]
			    b1[Billing migration]@{ ticket: PRJ-088, assigned: 'omar', priority: 'Very High' }
			    b2[Search relevance tuning]@{ ticket: PRJ-092, assigned: 'li' }
			  review[In Review]
			    c1[Rate limiting]@{ ticket: PRJ-077, assigned: 'sam', priority: 'High' }
			  testing[Testing]
			    d1[SSO login flow]@{ ticket: PRJ-070, assigned: 'maria' }
			    d2[Mobile layout]@{ ticket: PRJ-071, priority: 'Low' }
			  shipped[Shipped]
			    e1[Webhook retries]@{ ticket: PRJ-050 }
			    e2[Onboarding emails]@{ ticket: PRJ-061 }
			    e3[Faster CSV import]@{ ticket: PRJ-066 }
			"""),

		new("kanban-long-titles", "Long Card Titles", DiagramCategory.Kanban, """
			kanban
			  todo[To Do]
			    t1[Investigate why the nightly reconciliation job occasionally double counts refunds]@{ ticket: OPS-2210, assigned: 'a.very.long.assignee.name', priority: 'High' }
			    t2[Short]
			  doing[Doing]
			    t3[Write the migration guide for the new permissions model and review it with support]@{ assigned: 'dana' }
			"""),

		new("journey-support-ticket", "Support Ticket Journey", DiagramCategory.Journey, """
			journey
			  title Resolving a support ticket
			  section Report
			    Notice the problem: 2: Customer
			    Search the help centre: 3: Customer
			    Open a ticket: 4: Customer, Support
			  section Triage
			    Acknowledge ticket: 5: Support
			    Reproduce the issue: 3: Support, Engineer
			    Escalate to engineering: 2: Support, Engineer
			  section Fix
			    Ship a patch: 4: Engineer
			    Verify with customer: 5: Customer, Support
			  section Follow up
			    Update the docs: 3: Support
			    Send satisfaction survey: 5: Customer
			"""),

		new("journey-bad-day", "Rough Day", DiagramCategory.Journey, """
			journey
			  title A rough deployment
			  section Deploy
			    Merge the change: 5: Dev
			    Pipeline fails: 1: Dev, CI
			    Fix flaky test: 2: Dev
			  section Recover
			    Rollback: 2: Dev, Ops
			    Postmortem: 3: Dev, Ops
			    Celebrate the fix: 5: Dev
			"""),
	];
}
