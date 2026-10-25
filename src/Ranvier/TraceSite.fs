namespace Ranvier

/// <summary>Creation sites for the trace log.</summary>
module internal TraceSite =
#if FABLE_COMPILER
    /// <summary>The creation site of the calling frame; null under Fable.</summary>
    let capture () : string = null
#else
    open System
    open System.Collections.Concurrent
    open System.Diagnostics
    open System.IO
    open System.Reflection

    let private library = typeof<TraceEvent>.Assembly

    let private libraries = ConcurrentDictionary<Assembly, bool>()

    /// <summary>True for the core and for an assembly carrying <c>AssemblyMetadata("RanvierLibrary", "true")</c>.</summary>
    let private isLibrary (assembly: Assembly) =
        match libraries.TryGetValue assembly with
        | true, known -> known
        | _ ->
            let marked =
                Object.ReferenceEquals (assembly, library)
                || assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                   |> Seq.exists (fun a -> a.Key = "RanvierLibrary" && a.Value = "true")

            libraries[assembly] <- marked
            marked

    /// <summary>The frame's assembly name, <c>"Ranvier"</c> for a Ranvier library assembly, or null.</summary>
#if NET5_0_OR_GREATER
    [<CodeAnalysis.UnconditionalSuppressMessage("Trimming",
                                                "IL2026",
                                                Justification = "A trimmed method yields null from GetMethod, which reads as no assembly.")>]
#endif
    let private assemblyOf (frame: StackFrame) =
        match frame.GetMethod () with
        | null -> null
        | m when Platform.isNull m.DeclaringType -> null
        | m when isLibrary m.DeclaringType.Assembly -> "Ranvier"
        | m -> m.DeclaringType.Assembly.GetName().Name

    let private excluded (name: string) =
        Platform.isNull name
        || name = "Ranvier"
        || name = "FSharp.Core"
        || name = "System"
        || name.StartsWith "System."

    /// <summary>True for a source file under <c>src/Ranvier/</c>, <c>src/Ranvier.*/</c> or <c>src/Fable.Ranvier*/</c>.</summary>
    let private inLibrarySource (file: string) =
        let file = file.Replace (Path.DirectorySeparatorChar, '/')

        file.Contains "/src/Ranvier/"
        || file.Contains "/src/Ranvier."
        || file.Contains "/src/Fable.Ranvier"

    /// <summary>
    /// The <c>file:line</c> of the innermost frame with file info outside the Ranvier libraries (the core and every
    /// <c>RanvierLibrary</c> assembly), FSharp.Core and <c>System.*</c>, and outside the libraries' sources under
    /// <c>src/</c>. An FSI submission frame gives the script's <c>file:line</c>, or <c>stdin:line</c>, with line 0 when
    /// the submission carries no line info. <c>"?"</c> when no frame qualifies or the capture fails.
    /// </summary>
    let capture () : string =
        try
            let frames = StackTrace(1, true).GetFrames()
            let mutable site = "?"
            let mutable i = 0

            while i < frames.Length do
                let frame = frames[i]
                let file = frame.GetFileName ()
                let name = assemblyOf frame

                if
                    not (Platform.isNull name)
                    && name.StartsWith "FSI-ASSEMBLY"
                then
                    let script =
                        if String.IsNullOrEmpty file then
                            "stdin"
                        else
                            Path.GetFileName file

                    site <- String.Concat (script, ":", string (frame.GetFileLineNumber ()))
                    i <- frames.Length
                elif
                    not (String.IsNullOrEmpty file)
                    && not (excluded name)
                    && not (inLibrarySource file)
                then
                    site <- String.Concat (Path.GetFileName file, ":", string (frame.GetFileLineNumber ()))
                    i <- frames.Length
                else
                    i <- i + 1

            site
        with _ ->
            "?"
#endif
