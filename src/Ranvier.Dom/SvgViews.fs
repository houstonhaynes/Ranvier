namespace Ranvier.Dom

/// <summary>An actual SVG view value or the provider's reason that no typed value can be returned.</summary>
[<RequireQualifiedAccess>]
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type SvgViewAvailability<'T> =
    | Available of 'T
    /// <summary>The provider does not expose the requested DOM member.</summary>
    | NotExposed
    /// <summary>The exposed member reports that its operation is unsupported.</summary>
    | Unsupported of reason: string
    /// <summary>The exposed member returned no value with the required DOM type.</summary>
    | Unknown of reason: string

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
        NativeDisplay.union context depth precedence (box this) (fun nested ->
            match this with
            | SvgViewAvailability.Available (caseValue0) ->
                "Available",
                [
                    Some (fun _ ->
                        (match box (caseValue0) with
                         | :? bool as v -> NativeDisplay.boolean context nested v
                         | :? string as v -> NativeDisplay.text context nested v
                         | :? int as v -> NativeDisplay.int32 context nested v
                         | other -> NativeDisplay.opaque context nested "<generic>" other))
                ]
            | SvgViewAvailability.NotExposed -> "NotExposed", []
            | SvgViewAvailability.Unsupported (caseValue0) -> "Unsupported", [ Some (fun _ -> NativeDisplay.text context nested (caseValue0)) ]
            | SvgViewAvailability.Unknown (caseValue0) -> "Unknown", [ Some (fun _ -> NativeDisplay.text context nested (caseValue0)) ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>The observed current-view properties of one SVG root.</summary>
/// <remarks>An available view remains the provider's live DOM object; requested attributes do not establish applied view state.</remarks>
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type SvgViewState =
    {
        CurrentView: SvgViewAvailability<SVGViewSpec>
        UseCurrentView: SvgViewAvailability<bool>
    }

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, _precedence: int) =
        NativeDisplay.record context depth (box this) (fun nested ->
            [
                "CurrentView",
                (fun () ->
                    NativeDisplay.known context nested (this.CurrentView) (fun (retained: SvgViewAvailability<SVGViewSpec>) ->
                        retained.NativeDisplay (context, nested, 3)))
                "UseCurrentView",
                (fun () ->
                    NativeDisplay.known context nested (this.UseCurrentView) (fun (retained: SvgViewAvailability<System.Boolean>) ->
                        retained.NativeDisplay (context, nested, 3)))
            ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif
