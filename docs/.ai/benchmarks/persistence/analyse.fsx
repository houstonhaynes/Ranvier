// Paired head/base ratios per case: allocations from the workers' JSON, Stopwatch ns/op from the probe.
open System.IO
open System.Text.Json

let dir = Path.Combine (__SOURCE_DIRECTORY__, "runs")
let rounds = 5

let median (xs: float list) =
    let s = List.sort xs
    let n = s.Length

    if n % 2 = 1 then
        s[n / 2]
    else
        (s[n / 2 - 1] + s[n / 2]) / 2.0

let bytes side i =
    use json =
        JsonDocument.Parse (File.ReadAllText (Path.Combine (dir, $"%s{side}-%d{i}.json")))

    [
        for c in json.RootElement.GetProperty("Cases").EnumerateArray() do
            let ms = c.GetProperty ("Measurements")
            let n = ms[0].GetProperty("Ops").GetInt32()
            let b1 = ms[0].GetProperty("Bytes").GetInt64()
            let b2 = ms[1].GetProperty("Bytes").GetInt64()

            (c.GetProperty("Scenario").GetString()
             + " | "
             + c.GetProperty("Engine").GetString()),
            float (b2 - b1) / float n
    ]
    |> Map.ofList

let timing side i =
    File.ReadAllLines (Path.Combine (dir, $"%s{side}-%d{i}.txt"))
    |> Array.choose (fun line ->
        match line.Split '|' with
        | [| s; e; v |] -> Some (s + " | " + e, float v)
        | _ -> None)
    |> Map.ofArray

let report title (read: string -> int -> Map<string, float>) =
    printfn "\n## %s\n" title
    printfn "| Case | base (median) | head (median) | head/base median | paired range |"
    printfn "|---|---:|---:|---:|---:|"
    let bases = List.init rounds (read "base")
    let heads = List.init rounds (read "head")

    for key in heads[0].Keys do
        let h = heads |> List.map (fun m -> m[key])

        match bases[0].TryFind key with
        | None -> printfn "| %s | (absent) | %.1f | added | |" key (median h)
        | Some _ ->
            let b = bases |> List.map (fun m -> m[key])

            let ratios =
                List.zip b h
                |> List.choose (fun (x, y) ->
                    if x = 0.0 then
                        (if y = 0.0 then Some 1.0 else None)
                    else
                        Some (y / x))

            let range =
                if ratios.IsEmpty then
                    "n/a"
                else
                    $"%.3f{List.min ratios}..%.3f{List.max ratios}"

            let mid = if ratios.IsEmpty then "n/a" else $"%.3f{median ratios}"
            printfn "| %s | %.1f | %.1f | %s | %s |" key (median b) (median h) mid range

report "Allocated bytes per operation" bytes
report "Stopwatch ns per operation" timing
