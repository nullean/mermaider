using VerifyTUnit;

namespace Mermaider.Tests.Snapshots;

public class StateSpecTests
{
	[Test]
	public Task Simple_lifecycle() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			stateDiagram-v2
			[*] --> Draft
			Draft --> Review : submit
			Review --> Published : approve
			Review --> Draft : reject
			Published --> Archived : archive
			Archived --> [*]
			"""), "svg");

	[Test]
	public Task Multiple_end_states() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			stateDiagram-v2
			[*] --> Active
			Active --> Inactive : disable
			Active --> Closed : close
			Inactive --> Active : reactivate
			Inactive --> Closed : close
			Closed --> [*]
			"""), "svg");

	[Test]
	public Task Branching_transitions() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			stateDiagram-v2
			[*] --> Idle
			Idle --> Processing : submit
			Processing --> Success : ok
			Processing --> Failed : error
			Success --> [*]
			Failed --> Idle : retry
			Failed --> [*] : abort
			"""), "svg");

	[Test]
	public Task State_descriptions() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			stateDiagram-v2
			[*] --> Booting
			Booting : System is starting up
			Booting --> Ready : initialized
			Ready : Accepting connections
			Ready --> Processing : request
			Processing : Handling request
			Processing --> Ready : done
			"""), "svg");

	[Test]
	public Task V1_syntax() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			stateDiagram
			[*] --> Still
			Still --> [*]
			Still --> Moving
			Moving --> Still
			Moving --> Crash
			Crash --> [*]
			"""), "svg");

	[Test]
	public Task Linear_pipeline() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			stateDiagram-v2
			[*] --> Received
			Received --> Validated : validate
			Validated --> Enriched : enrich
			Enriched --> Stored : persist
			Stored --> Published : notify
			Published --> [*]
			"""), "svg");

	[Test]
	public Task Error_recovery_pattern() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			stateDiagram-v2
			[*] --> Running
			Running --> Error : exception
			Error --> Recovering : auto_heal
			Recovering --> Running : recovered
			Recovering --> Fatal : max_retries
			Running --> Stopped : shutdown
			Fatal --> Stopped : operator
			Stopped --> [*]
			"""), "svg");

	[Test]
	public Task Order_workflow() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			stateDiagram-v2
			[*] --> Pending
			Pending --> Confirmed : payment_ok
			Pending --> Cancelled : timeout
			Confirmed --> Shipped : dispatch
			Shipped --> Delivered : deliver
			Delivered --> [*]
			Cancelled --> [*]
			Shipped --> Returned : return_request
			Returned --> Refunded : process_refund
			Refunded --> [*]
			"""), "svg");

	[Test]
	public Task Choice_pseudostate() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			stateDiagram-v2
			state isPositive <<choice>>
			[*] --> isPositive
			isPositive --> Positive : if n > 0
			isPositive --> Negative : if n < 0
			"""), "svg");

	[Test]
	public Task Fork_and_join() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			stateDiagram-v2
			state fork_state <<fork>>
			state join_state <<join>>
			[*] --> fork_state
			fork_state --> A
			fork_state --> B
			A --> join_state
			B --> join_state
			join_state --> [*]
			"""), "svg");

	[Test]
	public Task State_note() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			stateDiagram-v2
			[*] --> Active
			Active --> Inactive
			note right of Active : Important state
			"""), "svg");

	[Test]
	public Task Composite_state() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			stateDiagram-v2
			[*] --> First
			state First {
				[*] --> second
				second --> third
			}
			First --> Last
			"""), "svg");

	[Test]
	public Task ClassDef_and_style() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			stateDiagram-v2
			classDef badBadEvent fill:#f00,color:white,font-weight:bold,stroke-width:2px,stroke:yellow
			classDef movement font-style:italic
			classDef default fill:#ddd
			[*] --> Still
			Still --> [*]
			Still --> Moving
			Moving --> Still
			Moving --> Crash
			Crash --> [*]
			class Still default
			class Moving movement
			class Crash badBadEvent
			SomeState:::movement
			"""), "svg");
}
