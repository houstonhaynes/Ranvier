module Ranvier.Wire.Tests.Conformance

open System
open System.Numerics
open Expecto
open Ranvier.Wire.Thoth.Json.Core
open Ranvier.Wire
open Ranvier.Wire.Tests.WireSupport

/// <summary>A case's wire text: one text on both targets, or a .NET text and a Fable text (string escaping).</summary>
type WireText =
    | Same of string
    | Differs of dotnet: string * fable: string

let private own (text: WireText) =
    match text with
    | Same text -> text
    | Differs (dotnet, fable) -> if isFable then fable else dotnet

let private every (text: WireText) =
    match text with
    | Same text -> [ text ]
    | Differs (dotnet, fable) -> [ dotnet; fable ]

/// <summary>
/// Encoding the value gives this target's text; every text of the case decodes to the expected value and re-encodes
/// to this target's text.
/// </summary>
let private caseDecodingTo (name: string) (equal: 'T -> 'T -> bool) (codec: Codec<'T>) (value: 'T) (decoded: 'T) (text: WireText) =
    let wire = WireCodec.ofCodec codec

    testCase name
    <| fun () ->
        Expect.equal (wire.Encode value) (own text) "encoded text"

        for input in every text do
            let result = wire.Decode input |> expectOk input
            Expect.isTrue (equal result decoded) ("decoded value of " + input)
            Expect.equal (wire.Encode result) (own text) ("re-encoded text of " + input)

let private case name equal codec value text =
    caseDecodingTo name equal codec value value text

let private kindName (error: WireError) =
    match error with
    | WireError.Syntax _ -> "Syntax"
    | WireError.Limit _ -> "Limit"
    | WireError.Shape _ -> "Shape"

/// <summary>The text is an error of the kind on both targets.</summary>
let private rejects (name: string) (kind: WireError -> bool) (codec: Codec<'T>) (text: string) =
    testCase name
    <| fun () ->
        let error =
            (WireCodec.ofCodec codec).Decode text
            |> expectError name

        Expect.isTrue (kind error) (kindName error + " error: " + error.Message)

/// <summary>The text decodes to the value, which encodes to the canonical text, on both targets.</summary>
let private reads (name: string) (equal: 'T -> 'T -> bool) (codec: Codec<'T>) (text: string) (value: 'T) (canonical: string) =
    let wire = WireCodec.ofCodec codec

    testCase name
    <| fun () ->
        let result = wire.Decode text |> expectOk text
        Expect.isTrue (equal result value) ("decoded value of " + text)
        Expect.equal (wire.Encode result) canonical ("re-encoded text of " + text)

/// <summary>The text decodes to this target's value, or is a <c>Shape</c> error where this target's value is <c>None</c>.</summary>
let private readsOn (name: string) (codec: Codec<'T>) (text: string) (dotnet: 'T option) (fable: 'T option) =
    testCase name
    <| fun () ->
        let result = (WireCodec.ofCodec codec).Decode text

        match (if isFable then fable else dotnet) with
        | Some value -> Expect.equal (expectOk text result) value text
        | None -> Expect.isTrue (expectError text result |> isShape) text

type Shape =
    | Circle of radius: float
    | Rect of width: float * height: float
    | Dot

type OrderLine =
    {
        Sku: string
        Quantity: int
        Price: decimal
    }

type Order =
    {
        Id: Guid
        Customer: Person
        Lines: OrderLine list
        PlacedAt: DateTimeOffset
        Total: int64
        Shape: Shape option
    }

type Primitives =
    {
        I8: sbyte
        U8: byte
        I16: int16
        U16: uint16
        I32: int
        U32: uint32
        Flag: bool
        Letter: char
    }

let private shape: Codec<Shape> =
    variantCodec {
        let! circle = Codec.case "circle" Circle Codec.float
        and! rect = Codec.case "rect" Rect (Codec.tuple2 Codec.float Codec.float)
        and! dot = Codec.case "dot" (fun () -> Dot) Codec.unit

        return
            function
            | Circle r -> circle r
            | Rect (w, h) -> rect (w, h)
            | Dot -> dot ()
    }

let private orderLine: Codec<OrderLine> =
    objectCodec {
        let! sku = Codec.field "sku" _.Sku Codec.string
        and! quantity = Codec.field "quantity" _.Quantity Codec.int
        and! price = Codec.field "price" _.Price Codecs.decimal

        return
            {
                Sku = sku
                Quantity = quantity
                Price = price
            }
    }

let private order: Codec<Order> =
    objectCodec {
        let! id = Codec.field "id" _.Id Codec.guid
        and! customer = Codec.field "customer" _.Customer person
        and! lines = Codec.field "lines" _.Lines (Codec.list orderLine)
        and! placedAt = Codec.field "placedAt" _.PlacedAt Codecs.dateTimeOffset
        and! total = Codec.field "total" _.Total Codec.int64
        and! shape' = Codec.field "shape" _.Shape (Codec.lossyOption shape)

        return
            {
                Id = id
                Customer = customer
                Lines = lines
                PlacedAt = placedAt
                Total = total
                Shape = shape'
            }
    }

let private primitives: Codec<Primitives> =
    objectCodec {
        let! i8 = Codec.field "i8" _.I8 Codec.sbyte
        and! u8 = Codec.field "u8" _.U8 Codec.byte
        and! i16 = Codec.field "i16" _.I16 Codec.int16
        and! u16 = Codec.field "u16" _.U16 Codec.uint16
        and! i32 = Codec.field "i32" _.I32 Codec.int
        and! u32 = Codec.field "u32" _.U32 Codec.uint32
        and! flag = Codec.field "flag" _.Flag Codec.bool
        and! letter = Codec.field "letter" _.Letter Codec.char

        return
            {
                I8 = i8
                U8 = u8
                I16 = i16
                U16 = u16
                I32 = i32
                U32 = u32
                Flag = flag
                Letter = letter
            }
    }

type Builder =
    {
        Name: string
        Constructor: int option
    }

let private builder: Codec<Builder> =
    objectCodec {
        let! name = Codec.field "name" (fun (value: Builder) -> value.Name) Codec.string

        and! constructor =
            Codec.optional "constructor" (fun (value: Builder) -> value.Constructor) Codec.int

        return
            {
                Name = name
                Constructor = constructor
            }
    }

let private guidText = "0f8fad5b-d9cb-469f-a165-70867728950e"

let private guidValue = Guid.Parse guidText

let private alice =
    {
        Name = "Alice"
        Age = 30
        Email = Some "alice@example.com"
    }

let private sameOrder (a: Order) (b: Order) =
    sameInstantAndOffset a.PlacedAt b.PlacedAt
    && a = b

let private sameFloats (a: float list) (b: float list) =
    List.length a = List.length b
    && List.forall2 sameFloat a b

let private plain =
    "h"
    + u 0xE9
    + "llo "
    + u 0x65E5
    + u 0x672C
    + " \"q\" \\ / \n\t\r\b\f </script> & ' < >"

let private plainText =
    quote
    + "h"
    + u 0xE9
    + "llo "
    + u 0x65E5
    + u 0x672C
    + " \\\"q\\\" \\\\ / \\n\\t\\r\\b\\f </script> & ' < >"
    + quote

[<Tests>]
let tests =
    testList
        "wire conformance"
        [
            case "a record" (=) person alice (Same """{"name":"Alice","age":30,"email":"alice@example.com"}""")
            case "a record without its optional field" (=) person { Name = "Bob"; Age = 0; Email = None } (Same """{"name":"Bob","age":0}""")
            case "a union case with fields" (=) shape (Rect (2.5, 4.0)) (Same """{"rect":[2.5,4]}""")
            case "a union case without fields" (=) shape Dot (Same """{"dot":null}""")
            case "an option list" (=) (Codec.list (Codec.lossyOption Codec.int)) [ Some 1; None; Some -3 ] (Same "[1,null,-3]")
            case
                "an int list at the int32 bounds"
                (=)
                (Codec.list Codec.int)
                [ 1; 2; 3; -2147483648; 2147483647 ]
                (Same "[1,2,3,-2147483648,2147483647]")
            case "a map with string keys" (=) (Codec.dict Codec.int) (Map.ofList [ "b", 2; "a", 1; "ä", 3 ]) (Same """{"a":1,"b":2,"ä":3}""")
            case
                "a map with int keys"
                (=)
                (Codec.map' Codec.int Codec.string)
                (Map.ofList [ 10, "ten"; -1, "minus one" ])
                (Same """[[-1,"minus one"],[10,"ten"]]""")
            case
                "an int64 list"
                (=)
                (Codec.list Codec.int64)
                [ Int64.MinValue; Int64.MaxValue; 9007199254740993L; 0L ]
                (Same """["-9223372036854775808","9223372036854775807","9007199254740993","0"]""")
            case
                "a uint64 and a bigint"
                (=)
                (Codec.tuple2 Codec.uint64 Codecs.bigint)
                (UInt64.MaxValue, BigInteger.Parse "-123456789012345678901234567890")
                (Same """["18446744073709551615","-123456789012345678901234567890"]""")
            case
                "a decimal list"
                (=)
                (Codec.list Codecs.decimal)
                [ 79228162514264337593543950335M; 1.10M; 0.0000001M; -0.5M; 0M ]
                (Same """["79228162514264337593543950335","1.10","0.0000001","-0.5","0"]""")
            case
                "a DateTimeOffset with milliseconds"
                sameInstantAndOffset
                Codecs.dateTimeOffset
                (instant 1709599028789L 330.0)
                (Same "\"2024-03-05T06:07:08.789+05:30\"")
            case
                "a DateTimeOffset on a whole second"
                sameInstantAndOffset
                Codecs.dateTimeOffset
                (instant 1704067200000L 0.0)
                (Same "\"2024-01-01T00:00:00.000+00:00\"")
            case
                "a DateTimeOffset at a negative offset"
                sameInstantAndOffset
                Codecs.dateTimeOffset
                (instant 946713599005L -480.0)
                (Same "\"1999-12-31T23:59:59.005-08:00\"")
            case "a Guid" (=) Codec.guid (Guid.Parse "0f8fad5b-d9cb-469f-a165-70867728950e") (Same "\"0f8fad5b-d9cb-469f-a165-70867728950e\"")
            caseDecodingTo
                "a float list"
                sameFloats
                (Codec.list Codec.float)
                [ 4.0; 0.1; 1.0 / 3.0; 1e21; 1.5e-7; -0.0; 5e-324; nan ]
                [ 4.0; 0.1; 1.0 / 3.0; 1e21; 1.5e-7; 0.0; 5e-324; nan ]
                (Same """[4,0.1,0.3333333333333333,1e+21,1.5e-7,0,5e-324,"NaN"]""")
            case
                "primitives at their bounds"
                (=)
                primitives
                {
                    I8 = -128y
                    U8 = 255uy
                    I16 = -32768s
                    U16 = 65535us
                    I32 = 7
                    U32 = 4294967295u
                    Flag = true
                    Letter = char 0xE9
                }
                (Same (
                    """{"i8":-128,"u8":255,"i16":-32768,"u16":65535,"i32":7,"u32":4294967295,"flag":true,"letter":"""
                    + quote
                    + u 0xE9
                    + quote
                    + "}"
                ))
            case "a string written the same on both targets" (=) Codec.string plain (Same plainText)
            case
                "a supplementary-plane character"
                (=)
                Codec.string
                (u 0xD83C + u 0xDF89)
                (Differs (quote + esc "D83C" + esc "DF89" + quote, quote + u 0xD83C + u 0xDF89 + quote))
            case
                "control characters"
                (=)
                Codec.string
                (u 0x01 + u 0x1F)
                (Differs (quote + esc "0001" + esc "001F" + quote, quote + esc "0001" + esc "001f" + quote))
            case
                "DEL, a line separator and a no-break space"
                (=)
                Codec.string
                (u 0x7F + u 0x2028 + u 0xA0)
                (Differs (
                    quote
                    + esc "007F"
                    + esc "2028"
                    + esc "00A0"
                    + quote,
                    quote + u 0x7F + u 0x2028 + u 0xA0 + quote
                ))
            case
                "a nested record"
                sameOrder
                order
                {
                    Id = Guid.Parse "6f9619ff-8b86-d011-b42d-00c04fc964ff"
                    Customer = alice
                    Lines =
                        [
                            {
                                Sku = "A-1"
                                Quantity = 2
                                Price = 9.99M
                            }
                            {
                                Sku = "B-2"
                                Quantity = 1
                                Price = 100M
                            }
                        ]
                    PlacedAt = instant 1709599028789L 330.0
                    Total = 9007199254740993L
                    Shape = Some (Circle 1.25)
                }
                (Same
                    """{"id":"6f9619ff-8b86-d011-b42d-00c04fc964ff","customer":{"name":"Alice","age":30,"email":"alice@example.com"},"lines":[{"sku":"A-1","quantity":2,"price":"9.99"},{"sku":"B-2","quantity":1,"price":"100"}],"placedAt":"2024-03-05T06:07:08.789+05:30","total":"9007199254740993","shape":{"circle":1.25}}""")
            testCase "a lone surrogate does not encode"
            <| fun () ->
                Expect.throws
                    (fun () ->
                        (WireCodec.ofCodec Codec.string).Encode("a" + u 0xD800 + "b")
                        |> ignore)
                    "encode"
            rejects "a string holding an escaped lone surrogate is a syntax error" isSyntax Codec.string (quote + "a" + esc "d800" + "b" + quote)
            testCase "a repeated key takes its last value"
            <| fun () ->
                let decoded =
                    (WireCodec.ofCodec person).Decode """{"name":"A","name":"B","age":1}"""
                    |> expectOk "repeated key"

                Expect.equal decoded.Name "B" "name"
            testCase "an unknown field is ignored"
            <| fun () ->
                let decoded =
                    (WireCodec.ofCodec person).Decode """{"name":"A","age":1,"extra":[1,{"x":2}]}"""
                    |> expectOk "extra field"

                Expect.equal decoded { Name = "A"; Age = 1; Email = None } "decoded"
            rejects "an int field past int32" isShape person """{"name":"a","age":2147483648}"""
            rejects "an int field given 1e400" isShape person """{"name":"a","age":1e400}"""
            rejects "a float list given 1e400" isShape (Codec.list Codec.float) "[1,1e400]"
            rejects "a string field given an array" isShape person """{"name":["a"],"age":1}"""
            rejects "an unknown union tag" isShape shape """{"hexagon":null}"""
            rejects "a union object with no tag" isShape shape "{}"
            rejects "a Guid that is not a Guid" isShape Codec.guid "\"not-a-guid\""
            rejects "a char given two characters" isShape Codec.char "\"ab\""
            rejects "a byte given 256" isShape Codec.byte "256"
            rejects
                "an order whose total is a number past int64"
                isShape
                order
                """{"id":"6f9619ff-8b86-d011-b42d-00c04fc964ff","customer":{"name":"A","age":1},"lines":[],"placedAt":"2024-03-05T06:07:08.789+05:30","total":9223372036854775808,"shape":null}"""
            rejects
                "an order whose price is in exponent form"
                isShape
                order
                """{"id":"6f9619ff-8b86-d011-b42d-00c04fc964ff","customer":{"name":"A","age":1},"lines":[{"sku":"A","quantity":1,"price":"1e5"}],"placedAt":"2024-03-05T06:07:08.789+05:30","total":"1","shape":null}"""
            rejects
                "an order whose placedAt has no offset"
                isShape
                order
                """{"id":"6f9619ff-8b86-d011-b42d-00c04fc964ff","customer":{"name":"A","age":1},"lines":[],"placedAt":"2024-03-05T06:07:08","total":"1","shape":null}"""
            rejects
                "a field nested past the depth limit is a limit error"
                isLimit
                person
                ("""{"name":"a","age":1,"email":"""
                 + String.replicate 64 "["
                 + String.replicate 64 "]"
                 + "}")
            case "a float32" (=) Codec.float32 0.1f (Same "0.10000000149011612")
            case "the infinities" sameFloats (Codec.list Codec.float) [ infinity; -infinity ] (Same """["Infinity","-Infinity"]""")
            reads "an int written in exponent form" (=) Codec.int "1e3" 1000 "1000"
            reads
                "a bigint written as a number"
                (=)
                Codecs.bigint
                "-123456789012345678901234567890"
                (BigInteger.Parse "-123456789012345678901234567890")
                (quote + "-123456789012345678901234567890" + quote)
            reads "a decimal written as a number" (=) Codecs.decimal "1.10" 1.10M (quote + "1.10" + quote)
            reads "a float written by the old .NET writer" sameFloat Codec.float "1E+21" 1e21 "1e+21"
            reads
                "a DateTimeOffset written with Z and seven fraction digits"
                sameInstantAndOffset
                Codecs.dateTimeOffset
                "\"2024-03-05T06:07:08.7890000Z\""
                (instant 1709618828789L 0.0)
                "\"2024-03-05T06:07:08.789+00:00\""
            reads
                "a DateTimeOffset written with -00:00"
                sameInstantAndOffset
                Codecs.dateTimeOffset
                "\"2024-03-05T06:07:08.789-00:00\""
                (instant 1709618828789L 0.0)
                "\"2024-03-05T06:07:08.789+00:00\""
            reads
                "a DateTimeOffset written with lowercase t and z and trailing whitespace"
                sameInstantAndOffset
                Codecs.dateTimeOffset
                (quote
                 + "2024-03-05t06:07:08.789z "
                 + esc "0009"
                 + esc "000d"
                 + esc "000a"
                 + quote)
                (instant 1709618828789L 0.0)
                "\"2024-03-05T06:07:08.789+00:00\""
            rejects "an int given 1.0" isShape Codec.int "1.0"
            rejects "an int given 10e-1" isShape Codec.int "10e-1"
            rejects "a float32 given 1e39" isShape Codec.float32 "1e39"
            rejects "a DateTimeOffset with leading whitespace" isShape Codecs.dateTimeOffset "\" 2024-03-05T06:07:08.789+00:00\""
            for text in [ " 42"; "42 "; "+42"; "0042" ] do
                readsOn ("an int string " + quote + text + quote) Codec.int (quote + text + quote) (Some 42) (Some 42)
                readsOn ("an int64 string " + quote + text + quote) Codec.int64 (quote + text + quote) (Some 42L) (Some 42L)
            readsOn "an int64 number in exponent form" Codec.int64 "1e3" (Some 1000L) None
            readsOn "an int64 number with a negative exponent" Codec.int64 "10e-1" (Some 1L) None
            readsOn "a uint64 number with a negative exponent" Codec.uint64 "100e-2" (Some 1UL) None
            rejects "an int64 string in exponent form" isShape Codec.int64 "\"1e3\""
            reads "an int string with a tab and a line feed" (=) Codec.int (quote + esc "0009" + "42" + esc "000a" + quote) 42 "42"
            reads "an int string with a vertical tab and a form feed" (=) Codec.int (quote + esc "000b" + "42" + esc "000c" + quote) 42 "42"
            rejects "an int string with a hex prefix" isShape Codec.int "\"0x10\""
            rejects "an int string with a negative hex prefix" isShape Codec.int "\"-0x10\""
            rejects "an int string with an octal prefix" isShape Codec.int "\"0o17\""
            rejects "an int string with a binary prefix" isShape Codec.int "\"0b101\""
            rejects "an int string with underscores" isShape Codec.int "\"1_000\""
            rejects "an int string with a doubled underscore" isShape Codec.int "\"1__0\""
            rejects "an int string with leading and trailing underscores" isShape Codec.int "\"_1_\""
            rejects "an int string led by a byte order mark" isShape Codec.int (quote + u 0xFEFF + "42" + quote)
            rejects "an int string led by a no-break space" isShape Codec.int (quote + u 0xA0 + "42" + quote)
            rejects "an sbyte string 0xFF" isShape Codec.sbyte "\"0xFF\""
            rejects "an int string 0xFFFFFFFF" isShape Codec.int "\"0xFFFFFFFF\""
            rejects "an int64 string with a hex prefix" isShape Codec.int64 "\"0x10\""
            rejects "an int64 string 0xFFFFFFFFFFFFFFFF" isShape Codec.int64 "\"0xFFFFFFFFFFFFFFFF\""
            rejects "an int64 string with underscores" isShape Codec.int64 "\"1_000\""
            rejects "a uint64 string with a hex prefix" isShape Codec.uint64 "\"0x10\""
            rejects "a bigint string with a hex prefix" isShape Codecs.bigint "\"0x10\""
            rejects "an int string with a trailing NUL" isShape Codec.int (quote + "42" + esc "0000" + quote)
            rejects "an int64 string with a trailing NUL" isShape Codec.int64 (quote + "42" + esc "0000" + quote)
            reads "a byte string -0" (=) Codec.byte "\"-0\"" 0uy "0"
            reads "a uint16 string -0" (=) Codec.uint16 "\"-0\"" 0us "0"
            reads "a uint32 string -0" (=) Codec.uint32 "\"-0\"" 0u "0"
            reads "a uint64 string -0" (=) Codec.uint64 "\"-0\"" 0UL "\"0\""
            case
                "a map with keys in JavaScript's own-property order"
                (=)
                (Codec.dict Codec.int)
                (Map.ofList [ "10", 0; "2", 1; "a", 2; "__proto__", 3; "01", 4; "-1", 5; "4294967295", 6 ])
                (Same """{"2":1,"10":0,"-1":5,"01":4,"4294967295":6,"__proto__":3,"a":2}""")
            reads
                "key-value pairs in JavaScript's own-property order"
                (=)
                (Codec.keyValuePairs Codec.int)
                """{"b":1,"2":2,"__proto__":3}"""
                [ "2", 2; "b", 1; "__proto__", 3 ]
                """{"2":2,"b":1,"__proto__":3}"""
            reads
                "a map with a repeated key keeps its last value"
                (=)
                (Codec.dict Codec.int)
                """{"a":1,"a":2}"""
                (Map.ofList [ "a", 2 ])
                """{"a":2}"""
            reads
                "key-value pairs with a repeated key keep one pair"
                (=)
                (Codec.keyValuePairs Codec.int)
                """{"a":1,"b":3,"a":2}"""
                [ "a", 2; "b", 3 ]
                """{"a":2,"b":3}"""
            reads
                "a record beside keys named after Object.prototype members"
                (=)
                person
                """{"name":"A","age":1,"hasOwnProperty":0,"constructor":1,"toString":2,"__proto__":3}"""
                { Name = "A"; Age = 1; Email = None }
                """{"name":"A","age":1}"""
            case "a record whose absent optional field is named constructor" (=) builder { Name = "A"; Constructor = None } (Same """{"name":"A"}""")
            case
                "a record whose optional field named constructor is present"
                (=)
                builder
                { Name = "A"; Constructor = Some 1 }
                (Same """{"name":"A","constructor":1}""")
            reads "an upper-case Guid" (=) Codec.guid (quote + guidText.ToUpperInvariant () + quote) guidValue (quote + guidText + quote)
            reads "a Guid without hyphens" (=) Codec.guid (quote + guidText.Replace ("-", "") + quote) guidValue (quote + guidText + quote)
            reads "a Guid in braces" (=) Codec.guid (quote + "{" + guidText + "}" + quote) guidValue (quote + guidText + quote)
            reads "a Guid in parentheses" (=) Codec.guid (quote + "(" + guidText + ")" + quote) guidValue (quote + guidText + quote)
            reads
                "a Guid with surrounding whitespace"
                (=)
                Codec.guid
                (quote + " " + guidText + esc "0009" + quote)
                guidValue
                (quote + guidText + quote)
            rejects "a Guid in brackets" isShape Codec.guid (quote + "[" + guidText + "]" + quote)
            readsOn "a Guid in doubled braces" Codec.guid (quote + "{{" + guidText + "}}" + quote) None (Some guidValue)
            readsOn "a Guid with an unmatched brace" Codec.guid (quote + "{" + guidText + ")" + quote) None (Some guidValue)
            readsOn "a Guid led by a byte order mark" Codec.guid (quote + u 0xFEFF + guidText + quote) None (Some guidValue)
        ]
