using AwesomeAssertions;
using Mermaider.Icons;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class ArchitectureParserTests
{
	[Test]
	public void Parses_groups_services_and_junction()
	{
		var lines = new[]
		{
			"architecture-beta",
			"group k8s(cloud)[k8s]",
			"service edot(server)[EDOT] in k8s",
			"junction otlp",
		};

		var diagram = ArchitectureParser.Parse(lines);

		diagram.Groups.Should().ContainSingle();
		diagram.Groups[0].Id.Should().Be("k8s");
		diagram.Groups[0].Icon.Should().Be("cloud");
		diagram.Groups[0].Title.Should().Be("k8s");

		diagram.Services.Should().ContainSingle();
		diagram.Services[0].Id.Should().Be("edot");
		diagram.Services[0].Icon.Should().Be("server");
		diagram.Services[0].Title.Should().Be("EDOT");
		diagram.Services[0].GroupId.Should().Be("k8s");

		diagram.Junctions.Should().ContainSingle();
		diagram.Junctions[0].Id.Should().Be("otlp");
	}

	[Test]
	public void Parses_nested_groups()
	{
		var lines = new[]
		{
			"architecture-beta",
			"group outer(cloud)[Outer]",
			"group inner(cloud)[Inner] in outer",
			"service a(server)[A] in inner",
		};

		var diagram = ArchitectureParser.Parse(lines);

		diagram.Groups.Should().HaveCount(2);
		diagram.Groups.Single(g => g.Id == "inner").ParentId.Should().Be("outer");
		diagram.Services[0].GroupId.Should().Be("inner");
	}

	[Test]
	public void Parses_edge_sides_and_arrow_direction()
	{
		var lines = new[]
		{
			"architecture-beta",
			"service a(server)[A]",
			"service b(server)[B]",
			"a:L -- R:b",
		};

		var diagram = ArchitectureParser.Parse(lines);

		diagram.Edges.Should().ContainSingle();
		var edge = diagram.Edges[0];
		edge.SourceId.Should().Be("a");
		edge.SourceSide.Should().Be(ArchitectureSide.Left);
		edge.TargetId.Should().Be("b");
		edge.TargetSide.Should().Be(ArchitectureSide.Right);
		edge.SourceArrow.Should().BeFalse();
		edge.TargetArrow.Should().BeFalse();
	}

	[Test]
	[Arguments("-->", false, true)]
	[Arguments("<--", true, false)]
	[Arguments("<-->", true, true)]
	[Arguments("--", false, false)]
	public void Parses_arrow_variants(string arrow, bool expectSourceArrow, bool expectTargetArrow)
	{
		var lines = new[]
		{
			"architecture-beta",
			"service a(server)[A]",
			"service b(server)[B]",
			$"a:R {arrow} L:b",
		};

		var diagram = ArchitectureParser.Parse(lines);

		diagram.Edges[0].SourceArrow.Should().Be(expectSourceArrow);
		diagram.Edges[0].TargetArrow.Should().Be(expectTargetArrow);
	}

	[Test]
	public void Service_without_icon_falls_back_to_generic()
	{
		var lines = new[]
		{
			"architecture-beta",
			"service a[A]",
		};

		var diagram = ArchitectureParser.Parse(lines);

		diagram.Services[0].Icon.Should().Be(IconRegistry.FallbackName);
	}

	[Test]
	public void Reported_bug_diagram_parses_without_error()
	{
		var lines = """
			architecture-beta
			group k8s(cloud)[k8s]
			group ech(cloud)[ECH]

			service edot(server)[EDOT] in k8s
			service oteldemo(server)[OtelDemo] in k8s
			service es(server)[Elasticsearch] in ech
			service kbn(server)[Kibana] in ech
			service apm(server)[APM] in ech

			junction otlp

			edot:L -- R:otlp
			otlp:L -- T:apm
			oteldemo:L -- R:edot
			kbn:L -- T:es
			apm:L -- R:es
			""".Split('\n');

		var diagram = ArchitectureParser.Parse(lines);

		diagram.Groups.Should().HaveCount(2);
		diagram.Services.Should().HaveCount(5);
		diagram.Junctions.Should().ContainSingle();
		diagram.Edges.Should().HaveCount(5);
	}
}
