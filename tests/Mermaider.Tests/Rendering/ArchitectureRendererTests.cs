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
			    service db(database)[Database] in api
			    service disk(disk)[Disk] in api
			    service server(server)[Server] in api
			    db:R --> L:server
			    disk:T --> B:server
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
			    service db(database)[Database] in api
			    service disk(disk)[Disk] in api
			    service server(server)[Server] in api
			    db:R --> L:server
			    disk:T --> B:server
			""");

		svg.Should().Contain("API");
		svg.Should().Contain("Database");
		svg.Should().Contain("Disk");
		svg.Should().Contain("Server");
	}

	[Test]
	public void Still_renders_fixture_style_edge_form()
	{
		// WinPrint fixture uses id:P --> id:P; GH mermaid requires id:P --> P:id
		var svg = MermaidRenderer.RenderSvg("""
			architecture-beta
			    group api(cloud)[API]
			    service db(database)[Database]
			    api:B --> db:T
			""");

		svg.Should().StartWith("<svg");
		svg.Should().Contain("Database");
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
		// Mermaid-like solid blue icon tiles
		svg.Should().Contain("#326ce5");
	}

	[Test]
	public void Places_services_from_edge_ports_not_vertical_stack()
	{
		// db:R --> L:server puts Database left of Server (not stacked)
		var svg = MermaidRenderer.RenderSvg("""
			architecture-beta
			    group api(cloud)[API]
			    service db(database)[Database] in api
			    service disk(disk)[Disk] in api
			    service server(server)[Server] in api
			    db:R --> L:server
			    disk:T --> B:server
			""");

		// Extract service group x positions from data-id order is unstable; check for horizontal spread via multiple distinct tile x=
		// Server and Database must not share the same x (stacked layout had identical x for all).
		var dbIdx = svg.IndexOf("data-id=\"db\"", StringComparison.Ordinal);
		var serverIdx = svg.IndexOf("data-id=\"server\"", StringComparison.Ordinal);
		dbIdx.Should().BeGreaterThan(0);
		serverIdx.Should().BeGreaterThan(0);
		var dbRect = svg.IndexOf("<rect", dbIdx, StringComparison.Ordinal);
		var serverRect = svg.IndexOf("<rect", serverIdx, StringComparison.Ordinal);
		var dbX = ExtractAttr(svg, dbRect, "x");
		var serverX = ExtractAttr(svg, serverRect, "x");
		dbX.Should().NotBe(serverX);
	}

	private static string ExtractAttr(string svg, int from, string name)
	{
		var key = name + "=\"";
		var i = svg.IndexOf(key, from, StringComparison.Ordinal);
		i.Should().BeGreaterThanOrEqualTo(0);
		var start = i + key.Length;
		var end = svg.IndexOf('"', start);
		return svg[start..end];
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
