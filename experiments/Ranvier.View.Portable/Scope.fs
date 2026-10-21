namespace Ranvier.View.Foundation

open Ranvier

/// <summary>The receiver of the pending reads and failures of the bindings, rows and handlers mounted under it.</summary>
type ViewScope =
    /// <summary>True for a boundary: its bindings report pending reads through <c>Wait</c> and <c>Ready</c>.</summary>
    abstract Tracks: bool
    /// <summary>Records a binding whose read is pending.</summary>
    abstract Wait: unit -> unit
    /// <summary>
    /// Records a new binding as waiting until its first read, while the scope's content is hidden. Returns true when
    /// recorded; <c>Ready</c> releases it.
    /// </summary>
    abstract Expect: unit -> bool
    /// <summary>Records that a waiting binding read a value or was disposed.</summary>
    abstract Ready: unit -> unit
    /// <summary>Takes failures in the order they occurred; a pending read is dropped.</summary>
    abstract Fail: exn list -> unit
    /// <summary>Takes failures in the order they occurred, a pending read included.</summary>
    abstract FailAll: exn list -> unit

/// <summary>The bits of a binding's state word, and its transitions.</summary>
module BindingState =
    /// <summary>The binding's last read raised an exception other than a pending read.</summary>
    [<Literal>]
    let internal ReadFailed = 1

    /// <summary>The binding's last read was pending and the binding is registered with its scope.</summary>
    [<Literal>]
    let Waiting = 2

    /// <summary>
    /// The state word of a new binding: <c>Waiting</c> when a tracking scope records it as waiting for its first read,
    /// 0 otherwise. A <c>Waiting</c> word hides the scope's content until <c>ready</c> releases it.
    /// </summary>
    let started (scope: ViewScope) : int =
        if scope.Tracks && scope.Expect () then Waiting else 0

    /// <summary>
    /// The state word after a read raised <c>error</c>. A pending read sets <c>Waiting</c> and registers the binding
    /// with a tracking scope, once until <c>ready</c> releases it; any other error sets <c>ReadFailed</c>. Store the
    /// word, then call <c>report</c>.
    /// </summary>
    let rejected (scope: ViewScope) (state: int) (error: exn) : int =
        match error with
        | NotReadyException _ ->
            if state &&& Waiting = 0 && scope.Tracks then
                scope.Wait ()
                state ||| Waiting
            else
                state
        | _ -> state ||| ReadFailed

    /// <summary>Sends a read error to the scope; a pending read is dropped.</summary>
    let report (scope: ViewScope) (error: exn) =
        match error with
        | NotReadyException _ -> ()
        | _ -> scope.Fail [ error ]

    /// <summary>The state word after a successful read, with <c>ReadFailed</c> cleared; <c>clear</c> runs when it was set.</summary>
    let recovered (clear: unit -> unit) (state: int) : int =
        if state &&& ReadFailed <> 0 then
            clear ()
            state &&& ~~~ReadFailed
        else
            state

    /// <summary>The state word with <c>Waiting</c> cleared. Store it, then call <c>ready</c> with the word before.</summary>
    let released (state: int) : int =
        state &&& ~~~Waiting

    /// <summary>
    /// Releases a binding whose word before <c>released</c> was <c>Waiting</c> from its scope. Call after the value
    /// read is committed, and when the binding's owner is disposed. Call once per <c>released</c>; a second call with the
    /// same word releases the scope twice.
    /// </summary>
    let ready (scope: ViewScope) (previous: int) =
        if previous &&& Waiting <> 0 then
            scope.Ready ()
