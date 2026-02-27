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

app.MapGet("/", (HttpContext ctx) =>
{
	ctx.Response.ContentType = "text/html; charset=utf-8";
	return ctx.Response.WriteAsync(RenderIndex(null, "lightweight"));
});

app.MapGet("/theme/{theme}", async (HttpContext ctx, string theme) =>
{
	if (!Themes.BuiltIn.ContainsKey(theme))
	{
		ctx.Response.StatusCode = 404;
		await ctx.Response.WriteAsync($"Unknown theme: {theme}");
		return;
	}

	ctx.Response.ContentType = "text/html; charset=utf-8";
	await ctx.Response.WriteAsync(RenderIndex(theme, "lightweight"));
});

app.MapGet("/engine/{engine}", (HttpContext ctx, string engine, string? theme) =>
{
	ctx.Response.ContentType = "text/html; charset=utf-8";
	return ctx.Response.WriteAsync(RenderIndex(theme, engine));
});

app.MapGet("/svg/{slug}", (string slug, string? theme, string? engine) =>
{
	var example = DiagramExamples.All.FirstOrDefault(e => e.Slug == slug);
	if (example == default)
		return Results.NotFound($"Unknown diagram: {slug}");

	var options = ResolveOptions(theme, engine);
	try
	{
		var svg = MermaidRenderer.RenderSvg(example.Source, options);
		return Results.Content(svg, "image/svg+xml");
	}
	catch (MermaidParseException ex)
	{
		return Results.Problem(ex.Message, statusCode: 400);
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

string RenderIndex(string? activeTheme, string activeEngine)
{
	var themeQuery = activeTheme is not null ? $"&theme={activeTheme}" : "";
	var pageBg = activeTheme is not null && Themes.BuiltIn.TryGetValue(activeTheme, out var tc) ? tc.Bg : "#f8f9fa";
	var pageFg = activeTheme is not null && Themes.BuiltIn.TryGetValue(activeTheme, out var tc2) ? tc2.Fg : "#1a1a2e";
	var isSideBySide = activeEngine == "side-by-side";
	var themeQs = activeTheme is not null ? $"?theme={activeTheme}" : "";

	var themeLinks = string.Join("\n",
		Themes.BuiltIn.Keys.OrderBy(k => k).Select(name =>
		{
			var active = name == activeTheme ? " class=\"active\"" : "";
			return $"        <a href=\"/theme/{name}\"{active}>{WebUtility.HtmlEncode(name)}</a>";
		}));

	var engineLinks = new[] { "lightweight", "msagl", "side-by-side" }
		.Select(eng =>
		{
			var active = eng == activeEngine ? " class=\"active\"" : "";
			var themePart = activeTheme is not null ? $"?theme={activeTheme}" : "";
			return $"        <a href=\"/engine/{eng}{themePart}\"{active}>{WebUtility.HtmlEncode(eng)}</a>";
		});
	var engineBar = string.Join("\n", engineLinks);

	string diagrams;
	if (isSideBySide)
	{
		diagrams = string.Join("\n", DiagramExamples.All.Select(e =>
		{
			var escapedSource = WebUtility.HtmlEncode(e.Source.Trim());
			var htmlSafeJson = WebUtility.HtmlEncode(JsonSerializer.Serialize(e.Source.Trim()));
			return $"""
				    <div class="card">
				      <div class="card-header">
				        <h2>{WebUtility.HtmlEncode(e.Title)}</h2>
				        <button class="toggle" onclick="toggle(this)" title="Toggle source">source</button>
				      </div>
				      <div class="side-by-side three-col">
				        <div class="engine-col">
				          <div class="engine-label">Mermaider (Sugiyama)</div>
				          <div class="svg-container">
				            <img src="/svg/{e.Slug}?engine=lightweight{themeQuery}" alt="{WebUtility.HtmlEncode(e.Title)} — Lightweight" loading="lazy" />
				          </div>
				        </div>
				        <div class="engine-col">
				          <div class="engine-label">Mermaider (MSAGL)</div>
				          <div class="svg-container">
				            <img src="/svg/{e.Slug}?engine=msagl{themeQuery}" alt="{WebUtility.HtmlEncode(e.Title)} — MSAGL" loading="lazy" />
				          </div>
				        </div>
				        <div class="engine-col">
				          <div class="engine-label">beautiful-mermaid (TS)</div>
				          <div class="svg-container bm-render" data-source="{htmlSafeJson}">
				            <span class="bm-loading">loading…</span>
				          </div>
				        </div>
				      </div>
				      <pre class="source collapsed"><code>{escapedSource}</code></pre>
				    </div>
				""";
		}));
	}
	else
	{
		var engineParam = activeEngine == "msagl" ? "msagl" : "lightweight";
		diagrams = string.Join("\n", DiagramExamples.All.Select(e =>
		{
			var escapedSource = WebUtility.HtmlEncode(e.Source.Trim());
			return $"""
				    <div class="card">
				      <div class="card-header">
				        <h2>{WebUtility.HtmlEncode(e.Title)}</h2>
				        <button class="toggle" onclick="toggle(this)" title="Toggle source">source</button>
				      </div>
				      <div class="svg-container">
				        <img src="/svg/{e.Slug}?engine={engineParam}{themeQuery}" alt="{WebUtility.HtmlEncode(e.Title)}" loading="lazy" />
				      </div>
				      <pre class="source collapsed"><code>{escapedSource}</code></pre>
				    </div>
				""";
		}));
	}

	return $$"""
		<!DOCTYPE html>
		<html lang="en">
		<head>
		  <meta charset="utf-8" />
		  <meta name="viewport" content="width=device-width, initial-scale=1" />
		  <title>Mermaider Gallery</title>
		  <style>
		    *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
		    body {
		      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
		      background: {{pageBg}}; color: {{pageFg}};
		      padding: 2rem; max-width: 1600px; margin: 0 auto;
		    }
		    h1 { font-size: 1.8rem; margin-bottom: 0.5rem; }
		    .subtitle { opacity: 0.6; margin-bottom: 1rem; font-size: 0.95rem; }
		    .bar-label { font-size: 0.75rem; text-transform: uppercase; letter-spacing: 0.05em; opacity: 0.5; margin-bottom: 0.3rem; }
		    .theme-bar, .engine-bar {
		      display: flex; flex-wrap: wrap; gap: 0.4rem; margin-bottom: 1rem;
		      padding: 0.75rem; border-radius: 8px;
		      background: color-mix(in srgb, {{pageFg}} 8%, transparent);
		    }
		    .theme-bar a, .engine-bar a {
		      padding: 0.3rem 0.6rem; border-radius: 4px; font-size: 0.8rem;
		      text-decoration: none; color: {{pageFg}};
		      background: color-mix(in srgb, {{pageFg}} 6%, transparent);
		      transition: background 0.15s;
		    }
		    .theme-bar a:hover, .engine-bar a:hover { background: color-mix(in srgb, {{pageFg}} 15%, transparent); }
		    .theme-bar a.active, .engine-bar a.active {
		      background: color-mix(in srgb, {{pageFg}} 20%, transparent);
		      font-weight: 600;
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
		    .side-by-side {
		      display: grid; grid-template-columns: 1fr 1fr; gap: 1px;
		      background: color-mix(in srgb, {{pageFg}} 8%, transparent);
		    }
		    .side-by-side.three-col { grid-template-columns: 1fr 1fr 1fr; }
		    .engine-col {
		      background: color-mix(in srgb, {{pageBg}} 90%, {{pageFg}});
		    }
		    .engine-label {
		      text-align: center; font-size: 0.7rem; text-transform: uppercase;
		      letter-spacing: 0.05em; padding: 0.4rem 0; opacity: 0.5;
		      border-bottom: 1px solid color-mix(in srgb, {{pageFg}} 6%, transparent);
		    }
		    .svg-container { padding: 1rem; text-align: center; overflow-x: auto; }
		    .svg-container img, .svg-container svg { max-width: 100%; height: auto; }
		    .bm-loading { opacity: 0.3; font-size: 0.8rem; }
		    .bm-error { color: #e53e3e; font-size: 0.75rem; padding: 0.5rem; word-break: break-word; }
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
		  <p class="subtitle">SVG diagrams rendered with pure .NET — no browser, no JS runtime</p>

		  <div class="bar-label">Theme</div>
		  <div class="theme-bar">
		    <a href="/"{{(activeTheme is null ? " class=\"active\"" : "")}}>default</a>
		{{themeLinks}}
		  </div>

		  <div class="bar-label">Layout Engine</div>
		  <div class="engine-bar">
		{{engineBar}}
		  </div>

		{{diagrams}}

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
		        const resp = await fetch('/render{{themeQs}}', { method: 'POST', body: src });
		        if (!resp.ok) { out.innerHTML = '<p class="error">' + (await resp.text()) + '</p>'; return; }
		        const blob = await resp.blob();
		        const url = URL.createObjectURL(blob);
		        out.innerHTML = '<img src="' + url + '" />';
		      } catch (e) { out.innerHTML = '<p class="error">' + e.message + '</p>'; }
		    }
		  </script>

		  <script type="module">
		    const containers = document.querySelectorAll('.bm-render');
		    if (containers.length > 0) {
		      try {
		        const bm = await import('https://esm.sh/beautiful-mermaid@1.1.3');
		        for (const el of containers) {
		          try {
		            const source = JSON.parse(el.dataset.source);
		            const svg = await bm.renderMermaidSVGAsync(source);
		            el.innerHTML = svg;
		          } catch (err) {
		            el.innerHTML = '<span class="bm-error">' + err.message + '</span>';
		          }
		        }
		      } catch (err) {
		        for (const el of containers) {
		          el.innerHTML = '<span class="bm-error">Failed to load beautiful-mermaid: ' + err.message + '</span>';
		        }
		      }
		    }
		  </script>
		</body>
		</html>
		""";
}
