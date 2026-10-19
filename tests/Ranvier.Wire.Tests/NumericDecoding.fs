module Ranvier.Wire.Tests.NumericDecoding

open System.Numerics
open Expecto
open Ranvier.Wire.Thoth.Json.Core
open Ranvier.Wire
open Ranvier.Wire.Tests.WireSupport

let private decodes (codec: Codec<'T>) (text: string) (expected: 'T) =
    testCase ("decodes " + text)
    <| fun () ->
        Expect.equal
            ((WireCodec.ofCodec codec).Decode text
             |> expectOk text)
            expected
            text

let private rejects (codec: Codec<'T>) (text: string) =
    testCase ("rejects " + text)
    <| fun () ->
        Expect.isTrue
            ((WireCodec.ofCodec codec).Decode text
             |> expectError text
             |> isShape)
            text

let private rejectsWith (codec: Codec<'T>) (text: string) (fragment: string) =
    testCase ("rejects " + text)
    <| fun () ->
        let error =
            (WireCodec.ofCodec codec).Decode text
            |> expectError text

        Expect.isTrue (isShape error) text
        Expect.stringContains error.Message fragment text

let private sevens (count: int) =
    String.replicate count "7"

/// <summary>The value plain Thoth.Json decodes from the text, outside a <c>WireCodec</c>.</summary>
let private thothDecode (codec: Codec<'T>) (text: string) : Result<'T, string> =
#if FABLE_COMPILER
    Ranvier.Wire.Thoth.Json.JavaScript.Decode.fromString codec text
#else
    Ranvier.Wire.Thoth.Json.System.Text.Json.Decode.fromString codec text
#endif

let private digitsText = "a bigint written as digits"
let private decimalText = "a decimal written as digits with an optional fraction"
let private decimalRange = "outside the decimal range"

#if FABLE_COMPILER
/// <summary>The text decoded as on an engine whose <c>JSON.parse</c> gives a reviver no source text.</summary>
let private withoutSourceText (codec: Codec<'T>) (text: string) =
    Ranvier.Wire.Backend.decodeWithSourceText false WireLimits.Default codec.Decoder text
#endif

[<Tests>]
let tests =
    testList
        "numeric decoding"
        [
            testList
                "int"
                [
                    rejects Codec.int "1.0"
                    rejects Codec.int "1e-1"
                    decodes Codec.int "1e3" 1000
                    rejects Codec.int "2147483648"
                    decodes Codec.int "-0" 0
                    rejects Codec.int "1e400"
                    decodes Codec.int "\"42\"" 42
                    rejects Codec.int "1e-400"
                    rejects Codec.int "10000000000000001e-16"
                    rejects Codec.uint32 "4294967294999999999e-9"
                    decodes Codec.int "5E+2" 500
                    decodes Codec.uint32 "4294967295" 4294967295u
                    testCase "an int alternative to a bigint gives way to it for a 400-digit number"
                    <| fun () ->
                        let either =
                            Codec.create
                                (fun (value: BigInteger) -> Encode.string (string value))
                                (Decode.oneOf
                                    [
                                        Decode.int
                                        |> Decode.map (fun (value: int) -> BigInteger.Parse (string value))
                                        Codecs.bigint.Decoder
                                    ])

                        Expect.equal
                            ((WireCodec.ofCodec either).Decode(sevens 400)
                             |> expectOk "400 digits")
                            (BigInteger.Parse (sevens 400))
                            "400 digits"
                ]
            testList
                "int64"
                [
                    decodes Codec.int64 "9223372036854775807" 9223372036854775807L
                    decodes Codec.int64 "-9223372036854775808" -9223372036854775808L
                    rejects Codec.int64 "9223372036854775808"
                    rejects Codec.int64 "-9223372036854775809"
                    decodes Codec.int64 "9007199254740993" 9007199254740993L
                    rejects Codec.int64 "1.5"
                    decodes Codec.int64 "\"9007199254740993\"" 9007199254740993L
                    // A number in exponent form: .NET's Int64.TryParse allows the exponent, Fable's takes no styles.
#if FABLE_COMPILER
                    rejects Codec.int64 "1e3"
#else
                    decodes Codec.int64 "1e3" 1000L
#endif
                ]
            testList
                "uint64"
                [
                    decodes Codec.uint64 "18446744073709551615" 18446744073709551615UL
                    rejects Codec.uint64 "18446744073709551616"
                    rejects Codec.uint64 "-1"
                ]
            testList
                "float"
                [
                    rejects Codec.float "1e400"
                    rejects Codec.float "-1e400"
                    rejects Codec.float "\"1e400\""
                    decodes Codec.float "1.7976931348623157e308" 1.7976931348623157e308
                    decodes Codec.float "\"Infinity\"" infinity
                ]
            testList "float32" [ rejects Codec.float32 "1e400" ]
            testList
                "Codecs.bigint"
                [
                    decodes Codecs.bigint "9007199254740993" (BigInteger.Parse "9007199254740993")
                    decodes Codecs.bigint "-123456789012345678901234567890" (BigInteger.Parse "-123456789012345678901234567890")
                    rejectsWith Codecs.bigint "1.5" digitsText
                    rejectsWith Codecs.bigint "1e3" digitsText
                    decodes Codecs.bigint "\"-123456789012345678901234567890\"" (BigInteger.Parse "-123456789012345678901234567890")
                    decodes Codecs.bigint "\"5\"" (BigInteger.Parse "5")
                    rejectsWith Codecs.bigint "\" 5\"" digitsText
                    rejectsWith Codecs.bigint "\"+5\"" digitsText
                    rejectsWith Codecs.bigint "\"\"" digitsText
                    rejectsWith Codecs.bigint "true" digitsText
                    testCase "outside a WireCodec, a bigint over the default limit is a decoder error"
                    <| fun () ->
                        match thothDecode Codecs.bigint (quote + sevens 4301 + quote) with
                        | Ok _ -> failtest "the text decoded"
                        | Error message -> Expect.stringContains message "The bigint has 4301 digits, more than 4300." "message"

                        Expect.equal (thothDecode Codecs.bigint (quote + sevens 4300 + quote)) (Ok (BigInteger.Parse (sevens 4300))) "at the limit"
                    testCase "a bigint encodes as a string of digits"
                    <| fun () ->
                        let value = BigInteger.Parse "-123456789012345678901234567890"
                        Expect.equal ((WireCodec.ofCodec Codecs.bigint).Encode value) "\"-123456789012345678901234567890\"" "wire text"
                ]
            testList
                "the bigint digit limit"
                [
                    testCase "a bigint at the default limit of 4,300 digits decodes, as a number or a string"
                    <| fun () ->
                        let wire = WireCodec.ofCodec Codecs.bigint
                        Expect.equal ((wire.Decode (sevens 4300) |> expectOk "number").ToString().Length) 4300 "number"

                        Expect.equal
                            ((wire.Decode ("-" + sevens 4300)
                              |> expectOk "negative")
                                .ToString()
                                .Length)
                            4301
                            "negative"

                        Expect.equal
                            ((wire.Decode (quote + sevens 4300 + quote)
                              |> expectOk "string")
                                .ToString()
                                .Length)
                            4300
                            "string"
                    testCase "a bigint over the limit is a limit error, as a number or a string"
                    <| fun () ->
                        let wire = WireCodec.ofCodec Codecs.bigint

                        Expect.isTrue
                            (wire.Decode (sevens 4301)
                             |> expectError "number"
                             |> isLimit)
                            "number"

                        Expect.isTrue
                            (wire.Decode (quote + sevens 4301 + quote)
                             |> expectError "string"
                             |> isLimit)
                            "string"
                    testCase "a bigint over the limit inside a list is a limit error naming its path"
                    <| fun () ->
                        let wire = WireCodec.ofCodec (Codec.list (Codec.tuple2 Codec.int Codecs.bigint))
                        let text = "[[1,\"5\"],[2," + sevens 4301 + "]]"
                        let error = wire.Decode text |> expectError "nested"
                        Expect.isTrue (isLimit error) "nested"
                        Expect.stringContains error.Message "Error at: `$.[1].[1]`" "path"
                        Expect.stringContains error.Message "The bigint has 4301 digits, more than 4300." "reason"
                    testCase "a custom limit counts every digit, leading zeros included, and no sign"
                    <| fun () ->
                        let wire =
                            WireCodec.ofCodecWith
                                { WireLimits.Default with
                                    MaxBigIntDigits = 3
                                }
                                Codecs.bigint

                        Expect.equal (wire.Decode "999" |> expectOk "999") (BigInteger.Parse "999") "999"
                        Expect.equal (wire.Decode "-999" |> expectOk "-999") (BigInteger.Parse "-999") "-999"
                        let error = wire.Decode "1000" |> expectError "1000"
                        Expect.isTrue (isLimit error) "1000"
                        Expect.stringContains error.Message "4 digits" "message"

                        Expect.isTrue
                            (wire.Decode "\"0001\""
                             |> expectError "0001"
                             |> isLimit)
                            "0001"
                    testCase "MaxBigIntDigits outside 1 to 100,000 is rejected"
                    <| fun () ->
                        let limited (digits: int) =
                            WireCodec.ofCodecWith
                                { WireLimits.Default with
                                    MaxBigIntDigits = digits
                                }
                                Codecs.bigint
                            |> ignore

                        Expect.throws (fun () -> limited 0) "0"
                        Expect.throws (fun () -> limited 100001) "100001"
                        limited 1
                        limited 100000
                ]
            testList
                "Codecs.decimal"
                [
                    decodes Codecs.decimal "79228162514264337593543950335" 79228162514264337593543950335M
                    rejectsWith Codecs.decimal "1e30" decimalText
                    rejectsWith Codecs.decimal "1e5" decimalText
                    decodes Codecs.decimal "\"1.10\"" 1.10M
                    decodes Codecs.decimal "\"-0.5\"" -0.5M
                    decodes Codecs.decimal "\"0.0000000000000000000000000001\"" 0.0000000000000000000000000001M
                    rejectsWith Codecs.decimal "\"0.00000000000000000000000000001\"" decimalRange
                    rejectsWith Codecs.decimal "\"79228162514264337593543950336\"" decimalRange
                    rejectsWith Codecs.decimal "\"1e5\"" decimalText
                    rejectsWith Codecs.decimal "\"1,000.5\"" decimalText
                    rejectsWith Codecs.decimal "\".5\"" decimalText
                    rejectsWith Codecs.decimal "\"5.\"" decimalText
                    rejectsWith Codecs.decimal "\"-\"" decimalText
                    testCase "a decimal keeps its scale"
                    <| fun () -> Expect.equal ((WireCodec.ofCodec Codecs.decimal).Encode 1.10M) "\"1.10\"" "wire text"
                ]
#if !FABLE_COMPILER
            // .NET only: .NET formats numbers by the current culture; Fable has no cultures.
            testCase "bigint and decimal text is the same under any culture"
            <| fun () ->
                let previous = System.Globalization.CultureInfo.CurrentCulture

                try
                    System.Globalization.CultureInfo.CurrentCulture <- System.Globalization.CultureInfo "sv-SE"
                    Expect.equal ((WireCodec.ofCodec Codecs.bigint).Encode(BigInteger.Parse "-5")) "\"-5\"" "bigint"
                    Expect.equal ((WireCodec.ofCodec Codecs.decimal).Encode -1.50M) "\"-1.50\"" "decimal"
                finally
                    System.Globalization.CultureInfo.CurrentCulture <- previous
#endif
#if FABLE_COMPILER
            testCase "this engine's JSON.parse gives a reviver the source text of numbers"
            <| fun () -> Expect.isTrue Ranvier.Wire.Backend.sourceText "source text"
            testCase "without source text, a number decodes when its value is a safe integer or a float"
            <| fun () ->
                Expect.equal (withoutSourceText Codec.int "1" |> expectOk "int") 1 "int"

                Expect.equal
                    (withoutSourceText Codec.int "1.0"
                     |> expectOk "int 1.0")
                    1
                    "int 1.0"

                Expect.equal
                    (withoutSourceText Codec.int64 "9007199254740991"
                     |> expectOk "int64")
                    9007199254740991L
                    "int64"

                Expect.equal
                    (withoutSourceText Codecs.bigint "-9007199254740991"
                     |> expectOk "bigint")
                    (BigInteger.Parse "-9007199254740991")
                    "bigint"

                Expect.equal
                    (withoutSourceText Codecs.decimal "15"
                     |> expectOk "decimal")
                    15M
                    "decimal"

                Expect.equal
                    (withoutSourceText Codec.float "0.1"
                     |> expectOk "float")
                    0.1
                    "float"
            testCase "without source text, a number whose text matters is a shape error"
            <| fun () ->
                let expectUnavailable (name: string) (result: Result<'T, WireError>) =
                    let error = result |> expectError name
                    Expect.isTrue (isShape error) name
                    Expect.stringContains error.Message "source text" name

                expectUnavailable "int64 past 2^53" (withoutSourceText Codec.int64 "9007199254740993")
                expectUnavailable "bigint past 2^53" (withoutSourceText Codecs.bigint "9007199254740993")
                expectUnavailable "decimal with a fraction" (withoutSourceText Codecs.decimal "1.5")
                expectUnavailable "decimal 0.1" (withoutSourceText Codecs.decimal "0.1")
            testCase "without source text, a number reads as the safe integer its value is, whatever its text"
            <| fun () ->
                Expect.equal
                    (withoutSourceText Codecs.bigint "1e3"
                     |> expectOk "bigint 1e3")
                    (BigInteger.Parse "1000")
                    "bigint 1e3"

                Expect.equal
                    (withoutSourceText Codecs.bigint "1.0"
                     |> expectOk "bigint 1.0")
                    (BigInteger.Parse "1")
                    "bigint 1.0"

                Expect.equal
                    (withoutSourceText Codecs.decimal "1e5"
                     |> expectOk "decimal 1e5")
                    100000M
                    "decimal 1e5"

                Expect.equal
                    (withoutSourceText Codec.int "1e-400"
                     |> expectOk "int 1e-400")
                    0
                    "int 1e-400"
            testCase "without source text, numbers written as strings decode exactly"
            <| fun () ->
                Expect.equal
                    (withoutSourceText Codec.int64 "\"9007199254740993\""
                     |> expectOk "int64")
                    9007199254740993L
                    "int64"

                Expect.equal
                    (withoutSourceText Codecs.bigint "\"9007199254740993\""
                     |> expectOk "bigint")
                    (BigInteger.Parse "9007199254740993")
                    "bigint"

                Expect.equal
                    (withoutSourceText Codecs.decimal "\"1.50\""
                     |> expectOk "decimal")
                    1.50M
                    "decimal"
#endif
        ]
