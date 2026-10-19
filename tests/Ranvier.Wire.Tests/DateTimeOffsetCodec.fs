module Ranvier.Wire.Tests.DateTimeOffsetCodec

open System
open Expecto
open Ranvier.Wire
open Ranvier.Wire.Tests.WireSupport

let private codec = WireCodec.ofCodec Codecs.dateTimeOffset

let private writes (name: string) (value: DateTimeOffset) (text: string) =
    testCase (name + " writes " + text)
    <| fun () ->
        Expect.equal (codec.Encode value) (quote + text + quote) "wire text"

        let decoded =
            codec.Decode (quote + text + quote)
            |> expectOk text

        Expect.isTrue (sameInstantAndOffset decoded value) ("decoded " + text)

let private reads (text: string) (expected: DateTimeOffset) =
    testCase ("reads " + quote + text + quote)
    <| fun () ->
        let decoded =
            codec.Decode (quote + text + quote)
            |> expectOk text

        Expect.isTrue (sameInstantAndOffset decoded expected) text

let private rejects (text: string) =
    testCase ("rejects " + quote + text + quote)
    <| fun () ->
        Expect.isTrue
            (codec.Decode (quote + text + quote)
             |> expectError text
             |> isShape)
            text

[<Tests>]
let tests =
    testList
        "DateTimeOffset codec"
        [
            writes "milliseconds at +05:30" (instant 1709599028789L 330.0) "2024-03-05T06:07:08.789+05:30"
            writes "a whole second at UTC" (instant 1704067200000L 0.0) "2024-01-01T00:00:00.000+00:00"
            writes "five milliseconds at -08:00" (instant 946713599005L -480.0) "1999-12-31T23:59:59.005-08:00"
            writes "the millisecond before the epoch" (instant -1L 0.0) "1969-12-31T23:59:59.999+00:00"
            writes "year 50" (instant -60575004000000L 120.0) "0050-06-15T12:00:00.000+02:00"
            writes "the first instant" (instant -62135596800000L 0.0) "0001-01-01T00:00:00.000+00:00"
            writes "the last millisecond" (instant 253402300799999L 0.0) "9999-12-31T23:59:59.999+00:00"
            writes "+14:00" (instant 1709568428789L 840.0) "2024-03-05T06:07:08.789+14:00"
            writes "-14:00" (instant 1709669228789L -840.0) "2024-03-05T06:07:08.789-14:00"
            writes "-03:30" (instant 1709618828000L -210.0) "2024-03-05T02:37:08.000-03:30"
            writes "-00:30" (instant 1709618828000L -30.0) "2024-03-05T05:37:08.000-00:30"
            writes "the first instant at +14:00" (instant -62135596800000L 840.0) "0001-01-01T14:00:00.000+14:00"
            writes "the first day at -14:00" (instant -62135546400000L -840.0) "0001-01-01T00:00:00.000-14:00"
            writes "the last day at +14:00" (instant 253402250399999L 840.0) "9999-12-31T23:59:59.999+14:00"
            reads "2024-03-05T06:07:08Z" (instant 1709618828000L 0.0)
            reads "2024-03-05T06:07:08-00:00" (instant 1709618828000L 0.0)
            reads "2024-03-05T06:07:08.7891234+05:30" (instant 1709599028789L 330.0)
            reads "2024-03-05T06:07:08.7+05:30" (instant 1709599028700L 330.0)
            reads "2024-03-05T06:07:08.78+05:30" (instant 1709599028780L 330.0)
            reads "2024-02-29T00:00:00+00:00" (instant 1709164800000L 0.0)
            reads "2000-02-29T00:00:00+00:00" (instant 951782400000L 0.0)
            reads "2024-03-05t06:07:08+00:00" (instant 1709618828000L 0.0)
            reads "2024-03-05T06:07:08z" (instant 1709618828000L 0.0)
            reads "2024-03-05t06:07:08.789z" (instant 1709618828789L 0.0)
            reads "2024-03-05T06:07:08+05:30 " (instant 1709599028000L 330.0)
            reads
                ("2024-03-05T06:07:08Z"
                 + esc "0009"
                 + esc "000d"
                 + esc "000a"
                 + " ")
                (instant 1709618828000L 0.0)
            testCase "decoding keeps the wire offset"
            <| fun () ->
                for text, minutes in
                    [
                        "2024-03-05T06:07:08.789+05:30", 330.0
                        "1999-12-31T23:59:59.005-08:00", -480.0
                        "2024-01-01T00:00:00.000+00:00", 0.0
                    ] do
                    let decoded =
                        codec.Decode (quote + text + quote)
                        |> expectOk text

                    Expect.equal decoded.Offset.TotalMinutes minutes text
            rejects "2024-02-30T00:00:00.000+00:00"
            rejects "2023-02-29T00:00:00+00:00"
            rejects "1900-02-29T00:00:00+00:00"
            rejects "2024-04-31T00:00:00+00:00"
            rejects "2024-13-01T00:00:00+00:00"
            rejects "2024-00-01T00:00:00+00:00"
            rejects "2024-03-05T24:00:00+00:00"
            rejects "2024-03-05T06:60:00+00:00"
            rejects "2024-03-05T06:07:60+00:00"
            rejects "2024-03-05T06:07:08+15:00"
            rejects "2024-03-05T06:07:08+14:01"
            rejects "2024-03-05T06:07:08+05:60"
            rejects "2024-03-05T06:07:08"
            rejects "2024-03-05 06:07:08+00:00"
            rejects "2024-03-05T06:07:08.+00:00"
            rejects "2024-03-05T06:07:08.12345678+00:00"
            rejects "2024-03-05T06:07:08+0530"
            rejects " 2024-03-05T06:07:08+05:30"
            rejects (esc "0009" + "2024-03-05T06:07:08Z")
            rejects "2024-03-05T06:07:08 "
            rejects "0000-01-01T00:00:00+00:00"
            rejects "0001-01-01T00:00:00+00:01"
            rejects "9999-12-31T23:59:59.999-00:01"
            rejects "03/05/2024"
            rejects ""
            testCase "a rejected text names the accepted form"
            <| fun () ->
                let error =
                    codec.Decode (quote + "03/05/2024" + quote)
                    |> expectError "03/05/2024"

                Expect.stringContains error.Message "a datetimeoffset written yyyy-MM-dd(T|t)HH:mm:ss[.fffffff](Z|z|+HH:mm|-HH:mm)" "message"
            testCase "a number or null is a shape error"
            <| fun () ->
                for text in [ "0"; "null" ] do
                    Expect.isTrue (codec.Decode text |> expectError text |> isShape) text
#if !FABLE_COMPILER
            // .NET only: a Fable DateTimeOffset holds whole milliseconds.
            testCase "sub-millisecond ticks truncate to the millisecond at or before the instant"
            <| fun () ->
                Expect.equal (codec.Encode ((instant 1709599028789L 330.0).AddTicks 9999L)) "\"2024-03-05T06:07:08.789+05:30\"" "after the epoch"
                Expect.equal (codec.Encode ((instant -1L 0.0).AddTicks 9999L)) "\"1969-12-31T23:59:59.999+00:00\"" "before the epoch"
#endif
        ]
