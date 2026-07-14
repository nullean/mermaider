using System.Net;
using System.Text.Json;
using Mermaider;
using Mermaider.Gallery;
using Mermaider.Layout;
using Mermaider.Layout.Msagl;
using Mermaider.Models;
using Mermaider.Theming;

var msaglProvider = new MsaglLayoutProvider();

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var providerList = new (string Value, string Label)[]
{
	("mermaidjs", "mermaid.js"),
	("beautiful-mermaid", "beautiful-mermaid"),
	("naiad", "Naiad"),
};

// The rendered SVGs are transparent by default (MermaidRenderer's Transparent option defaults to
// true) — the ".provider-col" background is purely a page-chrome choice behind them, so switching
// it never touches diagram rendering itself.
var bgOptions = new (string Value, string Label)[]
{
	("", "Default"),
	("transparent", "Transparent (checkerboard)"),
	("white", "White"),
	("light-gray", "Light gray"),
	("dark", "Dark"),
};

_ = app.MapGet("/", (ctx) =>
{
	var theme = ctx.Request.Query["theme"].FirstOrDefault();
	var engine = ctx.Request.Query["engine"].FirstOrDefault() ?? "lightweight";
	var p1 = ctx.Request.Query["p1"].FirstOrDefault();
	var p2 = ctx.Request.Query["p2"].FirstOrDefault();
	var bg = ctx.Request.Query["bg"].FirstOrDefault();

	ctx.Response.ContentType = "text/html; charset=utf-8";
	return ctx.Response.WriteAsync(RenderComparePage(theme, engine, p1, p2, bg));
});

foreach (var cat in Enum.GetValues<DiagramCategory>())
{
	var slug = DiagramExamples.CategorySlug(cat);
	var category = cat;
	_ = app.MapGet($"/{slug}", (ctx) =>
	{
		var theme = ctx.Request.Query["theme"].FirstOrDefault();
		var engine = ctx.Request.Query["engine"].FirstOrDefault() ?? "lightweight";
		var p1 = ctx.Request.Query["p1"].FirstOrDefault();
		var p2 = ctx.Request.Query["p2"].FirstOrDefault();
		var bg = ctx.Request.Query["bg"].FirstOrDefault();

		ctx.Response.ContentType = "text/html; charset=utf-8";
		return ctx.Response.WriteAsync(RenderCategoryPage(category, theme, engine, p1, p2, bg));
	});
}

app.MapGet("/svg/{slug}", (HttpContext ctx, string slug) =>
{
	var q = ctx.Request.Query;
	var theme = q["theme"].FirstOrDefault();
	var engine = q["engine"].FirstOrDefault();
	var example = DiagramExamples.All.FirstOrDefault(e => e.Slug == slug);
	if (example is null)
		return Results.NotFound($"Unknown diagram: {slug}");

	if (engine == "naiad")
	{
		try
		{
			var svg = MermaidSharp.Mermaid.Render(example.Source, MermaidSharp.RenderOptions.Default);
			return Results.Content(svg, "image/svg+xml");
		}
		catch (Exception ex)
		{
			return Results.Content(ErrorSvg(ex.Message), "image/svg+xml");
		}
	}

	var options = ResolveOptions(q);
	try
	{
		var svg = MermaidRenderer.RenderSvg(example.Source, options);
		return Results.Content(svg, "image/svg+xml");
	}
	catch (MermaidParseException ex)
	{
		return Results.Content(ErrorSvg(ex.Message), "image/svg+xml");
	}
});

app.MapGet("/source/{slug}", (string slug) =>
{
	var example = DiagramExamples.All.FirstOrDefault(e => e.Slug == slug);
	return example is null
		? Results.NotFound($"Unknown diagram: {slug}")
		: Results.Content(example.Source, "text/plain; charset=utf-8");
});

app.MapPost("/render", async (HttpContext ctx) =>
{
	using var reader = new StreamReader(ctx.Request.Body);
	var source = await reader.ReadToEndAsync();

	if (string.IsNullOrWhiteSpace(source))
		return Results.BadRequest("POST body must contain Mermaid source text");

	var options = ResolveOptions(ctx.Request.Query);
	try
	{
		var svg = MermaidRenderer.RenderSvg(source, options);
		return Results.Content(svg, "image/svg+xml");
	}
	catch (MermaidParseException ex)
	{
		return Results.Problem(ex.Message, statusCode: 400);
	}
});

app.MapGet("/playground", ctx =>
{
	var q = ctx.Request.Query;
	var theme = q["theme"].FirstOrDefault();
	var engine = q["engine"].FirstOrDefault() ?? "lightweight";
	var slug = q["example"].FirstOrDefault() ?? DiagramExamples.All.FirstOrDefault()?.Slug;

	ctx.Response.ContentType = "text/html; charset=utf-8";
	return ctx.Response.WriteAsync(RenderPlaygroundPage(theme, engine, slug, q));
});

Console.WriteLine("Gallery running at http://localhost:5555");
app.Run("http://localhost:5555");

string ErrorSvg(string message)
{
	var escaped = WebUtility.HtmlEncode(message);
	return $"""
		<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 480 80">
		  <rect width="480" height="80" fill="#fff5f5" rx="8"/>
		  <text x="240" y="45" text-anchor="middle" fill="#c53030" font-size="12"
		        font-family="system-ui, sans-serif">{escaped}</text>
		</svg>
		""";
}

string ProviderColStyle(string? bg) => bg switch
{
	"transparent" =>
		"background-color:#ffffff;" +
		"background-image:linear-gradient(45deg,#ccc 25%,transparent 25%),linear-gradient(-45deg,#ccc 25%,transparent 25%),linear-gradient(45deg,transparent 75%,#ccc 75%),linear-gradient(-45deg,transparent 75%,#ccc 75%);" +
		"background-size:20px 20px;background-position:0 0,0 10px,10px -10px,-10px 0px;",
	"white" => "background:#ffffff;",
	"light-gray" => "background:#e5e5e5;",
	"dark" => "background:#1a1a1a;",
	_ => "background:linear-gradient(135deg, #f5f5f5 0%, #d9d9d9 100%);",
};

RenderOptions? ResolveOptions(IQueryCollection q)
{
	var theme = q["theme"].FirstOrDefault();
	var engine = q["engine"].FirstOrDefault();
	IGraphLayoutProvider? provider = engine?.ToLowerInvariant() == "msagl" ? msaglProvider : null;

	// Seed from named theme (if any)
	DiagramColors? base_ = null;
	if (theme is not null)
		_ = Themes.BuiltIn.TryGetValue(theme, out base_);

	// Per-channel overrides from query params win over the theme seed
	var bg = q["bg"].FirstOrDefault() ?? base_?.Bg;
	var fg = q["fg"].FirstOrDefault() ?? base_?.Fg;
	var line = q["line"].FirstOrDefault() ?? base_?.Line;
	var accent = q["accent"].FirstOrDefault() ?? base_?.Accent;
	var muted = q["muted"].FirstOrDefault() ?? base_?.Muted;
	var surface = q["surface"].FirstOrDefault() ?? base_?.Surface;
	var border = q["border"].FirstOrDefault() ?? base_?.Border;

	// Render option overrides
	double? padding = double.TryParse(q["padding"].FirstOrDefault(), out var p) ? p : null;
	double? nodeSpacing = double.TryParse(q["nodeSpacing"].FirstOrDefault(), out var ns) ? ns : null;
	double? layerSpacing = double.TryParse(q["layerSpacing"].FirstOrDefault(), out var ls) ? ls : null;
	bool? roundedEdges = q["rounded"].FirstOrDefault() is { } rv ? rv is not ("false" or "0") : null;
	bool? transparent = q["transparent"].FirstOrDefault() is { } tv ? tv is not ("false" or "0") : null;
	var font = q["font"].FirstOrDefault();
	var monoFont = q["monoFont"].FirstOrDefault();
	var fontSize = q["fontSize"].FirstOrDefault();

	// If nothing at all was set, skip allocating an options object
	if (bg is null && fg is null && padding is null && nodeSpacing is null && layerSpacing is null
		&& roundedEdges is null && transparent is null && font is null && monoFont is null && fontSize is null && provider is null)
		return null;

	return new RenderOptions
	{
		Bg = bg,
		Fg = fg,
		Line = line,
		Accent = accent,
		Muted = muted,
		Surface = surface,
		Border = border,
		Padding = padding,
		NodeSpacing = nodeSpacing,
		LayerSpacing = layerSpacing,
		RoundedEdges = roundedEdges ?? true,
		Transparent = transparent ?? true,
		Font = font,
		MonoFont = monoFont,
		FontSize = fontSize,
		LayoutProvider = provider,
	};
}

string RenderSectionBar(string activePath, string? theme, string engine)
{
	var examplesActive = activePath != "/playground" ? " active" : "";
	var playActive = activePath == "/playground" ? " active" : "";
	var examplesHref = $"/{BuildPageQs(theme, engine)}";
	return $"""
		<a href="{examplesHref}" class="section-link{examplesActive}">Compare</a>
		<a href="/playground" class="section-link{playActive}">Theme Playground</a>
		""";
}

string RenderNav(string activePath, string? theme, string engine, string? p1 = null, string? p2 = null, string? bg = null)
{
	var cats = Enum.GetValues<DiagramCategory>();
	var links = new List<string>();

	var homeActive = activePath == "/" ? " active" : "";
	links.Add($"<a href=\"/{BuildPageQs(theme, engine, p1, p2, bg)}\" class=\"nav-link{homeActive}\">Compare</a>");

	foreach (var cat in cats)
	{
		var slug = DiagramExamples.CategorySlug(cat);
		var label = DiagramExamples.CategoryLabel(cat);
		var count = DiagramExamples.ByCategory(cat).Length;
		var active = activePath == $"/{slug}" ? " active" : "";
		links.Add($"<a href=\"/{slug}{BuildPageQs(theme, engine, p1, p2, bg)}\" class=\"nav-link{active}\">{label} <span class=\"count\">{count}</span></a>");
	}

	return string.Join("\n    ", links);
}

string BuildPageQs(string? theme, string engine, string? p1 = null, string? p2 = null, string? bg = null)
{
	var parts = new List<string>();
	if (theme is not null)
		parts.Add($"theme={Uri.EscapeDataString(theme)}");
	if (engine != "lightweight")
		parts.Add($"engine={Uri.EscapeDataString(engine)}");
	if (!string.IsNullOrEmpty(p1))
		parts.Add($"p1={Uri.EscapeDataString(p1)}");
	if (!string.IsNullOrEmpty(p2))
		parts.Add($"p2={Uri.EscapeDataString(p2)}");
	if (!string.IsNullOrEmpty(bg))
		parts.Add($"bg={Uri.EscapeDataString(bg)}");
	return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
}

(string bg, string fg) PageColors(string? theme) =>
	theme is not null && Themes.BuiltIn.TryGetValue(theme, out var tc) ? (tc.Bg, tc.Fg) : ("#f8f9fa", "#1a1a2e");

string SharedStyles(string pageBg, string pageFg) => $$"""
	*, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
	body {
	  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
	  background: {{pageBg}}; color: {{pageFg}};
	  padding: 0; max-width: 1800px; margin: 0 auto;
	}
	header {
	  padding: 1.5rem 2rem 0;
	}
	header h1 { font-size: 1.8rem; margin-bottom: 0.3rem; }
	header .subtitle { opacity: 0.6; font-size: 0.95rem; margin-bottom: 0.8rem; }
	.section-bar {
	  display: flex; gap: 0;
	  border-bottom: 2px solid color-mix(in srgb, {{pageFg}} 12%, transparent);
	  margin: 0 -2rem; padding: 0 2rem;
	}
	.section-link {
	  padding: 0.55rem 1.3rem; font-size: 1rem; font-weight: 500;
	  text-decoration: none; color: {{pageFg}}; opacity: 0.5;
	  border-bottom: 3px solid transparent; margin-bottom: -2px;
	  transition: opacity 0.15s;
	}
	.section-link:hover { opacity: 0.85; }
	.section-link.active { opacity: 1; font-weight: 700; border-bottom-color: {{pageFg}}; }
	nav.main-nav {
	  display: flex; flex-wrap: wrap; gap: 0.3rem;
	  padding: 0.5rem 2rem; margin-bottom: 0.5rem;
	  border-bottom: 1px solid color-mix(in srgb, {{pageFg}} 10%, transparent);
	}
	.nav-link {
	  padding: 0.4rem 0.8rem; border-radius: 6px 6px 0 0; font-size: 0.85rem;
	  text-decoration: none; color: {{pageFg}}; opacity: 0.6;
	  transition: opacity 0.15s, background 0.15s;
	}
	.nav-link:hover { opacity: 1; background: color-mix(in srgb, {{pageFg}} 6%, transparent); }
	.nav-link.active {
	  opacity: 1; font-weight: 600;
	  background: color-mix(in srgb, {{pageFg}} 10%, transparent);
	  border-bottom: 2px solid {{pageFg}};
	}
	.nav-link .count {
	  font-size: 0.7rem; opacity: 0.5; margin-left: 0.2rem;
	}
	main { padding: 1rem 2rem 2rem; }
	.bar-label { font-size: 0.75rem; text-transform: uppercase; letter-spacing: 0.05em; opacity: 0.5; margin-bottom: 0.3rem; }
	.theme-bar {
	  display: flex; flex-wrap: wrap; gap: 0.4rem; margin-bottom: 1rem;
	  padding: 0.75rem; border-radius: 8px;
	  background: color-mix(in srgb, {{pageFg}} 8%, transparent);
	}
	.theme-bar a {
	  padding: 0.3rem 0.6rem; border-radius: 4px; font-size: 0.8rem;
	  text-decoration: none; color: {{pageFg}};
	  background: color-mix(in srgb, {{pageFg}} 6%, transparent);
	  transition: background 0.15s;
	}
	.theme-bar a:hover { background: color-mix(in srgb, {{pageFg}} 15%, transparent); }
	.theme-bar a.active {
	  background: color-mix(in srgb, {{pageFg}} 20%, transparent);
	  font-weight: 600;
	}
	.controls {
	  display: flex; flex-wrap: wrap; gap: 1.2rem; align-items: flex-end;
	  margin-bottom: 1.5rem; padding: 0.75rem 1rem; border-radius: 8px;
	  background: color-mix(in srgb, {{pageFg}} 8%, transparent);
	}
	.control-group { display: flex; flex-direction: column; gap: 0.25rem; }
	.control-group label {
	  font-size: 0.7rem; text-transform: uppercase;
	  letter-spacing: 0.05em; opacity: 0.5;
	}
	.control-group select {
	  padding: 0.35rem 0.6rem; border-radius: 5px;
	  border: 1px solid color-mix(in srgb, {{pageFg}} 20%, transparent);
	  background: {{pageBg}}; color: {{pageFg}};
	  font-size: 0.85rem; cursor: pointer;
	}
	.card {
	  border: 1px solid color-mix(in srgb, {{pageFg}} 12%, transparent);
	  border-radius: 10px; margin-bottom: 1.5rem; overflow: hidden;
	  background: color-mix(in srgb, {{pageBg}} 90%, {{pageFg}});
	}
	.card-header {
	  display: flex; justify-content: space-between; align-items: center;
	  padding: 0.75rem 1rem;
	  border-bottom: 1px solid color-mix(in srgb, {{pageFg}} 8%, transparent);
	}
	.card-header h2 { font-size: 1rem; font-weight: 600; }
	.card-header .feature-tag {
	  font-size: 0.65rem; padding: 0.15rem 0.5rem; border-radius: 3px;
	  background: color-mix(in srgb, {{pageFg}} 10%, transparent);
	  opacity: 0.7; margin-left: 0.5rem; font-weight: 400;
	}
	.toggle {
	  background: none; border: 1px solid color-mix(in srgb, {{pageFg}} 20%, transparent);
	  border-radius: 4px; padding: 0.2rem 0.5rem; font-size: 0.75rem;
	  cursor: pointer; color: {{pageFg}}; opacity: 0.6;
	}
	.toggle:hover { opacity: 1; }
	.compare-grid {
	  display: grid; grid-template-columns: 1fr; gap: 1px;
	  background: color-mix(in srgb, {{pageFg}} 8%, transparent);
	}
	.compare-grid.two-col { grid-template-columns: 1fr 1fr; }
	.compare-grid.three-col { grid-template-columns: 1fr 1fr 1fr; }
	.provider-col {
	  background: linear-gradient(135deg, #f5f5f5 0%, #d9d9d9 100%);
	}
	.provider-label {
	  text-align: center; font-size: 0.7rem; text-transform: uppercase;
	  letter-spacing: 0.05em; padding: 0.4rem 0; opacity: 0.5;
	  border-bottom: 1px solid color-mix(in srgb, {{pageFg}} 6%, transparent);
	}
	.svg-container { padding: 1rem; text-align: center; overflow-x: auto; }
	.svg-container img, .svg-container svg { max-width: 100%; height: auto; }
	.render-loading { opacity: 0.3; font-size: 0.8rem; }
	.render-error { color: #e53e3e; font-size: 0.75rem; padding: 0.5rem; word-break: break-word; }
	pre.source {
	  padding: 1rem; font-size: 0.8rem; overflow-x: auto;
	  background: color-mix(in srgb, {{pageFg}} 5%, transparent);
	  border-top: 1px solid color-mix(in srgb, {{pageFg}} 8%, transparent);
	  white-space: pre-wrap; word-break: break-word;
	}
	pre.source.collapsed { display: none; }
	.try-it {
	  margin-top: 2rem; padding: 1.5rem; border-radius: 10px;
	  border: 1px solid color-mix(in srgb, {{pageFg}} 12%, transparent);
	  background: color-mix(in srgb, {{pageBg}} 90%, {{pageFg}});
	}
	.try-it h2 { font-size: 1.1rem; margin-bottom: 0.75rem; }
	.try-it textarea {
	  width: 100%; min-height: 120px; font-family: monospace; font-size: 0.85rem;
	  padding: 0.75rem; border-radius: 6px; resize: vertical;
	  border: 1px solid color-mix(in srgb, {{pageFg}} 20%, transparent);
	  background: {{pageBg}}; color: {{pageFg}};
	}
	.try-it button {
	  margin-top: 0.5rem; padding: 0.5rem 1.2rem; border-radius: 6px;
	  border: none; cursor: pointer; font-weight: 600;
	  background: color-mix(in srgb, {{pageFg}} 15%, transparent); color: {{pageFg}};
	}
	.try-it button:hover { background: color-mix(in srgb, {{pageFg}} 25%, transparent); }
	#live-result { margin-top: 1rem; text-align: center; }
	#live-result img { max-width: 100%; }
	.error { color: #e53e3e; font-size: 0.85rem; margin-top: 0.5rem; }
	.section-intro {
	  margin-bottom: 1.5rem; padding: 1rem 1.25rem; border-radius: 8px;
	  background: color-mix(in srgb, {{pageFg}} 5%, transparent);
	  font-size: 0.9rem; line-height: 1.5;
	}
	.section-intro .feature-list {
	  display: flex; flex-wrap: wrap; gap: 0.4rem; margin-top: 0.5rem;
	}
	.section-intro .feature-pill {
	  font-size: 0.7rem; padding: 0.2rem 0.6rem; border-radius: 10px;
	  background: color-mix(in srgb, {{pageFg}} 10%, transparent);
	}
	.playground-layout {
	  display: grid; grid-template-columns: 1fr 1fr; gap: 1.5rem; align-items: start;
	}
	@media (max-width: 900px) { .playground-layout { grid-template-columns: 1fr; } }
	.playground-panel {
	  border: 1px solid color-mix(in srgb, {{pageFg}} 12%, transparent);
	  border-radius: 10px; overflow: hidden;
	  background: color-mix(in srgb, {{pageBg}} 90%, {{pageFg}});
	}
	.playground-panel h2 { font-size: 1rem; font-weight: 600; padding: 0.6rem 1rem;
	  border-bottom: 1px solid color-mix(in srgb, {{pageFg}} 8%, transparent); }
	.playground-panel textarea {
	  width: 100%; min-height: 180px; font-family: monospace; font-size: 0.85rem;
	  padding: 0.75rem; border: none; resize: vertical;
	  background: color-mix(in srgb, {{pageBg}} 95%, {{pageFg}}); color: {{pageFg}};
	}
	.playground-preview { padding: 1rem; text-align: center; min-height: 80px; background: #ffffff; }
	.playground-preview img { max-width: 100%; height: auto; }
	.playground-controls {
	  padding: 1rem; border-top: 1px solid color-mix(in srgb, {{pageFg}} 8%, transparent);
	  display: flex; flex-wrap: wrap; gap: 0.8rem; align-items: flex-end;
	}
	.play-ctrl { display: flex; flex-direction: column; gap: 0.2rem; min-width: 60px; }
	.play-ctrl label {
	  font-size: 0.65rem; text-transform: uppercase; letter-spacing: 0.04em; opacity: 0.5;
	}
	.play-ctrl input[type=color] { width: 44px; height: 28px; padding: 2px; border-radius: 4px;
	  border: 1px solid color-mix(in srgb, {{pageFg}} 20%, transparent); cursor: pointer; }
	.play-ctrl input[type=range] { width: 120px; accent-color: {{pageFg}}; }
	.play-ctrl select, .play-ctrl input[type=text] {
	  padding: 0.25rem 0.45rem; border-radius: 5px; font-size: 0.8rem;
	  border: 1px solid color-mix(in srgb, {{pageFg}} 20%, transparent);
	  background: {{pageBg}}; color: {{pageFg}};
	}
	.play-ctrl input[type=checkbox] { accent-color: {{pageFg}}; }
	.ctrl-val {
	  font-size: 0.75rem; font-variant-numeric: tabular-nums;
	  opacity: 0.75; margin-left: 0.35rem;
	}
	.ctrl-hint {
	  font-size: 0.62rem; opacity: 0.38; margin-top: 0.05rem; line-height: 1.2;
	}
	.pg-panel-title {
	  font-size: 1.05rem; font-weight: 600; padding: 0.6rem 0.8rem;
	  border-bottom: 1px solid color-mix(in srgb, {{pageFg}} 10%, transparent);
	}
	.pg-card.selected { outline: 2px solid {{pageFg}}; outline-offset: -2px; }
	.pg-card {
	  cursor: pointer;
	  transition: transform 0.1s, box-shadow 0.1s;
	}
	.pg-card:hover {
	  transform: translateY(-2px);
	  box-shadow: 0 4px 12px color-mix(in srgb, {{pageFg}} 18%, transparent);
	  outline: 1px solid color-mix(in srgb, {{pageFg}} 30%, transparent);
	}
	.play-ctrl-palette { min-width: unset; }
	.palette-row { display: flex; flex-wrap: wrap; gap: 3px; margin-top: 2px; }
	.palette-swatch {
	  width: 20px; height: 20px; border-radius: 3px;
	  border: 1px solid color-mix(in srgb, {{pageFg}} 15%, transparent);
	  flex-shrink: 0;
	}
	.filter-bar {
	  display: flex; flex-wrap: wrap; gap: 0.4rem; margin-bottom: 1rem; margin-top: 1.5rem;
	  padding: 0.6rem 0.8rem; border-radius: 8px;
	  background: color-mix(in srgb, {{pageFg}} 6%, transparent);
	}
	.filter-chip {
	  padding: 0.25rem 0.7rem; border-radius: 12px; font-size: 0.8rem; cursor: pointer;
	  border: 1px solid color-mix(in srgb, {{pageFg}} 20%, transparent);
	  background: transparent; color: {{pageFg}}; opacity: 0.6; transition: all 0.1s;
	}
	.filter-chip:hover, .filter-chip.active { opacity: 1;
	  background: color-mix(in srgb, {{pageFg}} 15%, transparent); }
	.filter-chip.active { font-weight: 600; }
	.pg-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 1rem; }
	.pg-card { border: 1px solid color-mix(in srgb, {{pageFg}} 10%, transparent);
	  border-radius: 8px; overflow: hidden; text-align: center;
	  background: color-mix(in srgb, {{pageBg}} 92%, {{pageFg}}); }
	.pg-card img { max-width: 100%; height: auto; display: block; }
	.pg-card-label { font-size: 0.75rem; padding: 0.35rem 0.5rem; opacity: 0.6; }
	.pg-edit-block {
	  border-top: 1px solid color-mix(in srgb, {{pageFg}} 10%, transparent);
	  padding: 0.8rem;
	}
	.pg-edit-block > label {
	  display: block; font-size: 0.65rem; text-transform: uppercase;
	  letter-spacing: 0.04em; opacity: 0.5; margin-bottom: 0.4rem;
	}
	#pg-edit {
	  width: 100%; min-height: 140px; font-family: monospace; font-size: 0.8rem;
	  padding: 0.6rem; border-radius: 5px; resize: vertical;
	  border: 1px solid color-mix(in srgb, {{pageFg}} 20%, transparent);
	  background: color-mix(in srgb, {{pageBg}} 95%, {{pageFg}}); color: {{pageFg}};
	  line-height: 1.45;
	}
	""";

string SharedScripts(string engine, string themeQuery) => $$"""
	<script>
	  function nav(key, value) {
	    const url = new URL(window.location);
	    if (value) url.searchParams.set(key, value);
	    else url.searchParams.delete(key);
	    window.location = url;
	  }
	  function toggle(btn) {
	    const pre = btn.closest('.card').querySelector('pre.source');
	    pre.classList.toggle('collapsed');
	    btn.textContent = pre.classList.contains('collapsed') ? 'source' : 'hide';
	  }
	  async function renderLive() {
	    const src = document.getElementById('source').value;
	    const out = document.getElementById('live-result');
	    out.innerHTML = '';
	    try {
	      const resp = await fetch('/render?engine={{engine}}{{themeQuery}}', { method: 'POST', body: src });
	      if (!resp.ok) { out.innerHTML = '<p class="error">' + (await resp.text()) + '</p>'; return; }
	      const blob = await resp.blob();
	      const url = URL.createObjectURL(blob);
	      out.innerHTML = '<img src="' + url + '" />';
	    } catch (e) { out.innerHTML = '<p class="error">' + e.message + '</p>'; }
	  }
	</script>
	""";

string MermaidJsScript() => """
	<script type="module">
	  const mjsContainers = document.querySelectorAll('.mjs-render');
	  const bmContainers = document.querySelectorAll('.bm-render');

	  if (mjsContainers.length > 0) {
	    try {
	      const { default: mermaid } = await import('https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs');
	      mermaid.initialize({ startOnLoad: false, theme: 'default', securityLevel: 'loose' });
	      let i = 0;
	      for (const el of mjsContainers) {
	        try {
	          const source = JSON.parse(el.dataset.source);
	          const { svg } = await mermaid.render('mjs-' + (i++), source);
	          el.innerHTML = svg;
	        } catch (err) {
	          el.innerHTML = '<span class="render-error">' + err.message + '</span>';
	        }
	      }
	    } catch (err) {
	      for (const el of mjsContainers) {
	        el.innerHTML = '<span class="render-error">Failed to load mermaid.js: ' + err.message + '</span>';
	      }
	    }
	  }

	  if (bmContainers.length > 0) {
	    try {
	      const bm = await import('https://esm.sh/beautiful-mermaid@1.1.3');
	      for (const el of bmContainers) {
	        try {
	          const source = JSON.parse(el.dataset.source);
	          const svg = await bm.renderMermaidSVGAsync(source);
	          el.innerHTML = svg;
	        } catch (err) {
	          el.innerHTML = '<span class="render-error">' + err.message + '</span>';
	        }
	      }
	    } catch (err) {
	      for (const el of bmContainers) {
	        el.innerHTML = '<span class="render-error">Failed to load beautiful-mermaid: ' + err.message + '</span>';
	      }
	    }
	  }
	</script>
	""";

string RenderCardSingle(DiagramExample e, string engine, string themeQuery, bool showFeature)
{
	var escapedSource = WebUtility.HtmlEncode(e.Source.Trim());
	var featureTag = showFeature && e.Feature is not null
		? $"<span class=\"feature-tag\">{WebUtility.HtmlEncode(e.Feature)}</span>"
		: "";

	return $$"""
		    <div class="card">
		      <div class="card-header">
		        <h2>{{WebUtility.HtmlEncode(e.Title)}}{{featureTag}}</h2>
		        <button class="toggle" onclick="toggle(this)" title="Toggle source">source</button>
		      </div>
		      <div class="svg-container">
		        <img src="/svg/{{e.Slug}}?engine={{engine}}{{themeQuery}}" alt="{{WebUtility.HtmlEncode(e.Title)}}" loading="lazy" />
		      </div>
		      <pre class="source collapsed"><code>{{escapedSource}}</code></pre>
		    </div>
		""";
}

string RenderCardCompare(DiagramExample e, string engine, string engineLabel, string themeQuery, string gridColsClass, IReadOnlyList<string> activeProviders, string? bg, bool showFeature = false)
{
	var escapedSource = WebUtility.HtmlEncode(e.Source.Trim());
	var htmlSafeJson = WebUtility.HtmlEncode(JsonSerializer.Serialize(e.Source.Trim()));
	var featureTag = showFeature && e.Feature is not null
		? $"<span class=\"feature-tag\">{WebUtility.HtmlEncode(e.Feature)}</span>"
		: "";

	var mermaiderCol = $$"""
		        <div class="provider-col" style="{{ProviderColStyle(bg)}}">
		          <div class="provider-label">{{engineLabel}}</div>
		          <div class="svg-container">
		            <img src="/svg/{{e.Slug}}?engine={{engine}}{{themeQuery}}" alt="{{WebUtility.HtmlEncode(e.Title)}}" loading="lazy" />
		          </div>
		        </div>
		""";

	var extraCols = string.Join("\n", activeProviders.Select(prov =>
		RenderProviderColumn(prov, e.Slug, e.Title, htmlSafeJson, bg)));

	return $$"""
		    <div class="card">
		      <div class="card-header">
		        <h2>{{WebUtility.HtmlEncode(e.Title)}}{{featureTag}}</h2>
		        <button class="toggle" onclick="toggle(this)" title="Toggle source">source</button>
		      </div>
		      <div class="compare-grid{{gridColsClass}}">
		{{mermaiderCol}}
		{{extraCols}}
		      </div>
		      <pre class="source collapsed"><code>{{escapedSource}}</code></pre>
		    </div>
		""";
}

string RenderCategoryPage(DiagramCategory category, string? theme, string engine, string? p1, string? p2, string? bg)
{
	if (p1 is not null && !providerList.Any(x => x.Value == p1))
		p1 = null;
	if (p2 is not null && !providerList.Any(x => x.Value == p2))
		p2 = null;

	var (pageBg, pageFg) = PageColors(theme);
	var themeQuery = theme is not null ? $"&theme={theme}" : "";
	var catSlug = DiagramExamples.CategorySlug(category);
	var catLabel = DiagramExamples.CategoryLabel(category);
	var examples = DiagramExamples.ByCategory(category);

	var themeLinks = RenderThemeBar(theme, engine, $"/{catSlug}", p1, p2, bg);
	var engineOptions = BuildSelectOptions(engine, [("lightweight", "Sugiyama (built-in)"), ("msagl", "MSAGL")]);
	var p1Options = BuildSelectOptions(p1 ?? "", [("", "— none —"), .. providerList]);
	var p2Options = BuildSelectOptions(p2 ?? "", [("", "— none —"), .. providerList]);
	var bgOptionsHtml = BuildSelectOptions(bg ?? "", bgOptions);
	var navHtml = RenderNav($"/{catSlug}", theme, engine, p1, p2, bg);
	var sectionBarHtml = RenderSectionBar($"/{catSlug}", theme, engine);

	var activeProviders = new List<string>();
	if (!string.IsNullOrEmpty(p1))
		activeProviders.Add(p1);
	if (!string.IsNullOrEmpty(p2))
		activeProviders.Add(p2);

	var colCount = 1 + activeProviders.Count;
	var gridColsClass = colCount switch { 2 => " two-col", 3 => " three-col", _ => "" };
	var engineLabel = engine == "msagl" ? "Mermaider (MSAGL)" : "Mermaider (Sugiyama)";

	var features = examples.Where(e => e.Feature is not null).Select(e => e.Feature).Distinct().ToArray();
	var featurePills = features.Length > 0
		? $"<div class=\"feature-list\">{string.Join("", features.Select(f => $"<span class=\"feature-pill\">{WebUtility.HtmlEncode(f)}</span>"))}</div>"
		: "";

	var cards = colCount == 1
		? string.Join("\n", examples.Select(e => RenderCardSingle(e, engine, themeQuery, showFeature: true)))
		: string.Join("\n", examples.Select(e => RenderCardCompare(e, engine, engineLabel, themeQuery, gridColsClass, activeProviders, bg, showFeature: true)));

	var defaultSource = examples.Length > 0 ? examples[0].Source.Trim() : "graph TD\n  A --> B";

	return $$"""
		<!DOCTYPE html>
		<html lang="en">
		<head>
		  <meta charset="utf-8" />
		  <meta name="viewport" content="width=device-width, initial-scale=1" />
		  <title>Mermaider — {{catLabel}} Diagrams</title>
		  <style>
		{{SharedStyles(pageBg, pageFg)}}
		  </style>
		</head>
		<body>
		  <header>
		    <h1>Mermaider Gallery</h1>
		    <p class="subtitle">{{catLabel}} diagram examples &amp; features</p>
		    <div class="section-bar">{{sectionBarHtml}}</div>
		  </header>

		  <nav class="main-nav">
		    {{navHtml}}
		  </nav>

		  <main>
		    <div class="bar-label">Theme</div>
		    {{themeLinks}}

		    <div class="controls">
		      <div class="control-group">
		        <label for="sel-engine">Mermaider Engine</label>
		        <select id="sel-engine" onchange="nav('engine', this.value)">
		{{engineOptions}}
		        </select>
		      </div>
		      <div class="control-group">
		        <label for="sel-p1">Compare with</label>
		        <select id="sel-p1" onchange="nav('p1', this.value)">
		{{p1Options}}
		        </select>
		      </div>
		      <div class="control-group">
		        <label for="sel-p2">and</label>
		        <select id="sel-p2" onchange="nav('p2', this.value)">
		{{p2Options}}
		        </select>
		      </div>
		      <div class="control-group">
		        <label for="sel-bg">Comparison background</label>
		        <select id="sel-bg" onchange="nav('bg', this.value)">
		{{bgOptionsHtml}}
		        </select>
		      </div>
		    </div>

		    <div class="section-intro">
		      {{examples.Length}} examples covering {{catLabel}} diagram features.
		      {{featurePills}}
		    </div>

		{{cards}}

		    <div class="try-it">
		      <h2>Try It — {{catLabel}}</h2>
		      <textarea id="source" spellcheck="false">{{WebUtility.HtmlEncode(defaultSource)}}</textarea>
		      <button onclick="renderLive()">Render</button>
		      <div id="live-result"></div>
		    </div>
		  </main>

		{{SharedScripts(engine, themeQuery)}}
		{{MermaidJsScript()}}
		</body>
		</html>
		""";
}

string RenderComparePage(string? theme, string engine, string? p1, string? p2, string? bg)
{
	if (p1 is not null && !providerList.Any(x => x.Value == p1))
		p1 = null;
	if (p2 is not null && !providerList.Any(x => x.Value == p2))
		p2 = null;

	var (pageBg, pageFg) = PageColors(theme);
	var themeQuery = theme is not null ? $"&theme={theme}" : "";

	var themeLinks = RenderThemeBar(theme, engine, "/", p1, p2, bg);
	var engineOptions = BuildSelectOptions(engine, [("lightweight", "Sugiyama (built-in)"), ("msagl", "MSAGL")]);
	var p1Options = BuildSelectOptions(p1 ?? "", [("", "— none —"), .. providerList]);
	var p2Options = BuildSelectOptions(p2 ?? "", [("", "— none —"), .. providerList]);
	var bgOptionsHtml = BuildSelectOptions(bg ?? "", bgOptions);
	var navHtml = RenderNav("/", theme, engine, p1, p2, bg);
	var sectionBarHtml = RenderSectionBar("/", theme, engine);

	var activeProviders = new List<string>();
	if (!string.IsNullOrEmpty(p1))
		activeProviders.Add(p1);
	if (!string.IsNullOrEmpty(p2))
		activeProviders.Add(p2);

	var colCount = 1 + activeProviders.Count;
	var gridColsClass = colCount switch { 2 => " two-col", 3 => " three-col", _ => "" };
	var engineLabel = engine == "msagl" ? "Mermaider (MSAGL)" : "Mermaider (Sugiyama)";

	var cards = colCount == 1
		? string.Join("\n", DiagramExamples.All.Select(e => RenderCardSingle(e, engine, themeQuery, showFeature: false)))
		: string.Join("\n", DiagramExamples.All.Select(e => RenderCardCompare(e, engine, engineLabel, themeQuery, gridColsClass, activeProviders, bg)));

	return $$"""
		<!DOCTYPE html>
		<html lang="en">
		<head>
		  <meta charset="utf-8" />
		  <meta name="viewport" content="width=device-width, initial-scale=1" />
		  <title>Mermaider Gallery — Compare</title>
		  <style>
		{{SharedStyles(pageBg, pageFg)}}
		  </style>
		</head>
		<body>
		  <header>
		    <h1>Mermaider Gallery</h1>
		    <p class="subtitle">Compare Mermaid diagram renderers side by side</p>
		    <div class="section-bar">{{sectionBarHtml}}</div>
		  </header>

		  <nav class="main-nav">
		    {{navHtml}}
		  </nav>

		  <main>
		    <div class="bar-label">Theme</div>
		    {{themeLinks}}

		    <div class="controls">
		      <div class="control-group">
		        <label for="sel-engine">Mermaider Engine</label>
		        <select id="sel-engine" onchange="nav('engine', this.value)">
		{{engineOptions}}
		        </select>
		      </div>
		      <div class="control-group">
		        <label for="sel-p1">Compare with</label>
		        <select id="sel-p1" onchange="nav('p1', this.value)">
		{{p1Options}}
		        </select>
		      </div>
		      <div class="control-group">
		        <label for="sel-p2">and</label>
		        <select id="sel-p2" onchange="nav('p2', this.value)">
		{{p2Options}}
		        </select>
		      </div>
		      <div class="control-group">
		        <label for="sel-bg">Comparison background</label>
		        <select id="sel-bg" onchange="nav('bg', this.value)">
		{{bgOptionsHtml}}
		        </select>
		      </div>
		    </div>

		{{cards}}

		    <div class="try-it">
		      <h2>Try It</h2>
		      <textarea id="source" spellcheck="false">graph TD
		  A[Hello] --> B{World}
		  B -->|Yes| C[Great]
		  B -->|No| D[Hmm]</textarea>
		      <button onclick="renderLive()">Render</button>
		      <div id="live-result"></div>
		    </div>
		  </main>

		{{SharedScripts(engine, themeQuery)}}
		{{MermaidJsScript()}}
		</body>
		</html>
		""";
}

string RenderPlaygroundPage(string? theme, string engine, string? selectedSlug, IQueryCollection q)
{
	var (pageBg, pageFg) = PageColors(theme);
	var sectionBarHtml = RenderSectionBar("/playground", theme, engine);

	var allExamples = DiagramExamples.All;
	var selectedEx = allExamples.FirstOrDefault(e => e.Slug == selectedSlug) ?? allExamples[0];

	// Seed color values from theme or defaults
	DiagramColors? baseColors = null;
	if (theme is not null)
		_ = Themes.BuiltIn.TryGetValue(theme, out baseColors);
	var defaultBg = q["bg"].FirstOrDefault() ?? baseColors?.Bg ?? "#FFFFFF";
	var defaultFg = q["fg"].FirstOrDefault() ?? baseColors?.Fg ?? "#27272A";
	var defaultLine = q["line"].FirstOrDefault() ?? baseColors?.Line ?? "";
	var defaultAccent = q["accent"].FirstOrDefault() ?? baseColors?.Accent ?? "#3b82f6";
	var defaultMuted = q["muted"].FirstOrDefault() ?? baseColors?.Muted ?? "";
	var defaultSurface = q["surface"].FirstOrDefault() ?? baseColors?.Surface ?? "";
	var defaultBorder = q["border"].FirstOrDefault() ?? baseColors?.Border ?? "";
	var defaultPadding = q["padding"].FirstOrDefault() ?? "40";
	var defaultNs = q["nodeSpacing"].FirstOrDefault() ?? "28";
	var defaultLs = q["layerSpacing"].FirstOrDefault() ?? "48";
	var defaultRounded = q["rounded"].FirstOrDefault() ?? "true";
	var defaultTransp = q["transparent"].FirstOrDefault() ?? "true";
	var defaultFont = q["font"].FirstOrDefault() ?? "Inter";
	var defaultMonoFont = q["monoFont"].FirstOrDefault() ?? "";
	var roundedChecked = defaultRounded is not ("false" or "0") ? " checked" : "";
	var transpChecked = defaultTransp is not ("false" or "0") ? " checked" : "";

	// Build base theme picker options
	var themeOptions = string.Join("\n",
		new[] { ("", "default") }.Concat(Themes.BuiltIn.Keys.OrderBy(k => k).Select(k => (k, k)))
		.Select(t =>
		{
			var sel = (t.Item1 == theme) || (t.Item1 == "" && theme is null) ? " selected" : "";
			return $"<option value=\"{WebUtility.HtmlEncode(t.Item1)}\"{sel}>{WebUtility.HtmlEncode(t.Item2)}</option>";
		}));

	// Filter chips
	var categories = Enum.GetValues<DiagramCategory>();
	var filterChips = "<button class=\"filter-chip active\" data-cat=\"\">All</button>\n" +
		string.Join("\n", categories.Select(c =>
			$"<button class=\"filter-chip\" data-cat=\"{DiagramExamples.CategorySlug(c)}\">{WebUtility.HtmlEncode(DiagramExamples.CategoryLabel(c))}</button>"));

	// Gallery grid — cards are clickable to select the preview
	var gridCards = string.Join("\n", allExamples.Select(e =>
	{
		var selected = e.Slug == selectedEx.Slug ? " selected" : "";
		var titleJs = WebUtility.HtmlEncode(JsonSerializer.Serialize(e.Title));
		return $$"""
		    <div class="pg-card{{selected}}" data-cat="{{DiagramExamples.CategorySlug(e.Category)}}" data-slug="{{e.Slug}}"
		      onclick="pgSelectCard('{{e.Slug}}',{{titleJs}})">
		      <img class="pg-thumb" alt="{{WebUtility.HtmlEncode(e.Title)}}" loading="lazy" />
		      <div class="pg-card-label">{{WebUtility.HtmlEncode(e.Title)}}</div>
		    </div>
		""";
	}));

	// Slugs JSON for JS
	var slugsJson = "[" + string.Join(",",
		allExamples.Select(e => $"{{\"slug\":\"{e.Slug}\",\"cat\":\"{DiagramExamples.CategorySlug(e.Category)}\"}}")) + "]";

	return $$"""
		<!DOCTYPE html>
		<html lang="en">
		<head>
		  <meta charset="utf-8" />
		  <meta name="viewport" content="width=device-width,initial-scale=1" />
		  <title>Playground — Mermaider Gallery</title>
		  <style>
		{{SharedStyles(pageBg, pageFg)}}
		  </style>
		</head>
		<body>
		<header>
		  <h1>Mermaider Gallery</h1>
		  <p class="subtitle">Theme playground</p>
		  <div class="section-bar">{{sectionBarHtml}}</div>
		</header>
		<main>
		  <div class="playground-layout">
		    <!-- Left: controls -->
		    <div class="playground-panel">
		      <div class="pg-panel-title">Editor</div>
		      <div class="playground-controls">
		        <div class="play-ctrl">
		          <label>Base theme</label>
		          <select id="pg-theme" onchange="pgThemeChanged(this.value)">
		{{themeOptions}}
		          </select>
		        </div>
		        <div class="play-ctrl"><label>bg</label><input type="color" id="pg-bg" value="{{defaultBg}}" oninput="pgScheduleRender()" /></div>
		        <div class="play-ctrl"><label>fg</label><input type="color" id="pg-fg" value="{{defaultFg}}" oninput="pgScheduleRender()" /></div>
		        <div class="play-ctrl"><label>accent</label><input type="color" id="pg-accent" value="{{defaultAccent}}" oninput="pgScheduleRender()" /></div>
		        <div class="play-ctrl"><label>line</label><input type="color" id="pg-line" value="{{(defaultLine.Length > 0 ? defaultLine : "#888888")}}" oninput="pgScheduleRender()" /></div>
		        <div class="play-ctrl"><label>muted</label><input type="color" id="pg-muted" value="{{(defaultMuted.Length > 0 ? defaultMuted : "#777777")}}" oninput="pgScheduleRender()" /></div>
		        <div class="play-ctrl play-ctrl-palette">
		          <label title="Data palette used by pie, gantt, timeline, gitgraph, sankey, radar, mindmap, venn, journey, packet, xychart, treemap (CategoricalPalette.cs)">data palette</label>
		          <div class="palette-row">{{PaletteSwatches()}}</div>
		        </div>
		        <div class="play-ctrl">
		          <label>padding <span class="ctrl-val" id="pg-pad-val">{{defaultPadding}}</span></label>
		          <input type="range" id="pg-pad" min="0" max="100" value="{{defaultPadding}}"
		            oninput="document.getElementById('pg-pad-val').textContent=this.value;pgScheduleRender()" />
		          <span class="ctrl-hint">px around the diagram</span>
		        </div>
		        <div class="play-ctrl">
		          <label>node spacing <span class="ctrl-val" id="pg-ns-val">{{defaultNs}}</span></label>
		          <input type="range" id="pg-ns" min="4" max="80" value="{{defaultNs}}"
		            oninput="document.getElementById('pg-ns-val').textContent=this.value;pgScheduleRender()" />
		          <span class="ctrl-hint">px between sibling nodes</span>
		        </div>
		        <div class="play-ctrl">
		          <label>layer spacing <span class="ctrl-val" id="pg-ls-val">{{defaultLs}}</span></label>
		          <input type="range" id="pg-ls" min="8" max="120" value="{{defaultLs}}"
		            oninput="document.getElementById('pg-ls-val').textContent=this.value;pgScheduleRender()" />
		          <span class="ctrl-hint">px between layout layers</span>
		        </div>
		        <div class="play-ctrl"><label>font</label>
		          <select id="pg-font" onchange="pgScheduleRender()">
		            <option value="Inter"{{(defaultFont == "Inter" ? " selected" : "")}}>Inter</option>
		            <option value="system-ui"{{(defaultFont == "system-ui" ? " selected" : "")}}>system-ui</option>
		            <option value="Georgia"{{(defaultFont == "Georgia" ? " selected" : "")}}>Georgia</option>
		            <option value="serif"{{(defaultFont == "serif" ? " selected" : "")}}>serif</option>
		            <option value="sans-serif"{{(defaultFont == "sans-serif" ? " selected" : "")}}>sans-serif</option>
		          </select>
		        </div>
		        <div class="play-ctrl"><label title="mono font — used for code/type text (ER, Class)">mono font</label>
		          <select id="pg-mono-font" onchange="pgScheduleRender()">
		            <option value=""{{(defaultMonoFont == "" ? " selected" : "")}}>default</option>
		            <option value="ui-monospace"{{(defaultMonoFont == "ui-monospace" ? " selected" : "")}}>ui-monospace</option>
		            <option value="monospace"{{(defaultMonoFont == "monospace" ? " selected" : "")}}>monospace</option>
		            <option value="Courier New"{{(defaultMonoFont == "Courier New" ? " selected" : "")}}>Courier New</option>
		            <option value="Menlo"{{(defaultMonoFont == "Menlo" ? " selected" : "")}}>Menlo</option>
		          </select>
		        </div>
		        <div class="play-ctrl"><label title="rounded edges">rounded</label>
		          <input type="checkbox" id="pg-rounded"{{roundedChecked}} onchange="pgScheduleRender()" />
		        </div>
		        <div class="play-ctrl"><label>transparent</label>
		          <input type="checkbox" id="pg-transp"{{transpChecked}} onchange="pgScheduleRender()" />
		        </div>
		      </div>
		      <div class="pg-edit-block">
		        <label>Edit Diagram</label>
		        <textarea id="pg-edit" spellcheck="false" oninput="pgScheduleRender()"></textarea>
		      </div>
		    </div>
		    <!-- Right: live preview -->
		    <div class="playground-panel">
		      <div class="pg-panel-title">Preview Selected: <span id="pg-preview-title">{{WebUtility.HtmlEncode(selectedEx.Title)}}</span></div>
		      <div class="playground-preview" id="pg-out"><span style="opacity:.3">Rendering…</span></div>
		    </div>
		  </div>

		  <!-- Gallery grid -->
		  <div class="filter-bar">
		{{filterChips}}
		  </div>
		  <div class="pg-grid" id="pg-grid">
		{{gridCards}}
		  </div>
		</main>
		{{SharedScripts(engine, "")}}
		{{PlaygroundScripts(slugsJson, selectedEx.Source)}}
		</body>
		</html>
		""";
}

string PlaygroundScripts(string slugsJson, string initialSource) => $$"""
	<script>
	  const PG_SLUGS = {{slugsJson}};
	  let pgTimer = null;
	  let pgCurrentSlug = document.querySelector('.pg-card.selected')?.dataset.slug || (PG_SLUGS[0]?.slug ?? '');

	  function pgBuildQs() {
	    const p = new URLSearchParams();
	    const bg = document.getElementById('pg-bg').value;
	    const fg = document.getElementById('pg-fg').value;
	    const accent = document.getElementById('pg-accent').value;
	    const line = document.getElementById('pg-line').value;
	    const muted = document.getElementById('pg-muted').value;
	    const pad = document.getElementById('pg-pad').value;
	    const ns = document.getElementById('pg-ns').value;
	    const ls = document.getElementById('pg-ls').value;
	    const font = document.getElementById('pg-font').value;
	    const monoFont = document.getElementById('pg-mono-font').value;
	    const rounded = document.getElementById('pg-rounded').checked ? 'true' : 'false';
	    const transp = document.getElementById('pg-transp').checked ? 'true' : 'false';
	    if (bg) p.set('bg', bg);
	    if (fg) p.set('fg', fg);
	    if (accent) p.set('accent', accent);
	    if (line) p.set('line', line);
	    if (muted) p.set('muted', muted);
	    p.set('padding', pad);
	    p.set('nodeSpacing', ns);
	    p.set('layerSpacing', ls);
	    if (font !== 'Inter') p.set('font', font);
	    if (monoFont) p.set('monoFont', monoFont);
	    p.set('rounded', rounded);
	    p.set('transparent', transp);
	    return p.toString();
	  }

	  async function pgRender() {
	    const out = document.getElementById('pg-out');
	    const qs = pgBuildQs();
	    const src = document.getElementById('pg-edit')?.value;
	    if (!src) return;
	    out.innerHTML = '<span style="opacity:.3">Rendering…</span>';
	    try {
	      const resp = await fetch('/render?' + qs, { method: 'POST', body: src });
	      if (!resp.ok) {
	        const txt = await resp.text();
	        out.innerHTML = '<span style="color:#e53e3e;font-size:.8rem">' + txt + '</span>';
	      } else {
	        const blob = await resp.blob();
	        const url = URL.createObjectURL(blob);
	        const img = new Image();
	        img.src = url;
	        img.style.maxWidth = '100%';
	        out.innerHTML = '';
	        out.appendChild(img);
	      }
	    } catch(e) {
	      out.innerHTML = '<span style="color:#e53e3e">' + e.message + '</span>';
	    }
	    pgUpdateGrid();
	  }

	  function pgUpdateGrid() {
	    const qs = pgBuildQs();
	    const imgs = document.querySelectorAll('.pg-thumb');
	    imgs.forEach((img, i) => {
	      if (i < PG_SLUGS.length) {
	        img.src = '/svg/' + PG_SLUGS[i].slug + '?' + qs;
	      }
	    });
	  }

	  function pgScheduleRender() {
	    clearTimeout(pgTimer);
	    pgTimer = setTimeout(pgRender, 300);
	  }

	  async function pgSelectCard(slug, title) {
	    pgCurrentSlug = slug;
	    document.getElementById('pg-preview-title').textContent = title;
	    document.querySelectorAll('.pg-card').forEach(c => c.classList.remove('selected'));
	    const card = document.querySelector('.pg-card[data-slug="' + slug + '"]');
	    if (card) card.classList.add('selected');
	    try {
	      const resp = await fetch('/source/' + slug);
	      if (resp.ok) document.getElementById('pg-edit').value = await resp.text();
	    } catch(e) { /* ignore */ }
	    window.scrollTo({ top: 0, behavior: 'smooth' });
	    pgRender();
	  }

	  function pgThemeChanged(theme) {
	    const themeData = {{ThemesJson()}};
	    if (theme && themeData[theme]) {
	      const t = themeData[theme];
	      if (t.bg) document.getElementById('pg-bg').value = t.bg;
	      if (t.fg) document.getElementById('pg-fg').value = t.fg;
	      if (t.accent) document.getElementById('pg-accent').value = t.accent;
	      if (t.line) document.getElementById('pg-line').value = t.line;
	      if (t.muted) document.getElementById('pg-muted').value = t.muted;
	    }
	    pgScheduleRender();
	  }

	  // Filter chips
	  document.querySelectorAll('.filter-chip').forEach(chip => {
	    chip.addEventListener('click', () => {
	      document.querySelectorAll('.filter-chip').forEach(c => c.classList.remove('active'));
	      chip.classList.add('active');
	      const cat = chip.dataset.cat;
	      document.querySelectorAll('.pg-card').forEach(card => {
	        card.style.display = (!cat || card.dataset.cat === cat) ? '' : 'none';
	      });
	    });
	  });

	  // Populate textarea with selected example source, then render
	  document.getElementById('pg-edit').value = {{System.Text.Json.JsonSerializer.Serialize(initialSource)}};
	  pgRender();
	</script>
	""";

// 12-color Tableau sequence — mirrors CategoricalPalette.cs (src/Mermaider/Rendering/CategoricalPalette.cs)
string PaletteSwatches()
{
	string[] colors =
	[
		"#4e79a7", "#f28e2b", "#e15759", "#76b7b2",
		"#59a14f", "#edc948", "#b07aa1", "#ff9da7",
		"#9c755f", "#bab0ac", "#86bcb6", "#8cd17d",
	];
	return string.Join("", colors.Select(c =>
		$"<div class=\"palette-swatch\" style=\"background:{c}\" title=\"{c}\"></div>"));
}

string ThemesJson()
{
	var entries = Themes.BuiltIn.Select(kv =>
	{
		var c = kv.Value;
		var parts = new List<string> { $"\"bg\":\"{c.Bg}\"", $"\"fg\":\"{c.Fg}\"" };
		if (c.Accent is not null)
			parts.Add($"\"accent\":\"{c.Accent}\"");
		if (c.Line is not null)
			parts.Add($"\"line\":\"{c.Line}\"");
		if (c.Muted is not null)
			parts.Add($"\"muted\":\"{c.Muted}\"");
		return $"\"{kv.Key}\":{{{string.Join(",", parts)}}}";
	});
	return "{" + string.Join(",", entries) + "}";
}

string RenderThemeBar(string? theme, string engine, string basePath, string? p1 = null, string? p2 = null, string? bg = null)
{
	var themeLinks = string.Join("\n",
		Themes.BuiltIn.Keys.OrderBy(k => k).Select(name =>
		{
			var active = name == theme ? " class=\"active\"" : "";
			var qs = BuildFullQs(name, engine, basePath, p1, p2, bg);
			return $"    <a href=\"{basePath}{qs}\"{active}>{WebUtility.HtmlEncode(name)}</a>";
		}));
	var defaultActive = theme is null ? " class=\"active\"" : "";
	var defaultQs = BuildFullQs(null, engine, basePath, p1, p2, bg);

	return $"""
		<div class="theme-bar">
		    <a href="{basePath}{defaultQs}"{defaultActive}>default</a>
		{themeLinks}
		  </div>
		""";
}

string BuildFullQs(string? theme, string engine, string basePath, string? p1 = null, string? p2 = null, string? bg = null)
{
	var parts = new List<string>();
	if (theme is not null)
		parts.Add($"theme={Uri.EscapeDataString(theme)}");
	if (engine != "lightweight")
		parts.Add($"engine={Uri.EscapeDataString(engine)}");
	if (!string.IsNullOrEmpty(p1))
		parts.Add($"p1={Uri.EscapeDataString(p1)}");
	if (!string.IsNullOrEmpty(p2))
		parts.Add($"p2={Uri.EscapeDataString(p2)}");
	if (!string.IsNullOrEmpty(bg))
		parts.Add($"bg={Uri.EscapeDataString(bg)}");
	return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
}

string RenderProviderColumn(string provider, string slug, string title, string htmlSafeJson, string? bg)
{
	var label = provider switch
	{
		"mermaidjs" => "mermaid.js",
		"beautiful-mermaid" => "beautiful-mermaid",
		"naiad" => "Naiad",
		_ => provider
	};

	var content = provider switch
	{
		"naiad" => $"<img src=\"/svg/{slug}?engine=naiad\" alt=\"{WebUtility.HtmlEncode(title)} — Naiad\" loading=\"lazy\" />",
		"mermaidjs" => $"<div class=\"mjs-render\" data-source=\"{htmlSafeJson}\"><span class=\"render-loading\">loading…</span></div>",
		"beautiful-mermaid" => $"<div class=\"bm-render\" data-source=\"{htmlSafeJson}\"><span class=\"render-loading\">loading…</span></div>",
		_ => ""
	};

	return $$"""
		        <div class="provider-col" style="{{ProviderColStyle(bg)}}">
		          <div class="provider-label">{{label}}</div>
		          <div class="svg-container">{{content}}</div>
		        </div>
		""";
}

string BuildSelectOptions(string selectedValue, (string Value, string Label)[] options) =>
	string.Join("\n", options.Select(o =>
	{
		var sel = o.Value == selectedValue ? " selected" : "";
		return $"        <option value=\"{WebUtility.HtmlEncode(o.Value)}\"{sel}>{WebUtility.HtmlEncode(o.Label)}</option>";
	}));
