module Versions

open Proc.Fs

/// The version MinVer computes from the tags, which is the version the packages get
let current =
    lazy (shell {
        sh $"dotnet tool restore"
        let! version = text $"dotnet minver -p canary.0 -m 0.1"
        return version
    })

/// The informational version the package validator expects: the version, plus the commit it was built from
let informational =
    lazy (shell {
        if Paths.IncludeGitHashInInformational then
            let! sha = text $"git rev-parse HEAD"
            return $"{current.Value}+{sha}"
        else
            return current.Value
    })
