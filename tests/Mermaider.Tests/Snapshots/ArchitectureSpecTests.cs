using VerifyTUnit;

namespace Mermaider.Tests.Snapshots;

public class ArchitectureSpecTests
{
	[Test]
	public Task Groups_and_services_with_icons() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			architecture-beta
			group api(cloud)[API]
			service db(database)[Database] in api
			service disk1(disk)[Storage] in api
			service server(server)[Server] in api
			server:T -- B:disk1
			server:L -- R:db
			"""), "svg");

	[Test]
	public Task Nested_groups() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			architecture-beta
			group outer(cloud)[Outer]
			group inner(cloud)[Inner] in outer
			service a(server)[A] in inner
			service b(server)[B] in outer
			a:R -- L:b
			"""), "svg");

	[Test]
	public Task Junction_routing() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			architecture-beta
			service a(server)[A]
			service b(server)[B]
			service c(server)[C]
			junction j
			a:R -- L:j
			j:R -- L:b
			j:B -- T:c
			"""), "svg");

	[Test]
	public Task All_edge_arrow_directions() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			architecture-beta
			service a(server)[A]
			service b(server)[B]
			service c(server)[C]
			service d(server)[D]
			a:R -- L:b
			b:R --> L:c
			c:R <-- L:d
			"""), "svg");

	[Test]
	public Task Vendor_icon_packs() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			architecture-beta
			group cloud1(cloud)[Cloud]
			service compute(aws:compute)[EC2] in cloud1
			service storage(aws:storage)[S3] in cloud1
			service search(elastic:elasticsearch)[Elasticsearch] in cloud1
			compute:R -- L:storage
			compute:B -- T:search
			"""), "svg");

	[Test]
	public Task Reported_k8s_ech_diagram() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
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
			"""), "svg");
}
