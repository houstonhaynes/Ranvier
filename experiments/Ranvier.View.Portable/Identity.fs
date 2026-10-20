namespace Ranvier.View.Foundation

open System

/// <summary>The kinds of step from an instance's path to the path of an instance one level below it.</summary>
module InstanceStep =
    /// <summary>The root instance of a render or a mount; the step ignores its parent.</summary>
    [<Literal>]
    let Root = 0

    /// <summary>An inlined child, by its index in the definition's child list.</summary>
    [<Literal>]
    let Child = 1

    /// <summary>A region row, by the region's index among its part's regions and the row's creation index.</summary>
    [<Literal>]
    let Row = 2

    /// <summary>A template reached from authored content, by its index among the templates under the same path.</summary>
    [<Literal>]
    let Authored = 3

/// <summary>
/// The positional path of a component instance: the steps from the root instance, written as text such as
/// <c>0.c1.r0.3</c>. The path depends on definition structure and row order only.
/// </summary>
[<Sealed; AllowNullLiteral>]
type InstancePath private (parent: InstancePath, step: string, a: int, b: int) =
    let mutable text: string = null

    static member val Root = InstancePath (null, null, 0, 0)

    member this.Child(k: int) =
        InstancePath (this, "c", k, -1)

    member this.Row(region: int, row: int) =
        InstancePath (this, "r", region, row)

    member this.Authored(k: int) =
        InstancePath (this, "a", k, -1)

    /// <summary>The path as text, built on the first read.</summary>
    member _.Text: string =
        if isNull text then
            text <-
                if isNull parent then
                    "0"
                elif b < 0 then
                    parent.Text + "." + step + string a
                else
                    parent.Text
                    + "."
                    + step
                    + string a
                    + "."
                    + string b

        text

    /// <summary>The path one step of <c>kind</c> below <c>parent</c>; see <c>InstanceStep</c> for the arguments.</summary>
    static member internal Below(parent: InstancePath, kind: int, a: int, b: int) : InstancePath =
        match kind with
        | InstanceStep.Root -> InstancePath.Root
        | InstanceStep.Child -> parent.Child a
        | InstanceStep.Row -> parent.Row (a, b)
        | _ -> parent.Authored a

/// <summary>The window of an enclosing setup, held while a setup nested inside it runs.</summary>
[<NoEquality; NoComparison>]
type internal IdentityWindow =
    {
        Parent: InstancePath
        Kind: int
        A: int
        B: int
        Path: InstancePath
        Issued: int
        Window: int
    }

/// <summary>
/// The ids of one render or one mount. A template instance's setup runs inside a window opened by <c>Enter</c>, and its
/// <c>CreateId</c> calls return <c>prefix-path-n</c>, <c>n</c> counting from 0 within the window.
/// </summary>
[<Sealed>]
type IdentityScope(prefix: string) =
    let mutable opened = false
    let mutable windowParent: InstancePath = null
    let mutable windowKind = 0
    let mutable windowA = 0
    let mutable windowB = 0
    // The open window's path, null until its first request.
    let mutable path: InstancePath = null
    let mutable issued = 0
    // The open window's number, unique within the scope; 0 outside every window.
    let mutable window = 0
    let mutable windowCount = 0
    // The windows enclosing the open one, innermost last; null until a window opens inside another.
    let mutable enclosing: ResizeArray<IdentityWindow> = null
    let mutable authored = 0

    let notInSetup () =
        InvalidOperationException "CreateId is valid only while a component's setup runs"

    member _.Prefix = prefix

    /// <summary>The open window's number, unique within the scope; 0 outside every window.</summary>
    member _.Window = window

    /// <summary>
    /// Opens the setup window of the instance one step of <c>kind</c> below <c>parent</c>: <c>InstanceStep.Root</c>
    /// ignores <c>parent</c>, <c>Child</c> and <c>Authored</c> take their index from <c>a</c>, and <c>Row</c> takes the
    /// region from <c>a</c> and the row from <c>b</c>.
    /// </summary>
    member _.Enter(parent: InstancePath, kind: int, a: int, b: int) : unit =
        if opened then
            if isNull enclosing then
                enclosing <- ResizeArray ()

            enclosing.Add
                {
                    Parent = windowParent
                    Kind = windowKind
                    A = windowA
                    B = windowB
                    Path = path
                    Issued = issued
                    Window = window
                }

        windowCount <- windowCount + 1
        window <- windowCount
        opened <- true
        windowParent <- parent
        windowKind <- kind
        windowA <- a
        windowB <- b
        path <- null
        issued <- 0

    /// <summary>Closes the open window and reopens the window it was entered in, if any.</summary>
    member _.Leave() : unit =
        if not (isNull enclosing) && enclosing.Count > 0 then
            let outer = enclosing[enclosing.Count - 1]
            enclosing.RemoveAt (enclosing.Count - 1)
            windowParent <- outer.Parent
            windowKind <- outer.Kind
            windowA <- outer.A
            windowB <- outer.B
            path <- outer.Path
            issued <- outer.Issued
            window <- outer.Window
        else
            opened <- false
            windowParent <- null
            path <- null
            window <- 0

    /// <summary>The open window's path, allocated on the first call within the window.</summary>
    /// <exception cref="T:System.InvalidOperationException">No setup window is open.</exception>
    member _.Path() : InstancePath =
        if not opened then
            raise (InvalidOperationException "An instance path is available only while a component's setup runs")

        if isNull path then
            path <- InstancePath.Below (windowParent, windowKind, windowA, windowB)

        path

    /// <summary><c>prefix-path-n</c> for the instance whose setup runs, <c>n</c> counting from 0 within its window.</summary>
    /// <exception cref="T:System.InvalidOperationException">No setup window is open.</exception>
    member this.CreateId() : string =
        if not opened then
            raise (notInSetup ())

        let text = this.Path().Text
        let n = issued
        issued <- n + 1
        prefix + "-" + text + "-" + string n

    /// <summary>
    /// <c>CreateId</c> for the instance whose setup opened window <c>owner</c>, a <c>Window</c> value read inside that
    /// setup.
    /// </summary>
    /// <exception cref="T:System.InvalidOperationException"><c>owner</c> is not the open window.</exception>
    member this.CreateIdIn(owner: int) : string =
        if owner = 0 || owner <> window then
            raise (notInSetup ())

        this.CreateId ()

    /// <summary><c>prefix-path-an</c> for an authored component under <c>path</c>, <c>n</c> counting across the scope.</summary>
    member _.CreateAuthoredId(path: InstancePath) : string =
        let n = authored
        authored <- n + 1
        prefix + "-" + path.Text + "-a" + string n
