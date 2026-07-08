namespace Mermaider.Gallery;

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
	];
}

