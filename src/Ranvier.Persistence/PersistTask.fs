namespace Ranvier.Persistence

open System.Threading.Tasks
#if FABLE_COMPILER
open Fable.Core
#endif

/// <summary>Task operations with one meaning on .NET and under Fable, where a <c>Task</c> is a <c>Promise</c>.</summary>
module internal PersistTask =
    /// <summary>A finished task.</summary>
    let completed () : Task =
#if FABLE_COMPILER
        Task.FromResult () :> Task
#else
        Task.CompletedTask
#endif

#if FABLE_COMPILER
    /// <summary>The task of <c>f</c> applied to the result of <c>work</c>; a raise in <c>f</c> faults it.</summary>
    [<Emit("$0.then($1)")>]
    let map (work: Task<'T>) (f: 'T -> 'U) : Task<'U> = jsNative

    /// <summary>The task <c>next</c> returns, started once <c>tail</c> has completed in any state.</summary>
    [<Emit("$0.then($1, $1)")>]
    let after (tail: Task) (next: unit -> Task) : Task = jsNative
#else
    /// <summary>The task of <c>f</c> applied to the result of <c>work</c>; a raise in <c>f</c> faults it.</summary>
    let map (work: Task<'T>) (f: 'T -> 'U) : Task<'U> =
        task {
            let! value = work.ConfigureAwait false
            return f value
        }

    /// <summary>The task <c>next</c> returns, started once <c>tail</c> has completed in any state.</summary>
    let after (tail: Task) (next: unit -> Task) : Task =
        task {
            try
                do! tail.ConfigureAwait false
            with _ ->
                ()

            do! (next ()).ConfigureAwait false
        }
        :> Task
#endif
