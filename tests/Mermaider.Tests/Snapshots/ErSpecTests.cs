using VerifyTUnit;

namespace Mermaider.Tests.Snapshots;

public class ErSpecTests
{
	[Test]
	public Task All_cardinality_combinations() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			erDiagram
			A ||--|| B : one-to-one
			C ||--o| D : one-to-zero-or-one
			E ||--o{ F : one-to-zero-or-many
			G ||--|{ H : one-to-one-or-many
			"""), "svg");

	[Test]
	public Task Non_identifying_relationships() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			erDiagram
			PARENT ||--|{ CHILD : has
			CATEGORY ||..o{ PRODUCT : classifies
			TEAM }|..|{ MEMBER : includes
			"""), "svg");

	[Test]
	public Task Attributes_with_keys() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			erDiagram
			USER {
			int id PK
			string username UK
			string email UK
			string password_hash
			date created_at
			}
			SESSION {
			int id PK
			int user_id FK
			string token UK
			datetime expires_at
			}
			USER ||--o{ SESSION : has
			"""), "svg");

	[Test]
	public Task Attributes_with_comments() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			erDiagram
			PRODUCT {
			int id PK "Auto-increment"
			string name "Required"
			decimal price "In USD"
			int stock "Non-negative"
			}
			"""), "svg");

	[Test]
	public Task Complex_blog_schema() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			erDiagram
			USER ||--o{ POST : writes
			USER ||--o{ COMMENT : writes
			POST ||--o{ COMMENT : has
			POST }o--o{ TAG : tagged
			USER {
			int id PK
			string username UK
			string email
			date joined
			}
			POST {
			int id PK
			string title
			text body
			date published
			bool draft
			}
			COMMENT {
			int id PK
			text content
			date created
			}
			TAG {
			int id PK
			string name UK
			}
			"""), "svg");

	[Test]
	public Task Recursive_relationship() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			erDiagram
			EMPLOYEE ||--o{ EMPLOYEE : manages
			EMPLOYEE {
			int id PK
			string name
			int manager_id FK
			}
			"""), "svg");

	[Test]
	public Task Entities_without_attributes() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			erDiagram
			CUSTOMER ||--o{ ORDER : places
			ORDER ||--|{ LINE_ITEM : contains
			LINE_ITEM }|--|| PRODUCT : references
			PRODUCT }o--o{ CATEGORY : belongs_to
			"""), "svg");

	[Test]
	public Task Ecommerce_schema() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			erDiagram
			CUSTOMER ||--o{ ORDER : places
			ORDER ||--|{ ORDER_LINE : contains
			ORDER_LINE }|--|| PRODUCT : references
			PRODUCT }o--|| CATEGORY : belongs_to
			CUSTOMER {
			int id PK
			string name
			string email UK
			}
			ORDER {
			int id PK
			date order_date
			string status
			}
			ORDER_LINE {
			int id PK
			int quantity
			decimal unit_price
			}
			PRODUCT {
			int id PK
			string name
			decimal price
			int stock
			}
			CATEGORY {
			int id PK
			string name UK
			string description
			}
			"""), "svg");

	[Test]
	public Task Direction_top_down() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			erDiagram
			direction TD
			CUSTOMER ||--o{ ORDER : places
			ORDER ||--|{ LINE-ITEM : contains
			"""), "svg");

	[Test]
	public Task Optional_relationship_label() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			erDiagram
			CUSTOMER ||--o{ ORDER
			ORDER ||--|{ LINE-ITEM : contains
			"""), "svg");

	[Test]
	public Task Entity_alias() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			erDiagram
			p["Customer Account"] {
				int id PK
				string name
			}
			o[Order] {
				int id PK
				int customer_id FK
			}
			p ||--o{ o : places
			"""), "svg");
}
