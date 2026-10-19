module internal Ranvier.Wire.Helpers

open System
open System.Text
open Ranvier.Wire.Thoth.Json.Core
#if FABLE_COMPILER
open Fable.Core
open Fable.Core.JsInterop
#else
open System.Buffers
open System.Collections.Generic
open System.Globalization
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System.Text.Json
open System.Text.Json.Nodes
#endif

/// <summary>
/// The index of the first UTF-16 code unit of the text that is a surrogate without its pair; -1 when every surrogate
/// is paired or the text is null.
/// </summary>
let loneSurrogate (text: string) : int =
    let length = if isNull text then 0 else text.Length
    let mutable found = -1
    let mutable i = 0

    while found < 0 && i < length do
        let code = int text[i]

        if
            code >= 0xD800
            && code <= 0xDBFF
            && i + 1 < length
            && int text[i + 1] >= 0xDC00
            && int text[i + 1] <= 0xDFFF
        then
            i <- i + 2
        elif code >= 0xD800 && code <= 0xDFFF then
            found <- i
        else
            i <- i + 1

    found

let private requireScalars (text: string) =
    let index = loneSurrogate text

    if index >= 0 then
        invalidArg
            "value"
            ("A string holds a lone surrogate at index "
             + string index
             + "; wire text carries Unicode scalar values only.")

/// <summary>The helpers of <c>inner</c>, raising <c>ArgumentException</c> for a string, char or object key holding a lone surrogate.</summary>
let encoding (inner: IEncoderHelpers<'J>) : IEncoderHelpers<'J> =
    { new IEncoderHelpers<'J> with
        member _.encodeString value =
            requireScalars value
            inner.encodeString value

        member _.encodeChar value =
            requireScalars (string value)
            inner.encodeChar value

        member _.encodeDecimalNumber value =
            inner.encodeDecimalNumber value

        member _.encodeBool value =
            inner.encodeBool value

        member _.encodeNull() =
            inner.encodeNull ()

        member _.encodeObject values =
            inner.encodeObject (
                values
                |> Seq.map (fun (key, value) ->
                    requireScalars key
                    key, value)
            )

        member _.encodeArray values =
            inner.encodeArray values

        member _.encodeList values =
            inner.encodeList values

        member _.encodeSeq values =
            inner.encodeSeq values

        member _.encodeResizeArray values =
            inner.encodeResizeArray values

        member _.encodeSignedIntegralNumber value =
            inner.encodeSignedIntegralNumber value

        member _.encodeUnsignedIntegralNumber value =
            inner.encodeUnsignedIntegralNumber value
    }

/// <summary>The caps of a <c>WireCodec</c> decode, for the decoders that enforce them.</summary>
type DecodeScope(maxBigIntDigits: int, limitIsError: bool) =
    member _.MaxBigIntDigits = maxBigIntDigits

    /// <summary>
    /// Whether a decoder gives a value over a cap as a decoder error, which carries its path; otherwise it raises
    /// <c>LimitExceeded</c>.
    /// </summary>
    member _.LimitIsError = limitIsError

/// <summary>Raised by a decoder for a value over a <c>DecodeScope</c> cap; <c>WireCodec</c> gives it as <c>WireError.Limit</c>.</summary>
type LimitExceeded(message: string) =
    inherit Exception(message)

/// <summary>Whether a JSON number's text is <c>-?digits</c> with an optional non-negative exponent.</summary>
let private isIntegralText (text: string) =
    text.IndexOf '.' < 0
    && not (text.Contains "e-")
    && not (text.Contains "E-")

let private isFiniteWhole (number: float) =
    not (Double.IsInfinity number)
    && not (Double.IsNaN number)
    && Math.Floor number = number

type private ScopedHelpers<'J>(inner: IDecoderHelpers<'J>, numberText: 'J -> string option, maxBigIntDigits: int, limitIsError: bool) =
    inherit DecodeScope(maxBigIntDigits, limitIsError)

    interface IDecoderHelpers<'J> with
        member _.isString value =
            inner.isString value

        member _.isNumber value =
            inner.isNumber value

        member _.isBoolean value =
            inner.isBoolean value

        member _.isNullValue value =
            inner.isNullValue value

        member _.isArray value =
            inner.isArray value

        member _.isObject value =
            inner.isObject value

        member _.hasProperty name value =
            inner.hasProperty name value

        member _.isIntegralValue value =
            inner.isNumber value
            && (match numberText value with
                | Some text when isIntegralText text ->
#if FABLE_COMPILER
                    isFiniteWhole (inner.asFloat value)
#else
                    match Double.TryParse (text, NumberStyles.Float, CultureInfo.InvariantCulture) with
                    | true, number -> isFiniteWhole number
                    | _ -> false
#endif
                | _ -> false)

        member _.asString value =
            inner.asString value

        member _.asFloat value =
            inner.asFloat value

        member _.asFloat32 value =
            inner.asFloat32 value

        member _.asInt value =
            inner.asInt value

        member _.asBoolean value =
            inner.asBoolean value

        member _.asArray value =
            inner.asArray value

        member _.getProperty(name, value) =
            inner.getProperty (name, value)

        member _.getProperties value =
            inner.getProperties value

        member _.anyToString value =
            inner.anyToString value

        member _.numberToString value =
            match numberText value with
            | Some text -> text
            | None ->
                failwith (
                    "The number's source text is unavailable: this JavaScript engine's JSON.parse gives a reviver no source text"
                    + " (Node 21+, Chrome 114+, Firefox 135+ and Safari 18.4+ do)."
                )

/// <summary>
/// The helpers of <c>inner</c> for one decode under <c>maxBigIntDigits</c>, raising <c>LimitExceeded</c> for a value
/// over it. A number's text is <c>numberText</c>'s (<c>None</c> when unknown, which makes <c>numberToString</c> raise),
/// and a number is integral exactly when its text is known, is <c>-?digits</c> with an optional non-negative exponent,
/// and denotes a finite whole number; the check returns <c>false</c> for every other number.
/// </summary>
let decoding (numberText: 'J -> string option) (maxBigIntDigits: int) (inner: IDecoderHelpers<'J>) : IDecoderHelpers<'J> =
    ScopedHelpers (inner, numberText, maxBigIntDigits, false)

/// <summary>The helpers of <c>decoding</c>, giving a value over <c>maxBigIntDigits</c> as a decoder error with its path.</summary>
let locating (numberText: 'J -> string option) (maxBigIntDigits: int) (inner: IDecoderHelpers<'J>) : IDecoderHelpers<'J> =
    ScopedHelpers (inner, numberText, maxBigIntDigits, true)

let private echoLength = 256

/// <summary>
/// The text, or its first 256 UTF-16 code units followed by <c>…</c> when it is longer; a surrogate pair at the cut is
/// left out whole.
/// </summary>
let private capEcho (text: string) : string =
    if text.Length <= echoLength then
        text
    else
        let last = int text[echoLength - 1]

        let cut =
            if last >= 0xD800 && last <= 0xDBFF then
                echoLength - 1
            else
                echoLength

        text.Substring (0, cut) + "…"

#if !FABLE_COMPILER
/// <summary>
/// Whether ECMAScript orders the key as an array index: the canonical decimal text of an integer from 0 to 4294967294.
/// </summary>
let private isArrayIndex (key: string) =
    let mutable result =
        key.Length >= 1
        && key.Length <= 10
        && (key.Length = 1 || key[0] <> '0')

    let mutable i = 0

    while result && i < key.Length do
        result <- key[i] >= '0' && key[i] <= '9'
        i <- i + 1

    result
    && (key.Length < 10
        || String.CompareOrdinal (key, "4294967294") <= 0)

/// <summary>
/// The items in ECMAScript own-property order of their keys: the array indexes in ascending numeric order, then the
/// other keys in their given order.
/// </summary>
let private inPropertyOrder (key: 'T -> string) (items: 'T array) : 'T array =
    if Array.exists (fun item -> isArrayIndex (key item)) items then
        Array.append
            (items
             |> Array.filter (fun item -> isArrayIndex (key item))
             |> Array.sortBy (fun item -> UInt32.Parse (key item, CultureInfo.InvariantCulture)))
            (items
             |> Array.filter (fun item -> not (isArrayIndex (key item))))
    else
        items

/// <summary>
/// The System.Text.Json encoder helpers, with every double written as ECMAScript number text and every object's keys
/// in ECMAScript own-property order.
/// </summary>
let netEncoding: IEncoderHelpers<JsonNode> =
    let inner = Ranvier.Wire.Thoth.Json.System.Text.Json.Encode.helpers

    { new IEncoderHelpers<JsonNode> with
        member _.encodeString value =
            inner.encodeString value

        member _.encodeChar value =
            inner.encodeChar value

        member _.encodeDecimalNumber value =
            JsonNode.Parse (Numbers.ecmaString value)

        member _.encodeBool value =
            inner.encodeBool value

        member _.encodeNull() =
            inner.encodeNull ()

        member _.encodeObject values =
            inner.encodeObject (values |> Seq.toArray |> inPropertyOrder fst)

        member _.encodeArray values =
            inner.encodeArray values

        member _.encodeList values =
            inner.encodeList values

        member _.encodeSeq values =
            inner.encodeSeq values

        member _.encodeResizeArray values =
            inner.encodeResizeArray values

        member _.encodeSignedIntegralNumber value =
            inner.encodeSignedIntegralNumber value

        member _.encodeUnsignedIntegralNumber value =
            inner.encodeUnsignedIntegralNumber value
    }

/// <summary>
/// An object's members, read from the element at the first lookup: its keys in ECMAScript own-property order, and a
/// repeated key at its first position with its last value, as <c>JSON.parse</c> gives them.
/// </summary>
[<Sealed>]
type private ObjectMembers(element: JsonElement) =
    let found = Dictionary<string, JsonElement>(StringComparer.Ordinal)

    let keys =
        let order = ResizeArray<string>()

        for property in element.EnumerateObject () do
            let name = property.Name

            if not (found.ContainsKey name) then
                order.Add name

            found[name] <- property.Value

        inPropertyOrder id (order.ToArray ())

    member _.Keys = keys

    member _.TryFind(key: string) : JsonElement voption =
        match found.TryGetValue key with
        | true, value -> ValueSome value
        | _ -> ValueNone

/// <summary>The byte offset of the element's text from the root's, which identifies the element within its document.</summary>
let private offsetIn (root: JsonElement) (element: JsonElement) : int64 =
    let rootText = JsonMarshal.GetRawUtf8Value root
    let text = JsonMarshal.GetRawUtf8Value element
    int64 (Unsafe.ByteOffset (&MemoryMarshal.GetReference rootText, &MemoryMarshal.GetReference text))

/// <summary>Raised by <c>EchoBuffer</c> on a request for memory once it holds its capacity.</summary>
type private EchoFull() =
    inherit Exception("The echo buffer is full.")

/// <summary>A buffer writer holding the first <c>capacity</c> bytes written to it.</summary>
type private EchoBuffer(capacity: int) =
    let kept: byte array = Array.zeroCreate capacity
    let mutable count = 0
    let mutable current: byte array = Array.empty

    /// <summary>The bytes held, as UTF-8 text.</summary>
    member _.Text = Encoding.UTF8.GetString (kept, 0, min count capacity)

    interface IBufferWriter<byte> with
        member _.Advance written =
            let held = min count capacity
            Array.blit current 0 kept held (min written (capacity - held))
            count <- count + written

        member _.GetMemory sizeHint =
            if count >= capacity then
                raise (EchoFull ())

            current <- Array.zeroCreate (max sizeHint 256)
            Memory<byte> current

        member this.GetSpan sizeHint =
            (this :> IBufferWriter<byte>).GetMemory(sizeHint).Span

/// <summary>
/// The element's text for an error message: a number's source text, or at most 256 characters of any other value's
/// indented text, written into a buffer of 1,024 bytes.
/// </summary>
let private echo (element: JsonElement) : string =
    if element.ValueKind = JsonValueKind.Number then
        element.GetRawText ()
    else
        let buffer = EchoBuffer 1024

        try
            use writer =
                new Utf8JsonWriter (buffer, JsonWriterOptions (Indented = true, NewLine = "\n", IndentSize = 4))

            element.WriteTo writer
            writer.Flush ()
        with :? EchoFull ->
            ()

        capEcho buffer.Text

/// <summary>
/// The System.Text.Json decoder helpers for one decode of the document whose root is <c>root</c>. An object's members
/// are read once, so each lookup is a hash lookup and <c>getProperties</c> gives the keys in ECMAScript own-property
/// order; a value is echoed as <c>echo</c> gives it.
/// </summary>
let netDecoding (root: JsonElement) : IDecoderHelpers<JsonElement> =
    let inner = Ranvier.Wire.Thoth.Json.System.Text.Json.Decode.helpers
    let objects = Dictionary<int64, ObjectMembers>()

    let membersOf (value: JsonElement) =
        if value.ValueKind <> JsonValueKind.Object then
            raise (InvalidOperationException "Object members are read from a JSON object only.")

        let offset = offsetIn root value

        match objects.TryGetValue offset with
        | true, members -> members
        | _ ->
            let members = ObjectMembers value
            objects[offset] <- members
            members

    { new IDecoderHelpers<JsonElement> with
        member _.isString value =
            inner.isString value

        member _.isNumber value =
            inner.isNumber value

        member _.isBoolean value =
            inner.isBoolean value

        member _.isNullValue value =
            inner.isNullValue value

        member _.isArray value =
            inner.isArray value

        member _.isObject value =
            inner.isObject value

        member _.hasProperty name value =
            value.ValueKind = JsonValueKind.Object
            && (membersOf value).TryFind(name).IsSome

        member _.isIntegralValue value =
            inner.isIntegralValue value

        member _.asString value =
            inner.asString value

        member _.asFloat value =
            inner.asFloat value

        member _.asFloat32 value =
            inner.asFloat32 value

        member _.asInt value =
            inner.asInt value

        member _.asBoolean value =
            inner.asBoolean value

        member _.asArray value =
            inner.asArray value

        member _.getProperty(name, value) =
            match (membersOf value).TryFind name with
            | ValueSome found -> found
            | ValueNone -> raise (KeyNotFoundException ("The object has no member named " + name + "."))

        member _.getProperties value =
            (membersOf value).Keys :> string seq

        member _.anyToString value =
            echo value

        member _.numberToString value =
            inner.numberToString value
    }

/// <summary>A number's text, read verbatim from the document.</summary>
let netNumberText (value: JsonElement) : string option =
    Some (value.GetRawText ())
#else
let private stringify (value: obj) : string =
    emitJsExpr value "JSON.stringify($0)"

/// <summary>
/// Appends the value's <c>JSON.stringify(value, null, 4)</c> text to the builder, stopping once the builder holds more
/// than 256 characters.
/// </summary>
let rec private render (builder: StringBuilder) (indent: string) (value: obj) : unit =
    if builder.Length <= echoLength then
        let inner = indent + "    "

        if JS.Constructors.Array.isArray value then
            let items: obj array = unbox value

            if items.Length = 0 then
                builder.Append "[]" |> ignore
            else
                builder.Append "[" |> ignore
                let mutable i = 0

                while i < items.Length && builder.Length <= echoLength do
                    builder.Append(if i = 0 then "\n" else ",\n").Append inner
                    |> ignore

                    render builder inner items[i]
                    i <- i + 1

                builder.Append("\n").Append(indent).Append "]"
                |> ignore
        elif emitJsExpr value "$0 !== null && typeof $0 === 'object' && !($0 instanceof Number)" then
            let keys: string array = emitJsExpr value "Object.keys($0)"

            if keys.Length = 0 then
                builder.Append "{}" |> ignore
            else
                builder.Append "{" |> ignore
                let mutable i = 0

                while i < keys.Length && builder.Length <= echoLength do
                    builder.Append(if i = 0 then "\n" else ",\n").Append(inner).Append(stringify (box keys[i])).Append ": "
                    |> ignore

                    render builder inner (emitJsExpr (value, keys[i]) "$0[$1]")
                    i <- i + 1

                builder.Append("\n").Append(indent).Append "}"
                |> ignore
        elif
            value :? string
            && (unbox<string> value).Length > echoLength
        then
            builder.Append (stringify (box ((unbox<string> value).Substring(0, echoLength + 1))))
            |> ignore
        else
            builder.Append (stringify value) |> ignore

/// <summary>The JavaScript encoder helpers, writing each object with a null prototype so that every key is its own property.</summary>
let fableEncoding: IEncoderHelpers<obj> =
    let inner = Ranvier.Wire.Thoth.Json.JavaScript.Encode.helpers

    { new IEncoderHelpers<obj> with
        member _.encodeString value =
            inner.encodeString value

        member _.encodeChar value =
            inner.encodeChar value

        member _.encodeDecimalNumber value =
            inner.encodeDecimalNumber value

        member _.encodeBool value =
            inner.encodeBool value

        member _.encodeNull() =
            inner.encodeNull ()

        member _.encodeObject values =
            let o: obj = emitJsExpr () "Object.create(null)"

            for key, value in values do
                o?(key) <- value

            o

        member _.encodeArray values =
            inner.encodeArray values

        member _.encodeList values =
            inner.encodeList values

        member _.encodeSeq values =
            inner.encodeSeq values

        member _.encodeResizeArray values =
            inner.encodeResizeArray values

        member _.encodeSignedIntegralNumber value =
            inner.encodeSignedIntegralNumber value

        member _.encodeUnsignedIntegralNumber value =
            inner.encodeUnsignedIntegralNumber value
    }

/// <summary>
/// The JavaScript decoder helpers, reading only an object's own properties, and echoing a number as its source text
/// and any other value as at most 256 characters of its <c>JSON.stringify(value, null, 4)</c> text.
/// </summary>
let fableDecoding: IDecoderHelpers<obj> =
    let inner = Ranvier.Wire.Thoth.Json.JavaScript.Decode.helpers

    { new IDecoderHelpers<obj> with
        member _.isString value =
            inner.isString value

        member _.isNumber value =
            inner.isNumber value

        member _.isBoolean value =
            inner.isBoolean value

        member _.isNullValue value =
            inner.isNullValue value

        member _.isArray value =
            inner.isArray value

        member _.isObject value =
            inner.isObject value

        member _.hasProperty name value =
            emitJsExpr (value, name) "Object.prototype.hasOwnProperty.call($0, $1)"

        member _.isIntegralValue value =
            inner.isIntegralValue value

        member _.asString value =
            inner.asString value

        member _.asFloat value =
            inner.asFloat value

        member _.asFloat32 value =
            inner.asFloat32 value

        member _.asInt value =
            inner.asInt value

        member _.asBoolean value =
            inner.asBoolean value

        member _.asArray value =
            inner.asArray value

        member _.getProperty(name, value) =
            emitJsExpr (value, name) "Object.prototype.hasOwnProperty.call($0, $1) ? $0[$1] : undefined"

        member _.getProperties value =
            inner.getProperties value

        member _.anyToString value =
            if inner.isNumber value then
                inner.anyToString value
            else
                let builder = StringBuilder ()
                render builder "" value
                capEcho (builder.ToString ())

        member _.numberToString value =
            inner.numberToString value
    }
#endif
