/// Builds the public documentation locally, exactly as CI does, and serves it.
///
/// The reason this needs a target at all: `docs-builder serve` renders pages on demand and knows
/// nothing about the branded landing page, which is a standalone HTML file that replaces the
/// generated `index.html` after the build. So the only way to preview the real site is to build,
/// apply the override, and serve the output — which is what this does, in the order CI does it.
module Documentation

open System
open System.IO
open System.IO.Compression
open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks
open ProcNet

let private exec binary args = Proc.Exec(binary, List.toArray args) |> ignore

/// The sub-path GitHub Pages serves this repo from. Empty when the site is hosted at the domain
/// root (mermaider.nullean.net). Three files have to agree on it: this one,
/// `.github/workflows/docs.yml` (the `prefix:` input, absent when empty) and the `<base href>`
/// in the landing page. `checkPrefixesAgree` below turns drift between them into a local failure
/// rather than a 404 in production.
let PathPrefix = ""

let private docsSource = "docs"
let private landingPage = Path.Combine(docsSource, "mermaider-landing.html")
let private workflow = Path.Combine(".github", "workflows", "docs.yml")

/// docs-builder always writes here.
let private htmlOutput = Path.Combine(".artifacts", "docs", "html")

// ─────────────────────────────  acquiring docs-builder  ─────────────────────────────

let private toolPath =
    let exe = if OperatingSystem.IsWindows() then "docs-builder.exe" else "docs-builder"
    Path.Combine(".artifacts", "tools", exe)

let private archiveName () =
    let arch =
        match Runtime.InteropServices.RuntimeInformation.OSArchitecture with
        | Runtime.InteropServices.Architecture.Arm64 -> "arm64"
        | Runtime.InteropServices.Architecture.X64 -> "x64"
        | other -> failwithf "docs-builder ships no binary for %O" other
    if OperatingSystem.IsMacOS() then sprintf "docs-builder-mac-%s.zip" arch
    elif OperatingSystem.IsLinux() then sprintf "docs-builder-linux-%s.zip" arch
    elif OperatingSystem.IsWindows() then sprintf "docs-builder-win-%s.zip" arch
    else failwith "unsupported operating system for docs-builder"

let ensureTool () =
    if File.Exists toolPath then toolPath
    else

    let archive = archiveName ()
    let version =
        match Environment.GetEnvironmentVariable "DOCS_BUILDER_VERSION" with
        | null | "" -> "latest"
        | v -> v
    let url =
        match version with
        | "latest" -> sprintf "https://github.com/elastic/docs-builder/releases/latest/download/%s" archive
        | v -> sprintf "https://github.com/elastic/docs-builder/releases/download/%s/%s" v archive

    printfn "docs-builder not cached, downloading %s" url
    Directory.CreateDirectory(Path.GetDirectoryName toolPath) |> ignore

    let zip = Path.Combine(Path.GetTempPath(), archive)
    use client = new HttpClient()
    client.Timeout <- TimeSpan.FromMinutes 5.0
    do
        use response = client.GetAsync(url).GetAwaiter().GetResult()
        response.EnsureSuccessStatusCode() |> ignore
        use file = File.Create zip
        response.Content.CopyToAsync(file).GetAwaiter().GetResult()

    let name = Path.GetFileName toolPath
    do
        use zipFile = ZipFile.OpenRead zip
        let entry =
            zipFile.Entries
            |> Seq.tryFind (fun e -> String.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
            |> Option.defaultWith (fun () -> failwithf "%s did not contain %s" archive name)
        entry.ExtractToFile(toolPath, true)
    File.Delete zip

    if not (OperatingSystem.IsWindows()) then
        File.SetUnixFileMode(
            toolPath,
            UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute
            ||| UnixFileMode.GroupRead ||| UnixFileMode.GroupExecute
            ||| UnixFileMode.OtherRead ||| UnixFileMode.OtherExecute)

    printfn "docs-builder cached at %s" toolPath
    toolPath

// ─────────────────────────────  prefix agreement check  ─────────────────────────────

let checkPrefixesAgree () =
    let expectedBase =
        if PathPrefix = "" then "/" else sprintf "/%s/" PathPrefix

    let landing = File.ReadAllText landingPage
    let m = Text.RegularExpressions.Regex.Match(landing, "<base\\s+href=\"([^\"]*)\"")
    if not m.Success then
        failwithf "%s has no <base href>; its relative links cannot resolve" landingPage
    if m.Groups[1].Value <> expectedBase then
        failwithf
            "%s has <base href=\"%s\"> but the site builds with PathPrefix '%s' (expected \"%s\"). See Documentation.PathPrefix."
            landingPage m.Groups[1].Value PathPrefix expectedBase

    if File.Exists workflow then
        let yaml = File.ReadAllText workflow
        let w = Text.RegularExpressions.Regex.Match(yaml, "prefix:\\s*(\\S+)")
        if PathPrefix = "" && w.Success then
            failwithf
                "%s has a 'prefix: %s' input but Documentation.PathPrefix is empty. Remove the prefix input or set PathPrefix."
                workflow w.Groups[1].Value
        elif PathPrefix <> "" && (not w.Success || w.Groups[1].Value <> PathPrefix) then
            failwithf
                "%s builds with prefix '%s' but Documentation.PathPrefix is '%s'. These must agree."
                workflow (if w.Success then w.Groups[1].Value else "(none)") PathPrefix

// ─────────────────────────────  build  ─────────────────────────────

let build () =
    checkPrefixesAgree ()
    let tool = ensureTool ()

    let args =
        if PathPrefix = "" then ["build"; "--path"; docsSource]
        else ["build"; "--path"; docsSource; "--path-prefix"; PathPrefix]
    exec tool args

    if not (Directory.Exists htmlOutput) then
        failwithf "docs-builder reported success but %s does not exist" htmlOutput

    File.Copy(landingPage, Path.Combine(htmlOutput, "index.html"), true)
    printfn "applied the landing page override -> %s" (Path.Combine(htmlOutput, "index.html"))

// ─────────────────────────────  serve  ─────────────────────────────

let private contentType (path: string) =
    match Path.GetExtension(path).ToLowerInvariant() with
    | ".html" | ".htm" -> "text/html; charset=utf-8"
    | ".css" -> "text/css; charset=utf-8"
    | ".js" | ".mjs" -> "text/javascript; charset=utf-8"
    | ".json" -> "application/json; charset=utf-8"
    | ".svg" -> "image/svg+xml"
    | ".woff2" -> "font/woff2"
    | ".woff" -> "font/woff"
    | ".ttf" -> "font/ttf"
    | ".png" -> "image/png"
    | ".jpg" | ".jpeg" -> "image/jpeg"
    | ".gif" -> "image/gif"
    | ".webp" -> "image/webp"
    | ".avif" -> "image/avif"
    | ".ico" -> "image/x-icon"
    | ".txt" -> "text/plain; charset=utf-8"
    | ".xml" -> "application/xml; charset=utf-8"
    | ".wasm" -> "application/wasm"
    | _ -> "application/octet-stream"

let private write (response: HttpListenerResponse) (path: string) =
    response.ContentType <- contentType path
    let bytes = File.ReadAllBytes path
    response.OutputStream.Write(bytes, 0, bytes.Length)

let private notFound (response: HttpListenerResponse) (raw: string) =
    response.StatusCode <- 404
    response.ContentType <- "text/plain; charset=utf-8"
    let body = Encoding.UTF8.GetBytes(sprintf "404 %s" raw)
    response.OutputStream.Write(body, 0, body.Length)

let private handle (root: string) (context: HttpListenerContext) =
    let response = context.Response
    try
        try
            let raw = Uri.UnescapeDataString context.Request.Url.AbsolutePath

            let relative =
                if PathPrefix = "" then raw.TrimStart('/')
                else
                    let mount = sprintf "/%s" PathPrefix
                    if raw = "/" || raw = "" then
                        response.Redirect(mount + "/")
                        null
                    elif raw = mount then
                        response.Redirect(mount + "/")
                        null
                    elif not (raw.StartsWith(mount + "/", StringComparison.Ordinal)) then
                        notFound response raw
                        null
                    else
                        raw.Substring(mount.Length).TrimStart('/')

            if relative <> null then
                let candidate = Path.GetFullPath(Path.Combine(root, relative))
                if not (candidate.StartsWith(root, StringComparison.Ordinal)) then notFound response raw
                elif File.Exists candidate then write response candidate
                elif Directory.Exists candidate then
                    if not (raw.EndsWith "/") then response.Redirect(raw + "/")
                    else
                        let index = Path.Combine(candidate, "index.html")
                        if File.Exists index then write response index else notFound response raw
                else notFound response raw
        with e ->
            response.StatusCode <- 500
            let body = Encoding.UTF8.GetBytes e.Message
            response.OutputStream.Write(body, 0, body.Length)
    finally
        response.OutputStream.Close()

let serve (port: int) =
    let root = Path.GetFullPath htmlOutput
    let url =
        if PathPrefix = "" then sprintf "http://localhost:%d/" port
        else sprintf "http://localhost:%d/%s/" port PathPrefix

    let listener = new HttpListener()
    listener.Prefixes.Add(sprintf "http://localhost:%d/" port)
    try listener.Start()
    with :? HttpListenerException ->
        failwithf "could not listen on port %d — it is probably already in use. Pass --port <n>." port

    printfn ""
    printfn "  documentation serving at %s" url
    printfn "  ctrl-c to stop; re-run './build.sh docs' to pick up edits"
    printfn ""

    let headless =
        [ "CI"; "TF_BUILD"; "GITHUB_ACTIONS" ]
        |> List.exists (fun v -> not (String.IsNullOrEmpty(Environment.GetEnvironmentVariable v)))
    if not headless then
        try
            let opener, args =
                if OperatingSystem.IsMacOS() then "open", url
                elif OperatingSystem.IsWindows() then "cmd", sprintf "/c start %s" url
                else "xdg-open", url
            Diagnostics.ProcessStartInfo(opener, Arguments = args, UseShellExecute = false)
            |> Diagnostics.Process.Start
            |> ignore
        with _ -> ()

    let mutable running = true
    Console.CancelKeyPress.Add(fun e ->
        e.Cancel <- true
        running <- false
        listener.Stop())

    while running do
        try
            let context = listener.GetContext()
            Task.Run(fun () -> handle root context) |> ignore
        with
        | :? HttpListenerException -> ()
        | :? ObjectDisposedException -> ()

    (listener :> IDisposable).Dispose()
    printfn "stopped"
