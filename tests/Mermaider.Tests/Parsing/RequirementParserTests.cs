using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class RequirementParserTests
{
	[Test]
	public void Parses_basic_requirement_and_element()
	{
		var lines = new[]
		{
			"requirementDiagram",
			"requirement test_req {",
			"id: 1",
			"text: the test text.",
			"risk: high",
			"verifymethod: test",
			"}",
			"element test_entity {",
			"type: simulation",
			"}",
			"test_entity - satisfies -> test_req",
		};

		var diagram = RequirementParser.Parse(lines);

		diagram.Requirements.Should().HaveCount(1);
		var req = diagram.Requirements[0];
		req.Name.Should().Be("test_req");
		req.Kind.Should().Be(RequirementKind.Requirement);
		req.Id.Should().Be("1");
		req.Text.Should().Be("the test text.");
		req.Risk.Should().Be(RequirementRisk.High);
		req.VerifyMethod.Should().Be(RequirementVerifyMethod.Test);

		diagram.Elements.Should().HaveCount(1);
		diagram.Elements[0].Name.Should().Be("test_entity");
		diagram.Elements[0].Type.Should().Be("simulation");

		diagram.Relations.Should().HaveCount(1);
		diagram.Relations[0].Source.Should().Be("test_entity");
		diagram.Relations[0].Target.Should().Be("test_req");
		diagram.Relations[0].Type.Should().Be(RequirementRelationType.Satisfies);
	}

	[Test]
	public void Parses_all_requirement_kinds()
	{
		var lines = new[]
		{
			"requirementDiagram",
			"functionalRequirement f {",
			"}",
			"interfaceRequirement i {",
			"}",
			"performanceRequirement p {",
			"}",
			"physicalRequirement ph {",
			"}",
			"designConstraint d {",
			"}",
		};

		var diagram = RequirementParser.Parse(lines);

		diagram.Requirements.Select(r => r.Kind).Should().BeEquivalentTo(
		[
			RequirementKind.FunctionalRequirement,
			RequirementKind.InterfaceRequirement,
			RequirementKind.PerformanceRequirement,
			RequirementKind.PhysicalRequirement,
			RequirementKind.DesignConstraint,
		]);
	}

	[Test]
	public void Parses_case_insensitive_property_keys()
	{
		var lines = new[]
		{
			"requirementDiagram",
			"requirement r {",
			"ID: ABC",
			"TEXT: Hello",
			"RISK: medium",
			"VERIFYMETHOD: inspection",
			"}",
			"element e {",
			"TYPE: widget",
			"DOCREF: docs/a.md",
			"}",
		};

		var diagram = RequirementParser.Parse(lines);

		diagram.Requirements[0].Id.Should().Be("ABC");
		diagram.Requirements[0].Text.Should().Be("Hello");
		diagram.Requirements[0].Risk.Should().Be(RequirementRisk.Medium);
		diagram.Requirements[0].VerifyMethod.Should().Be(RequirementVerifyMethod.Inspection);
		diagram.Elements[0].Type.Should().Be("widget");
		diagram.Elements[0].DocRef.Should().Be("docs/a.md");
	}

	[Test]
	public void Parses_reverse_relation()
	{
		var lines = new[]
		{
			"requirementDiagram",
			"requirement a {",
			"}",
			"element b {",
			"}",
			"a <- traces - b",
		};

		var diagram = RequirementParser.Parse(lines);

		diagram.Relations.Should().HaveCount(1);
		diagram.Relations[0].Source.Should().Be("b");
		diagram.Relations[0].Target.Should().Be("a");
		diagram.Relations[0].Type.Should().Be(RequirementRelationType.Traces);
	}

	[Test]
	public void Parses_all_relation_types()
	{
		var types = new[]
		{
			"contains", "copies", "derives", "satisfies", "verifies", "refines", "traces",
		};

		var lines = new List<string> { "requirementDiagram", "requirement a {", "}", "element b {", "}" };
		foreach (var t in types)
			lines.Add($"b - {t} -> a");

		var diagram = RequirementParser.Parse([.. lines]);

		diagram.Relations.Should().HaveCount(7);
		diagram.Relations.Select(r => r.Type).Should().BeEquivalentTo(
		[
			RequirementRelationType.Contains,
			RequirementRelationType.Copies,
			RequirementRelationType.Derives,
			RequirementRelationType.Satisfies,
			RequirementRelationType.Verifies,
			RequirementRelationType.Refines,
			RequirementRelationType.Traces,
		]);
	}

	[Test]
	public void Parses_direction()
	{
		var lines = new[]
		{
			"requirementDiagram",
			"direction LR",
			"requirement a {",
			"}",
		};

		var diagram = RequirementParser.Parse(lines);

		diagram.Direction.Should().Be(Direction.LR);
	}

	[Test]
	public void Parses_title()
	{
		var lines = new[]
		{
			"requirementDiagram",
			"title System Requirements",
			"requirement a {",
			"}",
		};

		var diagram = RequirementParser.Parse(lines);

		diagram.Title.Should().Be("System Requirements");
	}

	[Test]
	public void Default_direction_is_TB()
	{
		var lines = new[]
		{
			"requirementDiagram",
			"requirement a {",
			"}",
		};

		var diagram = RequirementParser.Parse(lines);

		diagram.Direction.Should().Be(Direction.TB);
	}
}
