module Ranvier.View.Dom.Binding

open Ranvier.View
open Ranvier
open Ranvier.View.Dom.Scheduling

/// <summary>Creates an owned text effect whose updates are committed through the supplied queue.</summary>
/// <remarks>The first settled value is written immediately. A suspended or failed read cancels any older queued write.
/// A failed immediate write after setup goes to <c>scope</c>; a failed queued write goes to the queue, which reports it
/// to <c>scope</c> after the flush. <c>afterCommit</c>, when not null, runs after each commit of a current value, written
/// or equal to the committed one, and when a flush stopped by <c>FailFastCancel</c> cancels that commit. The enclosing
/// owner disposes the effect.</remarks>
let internal startQueuedIn
    (scope: ViewScope)
    (settingUp: unit -> bool)
    registerCleanup
    isLive
    (queue: Queue)
    equals
    read
    write
    (afterCommit: unit -> unit)
    =
    let slot = queue.Reserve scope
    slot.Dropped <- afterCommit
    let mutable first = true
    let mutable version = 0
    let mutable hasCommitted = false
    let mutable committed = Unchecked.defaultof<_>
    registerCleanup (fun () -> queue.Cancel slot)

    Api.createEffect (fun () ->
        version <- version + 1
        let current = version
        queue.Cancel slot
        Api.onCleanup (fun () -> queue.Cancel slot)
        let value = read ()
        let owner = Api.getOwner ()

        let commit () =
            if
                isLive ()
                && not owner.IsDisposed
                && version = current
            then
                Api.runWithOwner owner (fun () ->
                    Api.untrack (fun () ->
                        let changed = not hasCommitted || not (equals committed value)

                        if
                            changed
                            && isLive ()
                            && not owner.IsDisposed
                            && version = current
                        then
                            write value

                            if version = current then
                                committed <- value
                                hasCommitted <- true))

                if not (isNull (box afterCommit)) then
                    afterCommit ()

        if first then
            first <- false

            if isNull (box scope) then
                commit ()
            else
                try
                    commit ()
                with error when not (settingUp ()) ->
                    scope.Fail [ error ]
        else
            queue.Enqueue (slot, commit))

/// <summary>Creates an owned text effect whose updates are committed through the supplied queue, with no scope.</summary>
let internal startQueued registerCleanup isLive (queue: Queue) equals read write =
    startQueuedIn Unchecked.defaultof<_> (fun () -> false) registerCleanup isLive queue equals read write Unchecked.defaultof<_>
