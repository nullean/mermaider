using Mermaider.Examples;

namespace Mermaider.Gallery;

// docs-builder domain-model diagrams — flowchart and ER examples drawn from a real,
// non-trivial documentation-tooling codebase. Useful as layout stress tests: multi-parent
// ER relationships, long skip-layer edges, and dense fan-out/fan-in flowcharts that go well
// beyond the synthetic examples in Mermaider.Examples.
public static partial class DiagramExamples
{
	static partial void AppendPrivateExamples(List<DiagramExample> list) =>
		list.AddRange(CreateDocsBuilderErdExamples());

	private static DiagramExample[] CreateDocsBuilderErdExamples() =>
	[
		// ── Flowchart diagrams from docs-builder ───────────────────────────

		new("db-flow-01-system", "DB: Level 0 — the system", DiagramCategory.Flowchart, """
			flowchart LR
			    subgraph Reference["Reference data (config/)"]
			        Catalog["Products, versioning systems"]
			    end
			    subgraph Sources["Sources"]
			        Repo["Repositories (Git)"]
			    end
			    subgraph Authoring["Authoring (in each repository)"]
			        DocSet["Documentation sets"]
			        OpenApi["OpenAPI specs"]
			        Changelog["Changelog entries"]
			    end
			    subgraph Build["Build"]
			        Isolated["Isolated build"]
			        Assembler["Assembler build"]
			        Codex["Codex build"]
			    end
			    subgraph Outputs["Outputs"]
			        Site["HTML sites"]
			        Links["Link graph"]
			        SpecIndex["OpenAPI repository"]
			        Search["Search indices"]
			        Notes["Release note bundles"]
			    end
			    Catalog --> DocSet
			    Repo --> DocSet
			    Repo --> OpenApi
			    Repo --> Changelog
			    OpenApi -->|"published per version"| SpecIndex
			    OpenApi -.->|"api: local spec"| DocSet
			    DocSet -.->|"api: spec + repository"| SpecIndex
			    SpecIndex --> Isolated
			    SpecIndex --> Assembler
			    DocSet --> Isolated
			    DocSet --> Assembler
			    DocSet --> Codex
			    Isolated --> Links
			    Links --> Assembler
			    Links --> Codex
			    Assembler --> Site
			    Codex --> Site
			    Isolated --> Site
			    Assembler --> Search
			    Changelog --> Notes
			    Notes --> Site
			""", "docs-builder"),

		new("db-flow-03-isolated", "DB: Level 2 — isolated build", DiagramCategory.Flowchart, """
			flowchart LR
			    subgraph Inputs["Inputs"]
			        Checkout["Checkout (one repository, one branch)"]
			        Vocab["Shared vocabulary (config/)"]
			    end
			    subgraph External["Read from outside, never checked out"]
			        LinkReg["Link registry"]
			        OpenApiRepo["OpenAPI repository"]
			        Bundles["Release note bundles (CDN)"]
			    end
			    subgraph Build["Isolated build"]
			        DocSet["Documentation set (docset.yml)"]
			        Toc["Table of contents"]
			        Content["Content: pages, snippets, images"]
			        Nav["Navigation tree"]
			    end
			    subgraph Outputs["Outputs"]
			        Html["HTML, llms.txt, Pagefind"]
			        Manifest["Link manifest (links.json)"]
			        Redirects["redirects.json"]
			        State["Generation state (.doc.state)"]
			    end
			    Checkout --> DocSet
			    Vocab --> DocSet
			    DocSet --> Toc --> Nav
			    DocSet --> Content
			    Content --> Nav
			    LinkReg -->|"cross_links"| Content
			    OpenApiRepo -->|"api"| Content
			    Bundles -->|"release_notes"| Content
			    Nav --> Html
			    Nav --> Manifest
			    DocSet --> Redirects
			    Manifest -->|"published on push"| LinkReg
			""", "docs-builder"),

		new("db-flow-04-composing", "DB: Level 3 — composing sites", DiagramCategory.Flowchart, """
			flowchart TB
			    subgraph Repos["Repositories (each with its own isolated build)"]
			        R1["elasticsearch"]
			        R2["kibana"]
			        R3["docs-content"]
			        R4["internal-team-docs"]
			    end
			    subgraph Registries["Link registries"]
			        S3Reg["S3 link registry (default docsets)"]
			        CodexReg["Git link index: elastic/codex-link-index, internal/"]
			    end
			    R1 -->|"links.json"| S3Reg
			    R2 -->|"links.json"| S3Reg
			    R3 -->|"links.json"| S3Reg
			    R4 -->|"links.json"| CodexReg
			    subgraph Assembler["Assembler"]
			        AsmCfg["assembler.yml + navigation.yml"]
			        Env["PublishEnvironment: content source to branch"]
			        AsmClone["Clone at registry commits"]
			        AsmBuild["Build each docset, re-homed under its mount path"]
			        AsmOut["Site, redirects, search index, sitemap, deploy plan"]
			    end
			    subgraph CodexB["Codex"]
			        CdxCfg["codex.yml (registry: internal)"]
			        CdxClone["Clone every member, pick docset with matching registry"]
			        CdxBuild["Build each docset under /r/{repo}"]
			        CdxOut["Internal site, grouped"]
			    end
			    S3Reg --> AsmClone
			    AsmCfg --> Env --> AsmClone --> AsmBuild --> AsmOut
			    CodexReg --> CdxClone
			    CdxCfg --> CdxClone --> CdxBuild --> CdxOut
			""", "docs-builder"),

		// ── ER diagrams from docs-builder ─────────────────────────────────

		new("db-erd-02-shared-vocab", "DB: Level 1 — shared vocabulary", DiagramCategory.Er, """
			erDiagram
			    VersioningSystem ||--|| SemVersion : "base"
			    VersioningSystem ||--|| SemVersion : "current"
			    Product }o--o| VersioningSystem : "versioning"
			    Product }o--o| Repository : "repository"
			    Applicability ||--o{ ApplicabilityEntry : "per key"
			    ApplicabilityEntry }o--|| ProductLifecycle : "lifecycle"
			    ApplicabilityEntry }o--o| VersionSpec : "version"
			    ApplicabilityEntry }o--|| VersioningSystem : "key resolves to"
			    ProductRelease }o--|| Product : "product"
			    ProductRelease }o--|| ChangelogLifecycle : "lifecycle"
			    ChangelogLifecycle }o--|| ProductLifecycle : "maps to"
			""", "docs-builder"),

		new("db-erd-05-building-blocks", "DB: Level 4 — how building blocks relate", DiagramCategory.Er, """
			erDiagram
			    Catalog ||--o{ DocumentationSet : "products, versioning"
			    Catalog ||--o{ ReleaseNotes : "products"
			    Repository ||--o{ DocumentationSet : "≤1 per Registry"
			    Repository ||--o| ReleaseNotes : "changelog.yml"
			    DocumentationSet ||--|{ Content : "contains"
			    DocumentationSet ||--|| Navigation : "table of contents"
			    DocumentationSet ||--o{ LinkManifest : "one per branch"
			    LinkManifest }o--|| LinkRegistry : "indexed by"
			    LinkRegistry ||--|| Registry : "one per registry"
			    Assembly ||--|{ Repository : "references"
			    Assembly }o--|| LinkRegistry : "pins commits from"
			    Assembly ||--|| SiteNavigation : "navigation.yml"
			    SiteNavigation ||--|{ Navigation : "mounts"
			    CodexSite ||--|| Registry : "environment"
			    CodexSite }o--|| LinkRegistry : "discovers members from"
			    Content }o--o{ LinkManifest : "cross-links resolve through"
			    Content }o--o{ ReleaseNotes : "{changelog} directive"
			    Repository ||--o{ OpenApiSpec : "publishes"
			    OpenApiSpec }o--|| OpenApiRepository : "published to"
			    DocumentationSet ||--o{ ApiReference : "api"
			    ApiReference }o--|| OpenApiSpec : "spec + repository"
			    Assembly ||--o{ SearchIndex : "per environment"
			""", "docs-builder"),

		new("db-erd-06-catalog", "DB: Catalog", DiagramCategory.Er, """
			erDiagram
			    Product }o--o| VersioningSystem : "versioning"
			    Product }o--o| Repository : "repository"
			    LegacyUrlMapping }o--|| Product : "product"
			""", "docs-builder"),

		new("db-erd-07-source", "DB: Source", DiagramCategory.Er, """
			erDiagram
			    Repository ||--o{ DocumentationSet : "≤1 per Registry"
			    Repository ||--|| ContentSourceBranches : "current, next, edge"
			    PublishEnvironment }o--|| ContentSource : "content_source"
			    Checkout }o--|| Repository : "clones"
			    Checkout }o--|| LinkRegistryEntry : "pinned commit"
			""", "docs-builder"),

		new("db-erd-08-docset", "DB: Documentation set", DiagramCategory.Er, """
			erDiagram
			    DocumentationSet ||--|| TableOfContents : "toc"
			    TableOfContents ||--o{ TocItem : "entries"
			    TocItem ||--o{ TocItem : "children"
			    DocumentationSet ||--o{ Redirect : "redirects.yml"
			    DocumentationSet ||--o{ CrossLinkDeclaration : "cross_links"
			    DocumentationSet ||--o{ ApiDeclaration : "api"
			    DocumentationSet }o--o{ Product : "products"
			    DocumentationSet }o--o| Registry : "registry"
			""", "docs-builder"),

		new("db-erd-09-content", "DB: Content", DiagramCategory.Er, """
			erDiagram
			    DocumentationSet ||--o{ DocumentationFile : "files"
			    Page ||--o| FrontMatter : "front matter"
			    Page ||--o{ Anchor : "anchors"
			    Page ||--o{ Link : "links"
			    InternalLink }o--|| Page : "target"
			    CrossLink }o--|| CrossLinkUri : "repo://path"
			    IncludeDirective }o--|| Snippet : "include"
			    FrontMatter ||--o| Applicability : "applies_to"
			""", "docs-builder"),

		new("db-erd-10-navigation", "DB: Navigation", DiagramCategory.Er, """
			erDiagram
			    TocItem ||--|| NavigationNode : "mirrors"
			    SiteNavigation ||--|{ SiteMount : "toc"
			    SiteMount }o--|| NavigationNode : "re-homes"
			    SiteNavigation ||--o{ Phantom : "phantoms"
			""", "docs-builder"),

		new("db-erd-11-link-graph", "DB: Link graph", DiagramCategory.Er, """
			erDiagram
			    DocumentationSet ||--o{ LinkManifest : "per branch"
			    Registry ||--|| LinkRegistry : "link-index.json"
			    LinkRegistry ||--o{ LinkRegistryEntry : "repository, branch"
			    LinkRegistryEntry ||--|| LinkManifest : "path"
			""", "docs-builder"),

		new("db-erd-12-assembly", "DB: Assembly", DiagramCategory.Er, """
			erDiagram
			    Assembly ||--|{ Repository : "references"
			    Assembly ||--|{ PublishEnvironment : "environments"
			    PublishEnvironment ||--o{ SearchIndex : "index suffix"
			    Build ||--|{ AssembledDocumentationSet : "assembles"
			    Build ||--|| CloudfrontRedirectMap : "redirects.json"
			    Build ||--o| SyncPlan : "deploy"
			""", "docs-builder"),

		new("db-erd-13-codex", "DB: Codex", DiagramCategory.Er, """
			erDiagram
			    CodexSite ||--|| Registry : "environment"
			    CodexSite ||--o{ CodexDocumentationSet : "from LinkRegistry"
			    CodexDocumentationSet ||--|| DocumentationSet : "docset"
			    CodexDocumentationSet }o--o| CodexGroup : "codex.group"
			""", "docs-builder"),

		new("db-erd-14-release-notes", "DB: Release notes", DiagramCategory.Er, """
			erDiagram
			    Repository ||--o| ChangelogConfiguration : "changelog.yml"
			    ChangelogEntry ||--|{ ProductRelease : "products"
			    ProductRelease }o--|| Product : "product"
			    Bundle ||--o{ BundledEntry : "entries"
			    BundledEntry }o--|| ChangelogEntry : "name + checksum"
			    Bundle ||--o{ BundleAmend : "amend-N"
			    Product ||--o| BundleRegistry : "registry.json"
			""", "docs-builder"),

		new("db-erd-15-api-search", "DB: API reference and search", DiagramCategory.Er, """
			erDiagram
			    Repository ||--o{ OpenApiSpec : "publishes"
			    OpenApiRepository ||--o{ OpenApiSpec : "org/repo/branch/spec"
			    OpenApiRepository ||--|| ApiVersionIndex : "index.json"
			    OpenApiSpec ||--o{ ApiSpecVersion : "per version"
			    ApiVersionIndex ||--o{ ApiSpecVersion : "lists"
			    ApiDeclaration }o--|| OpenApiSpec : "spec + repository"
			    ApiDeclaration }o--|| Product : "product"
			    ApiDeclaration ||--o{ ApiSpecVersion : "renders"
			    ApiSpecVersion ||--o{ ApiTag : "tags"
			    ApiTag ||--o{ ApiOperation : "operations"
			    ApiSupplementalDoc }o--o| ApiOperation : "op-{id}"
			    Page ||--o| SearchDocument : "exported"
			    SearchIndex ||--o{ SearchDocument : "contains"
			""", "docs-builder"),

		new("db-erd-16-relationships-overview", "DB: Relationships overview", DiagramCategory.Er, """
			erDiagram
			    Repository ||--o{ DocumentationSet : "holds (≤1 per Registry)"
			    DocumentationSet }o--o| Registry : "registry"
			    Registry ||--|| LinkRegistry : "publishes into"
			    LinkRegistry ||--o{ LinkRegistryEntry : "repo, branch"
			    LinkRegistryEntry ||--|| LinkManifest : "path"
			    DocumentationSet ||--o{ LinkManifest : "one per branch"
			    Assembly ||--|{ Repository : "references"
			    Assembly ||--|{ PublishEnvironment : "environments"
			    CodexSite ||--|| Registry : "environment"
			    DocumentationSet ||--|| TableOfContents : "toc"
			    DocumentationSet ||--o{ DocumentationFile : "contains"
			    DocumentationSet }o--o{ Product : "products"
			    Product }o--o| VersioningSystem : "versioning"
			    Repository ||--o| ChangelogConfiguration : "changelog.yml"
			    Product ||--o{ Bundle : "released in"
			""", "docs-builder"),

		new("db-erd-17-catalog", "DB: Relationships — Catalog", DiagramCategory.Er, """
			erDiagram
			    Product }o--o| VersioningSystem : "versioning (defaults to id)"
			    Product }o--o| Repository : "repository (defaults to id)"
			    Product ||--|| ProductFeatures : "features"
			    LegacyUrlMapping }o--|| Product : "product"
			    LegacyPage }o--|| LegacyUrlMapping : "matched by base URL"
			    VersioningSystem ||--|| SemVersion : "base"
			    VersioningSystem ||--|| SemVersion : "current"
			    QueryRule }o--o{ SearchDocument : "actions.ids = path"
			""", "docs-builder"),

		new("db-erd-18-source", "DB: Relationships — Source", DiagramCategory.Er, """
			erDiagram
			    Assembly ||--|| NarrativeRepository : "narrative"
			    Assembly ||--|{ Repository : "references"
			    Repository ||--|| ContentSourceBranches : "current / next / edge"
			    PublishEnvironment }o--|| ContentSource : "content_source"
			    ContentSource ||--o{ ContentSourceBranches : "selects branch"
			    Checkout }o--|| Repository : "repository"
			    Checkout ||--o| DocumentationSet : "resolves to"
			    Checkout }o--|| LinkRegistryEntry : "pinned CommitSha"
			    DocumentationSet }o--|| GitOrigin : "origin"
			""", "docs-builder"),

		new("db-erd-19-docset", "DB: Relationships — Documentation set", DiagramCategory.Er, """
			erDiagram
			    DocumentationSet ||--|| TableOfContents : "toc"
			    TableOfContents ||--o{ TocItem : "toc entries"
			    TocItem ||--o{ TocItem : "children"
			    NestedTocItem ||--|| TableOfContents : "toc.yml in folder"
			    FileItem }o--|| Page : "file"
			    FolderItem ||--o| IndexFileItem : "index"
			    CrossLinkItem }o--|| CrossLinkUri : "crosslink"
			    TocItem }o--o| Cta : "default_cta"
			    DocumentationSet ||--o{ Substitution : "subs"
			    DocumentationSet ||--o{ Cta : "cta"
			    DocumentationSet }o--o| Cta : "default_cta"
			    DocumentationSet ||--o{ CrossLinkDeclaration : "cross_links"
			    CrossLinkDeclaration }o--|| Repository : "repository name"
			    CrossLinkDeclaration }o--o| Registry : "registry:// prefix"
			    DocumentationSet ||--o{ Redirect : "redirects.yml"
			    DocumentationSet ||--o{ ApiDeclaration : "api"
			    ApiDeclaration }o--|| Product : "product"
			    DocumentationSet ||--o{ ReleaseNotesSubscription : "release_notes"
			    ReleaseNotesSubscription }o--|| Product : "product"
			    DocumentationSet }o--o{ Product : "products"
			    DocumentationSet }o--o| CodexGroup : "codex.group"
			    DocumentationSet ||--o{ FeatureToggle : "features"
			    DocumentationSet ||--o{ HintSuppression : "suppress"
			""", "docs-builder"),

		new("db-erd-20-content", "DB: Relationships — Content", DiagramCategory.Er, """
			erDiagram
			    DocumentationSet ||--o{ DocumentationFile : "files"
			    Page ||--o| FrontMatter : "--- yaml ---"
			    FrontMatter ||--o| Applicability : "applies_to"
			    FrontMatter }o--o{ Product : "products"
			    FrontMatter }o--o| Cta : "cta.id"
			    FrontMatter ||--o{ PageSubstitution : "sub"
			    FrontMatter }o--o{ LegacyPage : "mapped_pages"
			    FrontMatter }o--o| ListingGroup : "listing.group"
			    Page ||--o{ Heading : "## text"
			    Page ||--o{ Anchor : "collected"
			    Heading ||--|| Anchor : "slug"
			    Page ||--o{ Directive : "contains"
			    Page ||--o{ Link : "contains"
			    InternalLink }o--|| Page : "path"
			    InternalLink }o--o| Anchor : "#anchor"
			    CrossLink }o--|| CrossLinkUri : "repo://path"
			    ImageLink }o--|| Image : "src"
			    IncludeDirective }o--|| Snippet : "path"
			    Snippet ||--o{ Anchor : "contributes"
			    SettingsDirective }o--|| SettingsReference : "path"
			    ChangelogDirective }o--|| Product : "argument"
			    RelatedLearningDirective }o--|{ RelatedLearningLink : "ids"
			    PageCardDirective }o--|| Page : "local link"
			    Applicability ||--o{ ApplicabilityEntry : "per key"
			    ApplicabilityEntry }o--|| VersionSpec : "version"
			""", "docs-builder"),

		new("db-erd-21-navigation", "DB: Relationships — Navigation", DiagramCategory.Er, """
			erDiagram
			    DocumentationSet ||--|| DocumentationSetNavigation : "builds"
			    DocumentationSetNavigation ||--o{ NavigationNode : "tree"
			    TocItem ||--|| NavigationNode : "mirrors"
			    FileNavigation }o--|| Page : "model"
			    TocNavigation ||--|| TableOfContents : "toc.yml"
			    SiteNavigation ||--|{ SiteMount : "toc"
			    SiteMount ||--o{ SiteMount : "children"
			    SiteSection ||--o{ SiteMount : "children"
			    SiteNavigation ||--o{ SiteSection : "section (preview)"
			    SiteMount }o--|| TocNavigation : "toc: repo://path"
			    SiteNavigation ||--o{ Phantom : "phantoms"
			    Phantom }o--|| TableOfContents : "toc"
			    SiteMount ||--|| HomeProvider : "re-homes subtree"
			""", "docs-builder"),

		new("db-erd-22-link-graph", "DB: Relationships — Link graph", DiagramCategory.Er, """
			erDiagram
			    DocumentationSet ||--o{ LinkManifest : "per branch"
			    LinkManifest ||--|| GitOrigin : "origin"
			    LinkManifest ||--|{ LinkManifestPage : "links"
			    LinkManifestPage ||--o{ Anchor : "anchors"
			    LinkManifest ||--o{ Redirect : "redirects"
			    LinkManifest ||--o{ CrossLinkUri : "cross_links (outbound)"
			    Registry ||--|| LinkRegistry : "link-index.json"
			    LinkRegistry ||--o{ LinkRegistryEntry : "repository, branch"
			    LinkRegistryEntry ||--|| LinkManifest : "path"
			    Checkout ||--|| LinkRegistrySnapshot : "link-index.snapshot.json"
			""", "docs-builder"),

		new("db-erd-23-publishing", "DB: Relationships — Publishing", DiagramCategory.Er, """
			erDiagram
			    Assembly ||--|{ PublishEnvironment : "environments"
			    PublishEnvironment ||--o{ FeatureFlag : "feature_flags"
			    PublishEnvironment ||--o| AnalyticsIntegration : "google_tag_manager, optimizely"
			    PublishEnvironment ||--o{ SearchIndex : "index name suffix"
			    PublishEnvironment ||--o| RedirectStore : "KVS"
			    Build }o--|| BuildType : "type"
			    Build }o--o| PublishEnvironment : "environment"
			    Build ||--|{ AssembledDocumentationSet : "assembles"
			    AssembledDocumentationSet ||--|| DocumentationSet : "docset"
			    Build ||--o{ GenerationState : "per docset"
			    Build ||--o| BuildStamp : "assembler only"
			    Build ||--|| CloudfrontRedirectMap : "redirects.json"
			    CloudfrontRedirectMap }o--|{ Redirect : "derived from"
			    CloudfrontRedirectMap }o--|| RedirectStore : "deployed to"
			    Build ||--o| SyncPlan : "deploy plan"
			""", "docs-builder"),

		new("db-erd-24-codex", "DB: Relationships — Codex", DiagramCategory.Er, """
			erDiagram
			    CodexSite ||--|| Registry : "environment = registry"
			    CodexSite ||--o{ CodexGroup : "groups"
			    CodexSite ||--o{ CodexDocumentationSet : "from LinkRegistry"
			    CodexDocumentationSet ||--|| DocumentationSet : "docset"
			    CodexDocumentationSet }o--o| CodexGroup : "codex.group"
			    Registry ||--|| LinkRegistry : "git: elastic/codex-link-index/{registry}"
			""", "docs-builder"),

		new("db-erd-25-release-notes", "DB: Relationships — Release notes", DiagramCategory.Er, """
			erDiagram
			    Repository ||--o| ChangelogConfiguration : "changelog.yml"
			    ChangelogConfiguration ||--o| Pivot : "pivot"
			    ChangelogConfiguration ||--o| Rules : "rules"
			    ChangelogConfiguration ||--o{ BundleProfile : "bundle.profiles"
			    ChangelogConfiguration ||--o{ ReleaseTrigger : "bundle.releases"
			    ReleaseTrigger }o--|| BundleProfile : "profile name"
			    ChangelogConfiguration ||--o| ProductDefaults : "products"
			    ProductDefaults }o--o{ Product : "available, default"
			    BundleProfile }o--o| Product : "product"
			    EntryPool ||--o{ ChangelogEntry : "changelog/{org}/{repo}/{branch}/"
			    EntryPool ||--o{ ChangelogNote : "note-*.yml"
			    ChangelogEntry ||--|{ ProductRelease : "products"
			    ChangelogNote ||--|{ ProductRelease : "products (versions)"
			    ProductRelease }o--|| Product : "product"
			    ChangelogMarker }o--|| ChangelogEntry : "link"
			    Bundle ||--|{ BundledProduct : "products"
			    BundledProduct }o--|| Product : "product"
			    Bundle ||--o{ BundledEntry : "entries"
			    BundledEntry }o--|| ChangelogEntry : "file.name + checksum"
			    Bundle ||--o{ BundleAmend : "amend-N"
			    Bundle ||--o| BundleNotesAmend : "amend-notes"
			    Product ||--o| BundleRegistry : "bundle/{product}/registry.json"
			    BundleRegistry ||--o{ Bundle : "bundles[]"
			    NotesIndex ||--o{ ChangelogNote : "notes[]"
			""", "docs-builder"),

		new("db-erd-26-api-reference", "DB: Relationships — API reference", DiagramCategory.Er, """
			erDiagram
			    Repository ||--o{ OpenApiSpec : "publishes, per branch"
			    OpenApiRepository ||--o{ OpenApiSpec : "org/repo/branch/spec"
			    OpenApiRepository ||--|| ApiVersionIndex : "index.json"
			    ApiDeclaration }o--|| OpenApiSpec : "repository + spec"
			    ApiDeclaration }o--|| Product : "product"
			    ApiDeclaration ||--o{ ApiSpecVersion : "renders"
			    ApiVersionIndex ||--o{ ApiSpecVersion : "org/repo, spec, moniker"
			    ApiSpecVersion ||--o{ ApiTagGroup : "x-tagGroups"
			    ApiTagGroup ||--o{ ApiTag : "tags"
			    ApiTag ||--o{ ApiEndpoint : "endpoints"
			    ApiEndpoint ||--|{ ApiOperation : "operations"
			    ApiSpecVersion ||--o{ ApiSchema : "components"
			    ApiSupplementalDoc }o--o| ApiOperation : "op-{operationId}"
			    ApiSupplementalDoc }o--o| ApiTag : "tag-{slug}"
			    ApiDeclaration ||--o{ ApiSupplementalDoc : "api/{key}/"
			    ApiOperation ||--o| DocumentationDocument : "search export"
			""", "docs-builder"),

		new("db-erd-27-search", "DB: Relationships — Search", DiagramCategory.Er, """
			erDiagram
			    SearchIndex ||--o{ SearchDocument : "contains"
			    Page ||--o| DocumentationDocument : "exported"
			    DocumentationDocument }o--o| Product : "product"
			    DocumentationDocument }o--o{ Product : "related_products"
			    DocumentationDocument ||--o{ ParentDocument : "parents (breadcrumbs)"
			    DocumentationDocument ||--o{ AppliesToEntry : "applies_to"
			    DocumentInference ||--|| DocumentationDocument : "decides product"
			""", "docs-builder"),
	];
}
