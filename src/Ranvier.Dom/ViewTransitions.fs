namespace Ranvier.Dom

open System

/// <summary>The promise whose settlement a same-document transition reports.</summary>
[<RequireQualifiedAccess>]
type ViewTransitionPromise =
    | UpdateCallbackDone
    | Ready
    | Finished

/// <summary>A transition promise's fulfillment, or its rejection's WebIDL exception name.</summary>
type ViewTransitionSettlement =
    {
        Promise: ViewTransitionPromise
        ErrorName: string option
    }

/// <summary>The transition's mutable type names, in insertion order.</summary>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type ViewTransitionTypes =
    /// <summary>The number of distinct type names.</summary>
    abstract Count: uint32
    /// <summary>Whether the type name belongs to the transition.</summary>
    abstract Has: value: string -> bool
    /// <summary>A snapshot of the type names in insertion order.</summary>
    abstract Values: unit -> string array
    /// <summary>Adds a type name; an existing name keeps its position.</summary>
    abstract Add: value: string -> unit
    /// <summary>Removes a type name and returns whether it was present.</summary>
    abstract Delete: value: string -> bool
    /// <summary>Removes every type name.</summary>
    abstract Clear: unit -> unit

/// <summary>An owned same-document transition; disposal skips it and stops application callbacks and observations.</summary>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type ViewTransitionRegistration =
    inherit IDisposable
    /// <summary>The engine transition, including skipTransition and its type-set handle.</summary>
    abstract Transition: ViewTransition
    /// <summary>The transition's live type names.</summary>
    abstract Types: ViewTransitionTypes
    /// <summary>Reports each promise once, including promises settled before registration; disposal stops delivery.</summary>
    abstract Observe: onSettled: (ViewTransitionSettlement -> unit) -> IDisposable

/// <summary>Starts same-document snapshot transitions in a borrowed document; disposal revokes its registrations.</summary>
/// <remarks>Update runs after old-state capture; an exception rejects the update with TypeError. Native delivery requires
/// the owning thread and host run loop. A native slim package raises NotSupportedException before starting a transition.</remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type ViewTransitionService =
    inherit IDisposable
    /// <summary>Starts a transition with an optional deferred update and initial type names.</summary>
    abstract Start: ?update: (unit -> unit) * ?types: string array -> ViewTransitionRegistration
