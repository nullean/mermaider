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

	[Test]
	public void Cyclic_group_parents_do_not_crash_and_render_svg()
	{
		// Unique ids with mutual parents form a cycle; must not StackOverflow.
		var svg = MermaidRenderer.RenderSvg("""
			architecture-beta
			    group a(cloud)[A] in b
			    group b(cloud)[B] in a
			    service s(server)[S] in a
			""");

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
		svg.Should().Contain("arch-group");
		svg.Should().Contain("arch-service");
		svg.Should().Contain(">S<");
	}

	[Test]
	public void Group_and_service_same_id_first_wins_single_data_id()
	{
		// group db then service db: service is dropped at parse; only one data-id="db".
		var svg = MermaidRenderer.RenderSvg("""
			architecture-beta
			    group db(cloud)[Group DB]
			    service db(database)[Service DB]
			    service other(server)[Other]
			    db:R --> L:other
			""");

		var idCount = 0;
		var search = "data-id=\"db\"";
		for (var i = 0; (i = svg.IndexOf(search, i, StringComparison.Ordinal)) >= 0; i += search.Length)
			idCount++;

		idCount.Should().Be(1);
		svg.Should().Contain("Group DB");
		svg.Should().NotContain("Service DB");
		svg.Should().Contain("data-id=\"other\"");
		// Edge still attaches to the group bounds for id db
		svg.Should().Contain("<path");
	}

	[Test]
	public void Nested_groups_render_two_group_boxes()
	{
		var svg = MermaidRenderer.RenderSvg("""
			architecture-beta
			    group public_api(cloud)[Public API]
			    group private_api(cloud)[Private API] in public_api
			    service db(database)[DB] in private_api
			""");

		svg.Should().Contain("data-id=\"public_api\"");
		svg.Should().Contain("data-id=\"private_api\"");
		svg.Should().Contain("Public API");
		svg.Should().Contain("Private API");
		svg.Should().Contain("data-id=\"db\"");
		// Two dashed group rects
		var groupCount = 0;
		var needle = "class=\"arch-group\"";
		for (var i = 0; (i = svg.IndexOf(needle, i, StringComparison.Ordinal)) >= 0; i += needle.Length)
			groupCount++;
		groupCount.Should().Be(2);
	}

	[Test]
	public void Bidirectional_edge_emits_marker_start_and_end()
	{
		var svg = MermaidRenderer.RenderSvg("""
			architecture-beta
			    service a(server)[A]
			    service b(server)[B]
			    a:R <--> L:b
			""");

		svg.Should().Contain("marker-end=\"url(#arch-arrow)\"");
		svg.Should().Contain("marker-start=\"url(#arch-arrow-start)\"");
	}

	[Test]
	public void Service_with_missing_parent_is_still_placed()
	{
		var svg = MermaidRenderer.RenderSvg("""
			architecture-beta
			    service x(server)[X] in nope
			    service y(database)[Y]
			    x:R --> L:y
			""");

		svg.Should().StartWith("<svg");
		svg.Should().Contain("data-id=\"x\"");
		svg.Should().Contain(">X<");
		svg.Should().Contain("data-id=\"y\"");
		svg.Should().Contain("<path");
		svg.Should().Contain("marker-end");
	}

	[Test]
	public void Group_with_missing_parent_is_still_placed()
	{
		var svg = MermaidRenderer.RenderSvg("""
			architecture-beta
			    group orphan(cloud)[Orphan] in missing
			    service s(server)[S] in orphan
			""");

		svg.Should().Contain("data-id=\"orphan\"");
		svg.Should().Contain("Orphan");
		svg.Should().Contain("data-id=\"s\"");
		svg.Should().Contain(">S<");
	}

	[Test]
	public void Edge_and_markers_use_theme_css_variables()
	{
		var svg = MermaidRenderer.RenderSvg("""
			architecture-beta
			    service a(server)[A]
			    service b(server)[B]
			    a:R --> L:b
			""");

		svg.Should().Contain("stroke=\"var(--_line)\"");
		svg.Should().Contain("fill=\"var(--_arrow)\"");
		svg.Should().NotContain("stroke=\"#1f2937\"");
		svg.Should().NotContain("fill=\"#1f2937\"");
	}
}
