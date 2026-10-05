namespace Mermaider.Examples;

public static partial class DiagramExamples
{
	private static DiagramExample[] CreateRequirementExamples() =>
	[
		// ── Requirement ────────────────────────────────────────────────

		new("requirement-basic", "Basic Requirement", DiagramCategory.Requirement, """
			requirementDiagram

			requirement test_req {
			id: 1
			text: the test text.
			risk: high
			verifymethod: test
			}

			element test_entity {
			type: simulation
			}

			test_entity - satisfies -> test_req
			"""),

		new("requirement-sysml", "SysML Traceability", DiagramCategory.Requirement, """
			requirementDiagram
			direction LR

			functionalRequirement login {
			id: REQ-1
			text: Users must authenticate.
			risk: medium
			verifymethod: test
			}

			performanceRequirement latency {
			id: REQ-2
			text: Auth must complete under 200ms.
			risk: high
			verifymethod: analysis
			}

			element auth_service {
			type: service
			docRef: design/auth.md
			}

			element login_ui {
			type: ui
			}

			auth_service - satisfies -> login
			login_ui - verifies -> login
			latency - derives -> login
			"""),

			new("requirement-kinds", "All Requirement Kinds", DiagramCategory.Requirement, """
				requirementDiagram

				requirement base_req {
				id: R-0
				text: Base requirement
				risk: low
				verifymethod: inspection
				}

				functionalRequirement functional_req {
				id: R-1
				text: Must do the thing
				risk: medium
				verifymethod: demonstration
				}

				interfaceRequirement interface_req {
				id: R-2
				text: Expose a REST API
				risk: low
				verifymethod: test
				}

				performanceRequirement performance_req {
				id: R-3
				text: Respond within 100ms
				risk: high
				verifymethod: analysis
				}

				physicalRequirement physical_req {
				id: R-4
				text: Fit in a 1U rack
				risk: low
				verifymethod: inspection
				}

				designConstraint design_constraint {
				id: R-5
				text: Use the company stack
				risk: medium
				verifymethod: inspection
				}

				base_req - contains -> functional_req
				base_req - contains -> interface_req
				base_req - contains -> performance_req
				base_req - contains -> physical_req
				base_req - contains -> design_constraint
				"""),

			new("requirement-relations", "Relation Types", DiagramCategory.Requirement, """
				requirementDiagram

				requirement parent {
				id: P-1
				text: Parent requirement
				risk: medium
				verifymethod: test
				}

				requirement child {
				id: P-1.1
				text: Refines the parent
				risk: low
				verifymethod: test
				}

				requirement copy {
				id: P-2
				text: A copy of the parent
				risk: low
				verifymethod: inspection
				}

				requirement derived {
				id: P-3
				text: Derived from the parent
				risk: high
				verifymethod: analysis
				}

				element design_doc {
				type: document
				docRef: docs/design.md
				}

				element test_suite {
				type: test suite
				}

				parent - contains -> child
				copy - copies -> child
				derived - derives -> copy
				design_doc - refines -> parent
				test_suite - verifies -> copy
				design_doc - traces -> child
				"""),

			new("requirement-two-systems", "Two Disconnected Systems", DiagramCategory.Requirement, """
				requirementDiagram

				functionalRequirement checkout {
				id: SHOP-1
				text: Customers can pay by card
				risk: high
				verifymethod: test
				}

				element payment_service {
				type: service
				}

				payment_service - satisfies -> checkout

				functionalRequirement alerting {
				id: OPS-1
				text: On-call is paged on a failed deploy
				risk: medium
				verifymethod: demonstration
				}

				element pager_rules {
				type: configuration
				docRef: ops/pager.yml
				}

				pager_rules - satisfies -> alerting
				"""),

			new("requirement-long-text", "Long Text and Names", DiagramCategory.Requirement, """
				requirementDiagram
				direction LR

				requirement a_requirement_with_a_really_long_name_that_wraps {
				id: LONG-1
				text: The system shall keep every audit record for at least seven years and make it retrievable within one business day on request.
				risk: high
				verifymethod: inspection
				}

				element retention_job {
				type: batch job
				docRef: ops/runbooks/retention-and-archival-policy.md
				}

				retention_job - satisfies -> a_requirement_with_a_really_long_name_that_wraps
				"""),
	];
}

