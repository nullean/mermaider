namespace Mermaider.Gallery;

public static partial class DiagramExamples
{
	private static DiagramExample[] CreateRealWorldExamples() =>
	[
		// ── Real World (RFC diagrams) ─────────────────────────────────

		new("rfc-path-flow", "Path Flow (V1)", DiagramCategory.RealWorld, """
			flowchart TD
			    navYml["`**navigation.yml**`"] --> getToc["GetTocMappings()\nURI → path_prefix"]
			    perRepo["`**Per-repo\ndocset.yml + toc.yml**`"] --> docSetNav["DocumentationSetNavigation\nURI → node tree"]
			    getToc --> tocMappings[NavigationTocMappings]
			    docSetNav --> tocNodes[TableOfContentNodes]
			    tocMappings --> siteNav["SiteNavigation\nmerges nodes via navigation.yml URI keys"]
			    tocNodes --> siteNav
			    siteNav --> pathProvider["GlobalNavigationPathProvider\ndetermines output file paths"]
			    siteNav --> sidebar["Sidebar HTML rendering\ndetermines sidebar tree"]
			"""),

		new("rfc-loading-flow", "Loading Flow", DiagramCategory.RealWorld, """
			flowchart TD
			    disk["All repos cloned to disk prior to build"]
			    disk --> v1Load["Load docset.yml + toc.yml\n(every repo)"]
			    disk --> uriResolve["navigation-v2.yml URI resolved\nto repo checkout on disk"]
			    v1Load --> v1Nodes["V1 TableOfContentNodes"]
			    uriResolve --> hasV2{"docset-v2.yml\npresent?"}
			    hasV2 -->|Yes| loadV2["Load toc-v2.yml\n(required — no fallback to toc.yml)"]
			    hasV2 -->|No| loadFallback["Use V1 toc nodes as fallback\nemits migration-progress warning"]
			    loadV2 --> v2Nodes["V2 TableOfContentNodes"]
			    loadFallback --> v2NodesFallback["V2 TableOfContentNodes\n(V1 structure)"]
			"""),

		new("rfc-virtual-remap", "Virtual Remap", DiagramCategory.RealWorld, """
			flowchart LR
			    src["`manage-data/ingest/some-page.md
			    *(source — unchanged on disk)*`"]
			    src --> v1["V1 build\npath_prefix: manage-data"]
			    src --> v2["V2 build\npath_prefix: elasticsearch-fundamentals"]
			    v1 --> v1out["/docs/manage-data/ingest/some-page"]
			    v2 --> v2out["/docs/elasticsearch-fundamentals/ingest/some-page"]
			"""),

		new("rfc-dual-path", "Dual Path Providers", DiagramCategory.RealWorld, """
			flowchart TD
			    navYml[navigation.yml] -->|GetTocMappings| v1Map[V1 NavigationTocMappings]
			    navV2Yml[navigation-v2.yml] -->|GetV2TocMappings| v2Map[V2 NavigationTocMappings]
			    v1Map --> valV1[/"V1 validation"/]
			    v2Map --> valV2[/"V2 validation"/]
			    v1Map --> flag{"nav-v2 flag"}
			    v2Map --> flag
			    flag -->|false| provV1["GlobalNavigationPathProvider\nwith V1 mappings → V1 paths + V1 sidebar"]
			    flag -->|true| provV2["GlobalNavigationPathProvider\nwith V2 mappings → V2 paths + V2 sidebar"]
			"""),

		new("rfc-migration-lifecycle", "Migration Lifecycle", DiagramCategory.RealWorld, """
			stateDiagram-v2
			    state "Not started" as NotStarted
			    state "Shadow nav created" as ShadowNav
			    state "Virtual remap active" as VirtualRemap
			    state "Physical restructure" as PhysicalRestructure
			    state "Cutover complete" as Cutover

			    [*] --> NotStarted
			    NotStarted --> ShadowNav : add docset-v2.yml + toc-v2.yml to main
			    ShadowNav --> VirtualRemap : assign new path_prefix in navigation-v2.yml
			    VirtualRemap --> PhysicalRestructure : run apply-nav-restructure\n(only if content moves)
			    VirtualRemap --> Cutover : no URL changes needed
			    PhysicalRestructure --> Cutover : merge feature branch
			    Cutover --> [*]
			"""),

		new("rfc-build-flow", "Build Flow with Dual Validation", DiagramCategory.RealWorld, """
			flowchart TD
			    subgraph config [Config]
			        navYml[navigation.yml]
			        navV2Yml[navigation-v2.yml]
			    end

			    subgraph repos [Per-repo checkouts]
			        docsetV1["docset.yml + toc.yml"]
			        docsetV2["docset-v2.yml + toc-v2.yml\n(if present)"]
			    end

			    navYml --> v1SiteNav[V1 SiteNavigation]
			    docsetV1 --> v1SiteNav
			    navV2Yml --> v2SiteNav[V2 SiteNavigationV2]
			    docsetV2 --> v2SiteNav
			    docsetV1 -. "V1 fallback" .-> v2SiteNav

			    v1SiteNav --> v1Map[V1 NavigationTocMappings]
			    v2SiteNav --> v2Map[V2 NavigationTocMappings]

			    v1Map --> valV1{"V1 valid?"}
			    v2Map --> valV2{"V2 valid?"}
			    valV1 -->|fail| errV1([Build error])
			    valV2 -->|fail| errV2([Build error])

			    valV1 -->|pass| flag{"nav-v2 flag"}
			    valV2 -->|pass| flag
			    flag -->|false| emitV1["V1 NavigationTocMappings injected\n→ V1 paths + V1 sidebar"]
			    flag -->|true| emitV2["V2 NavigationTocMappings injected\n→ V2 paths + V2 sidebar"]
			"""),

		new("rfc-integration-pipeline", "Integration Pipeline", DiagramCategory.RealWorld, """
			flowchart TD
			    subgraph inputs [Inputs]
			        assemblerYml["assembler.yml\nenv config · feature flags"]
			        navYml["navigation.yml"]
			        navV2Yml["navigation-v2.yml"]
			        repoCheckouts["Repo checkouts\ndocset.yml / docset-v2.yml\ntoc.yml / toc-v2.yml\ncontent .md files"]
			    end

			    subgraph navAssembly [Navigation Assembly]
			        v1Nav["V1 SiteNavigation\n+ V1 NavigationTocMappings"]
			        v2Nav["V2 SiteNavigationV2\n+ V2 NavigationTocMappings"]
			        fileLookup["NavigationDocumentationFileLookup\nfile path → output URL"]
			    end

			    subgraph dualVal ["Dual Validation — both must pass"]
			        valV1["V1\npath prefixes · completeness · links"]
			        valV2["V2\npath prefixes · completeness\ncross-nav aliasing · migration warnings"]
			    end

			    subgraph contentProc [Content Processing — per file]
			        relLinks["Relative links\nvalidate source-side\nemit URL from nav lookup"]
			        images["Images\nvalidate source-side\ncopy via path provider"]
			        includes["Include directives\n100% source-based\nunaffected by path changes"]
			        xlinks["Cross-links  repo://path\nresolved via NavigationTocMappings\n→ V1 or V2 output URL"]
			    end

			    subgraph outputLayer [Output]
			        pathProvider["GlobalNavigationPathProvider\nflag-injected: V1 or V2 mappings"]
			        sidebarHtml["Sidebar HTML\nV1 or V2 tree per flag"]
			        outputFiles["Output files\nwritten at V1 or V2 paths"]
			    end

			    navYml --> navAssembly
			    navV2Yml --> navAssembly
			    repoCheckouts --> navAssembly
			    navAssembly --> dualVal
			    navAssembly --> fileLookup
			    fileLookup --> contentProc
			    dualVal -->|both pass| contentProc
			    repoCheckouts -->|.md source files| contentProc
			    assemblerYml -->|nav-v2 flag| outputLayer
			    contentProc --> outputLayer
			"""),
	];
}

