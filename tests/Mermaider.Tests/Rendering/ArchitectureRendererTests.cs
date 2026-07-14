using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class ArchitectureRendererTests
{
	private const string Basic = """
		architecture-beta
		group api(cloud)[API]
		service db(database)[Database] in api
		service server(server)[Server] in api
		server:R -- L:db
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_group_and_service_nodes()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().Contain("class=\"architecture-group\" data-id=\"api\"");
		svg.Should().Contain("class=\"architecture-service\" data-id=\"db\"");
		svg.Should().Contain("class=\"architecture-service\" data-id=\"server\"");
	}

	[Test]
	public void Embeds_icon_as_base64_data_uri()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().Contain("<image ");
		svg.Should().Contain("href=\"data:image/svg+xml;base64,");
	}

	[Test]
	public void Contains_edge_path()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().Contain("class=\"architecture-edge\" data-source=\"server\" data-target=\"db\"");
	}

	[Test]
	public void Renders_junction_as_point()
	{
		var svg = MermaidRenderer.RenderSvg("""
			architecture-beta
			service a(server)[A]
			service b(server)[B]
			junction j
			a:R -- L:j
			j:R -- L:b
			""");

		svg.Should().Contain("class=\"architecture-junction\" data-id=\"j\"");
	}

	[Test]
	public void Renders_arrowheads_for_directional_edges()
	{
		var svg = MermaidRenderer.RenderSvg("""
			architecture-beta
			service a(server)[A]
			service b(server)[B]
			a:R --> L:b
			""");

		svg.Should().Contain("marker-end=\"url(#arch-arrow-end)\"");
	}

	[Test]
	public void Renders_reported_bug_diagram()
	{
		var svg = MermaidRenderer.RenderSvg("""
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
			""");

		svg.Should().StartWith("<svg");
		svg.Should().Contain("data-id=\"k8s\"");
		svg.Should().Contain("data-id=\"ech\"");
		svg.Should().Contain("data-id=\"edot\"");
		svg.Should().Contain("data-id=\"otlp\"");
	}

	[Test]
	public void Unknown_icon_falls_back_to_generic_without_throwing()
	{
		var svg = MermaidRenderer.RenderSvg("""
			architecture-beta
			service a(totally-unknown-icon)[A]
			""");

		svg.Should().Contain("data-icon=\"totally-unknown-icon\"");
		svg.Should().Contain("<image ");
	}
}
