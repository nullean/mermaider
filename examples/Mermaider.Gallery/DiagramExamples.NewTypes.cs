namespace Mermaider.Gallery;

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

		new("architecture-basic", "API & Storage", DiagramCategory.Architecture, """
			architecture-beta
			    group api(cloud)[API]
			    service db(database)[Database] in api
			    service disk(disk)[Disk] in api
			    service server(server)[Server] in api
			    db:R --> L:server
			    disk:T --> B:server
			"""),

		new("architecture-grouped", "Services in Group", DiagramCategory.Architecture, """
			architecture-beta
			    group public_api(cloud)[Public API]
			    service server(server)[Server] in public_api
			    service db(database)[Database] in public_api
			    service disk1(disk)[Storage] in public_api

			    db:R -- L:server
			    disk1:T -- B:server
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
	];
}