using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class C4RendererTests
{
	private const string BasicContext = """
		C4Context
		title System Context diagram for Internet Banking System
		Person(customer, "Banking Customer", "A customer of the bank.")
		System(banking, "Internet Banking System", "Allows customers to view accounts and make payments.")
		System_Ext(mail, "E-mail System", "Microsoft Exchange")
		Rel(customer, banking, "Uses")
		Rel(banking, mail, "Sends e-mails", "SMTP")
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(BasicContext);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_title_and_labels()
	{
		var svg = MermaidRenderer.RenderSvg(BasicContext);

		svg.Should().Contain("System Context diagram for Internet Banking System");
		svg.Should().Contain("Banking Customer");
		svg.Should().Contain("Internet Banking System");
		svg.Should().Contain("E-mail System");
		svg.Should().Contain("Uses");
		svg.Should().Contain("SMTP");
	}

	[Test]
	public void Uses_theme_vars_for_title_and_relations()
	{
		var svg = MermaidRenderer.RenderSvg(BasicContext);

		svg.Should().Contain("fill=\"var(--_text)\"");
		svg.Should().Contain("stroke=\"var(--_arrow)\"");
	}

	[Test]
	public void Renders_boundaries()
	{
		var svg = MermaidRenderer.RenderSvg("""
			C4Context
			title Nested
			Enterprise_Boundary(b0, "Enterprise") {
			  System(s1, "Core System")
			}
			""");

		svg.Should().Contain("Enterprise");
		svg.Should().Contain("Core System");
		svg.Should().Contain("stroke-dasharray");
	}

	[Test]
	public void Renders_container_diagram()
	{
		var svg = MermaidRenderer.RenderSvg("""
			C4Container
			title Containers
			Person(customer, "Customer")
			Container_Boundary(c1, "Internet Banking") {
			  Container(spa, "SPA", "Angular", "UI")
			  ContainerDb(db, "Database", "SQL", "Data")
			}
			Rel(customer, spa, "Uses", "HTTPS")
			""");

		svg.Should().StartWith("<svg");
		svg.Should().Contain("SPA");
		svg.Should().Contain("Database");
		svg.Should().Contain("Angular");
	}

	[Test]
	public void Renders_empty_diagram()
	{
		var svg = MermaidRenderer.RenderSvg("C4Context");

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Detects_c4_not_flowchart()
	{
		var svg = MermaidRenderer.RenderSvg("""
			C4Dynamic
			Component(a, "A")
			Component(b, "B")
			Rel(a, b, "calls")
			""");

		svg.Should().Contain("[component]");
		svg.Should().Contain("calls");
	}

	[Test]
	public void Accessibility_role_description()
	{
		var svg = MermaidRenderer.RenderSvg("""
			C4Context
			accTitle: Banking context
			Person(p, "P")
			""");

		svg.Should().Contain("aria-roledescription=\"C4 diagram\"");
		svg.Should().Contain("Banking context");
	}
}
