using AwesomeAssertions;
using Nullean.Snapshots;

namespace Mermaider.Tests.Snapshots;

public class StateSpecTests
{
	[Test]
	public void Simple_lifecycle() =>
		MermaidRenderer
			.RenderSvg(
				"""
			stateDiagram-v2
			[*] --> Draft
			Draft --> Review : submit
			Review --> Published : approve
			Review --> Draft : reject
			Published --> Archived : archive
			Archived --> [*]
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Multiple_end_states() =>
		MermaidRenderer
			.RenderSvg(
				"""
			stateDiagram-v2
			[*] --> Active
			Active --> Inactive : disable
			Active --> Closed : close
			Inactive --> Active : reactivate
			Inactive --> Closed : close
			Closed --> [*]
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Branching_transitions() =>
		MermaidRenderer
			.RenderSvg(
				"""
			stateDiagram-v2
			[*] --> Idle
			Idle --> Processing : submit
			Processing --> Success : ok
			Processing --> Failed : error
			Success --> [*]
			Failed --> Idle : retry
			Failed --> [*] : abort
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void State_descriptions() =>
		MermaidRenderer
			.RenderSvg(
				"""
			stateDiagram-v2
			[*] --> Booting
			Booting : System is starting up
			Booting --> Ready : initialized
			Ready : Accepting connections
			Ready --> Processing : request
			Processing : Handling request
			Processing --> Ready : done
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void V1_syntax() =>
		MermaidRenderer
			.RenderSvg(
				"""
			stateDiagram
			[*] --> Still
			Still --> [*]
			Still --> Moving
			Moving --> Still
			Moving --> Crash
			Crash --> [*]
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Linear_pipeline() =>
		MermaidRenderer
			.RenderSvg(
				"""
			stateDiagram-v2
			[*] --> Received
			Received --> Validated : validate
			Validated --> Enriched : enrich
			Enriched --> Stored : persist
			Stored --> Published : notify
			Published --> [*]
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Error_recovery_pattern() =>
		MermaidRenderer
			.RenderSvg(
				"""
			stateDiagram-v2
			[*] --> Running
			Running --> Error : exception
			Error --> Recovering : auto_heal
			Recovering --> Running : recovered
			Recovering --> Fatal : max_retries
			Running --> Stopped : shutdown
			Fatal --> Stopped : operator
			Stopped --> [*]
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Order_workflow() =>
		MermaidRenderer
			.RenderSvg(
				"""
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
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Choice_pseudostate() =>
		MermaidRenderer
			.RenderSvg(
				"""
			stateDiagram-v2
			state isPositive <<choice>>
			[*] --> isPositive
			isPositive --> Positive : if n > 0
			isPositive --> Negative : if n < 0
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Fork_and_join() =>
		MermaidRenderer
			.RenderSvg(
				"""
			stateDiagram-v2
			state fork_state <<fork>>
			state join_state <<join>>
			[*] --> fork_state
			fork_state --> A
			fork_state --> B
			A --> join_state
			B --> join_state
			join_state --> [*]
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void State_note() =>
		MermaidRenderer
			.RenderSvg(
				"""
			stateDiagram-v2
			[*] --> Active
			Active --> Inactive
			note right of Active : Important state
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Composite_state() =>
		MermaidRenderer
			.RenderSvg(
				"""
			stateDiagram-v2
			[*] --> First
			state First {
				[*] --> second
				second --> third
			}
			First --> Last
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);
}
