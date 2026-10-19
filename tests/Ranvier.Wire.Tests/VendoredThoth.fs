module Ranvier.Wire.Tests.VendoredThoth

open System
open Expecto
open Ranvier.Wire.Thoth.Json.Core
#if FABLE_COMPILER
open Ranvier.Wire.Thoth.Json.JavaScript
#else
open Ranvier.Wire.Thoth.Json.System.Text.Json
#endif

type Shape =
    | Circle of radius: float
    | Dot

let private shape: Codec<Shape> =
    variantCodec {
        let! circle = Codec.case "circle" Circle Codec.float
        and! dot = Codec.case "dot" (fun () -> Dot) Codec.unit

        return
            function
            | Circle r -> circle r
            | Dot -> dot ()
    }

let private failure (result: Result<'T, string>) =
    match result with
    | Ok _ -> "decoded"
    | Error message -> message

/// <summary>Passes when the decode failed with a BadPrimitiveExtra error naming the expected type and the reason.</summary>
let private expectOutOfRange (expected: string) (reason: string) (text: string) (result: Result<'T, string>) =
    let message = failure result
    Expect.stringContains message ("Expecting " + expected + " but instead got:") text

    Expect.stringContains message ("Reason: " + reason) text

[<Tests>]
let tests =
    testList
        "vendored Thoth.Json"
        [
            testCase "an object with an unknown variant tag is an error"
            <| fun () ->
                let message = failure (Decode.fromString shape """{"hexagon":null}""")
                Expect.stringContains message "Expected exactly one object key" "variant error"

            testCase "an array element error names its index"
            <| fun () ->
                let message = failure (Decode.fromString (Codec.array Codec.int) """[1,"x"]""")
                Expect.stringContains message "$.[1]" "array path"

            testCase "an sbyte above its range is an error"
            <| fun () ->
                let message = failure (Decode.fromString Codec.sbyte "128")
                Expect.stringContains message "Value was either too large or too small for a sbyte" "sbyte range"

            testCase "a float past the double range is an error, and the string Infinity still decodes"
            <| fun () ->
                for text in [ "1e400"; "-1e400"; "\"1e400\"" ] do
                    expectOutOfRange "a float" "Value is outside the float range" text (Decode.fromString Codec.float text)

                expectOutOfRange "a float32" "Value is outside the float32 range" "1e400" (Decode.fromString Codec.float32 "1e400")
                Expect.equal (Decode.fromString Codec.float "\"Infinity\"") (Ok infinity) "the string Infinity"

            testCase "a float32 whose double value rounds to infinity as a float32 is an error, and the single maximum still decodes"
            <| fun () ->
                for text in [ "1e39"; "-1e39"; "3.4028235677973366e38" ] do
                    expectOutOfRange "a float32" "Value is outside the float32 range" text (Decode.fromString Codec.float32 text)

                Expect.equal (Decode.fromString Codec.float32 "3.4028234663852886e38") (Ok Single.MaxValue) "the single maximum"
                Expect.equal (Decode.fromString Codec.float32 "\"Infinity\"") (Ok Single.PositiveInfinity) "the string Infinity"

            testCase "a list of 100,000 ints decodes in under three seconds"
            <| fun () ->
                let text =
                    "["
                    + String.concat "," (List.replicate 100000 "7")
                    + "]"

                let started = DateTime.UtcNow
                let decoded = Decode.fromString (Codec.list Codec.int) text
                let elapsed = (DateTime.UtcNow - started).TotalMilliseconds
                Expect.isTrue (decoded = Ok (List.replicate 100000 7)) "decoded list"
                Expect.isLessThan elapsed 3000.0 "elapsed milliseconds"
        ]
