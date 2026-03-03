using VerifyTUnit;

namespace Mermaider.Tests.Snapshots;

public class SequenceSpecTests
{
	[Test]
	public Task Actor_stick_figures() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			sequenceDiagram
			actor Alice
			actor Bob
			Alice->>Bob: Hi Bob
			Bob-->>Alice: Hey Alice
			"""), "svg");

	[Test]
	public Task Mixed_actor_participant() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			sequenceDiagram
			actor User
			participant API
			participant DB
			User->>API: GET /data
			API->>DB: SELECT *
			DB-->>API: rows
			API-->>User: 200 OK
			"""), "svg");

	[Test]
	public Task All_arrow_types() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			sequenceDiagram
			participant A
			participant B
			A->>B: Solid arrow
			B-->>A: Dashed arrow
			A-)B: Solid open
			B--)A: Dashed open
			A-xB: Solid cross
			B--xA: Dashed cross
			"""), "svg");

	[Test]
	public Task Self_message() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			sequenceDiagram
			participant Service
			Service->>Service: Initialize
			Service->>Service: Validate config
			Service-->>Service: Ready
			"""), "svg");

	[Test]
	public Task Stacked_activations() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			sequenceDiagram
			Client->>+Server: Request 1
			Client->>+Server: Request 2
			Server-->>-Client: Response 2
			Server-->>-Client: Response 1
			"""), "svg");

	[Test]
	public Task Opt_block() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			sequenceDiagram
			participant A
			participant B
			A->>B: Request
			opt Extra logging
			B->>B: Write to audit log
			end
			B-->>A: Response
			"""), "svg");

	[Test]
	public Task Par_block() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
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
			"""), "svg");

	[Test]
	public Task Critical_block() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			sequenceDiagram
			participant A
			participant B
			critical Establish connection
			A->>B: Connect
			B-->>A: Ack
			option Network timeout
			A->>A: Retry
			end
			"""), "svg");

	[Test]
	public Task Break_block() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			sequenceDiagram
			participant Client
			participant Server
			Client->>Server: Request
			break When rate limited
			Server-->>Client: 429 Too Many Requests
			end
			Server-->>Client: 200 OK
			"""), "svg");

	[Test]
	public Task Rect_highlight() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			sequenceDiagram
			participant A
			participant B
			rect rgb(200, 220, 100)
			A->>B: Inside highlight
			B-->>A: Reply
			end
			A->>B: Outside highlight
			"""), "svg");

	[Test]
	public Task Note_left_of() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			sequenceDiagram
			participant A
			participant B
			Note left of A: Left note
			A->>B: Hello
			Note right of B: Right note
			Note over A,B: Spanning note
			"""), "svg");

	[Test]
	public Task Nested_blocks() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
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
			"""), "svg");

	[Test]
	public Task Many_participants() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
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
			"""), "svg");

	[Test]
	public Task Three_participant_conversation() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
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
			"""), "svg");

	[Test]
	public Task Autonumber_default() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			sequenceDiagram
			autonumber
			Alice->>Bob: Hello
			Bob->>Alice: Hi
			Alice->>Bob: Bye
			"""), "svg");

	[Test]
	public Task Autonumber_with_start_and_step() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			sequenceDiagram
			autonumber 10 5
			Alice->>Bob: First
			Bob->>Alice: Second
			Alice->>Bob: Third
			"""), "svg");

	[Test]
	public Task Bidirectional_arrows() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			sequenceDiagram
			participant A
			participant B
			A<<->>B: Solid bidirectional
			A<<-->>B: Dashed bidirectional
			"""), "svg");

	[Test]
	public Task Box_grouping() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
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
			"""), "svg");

	[Test]
	public Task Create_and_destroy_actors() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			sequenceDiagram
			participant Alice
			Alice->>Bob: Hello
			create participant Carol
			Bob->>Carol: Hi Carol
			Carol-->>Bob: Thanks
			destroy Carol
			Bob-xCarol: Goodbye
			"""), "svg");
}
