module Paths

open System
open System.IO

let ToolName = "Mermaid"
let Repository = "nullean/mermaider"
/// The framework the public API diff reads from the previous release and from this build: the oldest the libraries
/// target, which every release ships
let MainTFM = "net10.0"
let SignKey = "84cc9c998b56df09"

let ValidateAssemblyName = true
let IncludeGitHashInInformational = true
let GenerateApiChanges = true

let Root =
    let mutable dir = DirectoryInfo(".")
    while dir.GetFiles("*.slnx").Length = 0 do dir <- dir.Parent
    Environment.CurrentDirectory <- dir.FullName
    dir

let RootRelative path = Path.GetRelativePath(Root.FullName, path)

/// The build output the API diff reads: a library's MainTFM folder, or the one folder of a project that targets a single
/// framework (a dotnet tool, net11.0 alone)
let ApiDiffOutput (projectDirectory: string) =
    let library = $".artifacts/bin/{projectDirectory}/release_{MainTFM}"
    if Directory.Exists library then library else $".artifacts/bin/{projectDirectory}/release"

let Output = DirectoryInfo(Path.Combine(Root.FullName, "build", "output"))

let ToolProject = DirectoryInfo(Path.Combine(Root.FullName, "src", ToolName))
