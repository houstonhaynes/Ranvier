namespace Ranvier.View.Arms

open System

#if !FABLE_COMPILER
/// <summary>A pattern variable read outside a reader of the instance, raised when a .NET site is built.</summary>
type BuildTimeReadException(variable: string, message: string) =
    inherit InvalidOperationException(message)

    /// <summary>The pattern variable, or the union field when the compiler removed the binding.</summary>
    member _.Variable = variable

/// <summary>A site rejected by the .NET lowering: AM003, AM004, AM006 or AM007.</summary>
type ArmsMatchException(message: string) =
    inherit InvalidOperationException(message)
#endif

module internal Errors =
    /// <summary>AM006; a Release Fable build raises the short text.</summary>
    let noArm () : 'a =
#if FABLE_COMPILER
#if DEBUG
        raise (InvalidOperationException Diagnostics.noArm)
#else
        raise (InvalidOperationException Diagnostics.noArmCode)
#endif
#else
        raise (ArmsMatchException Diagnostics.noArm)
#endif

    /// <summary>AM005.</summary>
    let noPlugin () : 'a =
        raise (InvalidOperationException Diagnostics.noPlugin)
