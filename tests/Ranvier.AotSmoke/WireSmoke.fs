module Ranvier.AotSmoke.WireSmoke

open Ranvier.Wire.Thoth.Json.Core
open Ranvier.Wire.Thoth.Json.System.Text.Json
open Ranvier.Wire

type private Shape =
    | Circle of radius: float
    | Dot

type private Point = { X: int; Label: string }

let private shape: Codec<Shape> =
    variantCodec {
        let! circle = Codec.case "circle" Circle Codec.float
        and! dot = Codec.case "dot" (fun () -> Dot) Codec.unit

        return
            function
            | Circle r -> circle r
            | Dot -> dot ()
    }

let private point: Codec<Point> =
    objectCodec {
        let! x = Codec.field "x" _.X Codec.int
        and! label = Codec.field "label" _.Label Codec.string
        return { X = x; Label = label }
    }

let private failure (result: Result<'T, string>) =
    match result with
    | Ok _ -> "decoded"
    | Error message -> message

let private kind (result: Result<'T, WireError>) =
    match result with
    | Ok _ -> "Ok"
    | Error (WireError.Syntax _) -> "Syntax"
    | Error (WireError.Limit _) -> "Limit"
    | Error (WireError.Shape _) -> "Shape"

/// <summary>Each wire check as its name, whether it passed, and the text it observed.</summary>
let checks () : (string * bool * string) list =
    let pointText = Encode.toString 0 (point.Encoder { X = 3; Label = "a" })
    let tag = failure (Decode.fromString shape """{"hexagon":null}""")
    let index = failure (Decode.fromString (Codec.array Codec.int) """[1,"x"]""")
    let overflow = failure (Decode.fromString Codec.float "1e400")
    let overflow32 = failure (Decode.fromString Codec.float32 "1e39")
    let points = WireCodec.ofCodec point
    let pointWire = points.Encode { X = 3; Label = "a" }

    let deep =
        (WireCodec.ofCodec Codec.value).Decode(String.replicate 65 "[" + String.replicate 65 "]")

    let strings = WireCodec.ofCodec Codec.string
    let loneText = strings.Decode ("\"a\\ud800b\"")

    let loneEncode =
        try
            strings.Encode ("a" + string (char 0xD800))
        with :? System.ArgumentException as ex ->
            ex.Message

    let int64Past = (WireCodec.ofCodec Codec.int64).Decode "9223372036854775808"
    let decimalPast = (WireCodec.ofCodec Codecs.decimal).Decode "1e30"

    let bigNumber =
        (WireCodec.ofCodec Codecs.bigint).Decode "-123456789012345678901234567890"

    let bigPast = (WireCodec.ofCodec Codecs.bigint).Decode(String.replicate 4301 "7")
    let floatPast = (WireCodec.ofCodec Codec.float).Decode "1e400"
    let dates = WireCodec.ofCodec Codecs.dateTimeOffset

    let dateText =
        dates.Encode (System.DateTimeOffset(2024, 3, 5, 6, 7, 8, 789, System.TimeSpan.FromMinutes 330.0).AddTicks 1234L)

    let oldDate = dates.Decode "\"2024-03-05T06:07:08.7891234+05:30\""

    let floatText =
        (WireCodec.ofCodec (Codec.list Codec.float)).Encode [ 4.0; 0.1; 1e21; 1.5e-7; -0.0; 5e-324; nan ]

    [
        "a vendored Thoth record encodes", pointText = """{"x":3,"label":"a"}""", pointText
        "an unknown variant tag is an error", tag.Contains "Expected exactly one object key", tag
        "an array element error names its index", index.Contains "$.[1]", index
        "a vendored float past the double range is an error", overflow.Contains "Value is outside the float range", overflow
        "a vendored float32 past the single range is an error", overflow32.Contains "Value is outside the float32 range", overflow32
        "a WireCodec record round-trips", points.Decode pointWire = Ok { X = 3; Label = "a" }, pointWire
        "unclosed JSON is a syntax error", kind (points.Decode "{") = "Syntax", kind (points.Decode "{")
        "nesting past the limit is a limit error", kind deep = "Limit", kind deep
        "null for a record is a shape error", kind (points.Decode "null") = "Shape", kind (points.Decode "null")
        "an escaped lone surrogate is a syntax error", kind loneText = "Syntax", kind loneText
        "a lone surrogate does not encode", loneEncode.Contains "lone surrogate", loneEncode
        "an int64 number past its range is a shape error", kind int64Past = "Shape", kind int64Past
        "a decimal number past the decimal range is a shape error", kind decimalPast = "Shape", kind decimalPast
        ("a bigint number decodes exactly", bigNumber = Ok (System.Numerics.BigInteger.Parse "-123456789012345678901234567890"), kind bigNumber)
        "a bigint past the digit limit is a limit error", kind bigPast = "Limit", kind bigPast
        "a float past the double range is a shape error", kind floatPast = "Shape", kind floatPast
        "a DateTimeOffset writes milliseconds and its offset", dateText = "\"2024-03-05T06:07:08.789+05:30\"", dateText
        ("a seven-digit DateTimeOffset reads at millisecond precision",
         (match oldDate with
          | Ok value ->
              value.Offset.TotalMinutes = 330.0
              && value.ToUnixTimeMilliseconds () = 1709599028789L
          | Error _ -> false),
         kind oldDate)
        "doubles write in ECMAScript form", floatText = """[4,0.1,1e+21,1.5e-7,0,5e-324,"NaN"]""", floatText
    ]
