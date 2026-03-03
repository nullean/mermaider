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

app.MapGet("/", (HttpContext ctx) =>
{
	var theme = ctx.Request.Query["theme"].FirstOrDefault();
	var engine = ctx.Request.Query["engine"].FirstOrDefault() ?? "lightweight";
	var p1 = ctx.Request.Query["p1"].FirstOrDefault();
	var p2 = ctx.Request.Query["p2"].FirstOrDefault();

	ctx.Response.ContentType = "text/html; charset=utf-8";
	return ctx.Response.WriteAsync(RenderComparePage(theme, engine, p1, p2));
});

app.MapGet("/svg/{slug}", (string slug, string? theme, string? engine) =>
{
	var example = DiagramExamples.All.FirstOrDefault(e => e.Slug == slug);
	if (example == default)
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

	var options = ResolveOptions(theme, engine);
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

app.MapPost("/render", async (HttpContext ctx, string? theme, string? engine) =>
{
	using var reader = new StreamReader(ctx.Request.Body);
	var source = await reader.ReadToEndAsync();

	if (string.IsNullOrWhiteSpace(source))
		return Results.BadRequest("POST body must contain Mermaid source text");

	var options = ResolveOptions(theme, engine);
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

RenderOptions? ResolveOptions(string? theme, string? engine)
{
	IGraphLayoutProvider? provider = engine?.ToLowerInvariant() == "msagl" ? msaglProvider : null;

	if (theme is null || !Themes.BuiltIn.TryGetValue(theme, out var colors))
		return provider is not null ? new RenderOptions { LayoutProvider = provider } : null;

	return new RenderOptions
	{
		Bg = colors.Bg,
		Fg = colors.Fg,
		Line = colors.Line,
		Accent = colors.Accent,
		Muted = colors.Muted,
		Surface = colors.Surface,
		Border = colors.Border,
		LayoutProvider = provider,
	};
}

string RenderComparePage(string? theme, string engine, string? p1, string? p2)
{
	if (p1 is not null && !providerList.Any(x => x.Value == p1)) p1 = null;
	if (p2 is not null && !providerList.Any(x => x.Value == p2)) p2 = null;

	var pageBg = theme is not null && Themes.BuiltIn.TryGetValue(theme, out var tc) ? tc.Bg : "#f8f9fa";
	var pageFg = theme is not null && Themes.BuiltIn.TryGetValue(theme, out var tc2) ? tc2.Fg : "#1a1a2e";

	var themeQuery = theme is not null ? $"&theme={theme}" : "";

	// Theme bar links
	var themeLinks = string.Join("\n",
		Themes.BuiltIn.Keys.OrderBy(k => k).Select(name =>
		{
			var active = name == theme ? " class=\"active\"" : "";
			var qs = BuildQs(name, engine, p1, p2);
			return $"        <a href=\"/{qs}\"{active}>{WebUtility.HtmlEncode(name)}</a>";
		}));
	var defaultThemeActive = theme is null ? " class=\"active\"" : "";
	var defaultThemeQs = BuildQs(null, engine, p1, p2);

	// Engine options
	var engineOptions = BuildSelectOptions(engine, [("lightweight", "Sugiyama (built-in)"), ("msagl", "MSAGL")]);

	// Provider options
	var p1Options = BuildSelectOptions(p1 ?? "", [("", "— none —"), .. providerList]);
	var p2Options = BuildSelectOptions(p2 ?? "", [("", "— none —"), .. providerList]);

	var activeProviders = new List<string>();
	if (!string.IsNullOrEmpty(p1)) activeProviders.Add(p1);
	if (!string.IsNullOrEmpty(p2)) activeProviders.Add(p2);

	var colCount = 1 + activeProviders.Count;
	var gridColsClass = colCount switch { 2 => " two-col", 3 => " three-col", _ => "" };

	var engineLabel = engine == "msagl" ? "Mermaider (MSAGL)" : "Mermaider (Sugiyama)";

	// Build diagram cards
	var cards = string.Join("\n", DiagramExamples.All.Select(e =>
	{
		var escapedSource = WebUtility.HtmlEncode(e.Source.Trim());
		var htmlSafeJson = WebUtility.HtmlEncode(JsonSerializer.Serialize(e.Source.Trim()));

		var mermaiderCol = $"""
					        <div class="provider-col">
					          <div class="provider-label">{engineLabel}</div>
					          <div class="svg-container">
					            <img src="/svg/{e.Slug}?engine={engine}{themeQuery}" alt="{WebUtility.HtmlEncode(e.Title)}" loading="lazy" />
					          </div>
					        </div>
					""";

		var extraCols = string.Join("\n", activeProviders.Select(prov =>
			RenderProviderColumn(prov, e.Slug, e.Title, htmlSafeJson)));

		if (colCount == 1)
		{
			return $"""
					    <div class="card">
					      <div class="card-header">
					        <h2>{WebUtility.HtmlEncode(e.Title)}</h2>
					        <button class="toggle" onclick="toggle(this)" title="Toggle source">source</button>
					      </div>
					      <div class="svg-container">
					        <img src="/svg/{e.Slug}?engine={engine}{themeQuery}" alt="{WebUtility.HtmlEncode(e.Title)}" loading="lazy" />
					      </div>
					      <pre class="source collapsed"><code>{escapedSource}</code></pre>
					    </div>
					""";
		}

		return $"""
				    <div class="card">
				      <div class="card-header">
				        <h2>{WebUtility.HtmlEncode(e.Title)}</h2>
				        <button class="toggle" onclick="toggle(this)" title="Toggle source">source</button>
				      </div>
				      <div class="compare-grid{gridColsClass}">
				{mermaiderCol}
				{extraCols}
				      </div>
				      <pre class="source collapsed"><code>{escapedSource}</code></pre>
				    </div>
				""";
	}));

	return $$"""
		<!DOCTYPE html>
		<html lang="en">
		<head>
		  <meta charset="utf-8" />
		  <meta name="viewport" content="width=device-width, initial-scale=1" />
		  <title>Mermaider Gallery — Compare</title>
		  <style>
		    *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
		    body {
		      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
		      background: {{pageBg}}; color: {{pageFg}};
		      padding: 2rem; max-width: 1800px; margin: 0 auto;
		    }
		    h1 { font-size: 1.8rem; margin-bottom: 0.5rem; }
		    .subtitle { opacity: 0.6; margin-bottom: 1rem; font-size: 0.95rem; }
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
		      background: color-mix(in srgb, {{pageBg}} 90%, {{pageFg}});
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
		  </style>
		</head>
		<body>
		  <h1>Mermaider Gallery</h1>
		  <p class="subtitle">Compare Mermaid diagram renderers side by side</p>

		  <div class="bar-label">Theme</div>
		  <div class="theme-bar">
		    <a href="/{{defaultThemeQs}}"{{defaultThemeActive}}>default</a>
		{{themeLinks}}
		  </div>

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
		</body>
		</html>
		""";
}

string RenderProviderColumn(string provider, string slug, string title, string htmlSafeJson)
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

	return $"""
				        <div class="provider-col">
				          <div class="provider-label">{label}</div>
				          <div class="svg-container">{content}</div>
				        </div>
				""";
}

string BuildQs(string? theme, string engine, string? p1, string? p2)
{
	var parts = new List<string>();
	if (theme is not null) parts.Add($"theme={Uri.EscapeDataString(theme)}");
	if (engine != "lightweight") parts.Add($"engine={Uri.EscapeDataString(engine)}");
	if (!string.IsNullOrEmpty(p1)) parts.Add($"p1={Uri.EscapeDataString(p1)}");
	if (!string.IsNullOrEmpty(p2)) parts.Add($"p2={Uri.EscapeDataString(p2)}");
	return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
}

string BuildSelectOptions(string selectedValue, (string Value, string Label)[] options)
{
	return string.Join("\n", options.Select(o =>
	{
		var sel = o.Value == selectedValue ? " selected" : "";
		return $"        <option value=\"{WebUtility.HtmlEncode(o.Value)}\"{sel}>{WebUtility.HtmlEncode(o.Label)}</option>";
	}));
}
