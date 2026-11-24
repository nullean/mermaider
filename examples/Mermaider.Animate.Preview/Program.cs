using System.Globalization;
using System.Net;
using System.Text;
using Mermaider.Animate;
using Mermaider.Models;

// A preview of Mermaider.Animate: every score in scores/ rendered live, read from disk on each request so an edit
// shows on refresh. Not a product page; it exists to look at the motion on a phone or a desktop.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var scores = Path.Combine(AppContext.BaseDirectory, "../../../../../examples/Mermaider.Animate.Preview/scores");
if (!Directory.Exists(scores))
	scores = Path.Combine(Directory.GetCurrentDirectory(), "scores");

AnimateOptions Options(HttpRequest request, PlaybackMode? mode = null)
{
	var dark = request.Query["theme"] == "dark";
	MotionPreset? motion = request.Query["motion"].ToString() switch
	{
		"crisp" => MotionPreset.Crisp,
		"still" => MotionPreset.None,
		"calm" => MotionPreset.Calm,
		_ => null,
	};
	PlaybackMode? requested = request.Query["mode"].ToString() switch
	{
		"once" => PlaybackMode.Once,
		"scrub" => PlaybackMode.Scrub,
		"loop" => PlaybackMode.Loop,
		_ => mode,
	};
	return new AnimateOptions { Render = dark ? new RenderOptions { Bg = "#0f1115", Fg = "#e6e8ee" } : null, Motion = motion, Mode = requested, };
}

IEnumerable<(string Name, string Title, string Source)> Scores() =>
	Directory
		.GetFiles(scores, "*.mmd")
		.OrderBy(f => f, StringComparer.Ordinal)
		.Select(file =>
		{
			var source = File.ReadAllText(file);
			var comment = source.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("%%", StringComparison.Ordinal));
			var title = comment is not null ? comment[2..].Trim() : Path.GetFileNameWithoutExtension(file);
			return (Path.GetFileNameWithoutExtension(file), title, source);
		});

string Query(HttpRequest request) => request.QueryString.HasValue ? request.QueryString.Value! : "";

string Page(HttpRequest request, string title, string body)
{
	var dark = request.Query["theme"] == "dark";
	var q = request.Query;

	string Link(string key, string value, string label)
	{
		var parts = new Dictionary<string, string?>(StringComparer.Ordinal);
		foreach (var (k, v) in q)
			parts[k] = v.ToString();
		parts[key] = value;
		var href = request.Path + "?" + string.Join(
			"&",
			parts.Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => $"{p.Key}={WebUtility.UrlEncode(p.Value)}")
		);
		var current = (q[key].ToString() is { Length: > 0 } c ? c : "") == value;
		return $"<a class=\"chip{(current ? " on" : "")}\" href=\"{WebUtility.HtmlEncode(href)}\">{label}</a>";
	}

	return $$"""
		<!doctype html>
		<html lang="en">
		<head>
		<meta charset="utf-8">
		<meta name="viewport" content="width=device-width, initial-scale=1">
		<title>{{WebUtility.HtmlEncode(title)}} · Mermaider.Animate</title>
		<style>
		  :root { color-scheme: {{(dark ? "dark" : "light")}}; --page: {{(dark ? "#0b0d10" : "#f4f4f5")}}; --card: {{(dark ? "#0f1115" : "#ffffff")}};
		    --ink: {{(dark ? "#e6e8ee" : "#18181b")}}; --soft: {{(dark ? "#9aa0ab" : "#71717a")}}; --rule: {{(dark ? "#232731" : "#e4e4e7")}}; }
		  * { box-sizing: border-box; }
		  body { margin: 0; background: var(--page); color: var(--ink); font: 15px/1.5 system-ui, -apple-system, 'Segoe UI', sans-serif; }
		  header { position: sticky; top: 0; z-index: 2; background: var(--page); border-bottom: 1px solid var(--rule); padding: 12px 20px; display: flex; flex-wrap: wrap; gap: 8px 16px; align-items: center; }
		  header h1 { font-size: 16px; margin: 0 12px 0 0; }
		  header a.home { color: inherit; text-decoration: none; }
		  .group { display: flex; gap: 4px; align-items: center; color: var(--soft); font-size: 13px; }
		  .chip { color: var(--soft); text-decoration: none; padding: 3px 9px; border: 1px solid var(--rule); border-radius: 6px; font-size: 13px; }
		  .chip.on { color: var(--ink); border-color: var(--ink); }
		  main { max-width: 1100px; margin: 0 auto; padding: 20px; display: grid; gap: 20px; }
		  section { background: var(--card); border: 1px solid var(--rule); border-radius: 10px; padding: 18px 20px; }
		  section h2 { font-size: 15px; margin: 0 0 4px; }
		  .meta { color: var(--soft); font-size: 13px; margin: 0 0 12px; }
		  .stage { display: flex; justify-content: center; overflow-x: auto; }
		  .stage svg { max-width: 100%; height: auto; }
		  ol.steps { margin: 12px 0 0; padding-left: 20px; color: var(--soft); font-size: 13px; }
		  .frames { display: flex; gap: 8px; overflow-x: auto; margin-top: 12px; }
		  .frames img { height: 90px; border: 1px solid var(--rule); border-radius: 6px; background: var(--card); }
		  details { margin-top: 10px; font-size: 13px; }
		  pre { background: var(--page); border: 1px solid var(--rule); border-radius: 6px; padding: 10px; overflow-x: auto; font: 12.5px/1.45 ui-monospace, 'SF Mono', monospace; }
		  .error { color: #e15759; white-space: pre-wrap; font: 13px/1.5 ui-monospace, monospace; }
		  textarea { width: 100%; min-height: 260px; font: 13px/1.45 ui-monospace, 'SF Mono', monospace; background: var(--page); color: var(--ink); border: 1px solid var(--rule); border-radius: 6px; padding: 10px; }
		  button { font: inherit; padding: 6px 14px; border-radius: 6px; border: 1px solid var(--ink); background: var(--ink); color: var(--card); margin-top: 8px; }
		  .spacer { height: 110vh; display: flex; align-items: center; justify-content: center; color: var(--soft); }
		</style>
		</head>
		<body>
		<header>
		  <h1><a class="home" href="/{{WebUtility.HtmlEncode(Query(request))}}">Mermaider.Animate</a></h1>
		  <span class="group">theme {{Link("theme", "", "light")}} {{Link("theme", "dark", "dark")}}</span>
		  <span class="group">motion {{Link("motion", "", "calm")}} {{Link("motion", "crisp", "crisp")}} {{Link("motion", "still", "still")}}</span>
		  <span class="group">mode {{Link("mode", "", "score")}} {{Link("mode", "loop", "loop")}} {{Link("mode", "once", "once")}}</span>
		  <span class="group">view {{Link("view", "", "svg")}} {{Link("view", "player", "player")}}</span>
		  <a class="chip" href="/play{{WebUtility.HtmlEncode(Query(request))}}">playground</a>
		</header>
		<main>
		{{body}}
		</main>
		</body>
		</html>
		""";
}

string Card(HttpRequest request, string name, string title, string source)
{
	var sb = new StringBuilder();
	_ = sb.Append("<section id=\"").Append(name).Append("\"><h2>").Append(WebUtility.HtmlEncode(title)).Append("</h2>");
	try
	{
		var animation = MermaidAnimator.Compile(source, Options(request));
		_ = sb
			.Append("<p class=\"meta\">")
			.Append(animation.Steps.Count)
			.Append(" steps · ")
			.Append((animation.ScoreDuration.TotalSeconds).ToString("0.0", CultureInfo.InvariantCulture))
			.Append(" s · ")
			.Append(animation.Mode.ToString().ToLowerInvariant())
			.Append($" · <a href=\"/svg/{name}{WebUtility.HtmlEncode(Query(request))}\">svg</a>")
			.Append($" · <a href=\"/img/{name}{WebUtility.HtmlEncode(Query(request))}\">as img</a>")
			.Append($" · <a href=\"/scrub/{name}{WebUtility.HtmlEncode(Query(request))}\">scrub</a>")
			.Append($" · <a href=\"/play?src={WebUtility.UrlEncode(source)}&amp;{WebUtility.HtmlEncode(Query(request).TrimStart('?'))}\">edit</a></p>");
		_ = sb.Append("<div class=\"stage\">").Append(request.Query["view"] == "player" ? animation.RenderPlayer() : animation.Svg).Append("</div>");
		if (animation.Steps.Any(s => s.Caption is not null))
		{
			_ = sb.Append("<ol class=\"steps\">");
			foreach (var step in animation.Steps)
				_ = sb
					.Append("<li>")
					.Append(WebUtility.HtmlEncode(step.Caption ?? "(no caption)"))
					.Append(" — ")
					.Append(step.Start.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture))
					.Append("–")
					.Append(step.End.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture))
					.Append(" s</li>");
			_ = sb.Append("</ol>");
		}
		_ = sb.Append("<div class=\"frames\" title=\"frames at the end of each step (RenderFrame)\">");
		foreach (var step in animation.Steps)
		{
			var t = Math.Max(0, step.End.TotalMilliseconds - 60).ToString("0", CultureInfo.InvariantCulture);
			_ = sb.Append($"<img loading=\"lazy\" alt=\"frame\" src=\"/frame/{name}?t={t}&amp;{WebUtility.HtmlEncode(Query(request).TrimStart('?'))}\">");
		}
		_ = sb.Append("</div>");
	}
	catch (Exception ex)
	{
		_ = sb.Append("<p class=\"error\">").Append(WebUtility.HtmlEncode(ex.Message)).Append("</p>");
	}
	_ = sb.Append("<details><summary>source</summary><pre>").Append(WebUtility.HtmlEncode(source)).Append("</pre></details></section>");
	return sb.ToString();
}

app.MapGet(
	"/",
	(HttpRequest request) => Results.Content(
		Page(request, "Examples", string.Concat(Scores().Select(s => Card(request, s.Name, s.Title, s.Source)))),
		"text/html; charset=utf-8"
	)
);

string? Find(string name) => Scores().Where(s => s.Name == name).Select(s => s.Source).FirstOrDefault();

app.MapGet(
	"/svg/{name}",
	(string name, HttpRequest request) => Find(name) is { } source
		? Results.Content(MermaidAnimator.RenderSvg(source, Options(request)), "image/svg+xml")
		: Results.NotFound()
);

app.MapGet(
	"/frame/{name}",
	(string name, double t, HttpRequest request) => Find(name) is { } source
		? Results.Content(MermaidAnimator.Compile(source, Options(request)).RenderFrame(TimeSpan.FromMilliseconds(t)), "image/svg+xml")
		: Results.NotFound()
);

app.MapGet(
	"/img/{name}",
	(string name, HttpRequest request) => Results.Content(
		Page(
			request,
			name,
			$"<section><h2>{WebUtility.HtmlEncode(name)} as &lt;img&gt;</h2><p class=\"meta\">The same SVG loaded as an image: CSS animation still plays; scrub cannot, so it shows the static diagram.</p><div class=\"stage\"><img src=\"/svg/{name}{WebUtility.HtmlEncode(Query(request))}\" style=\"max-width:100%\"></div></section>"
		),
		"text/html; charset=utf-8"
	)
);

app.MapGet("/scrub/{name}", (string name, HttpRequest request) =>
{
	if (Find(name) is not { } source)
		return Results.NotFound();
	string svg;
	try
	{
		svg = MermaidAnimator.RenderSvg(source, Options(request, PlaybackMode.Scrub) with { Mode = PlaybackMode.Scrub });
	}
	catch (Exception ex)
	{
		svg = $"<p class=\"error\">{WebUtility.HtmlEncode(ex.Message)}</p>";
	}
	return Results.Content(
		Page(
			request,
			name,
			$"<div class=\"spacer\">scroll down ↓</div><section><h2>{WebUtility.HtmlEncode(name)}, scrubbed by scroll</h2><div class=\"stage\">{svg}</div></section><div class=\"spacer\">↑ scroll back up to rewind</div>"
		),
		"text/html; charset=utf-8"
	);
});

IResult Play(HttpRequest request, string? src)
{
	var source = src ?? Scores().FirstOrDefault().Source ?? "flowchart LR\n  a --> b\n\nanimate\n";
	var body = new StringBuilder();
	_ = body
		.Append("<section><h2>Playground</h2><form method=\"post\" action=\"/play")
		.Append(WebUtility.HtmlEncode(Query(request)))
		.Append("\"><textarea name=\"src\" spellcheck=\"false\">")
		.Append(WebUtility.HtmlEncode(source))
		.Append("</textarea><br><button type=\"submit\">Render</button></form></section>");
	_ = body.Append(Card(request, "playground", "Result", source));
	return Results.Content(Page(request, "Playground", body.ToString()), "text/html; charset=utf-8");
}

app.MapGet("/play", (HttpRequest request, string? src) => Play(request, src));
app.MapPost("/play", async (HttpRequest request) =>
{
	var form = await request.ReadFormAsync();
	return Play(request, form["src"].ToString().Replace("\r\n", "\n"));
}).DisableAntiforgery();

app.Run();
