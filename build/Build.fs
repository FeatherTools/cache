open Alma.Build
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "Feather.Cache"
            Summary = "Library for F# cache wrappers."
            Git = Git.init ()
        }
        Specs =
            Spec.defaultLibrary
            |> Spec.mapLibrary (fun library -> { library with NugetApi = NugetApi.KeyInEnvironment "NUGET_API_KEY" })
    }

    args |> Args.run
