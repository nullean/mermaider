using AwesomeAssertions;
using Nullean.Snapshots;

namespace Mermaider.Tests.Snapshots;

public class ErSpecTests
{
	[Test]
	public void All_cardinality_combinations() =>
		MermaidRenderer
			.RenderSvg(
				"""
			erDiagram
			A ||--|| B : one-to-one
			C ||--o| D : one-to-zero-or-one
			E ||--o{ F : one-to-zero-or-many
			G ||--|{ H : one-to-one-or-many
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Non_identifying_relationships() =>
		MermaidRenderer
			.RenderSvg(
				"""
			erDiagram
			PARENT ||--|{ CHILD : has
			CATEGORY ||..o{ PRODUCT : classifies
			TEAM }|..|{ MEMBER : includes
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Attributes_with_keys() =>
		MermaidRenderer
			.RenderSvg(
				"""
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
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Attributes_with_comments() =>
		MermaidRenderer
			.RenderSvg(
				"""
			erDiagram
			PRODUCT {
			int id PK "Auto-increment"
			string name "Required"
			decimal price "In USD"
			int stock "Non-negative"
			}
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Complex_blog_schema() =>
		MermaidRenderer
			.RenderSvg(
				"""
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
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Recursive_relationship() =>
		MermaidRenderer
			.RenderSvg(
				"""
			erDiagram
			EMPLOYEE ||--o{ EMPLOYEE : manages
			EMPLOYEE {
			int id PK
			string name
			int manager_id FK
			}
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Entities_without_attributes() =>
		MermaidRenderer
			.RenderSvg(
				"""
			erDiagram
			CUSTOMER ||--o{ ORDER : places
			ORDER ||--|{ LINE_ITEM : contains
			LINE_ITEM }|--|| PRODUCT : references
			PRODUCT }o--o{ CATEGORY : belongs_to
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Ecommerce_schema() =>
		MermaidRenderer
			.RenderSvg(
				"""
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
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Direction_top_down() =>
		MermaidRenderer
			.RenderSvg(
				"""
			erDiagram
			direction TD
			CUSTOMER ||--o{ ORDER : places
			ORDER ||--|{ LINE-ITEM : contains
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Optional_relationship_label() =>
		MermaidRenderer
			.RenderSvg("""
			erDiagram
			CUSTOMER ||--o{ ORDER
			ORDER ||--|{ LINE-ITEM : contains
			""")
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Entity_alias() =>
		MermaidRenderer
			.RenderSvg(
				"""
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
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);
}
