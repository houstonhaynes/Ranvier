module Ranvier.Benchmarks.Config

open System
open System.IO
open BenchmarkDotNet.Columns
open BenchmarkDotNet.Configs
open BenchmarkDotNet.Diagnosers
open BenchmarkDotNet.Exporters
open BenchmarkDotNet.Exporters.Json
open BenchmarkDotNet.Jobs
open BenchmarkDotNet.Loggers
open BenchmarkDotNet.Reports

/// <summary>
/// Every benchmark here measures one primitive in isolation, so that two
/// implementations of the same primitive can be compared directly. That only
/// works if the runs themselves are comparable — same job, same columns, same
/// exporters — which is what this config is for.
/// </summary>
/// <remarks>
/// Read the numbers as comparisons, never as absolutes. A signal write is a
/// few nanoseconds; the machine, the power profile and whatever else is
/// running move that by more than most changes worth making. The question a
/// result answers is "did this get better than the last run on this machine",
/// not "how fast is it".
/// </remarks>
[<Literal>]
let private ResultsDirectory = "docs/.ai/benchmarks"

/// <summary>
/// Results land in the repository, not beside the binary, so a run can be
/// diffed against the last one. Resolved by walking up from the assembly
/// rather than from the working directory, so <c>dotnet run</c> from anywhere puts
/// them in the same place.
/// </summary>
let private artifactsPath =
    let rec findRoot (dir: DirectoryInfo) =
        if isNull dir then
            None
        elif dir.GetFiles "*.slnx" |> Array.isEmpty |> not then
            Some dir
        else
            findRoot dir.Parent

    match findRoot (DirectoryInfo AppContext.BaseDirectory) with
    | Some root -> Path.Combine (root.FullName, ResultsDirectory)
    | None -> Path.Combine (AppContext.BaseDirectory, ResultsDirectory)

let private withCommonSettings (config: ManualConfig) =
    config
        .AddColumnProvider(DefaultColumnProviders.Instance)
        .AddColumn(CategoriesColumn.Default)
        .AddColumn(StatisticColumn.OperationsPerSecond)
        .AddDiagnoser(MemoryDiagnoser.Default)
        .AddExporter(MarkdownExporter.GitHub)
        .AddExporter(JsonExporter.FullCompressed)
        .AddLogger(ConsoleLogger.Default)
        .WithSummaryStyle(SummaryStyle.Default.WithRatioStyle RatioStyle.Trend)
        .WithArtifactsPath
        artifactsPath

// These primitives are small enough that the loop overhead is a visible part
// of the measurement, so each measured call is repeated in an unrolled loop.
let private unroll = 16

let private runShape (isShort: bool) =
    (if isShort then Job.ShortRun else Job.Default).WithUnrollFactor unroll

/// <summary>
/// The publishable run. Minutes, not seconds.
/// </summary>
let full =
    ManualConfig.CreateEmpty ()
    |> withCommonSettings
    |> fun config -> config.AddJob ((runShape false).WithId("Default").AsDefault())

/// <summary>
/// A smoke run: enough to prove the harness works and to catch an
/// order-of-magnitude regression, not enough to publish.
/// </summary>
let short =
    ManualConfig.CreateEmpty ()
    |> withCommonSettings
    |> fun config -> config.AddJob ((runShape true).WithId("Short").AsDefault())

/// <summary>
/// Two jobs that build the same benchmark source with <c>RanvierTrace=false</c> (<c>Untraced</c>, the ratio
/// baseline) and <c>RanvierTrace=true</c> (<c>Traced</c>), with the run shape of <c>short</c> when
/// <c>isShort</c> and of <c>full</c> otherwise. The jobs replace the default job of <c>full</c> and <c>short</c>.
/// </summary>
let traced (isShort: bool) =
    let job (trace: bool) =
        let value = if trace then "true" else "false"
        (runShape isShort).WithArguments [| MsBuildArgument $"/p:RanvierTrace={value}" :> Argument |]

    ManualConfig.CreateEmpty().AddJob(job(false).WithId("Untraced").AsBaseline()).AddJob(job(true).WithId "Traced")

/// <summary>Runs the benchmarks of a class under the <c>traced</c> jobs, short when the process has <c>--short</c>.</summary>
[<AttributeUsage(AttributeTargets.Class)>]
type TracedConfigAttribute() =
    inherit Attribute()

    interface IConfigSource with
        member _.Config =
            traced (
                Environment.GetCommandLineArgs ()
                |> Array.contains "--short"
            )

/// <summary>
/// The <c>Traced</c> job of <c>traced</c> alone, for benchmarks of operations that exist only in a traced build.
/// </summary>
let tracedOnly (isShort: bool) =
    ManualConfig.CreateEmpty().AddJob((runShape isShort).WithArguments([| MsBuildArgument "/p:RanvierTrace=true" :> Argument |]).WithId "Traced")

/// <summary>Runs the benchmarks of a class under the <c>tracedOnly</c> job, short when the process has <c>--short</c>.</summary>
[<AttributeUsage(AttributeTargets.Class)>]
type TracedOnlyConfigAttribute() =
    inherit Attribute()

    interface IConfigSource with
        member _.Config =
            tracedOnly (
                Environment.GetCommandLineArgs ()
                |> Array.contains "--short"
            )
