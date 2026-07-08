using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class ArchitectureRendererTests
{
	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg("""
			architecture-beta
			    group api(cloud)[API]
			    service db(database)[Database]
			    service disk(disk)[Disk]
			    api:B --> db:T
			    api:B --> disk:T
			""");

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_group_and_service_labels()
	{
		var svg = MermaidRenderer.RenderSvg("""
			architecture-beta
			    group api(cloud)[API]
			    service db(database)[Database]
			    service disk(disk)[Disk]
			    api:B --> db:T
			    api:B --> disk:T
			""");

		svg.Should().Contain("API");
		svg.Should().Contain("Database");
		svg.Should().Contain("Disk");
	}

	[Test]
	public void Contains_dashed_group_and_service_boxes()
	{
		var svg = MermaidRenderer.RenderSvg("""
			architecture-beta
			    group api(cloud)[API]
			    service db(database)[Database] in api
			""");

		svg.Should().Contain("stroke-dasharray");
		svg.Should().Contain("arch-group");
		svg.Should().Contain("arch-service");
	}

	[Test]
	public void Contains_edge_paths()
	{
		var svg = MermaidRenderer.RenderSvg("""
			architecture-beta
			    service a(server)[A]
			    service b(server)[B]
			    a:R --> L:b
			""");

		svg.Should().Contain("<path");
		svg.Should().Contain("marker-end");
	}

	[Test]
	public void Renders_services_inside_group()
	{
		var svg = MermaidRenderer.RenderSvg("""
			architecture-beta
			    group public_api(cloud)[Public API]
			    service server(server)[Server] in public_api
			    service db(database)[Database] in public_api
			    db:R -- L:server
			""");

		svg.Should().Contain("Public API");
		svg.Should().Contain("Server");
		svg.Should().Contain("Database");
	}

	[Test]
	public void Renders_architecture_without_beta_header()
	{
		var svg = MermaidRenderer.RenderSvg("""
			architecture
			    service a(server)[A]
			""");

		svg.Should().StartWith("<svg");
		svg.Should().Contain("A");
	}

	[Test]
	public void Renders_empty_architecture()
	{
		var svg = MermaidRenderer.RenderSvg("architecture-beta");

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}
}
