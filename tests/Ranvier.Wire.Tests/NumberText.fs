module Ranvier.Wire.Tests.NumberText

open System
open Expecto
open Ranvier.Wire.Thoth.Json.Core
open Ranvier.Wire
open Ranvier.Wire.Tests.WireSupport

let private floats = WireCodec.ofCodec Codec.float

/// <summary>The value writes as the text and decodes back; -0 writes as 0 and decodes as 0.</summary>
let private writes (value: float) (text: string) =
    testCase ("writes " + text)
    <| fun () ->
        Expect.equal (floats.Encode value) text "wire text"
        let expected = if value = 0.0 then 0.0 else value
        Expect.isTrue (sameFloat (floats.Decode text |> expectOk text) expected) ("decoded " + text)

let private reads (text: string) (expected: float) (canonical: string) =
    testCase ("reads " + quote + text + quote)
    <| fun () ->
        let decoded = floats.Decode text |> expectOk text
        Expect.isTrue (sameFloat decoded expected) ("decoded " + text)
        Expect.equal (floats.Encode decoded) canonical "re-encoded text"

type Samples = { Label: string; Values: float list }

let private samples: Codec<Samples> =
    objectCodec {
        let! label = Codec.field "label" _.Label Codec.string
        and! values = Codec.field "values" _.Values (Codec.list Codec.float)
        return { Label = label; Values = values }
    }

[<Tests>]
let tests =
    testList
        "number text"
        [
            writes 4.0 "4"
            writes 0.1 "0.1"
            writes (1.0 / 3.0) "0.3333333333333333"
            writes 1e21 "1e+21"
            writes 1e20 "100000000000000000000"
            writes 1.5e-7 "1.5e-7"
            writes 1e-6 "0.000001"
            writes 1e-7 "1e-7"
            writes 5e-324 "5e-324"
            writes 2.2250738585072014e-308 "2.2250738585072014e-308"
            writes 1.7976931348623157e308 "1.7976931348623157e+308"
            writes 123456789012345680000.0 "123456789012345680000"
            writes (0.1 + 0.2) "0.30000000000000004"
            writes 1e15 "1000000000000000"
            writes 1e16 "10000000000000000"
            writes 9007199254740993.0 "9007199254740992"
            writes -1.5 "-1.5"
            writes -0.000001 "-0.000001"
            writes 1.2345e-7 "1.2345e-7"
            writes 999999999999999900000.0 "999999999999999900000"
            writes 1e300 "1e+300"
            writes -2e-300 "-2e-300"
            writes 4294967296.5 "4294967296.5"
            writes -0.0 "0"
            writes nan "\"NaN\""
            writes infinity "\"Infinity\""
            writes -infinity "\"-Infinity\""
            reads "1E+21" 1e21 "1e+21"
            reads "1.5E-07" 1.5e-7 "1.5e-7"
            reads "5E-324" 5e-324 "5e-324"
            reads "4.0" 4.0 "4"
            reads " \t 4 " 4.0 "4"
            reads "-0" -0.0 "0"
            reads "\"Infinity\"" infinity "\"Infinity\""
            testCase "a number past the double range is a shape error"
            <| fun () ->
                for text in [ "1e400"; "-1e400"; "1e309" ] do
                    Expect.isTrue (floats.Decode text |> expectError text |> isShape) text
            testCase "a pretty-printed payload from the old .NET writer decodes"
            <| fun () ->
                let text =
                    "{\r\n    \"label\": \"old\",\r\n    \"values\": [\r\n        1E+21,\r\n        1.5E-07,\r\n        -0\r\n    ]\r\n}"

                let wire = WireCodec.ofCodec samples
                let decoded = wire.Decode text |> expectOk "old payload"
                Expect.equal decoded.Label "old" "label"
                Expect.equal (wire.Encode decoded) """{"label":"old","values":[1e+21,1.5e-7,0]}""" "re-encoded text"
#if !FABLE_COMPILER
            // .NET only: reads the Node-generated corpus from the output directory.
            testCase "every double in the Node corpus writes as JSON.stringify does"
            <| fun () ->
                let lines =
                    IO.File.ReadAllLines (IO.Path.Combine (AppContext.BaseDirectory, "ecma-numbers.txt"))

                Expect.equal lines.Length 2027 "corpus size"

                for line in lines do
                    let tab = line.IndexOf '\t'

                    let value =
                        BitConverter.Int64BitsToDouble (Convert.ToInt64 (line.Substring (0, tab), 16))

                    Expect.equal (floats.Encode value) (line.Substring (tab + 1)) line

            testCase "every written double parses back to itself"
            <| fun () ->
                let random = Random 109
                let bytes = Array.zeroCreate<byte> 8
                let mutable count = 0

                while count < 20000 do
                    random.NextBytes bytes
                    let value = BitConverter.ToDouble (bytes, 0)

                    if Double.IsFinite value then
                        let text = floats.Encode value
                        Expect.equal (Double.Parse (text, Globalization.CultureInfo.InvariantCulture)) value text
                        count <- count + 1
#endif
        ]
