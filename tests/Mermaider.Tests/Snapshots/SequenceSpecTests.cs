using AwesomeAssertions;
using Nullean.Snapshots;

namespace Mermaider.Tests.Snapshots;

public class SequenceSpecTests
{
	[Test]
	public void Actor_stick_figures() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			actor Alice
			actor Bob
			Alice->>Bob: Hi Bob
			Bob-->>Alice: Hey Alice
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Mixed_actor_participant() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			actor User
			participant API
			participant DB
			User->>API: GET /data
			API->>DB: SELECT *
			DB-->>API: rows
			API-->>User: 200 OK
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void All_arrow_types() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			participant A
			participant B
			A->>B: Solid arrow
			B-->>A: Dashed arrow
			A-)B: Solid open
			B--)A: Dashed open
			A-xB: Solid cross
			B--xA: Dashed cross
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Self_message() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			participant Service
			Service->>Service: Initialize
			Service->>Service: Validate config
			Service-->>Service: Ready
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Stacked_activations() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			Client->>+Server: Request 1
			Client->>+Server: Request 2
			Server-->>-Client: Response 2
			Server-->>-Client: Response 1
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Opt_block() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			participant A
			participant B
			A->>B: Request
			opt Extra logging
			B->>B: Write to audit log
			end
			B-->>A: Response
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Par_block() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			participant Client
			participant Auth
			participant Data
			Client->>Auth: Validate token
			par Fetch in parallel
			Auth->>Data: Get user profile
			and
			Auth->>Data: Get permissions
			end
			Auth-->>Client: Combined result
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Critical_block() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			participant A
			participant B
			critical Establish connection
			A->>B: Connect
			B-->>A: Ack
			option Network timeout
			A->>A: Retry
			end
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Break_block() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			participant Client
			participant Server
			Client->>Server: Request
			break When rate limited
			Server-->>Client: 429 Too Many Requests
			end
			Server-->>Client: 200 OK
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Rect_highlight() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			participant A
			participant B
			rect rgb(200, 220, 100)
			A->>B: Inside highlight
			B-->>A: Reply
			end
			A->>B: Outside highlight
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Note_left_of() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			participant A
			participant B
			Note left of A: Left note
			A->>B: Hello
			Note right of B: Right note
			Note over A,B: Spanning note
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Nested_blocks() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			participant Client
			participant API
			participant DB
			alt Cache Hit
			API-->>Client: Cached
			else Cache Miss
			API->>DB: Query
			loop Retry on failure
			DB->>DB: Process
			end
			DB-->>API: Result
			API-->>Client: Fresh
			end
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Many_participants() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			participant Browser
			participant CDN
			participant LB as Load Balancer
			participant App
			participant Cache
			participant DB
			Browser->>CDN: Static assets
			Browser->>LB: API request
			LB->>App: Forward
			App->>Cache: Check cache
			Cache-->>App: Miss
			App->>DB: Query
			DB-->>App: Result
			App->>Cache: Store
			App-->>LB: Response
			LB-->>Browser: Response
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Three_participant_conversation() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			participant A as Alice
			participant B as Bob
			participant J as John
			A->>B: Hello Bob
			B->>J: Hi John
			J-->>A: Hey Alice
			A->>J: How are you?
			J-->>B: Tell Alice I'm fine
			B-->>A: John says hi
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Autonumber_default() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			autonumber
			Alice->>Bob: Hello
			Bob->>Alice: Hi
			Alice->>Bob: Bye
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Autonumber_with_start_and_step() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			autonumber 10 5
			Alice->>Bob: First
			Bob->>Alice: Second
			Alice->>Bob: Third
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Bidirectional_arrows() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			participant A
			participant B
			A<<->>B: Solid bidirectional
			A<<-->>B: Dashed bidirectional
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Box_grouping() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			box Purple Internal
			participant A
			participant B
			end
			box Yellow External
			participant C
			end
			A->>B: Internal msg
			B->>C: External msg
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Create_and_destroy_actors() =>
		MermaidRenderer
			.RenderSvg(
				"""
			sequenceDiagram
			participant Alice
			Alice->>Bob: Hello
			create participant Carol
			Bob->>Carol: Hi Carol
			Carol-->>Bob: Thanks
			destroy Carol
			Bob-xCarol: Goodbye
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);
}
