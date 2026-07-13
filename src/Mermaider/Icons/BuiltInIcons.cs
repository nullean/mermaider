using System.Collections.Frozen;

namespace Mermaider.Icons;

/// <summary>
/// The built-in icon set: Mermaid's default architecture pictograms, a curated selection of
/// vendor-flavored glyphs (AWS/Azure/GCP/Elastic), and a set of generic <c>ext:</c> components
/// for common architecture vocabulary (WAF, API gateway, Kubernetes, pods, pools, reverse proxy,
/// web, API) that doesn't belong to any one vendor. These are original, simplified shapes — not
/// vendors' official trademarked artwork — sanitized and validated once at startup via
/// <see cref="IconValidation"/>, the same path <see cref="IconRegistry.Register(string,string)"/> uses for
/// user-supplied icons.
/// <para>
/// Vendor and <c>ext:</c> icons are glyph-only (transparent background) — the gradient badge look
/// comes from <see cref="BadgeGradients"/>, which the architecture SVG renderer in turn uses to
/// paint the whole service box, not just the icon. Default-pack icons have no gradient entry and
/// render inside the plain themed node box instead.
/// </para>
/// </summary>
internal static class BuiltInIcons
{
	internal static readonly FrozenDictionary<string, string> Map = BuildMap();

	/// <summary>Light/dark gradient stop colors per vendor/ext icon name, keyed the same as <see cref="Map"/>.</summary>
	internal static readonly FrozenDictionary<string, (string Light, string Dark)> BadgeGradients = BuildGradients();

	// Vendor hue families, reused across every category that vendor offers.
	private const string AwsLight = "#f7a55e";
	private const string AwsDark = "#c15a00";
	private const string GcpLight = "#81c995";
	private const string GcpDark = "#188038";
	private const string AzureLight = "#4ea8e8";
	private const string AzureDark = "#00548f";

	// Neutral hue for generic ext: components — distinct from every vendor's color.
	private const string ExtLight = "#94a3b8";
	private const string ExtDark = "#475569";

	private static FrozenDictionary<string, (string Light, string Dark)> BuildGradients() =>
		new Dictionary<string, (string Light, string Dark)>(StringComparer.OrdinalIgnoreCase)
		{
			// AWS
			["aws:compute"] = (AwsLight, AwsDark),
			["aws:storage"] = (AwsLight, AwsDark),
			["aws:database"] = (AwsLight, AwsDark),
			["aws:networking"] = (AwsLight, AwsDark),
			["aws:serverless"] = (AwsLight, AwsDark),
			["aws:load-balancer"] = (AwsLight, AwsDark),
			["aws:queue"] = (AwsLight, AwsDark),
			["aws:cdn"] = (AwsLight, AwsDark),
			["aws:cache"] = (AwsLight, AwsDark),

			// GCP — kept clearly distinct from Azure's blue
			["gcp:compute"] = (GcpLight, GcpDark),
			["gcp:storage"] = (GcpLight, GcpDark),
			["gcp:database"] = (GcpLight, GcpDark),
			["gcp:networking"] = (GcpLight, GcpDark),
			["gcp:serverless"] = (GcpLight, GcpDark),
			["gcp:load-balancer"] = (GcpLight, GcpDark),
			["gcp:queue"] = (GcpLight, GcpDark),
			["gcp:cdn"] = (GcpLight, GcpDark),
			["gcp:cache"] = (GcpLight, GcpDark),

			// Azure
			["azure:compute"] = (AzureLight, AzureDark),
			["azure:storage"] = (AzureLight, AzureDark),
			["azure:database"] = (AzureLight, AzureDark),
			["azure:networking"] = (AzureLight, AzureDark),
			["azure:serverless"] = (AzureLight, AzureDark),
			["azure:load-balancer"] = (AzureLight, AzureDark),
			["azure:queue"] = (AzureLight, AzureDark),
			["azure:cdn"] = (AzureLight, AzureDark),
			["azure:cache"] = (AzureLight, AzureDark),

			// Elastic — each product keeps its own brand-adjacent hue
			["elastic:elasticsearch"] = ("#ffe066", "#c99a00"),
			["elastic:kibana"] = ("#4fd8cc", "#008f86"),
			["elastic:logstash"] = ("#4a4547", "#000000"),
			["elastic:beats"] = ("#4fd8d3", "#018f8a"),
			["elastic:fleet"] = ("#f78fc0", "#b8215f"),
			["elastic:serverless"] = ("#b39ddb", "#5e35b1"),
			["elastic:apm"] = ("#ff9575", "#d84315"),
			["elastic:security"] = ("#ef9a9a", "#c62828"),
			["elastic:observability"] = ("#90caf9", "#1565c0"),

			// Generic ext: components — neutral slate, distinct from any vendor
			["ext:waf"] = (ExtLight, ExtDark),
			["ext:api-gateway"] = (ExtLight, ExtDark),
			["ext:k8s"] = (ExtLight, ExtDark),
			["ext:pod"] = (ExtLight, ExtDark),
			["ext:pool"] = (ExtLight, ExtDark),
			["ext:reverse-proxy"] = (ExtLight, ExtDark),
			["ext:web"] = (ExtLight, ExtDark),
			["ext:api"] = (ExtLight, ExtDark),
			["ext:load-balancer"] = (ExtLight, ExtDark),
			["ext:queue"] = (ExtLight, ExtDark),
			["ext:cdn"] = (ExtLight, ExtDark),
			["ext:cache"] = (ExtLight, ExtDark),
		}.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

	private static FrozenDictionary<string, string> BuildMap()
	{
		var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			// ── Mermaid defaults ──────────────────────────────────────────
			["cloud"] = """
				<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
				<path d="M7 18a4 4 0 0 1-.5-7.97A5 5 0 0 1 16 8.5a4.5 4.5 0 0 1 1 8.9V18H7z" fill="#64748b"/>
				</svg>
				""",
			["database"] = """
				<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
				<path d="M5 6c0-1.7 3.1-3 7-3s7 1.3 7 3v12c0 1.7-3.1 3-7 3s-7-1.3-7-3z" fill="#64748b"/>
				<path d="M5 6c0 1.7 3.1 3 7 3s7-1.3 7-3" fill="none" stroke="#ffffff" stroke-opacity="0.4" stroke-width="1"/>
				<path d="M5 11.3c0 1.7 3.1 3 7 3s7-1.3 7-3" fill="none" stroke="#ffffff" stroke-opacity="0.4" stroke-width="1"/>
				<path d="M5 16.7c0 1.7 3.1 3 7 3s7-1.3 7-3" fill="none" stroke="#ffffff" stroke-opacity="0.4" stroke-width="1"/>
				</svg>
				""",
			["disk"] = """
				<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
				<rect x="4.5" y="6" width="15" height="2.4" rx="1.2" fill="#64748b"/>
				<path d="M5 8h14l-1.3 10.3A2 2 0 0 1 15.7 20H8.3a2 2 0 0 1-2-1.7z" fill="#64748b"/>
				</svg>
				""",
			["internet"] = """
				<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
				<circle cx="12" cy="12" r="9" fill="none" stroke="#64748b" stroke-width="2"/>
				<path d="M3 12h18" fill="none" stroke="#64748b" stroke-width="2"/>
				<path d="M12 3a14 14 0 0 1 0 18" fill="none" stroke="#64748b" stroke-width="2"/>
				<path d="M12 3a14 14 0 0 0 0 18" fill="none" stroke="#64748b" stroke-width="2"/>
				</svg>
				""",
			["server"] = """
				<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
				<rect x="4" y="4" width="16" height="6" rx="1" fill="#64748b"/>
				<rect x="4" y="14" width="16" height="6" rx="1" fill="#64748b"/>
				<circle cx="17" cy="7" r="1" fill="#ffffff"/>
				<circle cx="17" cy="17" r="1" fill="#ffffff"/>
				</svg>
				""",
			["generic"] = """
				<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
				<rect x="4" y="4" width="16" height="16" rx="3" fill="none" stroke="#64748b" stroke-width="2"/>
				<circle cx="12" cy="12" r="2" fill="#64748b"/>
				</svg>
				""",

			// ── AWS-flavored (curated, original shapes — not the official logos) ──
			["aws:compute"] = VendorGlyph(ChipGlyph),
			["aws:storage"] = VendorGlyph(StorageGlyph),
			["aws:database"] = VendorGlyph(DatabaseGlyph),
			["aws:networking"] = VendorGlyph(NetworkGlyph),
			["aws:serverless"] = VendorGlyph(ServerlessGlyph),
			["aws:load-balancer"] = VendorGlyph(LoadBalancerGlyph),
			["aws:queue"] = VendorGlyph(QueueGlyph),
			["aws:cdn"] = VendorGlyph(CdnGlyph),
			["aws:cache"] = VendorGlyph(CacheGlyph),

			// ── GCP-flavored ──
			["gcp:compute"] = VendorGlyph(ChipGlyph),
			["gcp:storage"] = VendorGlyph(StorageGlyph),
			["gcp:database"] = VendorGlyph(DatabaseGlyph),
			["gcp:networking"] = VendorGlyph(NetworkGlyph),
			["gcp:serverless"] = VendorGlyph(ServerlessGlyph),
			["gcp:load-balancer"] = VendorGlyph(LoadBalancerGlyph),
			["gcp:queue"] = VendorGlyph(QueueGlyph),
			["gcp:cdn"] = VendorGlyph(CdnGlyph),
			["gcp:cache"] = VendorGlyph(CacheGlyph),

			// ── Azure-flavored ──
			["azure:compute"] = VendorGlyph(ChipGlyph),
			["azure:storage"] = VendorGlyph(StorageGlyph),
			["azure:database"] = VendorGlyph(DatabaseGlyph),
			["azure:networking"] = VendorGlyph(NetworkGlyph),
			["azure:serverless"] = VendorGlyph(ServerlessGlyph),
			["azure:load-balancer"] = VendorGlyph(LoadBalancerGlyph),
			["azure:queue"] = VendorGlyph(QueueGlyph),
			["azure:cdn"] = VendorGlyph(CdnGlyph),
			["azure:cache"] = VendorGlyph(CacheGlyph),

			// ── Elastic (full small set) — each product gets its own glyph, not a shared category shape ──
			["elastic:elasticsearch"] = VendorGlyph(DatabaseGlyph),
			["elastic:kibana"] = VendorGlyph(ChartGlyph),
			["elastic:logstash"] = VendorGlyph(FunnelGlyph),
			["elastic:beats"] = VendorGlyph(StorageGlyph),
			["elastic:fleet"] = VendorGlyph(NetworkGlyph),
			["elastic:serverless"] = VendorGlyph(ServerlessGlyph),
			["elastic:apm"] = VendorGlyph(ApmGlyph),
			["elastic:security"] = VendorGlyph(SecurityGlyph),
			["elastic:observability"] = VendorGlyph(ObservabilityGlyph),

			// ── Generic ext: components — common architecture vocabulary, no single vendor ──
			["ext:waf"] = VendorGlyph(WafGlyph),
			["ext:api-gateway"] = VendorGlyph(ApiGatewayGlyph),
			["ext:k8s"] = VendorGlyph(K8sGlyph),
			["ext:pod"] = VendorGlyph(PodGlyph),
			["ext:pool"] = VendorGlyph(PoolGlyph),
			["ext:reverse-proxy"] = VendorGlyph(ReverseProxyGlyph),
			["ext:web"] = VendorGlyph(WebGlyph),
			["ext:api"] = VendorGlyph(ApiGlyph),
			["ext:load-balancer"] = VendorGlyph(LoadBalancerGlyph),
			["ext:queue"] = VendorGlyph(QueueGlyph),
			["ext:cdn"] = VendorGlyph(CdnGlyph),
			["ext:cache"] = VendorGlyph(CacheGlyph),
		};

		var validated = new Dictionary<string, string>(raw.Count, StringComparer.OrdinalIgnoreCase);
		foreach (var (name, svg) in raw)
			validated[name] = IconValidation.ValidateAndNormalize(name, svg);

		return validated.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
	}

	// Simple original glyphs (white, transparent background) — the vendor/ext gradient lives on
	// the service box itself (see BadgeGradients), not inside the icon. Glyph shape generally maps
	// to service *category* (compute/storage/database/...) so it stays recognizable across
	// vendors; products with no natural category equivalent get their own shape. All glyphs are
	// bold solid silhouettes with a similar visual weight — thin outlines or small center dots
	// read as visually smaller even at the same icon box size.
	private const string ChipGlyph = """<rect x="6" y="6" width="12" height="12" rx="2" fill="#ffffff"/><rect x="10" y="2" width="4" height="4" rx="1" fill="#ffffff"/><rect x="10" y="18" width="4" height="4" rx="1" fill="#ffffff"/><rect x="2" y="10" width="4" height="4" rx="1" fill="#ffffff"/><rect x="18" y="10" width="4" height="4" rx="1" fill="#ffffff"/>""";

	// Bucket silhouette — reads clearly as "object storage", distinct from the database cylinder.
	private const string StorageGlyph = """<rect x="4.5" y="6" width="15" height="2.4" rx="1.2" fill="#ffffff"/><path d="M5 8h14l-1.3 10.3A2 2 0 0 1 15.7 20H8.3a2 2 0 0 1-2-1.7z" fill="#ffffff"/>""";

	// Ringed cylinder — the classic database pictogram (subtle internal band lines via stroke-opacity).
	private const string DatabaseGlyph = """<path d="M5 6c0-1.7 3.1-3 7-3s7 1.3 7 3v12c0 1.7-3.1 3-7 3s-7-1.3-7-3z" fill="#ffffff"/><path d="M5 6c0 1.7 3.1 3 7 3s7-1.3 7-3" fill="none" stroke="#000000" stroke-opacity="0.25" stroke-width="1"/><path d="M5 11.3c0 1.7 3.1 3 7 3s7-1.3 7-3" fill="none" stroke="#000000" stroke-opacity="0.25" stroke-width="1"/><path d="M5 16.7c0 1.7 3.1 3 7 3s7-1.3 7-3" fill="none" stroke="#000000" stroke-opacity="0.25" stroke-width="1"/>""";

	private const string NetworkGlyph = """<circle cx="7" cy="7" r="3" fill="#ffffff"/><circle cx="17" cy="7" r="3" fill="#ffffff"/><circle cx="12" cy="18" r="3" fill="#ffffff"/><path d="M8.7 9.7l2.6 5.6M15.3 9.7l-2.6 5.6M10 7h4" stroke="#ffffff" stroke-width="2" fill="none"/>""";
	private const string ChartGlyph = """<rect x="5" y="12" width="4" height="8" fill="#ffffff"/><rect x="10.5" y="8" width="4" height="12" fill="#ffffff"/><rect x="16" y="4" width="4" height="16" fill="#ffffff"/>""";
	private const string FunnelGlyph = """<path d="M5 5h14l-5 7v6l-4 2v-8z" fill="#ffffff"/>""";

	// Vendor-neutral "function" symbol — the Greek letter lambda predates and is broader than any
	// one vendor's serverless branding.
	private const string ServerlessGlyph = """<path d="M14 4L9 13 6 20" fill="none" stroke="#ffffff" stroke-width="2.3" stroke-linecap="round" stroke-linejoin="round"/><path d="M9 13l9 7" fill="none" stroke="#ffffff" stroke-width="2.3" stroke-linecap="round"/>""";

	private const string LoadBalancerGlyph = """<circle cx="12" cy="5" r="2.2" fill="#ffffff"/><path d="M12 7.2v3.3M12 10.5L6 17M12 10.5v6.5M12 10.5l6 6.5" stroke="#ffffff" stroke-width="1.8" fill="none"/><circle cx="6" cy="18.5" r="2" fill="#ffffff"/><circle cx="12" cy="19" r="2" fill="#ffffff"/><circle cx="18" cy="18.5" r="2" fill="#ffffff"/>""";
	private const string QueueGlyph = """<rect x="3" y="9" width="4" height="6" rx="1" fill="#ffffff"/><rect x="9" y="9" width="4" height="6" rx="1" fill="#ffffff"/><rect x="15" y="9" width="4" height="6" rx="1" fill="#ffffff"/><path d="M19 12h3m-2-2l2 2-2 2" stroke="#ffffff" stroke-width="1.6" fill="none" stroke-linecap="round" stroke-linejoin="round"/>""";
	private const string CdnGlyph = """<circle cx="12" cy="12" r="6" fill="none" stroke="#ffffff" stroke-width="1.6"/><path d="M6 12h12M12 6a8 8 0 0 1 0 12M12 6a8 8 0 0 0 0 12" stroke="#ffffff" stroke-width="1.3" fill="none"/><circle cx="4" cy="6" r="1.6" fill="#ffffff"/><circle cx="20" cy="6" r="1.6" fill="#ffffff"/><circle cx="4" cy="18" r="1.6" fill="#ffffff"/><circle cx="20" cy="18" r="1.6" fill="#ffffff"/>""";
	private const string CacheGlyph = """<rect x="4" y="4" width="16" height="16" rx="3" fill="none" stroke="#ffffff" stroke-width="1.6"/><path d="M13 6L8 14h4l-1 6 6-9h-4z" fill="#ffffff"/>""";

	private const string WafGlyph = """<path d="M12 2l7 3v6c0 5-3.3 8.7-7 11-3.7-2.3-7-6-7-11V5z" fill="#ffffff"/><path d="M7.5 10h9M7.5 13h9M7.5 16h6" stroke="#000000" stroke-opacity="0.25" stroke-width="1.1"/>""";
	private const string ApiGatewayGlyph = """<rect x="3.5" y="4" width="3.5" height="16" rx="1" fill="#ffffff"/><rect x="17" y="4" width="3.5" height="16" rx="1" fill="#ffffff"/><path d="M9 12h6m-2.5-2.5L15 12l-2.5 2.5" stroke="#ffffff" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/>""";

	// A 7-spoke wheel abstraction (hexagon + center hub + short spokes) — original, not the
	// trademarked Kubernetes helm logo.
	private const string K8sGlyph = """<path d="M12 2.5l8 4.6v9.8L12 21.5l-8-4.6V7.1z" fill="none" stroke="#ffffff" stroke-width="1.6"/><circle cx="12" cy="12" r="3.2" fill="#ffffff"/><path d="M12 2.5v3.2M12 18.3v3.2M4.9 7.1l2.8 1.6M16.3 15.3l2.8 1.6M19.1 7.1l-2.8 1.6M7.7 15.3l-2.8 1.6" stroke="#ffffff" stroke-width="1.2"/>""";
	private const string PodGlyph = """<circle cx="9" cy="9" r="4.2" fill="#ffffff" fill-opacity="0.85"/><circle cx="15" cy="9" r="4.2" fill="#ffffff" fill-opacity="0.85"/><circle cx="12" cy="15" r="4.2" fill="#ffffff" fill-opacity="0.85"/>""";
	private const string PoolGlyph = """<rect x="3" y="10" width="14" height="9" rx="1.5" fill="#ffffff" fill-opacity="0.55"/><rect x="5" y="6.5" width="14" height="9" rx="1.5" fill="#ffffff" fill-opacity="0.78"/><rect x="7" y="3" width="14" height="9" rx="1.5" fill="#ffffff"/>""";
	private const string ReverseProxyGlyph = """<path d="M5 8h11m-3-3l3 3-3 3" stroke="#ffffff" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/><path d="M19 16H8m3-3l-3 3 3 3" stroke="#ffffff" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/>""";
	private const string WebGlyph = """<rect x="3" y="4" width="18" height="16" rx="2" fill="#ffffff"/><rect x="3" y="4" width="18" height="4.5" rx="2" fill="#000000" fill-opacity="0.2"/><circle cx="5.8" cy="6.2" r="0.7" fill="#000000" fill-opacity="0.4"/><circle cx="7.8" cy="6.2" r="0.7" fill="#000000" fill-opacity="0.4"/>""";
	private const string ApiGlyph = """<path d="M10 3c-2.2 0-3.2 1.1-3.2 3.3v2.6c0 1.1-.9 2.1-2.1 2.1v2c1.2 0 2.1 1 2.1 2.1v2.6c0 2.2 1 3.3 3.2 3.3" stroke="#ffffff" stroke-width="1.8" fill="none" stroke-linecap="round"/><path d="M14 3c2.2 0 3.2 1.1 3.2 3.3v2.6c0 1.1.9 2.1 2.1 2.1v2c-1.2 0-2.1 1-2.1 2.1v2.6c0 2.2-1 3.3-3.2 3.3" stroke="#ffffff" stroke-width="1.8" fill="none" stroke-linecap="round"/>""";

	private const string ApmGlyph = """<path d="M2.5 12h4l2-7 4 14 2-7h5.5" stroke="#ffffff" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/>""";
	private const string SecurityGlyph = """<path d="M12 2l7 3v6c0 5-3.3 8.7-7 11-3.7-2.3-7-6-7-11V5z" fill="#ffffff"/><circle cx="12" cy="11" r="1.8" fill="#000000" fill-opacity="0.3"/><path d="M12 12.5v3.5" stroke="#000000" stroke-opacity="0.3" stroke-width="1.6" stroke-linecap="round"/>""";
	private const string ObservabilityGlyph = """<path d="M2 12s4-6 10-6 10 6 10 6-4 6-10 6-10-6-10-6z" fill="none" stroke="#ffffff" stroke-width="1.6"/><circle cx="12" cy="12" r="3.2" fill="#ffffff"/>""";

	private static string VendorGlyph(string glyph) => $"""
		<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
		{glyph}
		</svg>
		""";
}
