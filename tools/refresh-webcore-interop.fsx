// Copies the managed interop of Ranvier.WebCore into src/Ranvier.View.WebCore/Interop: the P/Invoke declarations
// (tests/abi/Native.fs and NativeDom.fs) and the headers they are generated from, which the wrapper generator reads.
//
//   dotnet fsi tools/refresh-webcore-interop.fsx -- refresh [<Ranvier.WebCore checkout>]
//
// The checkout defaults to RANVIER_WEBCORE_REPO, else ../Ranvier.WebCore beside this repository. After a refresh, run
// the generator (tools/Ranvier.View.WebCore.Generate) and commit both.

open System
open System.IO

let repository = Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, ".."))

let target = Path.Combine (repository, "src", "Ranvier.View.WebCore", "Interop")

let files =
    [
        "tests/abi/Native.fs", "Native.fs"
        "tests/abi/NativeDom.fs", "NativeDom.fs"
        "host/RanvierWebCoreAbi.h", "RanvierWebCoreAbi.h"
        "host/RanvierWebCoreDom.h", "RanvierWebCoreDom.h"
    ]

let defaultSource () =
    match Environment.GetEnvironmentVariable "RANVIER_WEBCORE_REPO" with
    | null
    | "" -> Path.Combine (repository, "..", "Ranvier.WebCore")
    | path -> path

let refresh (source: string) =
    Directory.CreateDirectory target |> ignore

    for from, name in files do
        let path = Path.Combine (source, from)

        if not (File.Exists path) then
            failwith $"{path} does not exist; pass the Ranvier.WebCore checkout after 'refresh'"

        File.Copy (path, Path.Combine (target, name), true)
        printfn $"{from} -> src/Ranvier.View.WebCore/Interop/{name}"

match
    fsi.CommandLineArgs
    |> Array.skip 1
    |> Array.filter (fun a -> a <> "--")
with
| [| "refresh" |] -> refresh (defaultSource ())
| [| "refresh"; source |] -> refresh source
| _ -> printfn "usage: dotnet fsi tools/refresh-webcore-interop.fsx -- refresh [<Ranvier.WebCore checkout>]"
