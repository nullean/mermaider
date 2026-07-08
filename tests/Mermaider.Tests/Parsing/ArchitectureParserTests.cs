using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class ArchitectureParserTests
{
	[Test]
	public void Parses_group_and_services()
	{
		var lines = new[]
		{
			"architecture-beta",
			"group api(cloud)[API]",
			"service db(database)[Database]",
			"service disk(disk)[Disk]",
		};

		var diagram = ArchitectureParser.Parse(lines);

		diagram.Groups.Should().HaveCount(1);
		diagram.Groups[0].Id.Should().Be("api");
		diagram.Groups[0].Icon.Should().Be("cloud");
		diagram.Groups[0].Label.Should().Be("API");
		diagram.Services.Should().HaveCount(2);
		diagram.Services[0].Id.Should().Be("db");
		diagram.Services[0].Icon.Should().Be("database");
		diagram.Services[1].Id.Should().Be("disk");
	}

	[Test]
	public void Parses_service_in_group()
	{
		var lines = new[]
		{
			"architecture-beta",
			"group api(cloud)[API]",
			"service db(database)[Database] in api",
		};

		var diagram = ArchitectureParser.Parse(lines);

		diagram.Services[0].ParentId.Should().Be("api");
	}

	[Test]
	public void Parses_fixture_style_edges()
	{
		var lines = new[]
		{
			"architecture-beta",
			"group api(cloud)[API]",
			"service db(database)[Database]",
			"api:B --> db:T",
		};

		var diagram = ArchitectureParser.Parse(lines);

		diagram.Edges.Should().HaveCount(1);
		var edge = diagram.Edges[0];
		edge.SourceId.Should().Be("api");
		edge.SourcePort.Should().Be(ArchitecturePort.Bottom);
		edge.TargetId.Should().Be("db");
		edge.TargetPort.Should().Be(ArchitecturePort.Top);
		edge.ArrowToTarget.Should().BeTrue();
		edge.ArrowToSource.Should().BeFalse();
	}

	[Test]
	public void Parses_official_style_edges()
	{
		var lines = new[]
		{
			"architecture-beta",
			"service db(database)[Database]",
			"service server(server)[Server]",
			"db:R -- L:server",
		};

		var diagram = ArchitectureParser.Parse(lines);

		diagram.Edges.Should().HaveCount(1);
		var edge = diagram.Edges[0];
		edge.SourceId.Should().Be("db");
		edge.SourcePort.Should().Be(ArchitecturePort.Right);
		edge.TargetId.Should().Be("server");
		edge.TargetPort.Should().Be(ArchitecturePort.Left);
		edge.ArrowToTarget.Should().BeFalse();
		edge.ArrowToSource.Should().BeFalse();
	}

	[Test]
	public void Parses_bidirectional_arrow()
	{
		var lines = new[]
		{
			"architecture-beta",
			"service a(server)[A]",
			"service b(server)[B]",
			"a:R <--> L:b",
		};

		var diagram = ArchitectureParser.Parse(lines);

		diagram.Edges[0].ArrowToSource.Should().BeTrue();
		diagram.Edges[0].ArrowToTarget.Should().BeTrue();
	}

	[Test]
	public void Parses_architecture_without_beta()
	{
		var lines = new[]
		{
			"architecture",
			"service a(server)[A]",
		};

		var diagram = ArchitectureParser.Parse(lines);

		diagram.Services.Should().HaveCount(1);
		diagram.Services[0].Id.Should().Be("a");
	}

	[Test]
	public void Parses_nested_group()
	{
		var lines = new[]
		{
			"architecture-beta",
			"group public_api(cloud)[Public API]",
			"group private_api(cloud)[Private API] in public_api",
			"service db(database)[DB] in private_api",
		};

		var diagram = ArchitectureParser.Parse(lines);

		diagram.Groups.Should().HaveCount(2);
		diagram.Groups[1].ParentId.Should().Be("public_api");
		diagram.Services[0].ParentId.Should().Be("private_api");
	}

	[Test]
	public void Handles_empty_diagram()
	{
		var lines = new[] { "architecture-beta" };

		var diagram = ArchitectureParser.Parse(lines);

		diagram.Groups.Should().BeEmpty();
		diagram.Services.Should().BeEmpty();
		diagram.Edges.Should().BeEmpty();
	}

	[Test]
	public void Parses_edge_with_group_modifier_as_service_ids_v1()
	{
		// v1: {group} is accepted for syntax compatibility but attachment uses service ids
		// (parent-group boundary attach is not modeled yet).
		var lines = new[]
		{
			"architecture-beta",
			"group g1(cloud)[G1]",
			"group g2(cloud)[G2]",
			"service a(server)[A] in g1",
			"service b(server)[B] in g2",
			"a{group}:B --> T:b{group}",
		};

		var diagram = ArchitectureParser.Parse(lines);

		diagram.Edges.Should().HaveCount(1);
		diagram.Edges[0].SourceId.Should().Be("a");
		diagram.Edges[0].TargetId.Should().Be("b");
	}

	[Test]
	public void Duplicate_ids_across_group_and_service_first_wins()
	{
		var lines = new[]
		{
			"architecture-beta",
			"group db(cloud)[Group DB]",
			"service db(database)[Service DB]",
			"service other(server)[Other]",
		};

		var diagram = ArchitectureParser.Parse(lines);

		diagram.Groups.Should().HaveCount(1);
		diagram.Groups[0].Id.Should().Be("db");
		diagram.Groups[0].Label.Should().Be("Group DB");
		// Service with the same id is skipped so edge bounds cannot clobber the group.
		diagram.Services.Should().HaveCount(1);
		diagram.Services[0].Id.Should().Be("other");
	}

	[Test]
	public void Duplicate_group_id_first_wins()
	{
		var lines = new[]
		{
			"architecture-beta",
			"group a(cloud)[First]",
			"group a(cloud)[Second]",
		};

		var diagram = ArchitectureParser.Parse(lines);

		diagram.Groups.Should().HaveCount(1);
		diagram.Groups[0].Label.Should().Be("First");
	}
}
