module CommandLine

open Argu
open Microsoft.FSharp.Reflection

type Arguments =
    | [<CliPrefix(CliPrefix.None);SubCommand>] Clean
    | [<CliPrefix(CliPrefix.None);SubCommand>] Build
    | [<CliPrefix(CliPrefix.None);SubCommand>] Test

    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] GeneratePackages
    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] ValidatePackages
    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] GenerateApiChanges

    | [<CliPrefix(CliPrefix.None);SubCommand>] Docs

    | [<Inherit;AltCommandLine("-s")>] SingleTarget of bool
    | [<Inherit>] Port of int
with
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Clean -> "clean known output locations"
            | Build -> "Run build"
            | Test -> "Runs build then tests"
            | Docs -> "Build documentation and serve locally for preview"

            | SingleTarget _ -> "Runs the provided sub command without running their dependencies"
            | Port _ -> "Port to use when serving documentation (default: 8000)"

            | GeneratePackages
            | ValidatePackages
            | GenerateApiChanges
                -> "Undocumented, dependent target"
    member this.Name =
        match FSharpValue.GetUnionFields(this, typeof<Arguments>) with
        | case, _ -> case.Name.ToLowerInvariant()
