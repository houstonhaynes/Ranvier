module Ranvier.Wire.Tests.WireSupport

open System
open Expecto
open Ranvier.Wire.Thoth.Json.Core
open Ranvier.Wire

/// <summary>Whether the suite runs under Fable.</summary>
let isFable =
#if FABLE_COMPILER
    true
#else
    false
#endif

/// <summary>The UTF-16 code unit as a one-character string; lone surrogates included.</summary>
let u (code: int) =
    string (char code)

/// <summary>The JSON escape <c>\u</c> followed by the four hex digits, as written in wire text.</summary>
let esc (hex: string) =
    "\\u" + hex

let quote = "\""

/// <summary>The decoded value; a failed test naming the error otherwise.</summary>
let expectOk (message: string) (result: Result<'T, WireError>) : 'T =
    match result with
    | Ok value -> value
    | Error error -> failtest (message + ": " + error.Message)

/// <summary>The error; a failed test when the text decoded.</summary>
let expectError (message: string) (result: Result<'T, WireError>) : WireError =
    match result with
    | Ok _ -> failtest (message + ": the text decoded")
    | Error error -> error

let isSyntax (error: WireError) =
    match error with
    | WireError.Syntax _ -> true
    | _ -> false

let isLimit (error: WireError) =
    match error with
    | WireError.Limit _ -> true
    | _ -> false

let isShape (error: WireError) =
    match error with
    | WireError.Shape _ -> true
    | _ -> false

type Person =
    {
        Name: string
        Age: int
        Email: string option
    }

/// <summary>A record with an optional field, shared by the decoding and conformance suites.</summary>
let person: Codec<Person> =
    objectCodec {
        let! name = Codec.field "name" _.Name Codec.string
        and! age = Codec.field "age" _.Age Codec.int
        and! email = Codec.optional "email" _.Email Codec.string

        return
            {
                Name = name
                Age = age
                Email = email
            }
    }

/// <summary>The instant at the Unix milliseconds, shown in the offset (minutes).</summary>
let instant (milliseconds: int64) (offsetMinutes: float) =
    DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToOffset(TimeSpan.FromMinutes offsetMinutes)

/// <summary>Whether the values hold the same instant and the same offset.</summary>
/// <remarks>Unlike <c>=</c>, also compares the offset.</remarks>
let sameInstantAndOffset (a: DateTimeOffset) (b: DateTimeOffset) =
    a.UtcTicks = b.UtcTicks && a.Offset = b.Offset

/// <summary>Equal doubles, with NaN equal to NaN and 0.0 distinct from -0.0.</summary>
let sameFloat (a: float) (b: float) =
    (Double.IsNaN a && Double.IsNaN b)
    || (a = b && 1.0 / a = 1.0 / b)
