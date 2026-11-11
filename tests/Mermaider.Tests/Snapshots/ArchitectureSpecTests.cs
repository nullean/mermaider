using AwesomeAssertions;
using Nullean.Snapshots;

namespace Mermaider.Tests.Snapshots;

public class ArchitectureSpecTests
{
	[Test]
	public void Groups_and_services_with_icons() =>
		MermaidRenderer
			.RenderSvg(
				"""
			architecture-beta
			group api(cloud)[API]
			service db(database)[Database] in api
			service disk1(disk)[Storage] in api
			service server(server)[Server] in api
			server:T -- B:disk1
			server:L -- R:db
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Nested_groups() =>
		MermaidRenderer
			.RenderSvg(
				"""
			architecture-beta
			group outer(cloud)[Outer]
			group inner(cloud)[Inner] in outer
			service a(server)[A] in inner
			service b(server)[B] in outer
			a:R -- L:b
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Junction_routing() =>
		MermaidRenderer
			.RenderSvg(
				"""
			architecture-beta
			service a(server)[A]
			service b(server)[B]
			service c(server)[C]
			junction j
			a:R -- L:j
			j:R -- L:b
			j:B -- T:c
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void All_edge_arrow_directions() =>
		MermaidRenderer
			.RenderSvg(
				"""
			architecture-beta
			service a(server)[A]
			service b(server)[B]
			service c(server)[C]
			service d(server)[D]
			a:R -- L:b
			b:R --> L:c
			c:R <-- L:d
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Vendor_icon_packs() =>
		MermaidRenderer
			.RenderSvg(
				"""
			architecture-beta
			group cloud1(cloud)[Cloud]
			service compute(aws:compute)[EC2] in cloud1
			service storage(aws:storage)[S3] in cloud1
			service search(elastic:elasticsearch)[Elasticsearch] in cloud1
			compute:R -- L:storage
			compute:B -- T:search
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Reported_k8s_ech_diagram() =>
		MermaidRenderer
			.RenderSvg(
				"""
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
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);
}
