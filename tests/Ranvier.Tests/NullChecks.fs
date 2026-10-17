module Ranvier.Tests.NullChecks

open Expecto
open Ranvier

#if FABLE_COMPILER
[<Fable.Core.Emit("undefined")>]
let private undefinedValue: obj = Fable.Core.Util.jsNative

[<AllowNullLiteral>]
type private Holder() =
    member val Next: Holder = null with get, set
#endif

/// <summary>
/// Tests of the Fable implementation of <c>Platform.isNull</c>. The .NET implementation is FSharp.Core's <c>isNull</c>.
/// </summary>
[<Tests>]
let tests =
    testList
        "Platform.isNull"
        [
#if FABLE_COMPILER
            test "is true for null" {
                Expect.isTrue (Platform.isNull (null: obj)) "null object"
                Expect.isTrue (Platform.isNull (null: string)) "null string"
                Expect.isTrue (Platform.isNull (Holder().Next)) "unset field"
            }

            test "is false for a value" {
                Expect.isFalse (Platform.isNull (box 0)) "boxed zero"
                Expect.isFalse (Platform.isNull "") "empty string"
                Expect.isFalse (Platform.isNull (box false)) "boxed false"
                Expect.isFalse (Platform.isNull (Holder ())) "object"
                Expect.isFalse (Platform.isNull (Array.empty<int>)) "empty array"
            }

            test "is true for undefined, as isNull is" {
                Expect.isTrue (isNull undefinedValue) "FSharp.Core isNull"
                Expect.isTrue (Platform.isNull undefinedValue) "Platform.isNull"
            }
#endif
        ]
