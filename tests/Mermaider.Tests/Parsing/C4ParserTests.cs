using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class C4ParserTests
{
	[Test]
	public void Parses_title_and_kind()
	{
		var diagram = C4Parser.Parse(
		[
			"C4Context",
			"title System Context",
		]);

		diagram.Kind.Should().Be(C4DiagramKind.Context);
		diagram.Title.Should().Be("System Context");
	}

	[Test]
	public void Parses_person_and_system()
	{
		var diagram = C4Parser.Parse(
		[
			"C4Context",
			"""Person(customer, "Banking Customer", "A bank customer.")""",
			"""System(banking, "Internet Banking", "Allows payments.")""",
		]);

		diagram.RootNodes.Should().HaveCount(2);
		var person = diagram.RootNodes[0].Should().BeOfType<C4Element>().Subject;
		person.Alias.Should().Be("customer");
		person.Type.Should().Be(C4ElementType.Person);
		person.Label.Should().Be("Banking Customer");
		person.Description.Should().Be("A bank customer.");
		person.External.Should().BeFalse();

		var system = diagram.RootNodes[1].Should().BeOfType<C4Element>().Subject;
		system.Type.Should().Be(C4ElementType.System);
		system.Label.Should().Be("Internet Banking");
	}

	[Test]
	public void Parses_external_and_db_variants()
	{
		var diagram = C4Parser.Parse(
		[
			"C4Context",
			"""Person_Ext(c, "External Customer")""",
			"""SystemDb_Ext(db, "Mainframe DB", "Core data")""",
			"""SystemQueue(q, "Events", "async queue")""",
		]);

		var person = diagram.RootNodes[0].Should().BeOfType<C4Element>().Subject;
		person.External.Should().BeTrue();
		person.Type.Should().Be(C4ElementType.Person);

		var db = diagram.RootNodes[1].Should().BeOfType<C4Element>().Subject;
		db.External.Should().BeTrue();
		db.Type.Should().Be(C4ElementType.SystemDb);

		var q = diagram.RootNodes[2].Should().BeOfType<C4Element>().Subject;
		q.Type.Should().Be(C4ElementType.SystemQueue);
	}

	[Test]
	public void Parses_nested_boundary()
	{
		var diagram = C4Parser.Parse(
		[
			"C4Context",
			"""Enterprise_Boundary(b0, "Bank") {""",
			"""System(s1, "Core")""",
			"""System_Boundary(b1, "Inner") {""",
			"""System(s2, "Inner Sys")""",
			"}",
			"}",
		]);

		diagram.RootNodes.Should().HaveCount(1);
		var outer = diagram.RootNodes[0].Should().BeOfType<C4Boundary>().Subject;
		outer.Type.Should().Be(C4BoundaryType.Enterprise);
		outer.Label.Should().Be("Bank");
		outer.Children.Should().HaveCount(2);
		outer.Children[0].Should().BeOfType<C4Element>();
		var inner = outer.Children[1].Should().BeOfType<C4Boundary>().Subject;
		inner.Children.Should().HaveCount(1);
	}

	[Test]
	public void Parses_relations()
	{
		var diagram = C4Parser.Parse(
		[
			"C4Context",
			"""Person(a, "A")""",
			"""System(b, "B")""",
			"""Rel(a, b, "Uses", "HTTPS")""",
			"""BiRel(b, a, "Notifies")""",
		]);

		diagram.Relations.Should().HaveCount(2);
		diagram.Relations[0].From.Should().Be("a");
		diagram.Relations[0].To.Should().Be("b");
		diagram.Relations[0].Label.Should().Be("Uses");
		diagram.Relations[0].Technology.Should().Be("HTTPS");
		diagram.Relations[0].Bidirectional.Should().BeFalse();
		diagram.Relations[1].Bidirectional.Should().BeTrue();
	}

	[Test]
	public void Parses_rel_back_swaps_from_to()
	{
		var diagram = C4Parser.Parse(
		[
			"C4Context",
			"""Person(a, "A")""",
			"""System(b, "B")""",
			"""Rel_Back(a, b, "Notified by", "Events")""",
		]);

		diagram.Relations.Should().HaveCount(1);
		// Rel_Back(a, b, …) means arrow b → a (reverse of argument order).
		diagram.Relations[0].From.Should().Be("b");
		diagram.Relations[0].To.Should().Be("a");
		diagram.Relations[0].Label.Should().Be("Notified by");
		diagram.Relations[0].Technology.Should().Be("Events");
		diagram.Relations[0].Bidirectional.Should().BeFalse();
	}

	[Test]
	public void Parses_container_with_technology()
	{
		var diagram = C4Parser.Parse(
		[
			"C4Container",
			"""Container(spa, "SPA", "Angular", "UI layer")""",
			"""ContainerDb(db, "DB", "SQL", "Stores data")""",
		]);

		diagram.Kind.Should().Be(C4DiagramKind.Container);
		var spa = diagram.RootNodes[0].Should().BeOfType<C4Element>().Subject;
		spa.Type.Should().Be(C4ElementType.Container);
		spa.Technology.Should().Be("Angular");
		spa.Description.Should().Be("UI layer");
	}

	[Test]
	public void Parses_layout_config()
	{
		var diagram = C4Parser.Parse(
		[
			"C4Context",
			"""UpdateLayoutConfig($c4ShapeInRow="3", $c4BoundaryInRow="1")""",
			"""Person(a, "A")""",
		]);

		diagram.ShapeInRow.Should().Be(3);
		diagram.BoundaryInRow.Should().Be(1);
	}

	[Test]
	public void Parses_component_and_rel_index()
	{
		var diagram = C4Parser.Parse(
		[
			"C4Component",
			"""Component(c1, "Controller", "MVC", "Handles requests")""",
			"""RelIndex(1, c1, c1, "self")""",
		]);

		diagram.Kind.Should().Be(C4DiagramKind.Component);
		diagram.Relations.Should().HaveCount(1);
		diagram.Relations[0].From.Should().Be("c1");
	}

	[Test]
	public void SplitArgs_handles_quoted_commas()
	{
		var args = C4Parser.SplitArgs("customer, \"A, B, and C\", \"desc\"");
		args.Should().HaveCount(3);
		args[1].Should().Be("\"A, B, and C\"");
	}

	[Test]
	public void Parses_compact_header_title()
	{
		var diagram = C4Parser.Parse(
		[
			"C4Context title Banking",
			"""Person(a, "A")""",
		]);

		diagram.Title.Should().Be("Banking");
		diagram.Kind.Should().Be(C4DiagramKind.Context);
	}

	[Test]
	public void Parses_nested_deployment_node()
	{
		var diagram = C4Parser.Parse(
		[
			"C4Deployment",
			"""Deployment_Node(mob, "Mobile", "iOS") {""",
			"""Container(app, "App", "Xamarin", "UI")""",
			"}",
		]);

		diagram.Kind.Should().Be(C4DiagramKind.Deployment);
		var node = diagram.RootNodes[0].Should().BeOfType<C4Boundary>().Subject;
		node.IsDeploymentNode.Should().BeTrue();
		node.Technology.Should().Be("iOS");
		node.Children.Should().HaveCount(1);
	}
}
