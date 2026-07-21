using System.Text;
using AwesomeAssertions;
using Mermaider.Icons;

namespace Mermaider.Tests.Icons;

public class IconRegistryTests
{
	[Test]
	[Arguments("cloud")]
	[Arguments("database")]
	[Arguments("disk")]
	[Arguments("internet")]
	[Arguments("server")]
	[Arguments("generic")]
	public void Resolves_built_in_default_icons(string name)
	{
		IconRegistry.TryGet(name, out var svg).Should().BeTrue();
		svg.Should().StartWith("<svg");
	}

	[Test]
	[Arguments("aws:compute")]
	[Arguments("aws:storage")]
	[Arguments("aws:database")]
	[Arguments("aws:networking")]
	[Arguments("aws:serverless")]
	[Arguments("aws:load-balancer")]
	[Arguments("aws:queue")]
	[Arguments("aws:cdn")]
	[Arguments("aws:cache")]
	[Arguments("gcp:compute")]
	[Arguments("gcp:networking")]
	[Arguments("gcp:serverless")]
	[Arguments("gcp:load-balancer")]
	[Arguments("gcp:queue")]
	[Arguments("gcp:cdn")]
	[Arguments("gcp:cache")]
	[Arguments("azure:compute")]
	[Arguments("azure:networking")]
	[Arguments("azure:serverless")]
	[Arguments("azure:load-balancer")]
	[Arguments("azure:queue")]
	[Arguments("azure:cdn")]
	[Arguments("azure:cache")]
	[Arguments("elastic:elasticsearch")]
	[Arguments("elastic:kibana")]
	[Arguments("elastic:logstash")]
	[Arguments("elastic:beats")]
	[Arguments("elastic:fleet")]
	[Arguments("elastic:serverless")]
	[Arguments("elastic:apm")]
	[Arguments("elastic:security")]
	[Arguments("elastic:observability")]
	[Arguments("ext:waf")]
	[Arguments("ext:api-gateway")]
	[Arguments("ext:k8s")]
	[Arguments("ext:pod")]
	[Arguments("ext:pool")]
	[Arguments("ext:reverse-proxy")]
	[Arguments("ext:web")]
	[Arguments("ext:api")]
	[Arguments("ext:load-balancer")]
	[Arguments("ext:queue")]
	[Arguments("ext:cdn")]
	[Arguments("ext:cache")]
	public void Resolves_curated_vendor_icons(string name)
	{
		IconRegistry.TryGet(name, out var svg).Should().BeTrue();
		svg.Should().StartWith("<svg");
	}

	[Test]
	[Arguments("aws:serverless")]
	[Arguments("azure:load-balancer")]
	[Arguments("gcp:cache")]
	[Arguments("elastic:apm")]
	public void Vendor_icons_have_a_badge_gradient(string name)
	{
		IconRegistry.TryGetBadgeGradient(name, out _).Should().BeTrue();
	}

	[Test]
	[Arguments("ext:waf")]
	[Arguments("ext:k8s")]
	[Arguments("ext:api")]
	public void Ext_components_have_a_neutral_badge_gradient(string name)
	{
		IconRegistry.TryGetBadgeGradient(name, out var gradient).Should().BeTrue();
		gradient.Light.Should().Be("#94a3b8");
		gradient.Dark.Should().Be("#475569");
	}

	[Test]
	public void Default_pack_icons_have_no_badge_gradient()
	{
		IconRegistry.TryGetBadgeGradient("database", out _).Should().BeFalse();
	}

	[Test]
	public void Unknown_icon_falls_back_to_generic()
	{
		var svg = IconRegistry.Resolve("this-icon-does-not-exist");

		IconRegistry.TryGet(IconRegistry.FallbackName, out var generic).Should().BeTrue();
		svg.Should().Be(generic);
	}

	[Test]
	public void Null_or_blank_name_falls_back_to_generic()
	{
		IconRegistry.TryGet(IconRegistry.FallbackName, out var generic).Should().BeTrue();

		IconRegistry.Resolve(null).Should().Be(generic);
		IconRegistry.Resolve("").Should().Be(generic);
		IconRegistry.Resolve("   ").Should().Be(generic);
	}

	[Test]
	public void Register_and_resolve_custom_icon()
	{
		const string name = "test:custom-icon-registration";
		try
		{
			IconRegistry.Register(name, """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><rect x="0" y="0" width="24" height="24"/></svg>""");

			IconRegistry.TryGet(name, out var svg).Should().BeTrue();
			svg.Should().Contain("<rect");
			IconRegistry.Names.Should().Contain(name);
		}
		finally
		{
			IconRegistry.Unregister(name);
		}
	}

	[Test]
	public void Register_from_byte_span()
	{
		const string name = "test:custom-icon-from-bytes";
		try
		{
			var bytes = Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><circle cx="12" cy="12" r="8"/></svg>""");

			IconRegistry.Register(name, bytes);

			IconRegistry.TryGet(name, out var svg).Should().BeTrue();
			svg.Should().Contain("<circle");
		}
		finally
		{
			IconRegistry.Unregister(name);
		}
	}

	[Test]
	public void Register_from_stream_leaves_it_open()
	{
		const string name = "test:custom-icon-from-stream";
		try
		{
			var bytes = Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><polygon points="0,0 24,0 12,24"/></svg>""");
			using var stream = new MemoryStream(bytes);

			IconRegistry.Register(name, stream);

			IconRegistry.TryGet(name, out var svg).Should().BeTrue();
			svg.Should().Contain("<polygon");
			stream.CanRead.Should().BeTrue();
		}
		finally
		{
			IconRegistry.Unregister(name);
		}
	}

	[Test]
	public void Custom_registration_overrides_built_in_of_same_name()
	{
		try
		{
			IconRegistry.Register("server", """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><circle cx="12" cy="12" r="5" fill="#123456"/></svg>""");

			IconRegistry.TryGet("server", out var svg).Should().BeTrue();
			svg.Should().Contain("#123456");
		}
		finally
		{
			IconRegistry.Unregister("server");
		}
	}

	// Registration is reject-on-violation, not strip-and-accept: a malicious/malformed icon
	// throws immediately rather than silently registering a mutated version of what was passed in.

	[Test]
	public void Register_rejects_icon_containing_script()
	{
		var act = () => IconRegistry.Register(
			"test:malicious-icon",
			"""<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script><rect x="0" y="0" width="10" height="10"/></svg>""");

		act.Should().ThrowExactly<MermaidSvgException>();
		IconRegistry.TryGet("test:malicious-icon", out _).Should().BeFalse();
	}

	[Test]
	public void Register_rejects_icon_with_event_handler()
	{
		var act = () => IconRegistry.Register(
			"test:malicious-onclick-icon",
			"""<svg xmlns="http://www.w3.org/2000/svg"><rect x="0" y="0" width="10" height="10" onclick="alert(1)"/></svg>""");

		act.Should().ThrowExactly<MermaidSvgException>();
		IconRegistry.TryGet("test:malicious-onclick-icon", out _).Should().BeFalse();
	}

	[Test]
	public void Register_rejects_icon_with_external_href()
	{
		var act = () => IconRegistry.Register(
			"test:malicious-href-icon",
			"""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"><image xlink:href="http://evil.example/x.png" width="10" height="10"/></svg>""");

		act.Should().ThrowExactly<MermaidSvgException>();
		IconRegistry.TryGet("test:malicious-href-icon", out _).Should().BeFalse();
	}

	[Test]
	public void Register_rejects_icon_with_children_under_image()
	{
		var act = () => IconRegistry.Register(
			"test:image-with-children",
			"""<svg xmlns="http://www.w3.org/2000/svg"><image href="data:image/svg+xml;base64,PHN2ZyAvPg=="><rect x="0" y="0" width="4" height="4"/></image></svg>""");

		act.Should().ThrowExactly<MermaidSvgException>();
		IconRegistry.TryGet("test:image-with-children", out _).Should().BeFalse();
	}

	[Test]
	public void Register_rejects_non_svg_root()
	{
		var act = () => IconRegistry.Register("test:not-svg", "<div>not an svg</div>");

		act.Should().ThrowExactly<MermaidSvgException>();
	}

	[Test]
	public void Register_rejects_malformed_xml()
	{
		var act = () => IconRegistry.Register("test:malformed", "<svg><rect></svg>");

		act.Should().ThrowExactly<MermaidSvgException>();
	}
}
