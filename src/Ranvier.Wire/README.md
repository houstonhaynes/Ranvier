# Ranvier.Wire

The JSON wire codec of Ranvier's transports. A `WireCodec<'T>` writes a value as text and reads it back with the same
text on .NET, including NativeAOT, and under Fable. It is built on a vendored copy of
[Thoth.Json](https://github.com/thoth-org/Thoth.Json) ([Vendor/Thoth.Json](Vendor/Thoth.Json/README.md)) under the
namespace `Ranvier.Wire.Thoth.Json.Core`: write the contract's codecs with Thoth's `objectCodec` and `variantCodec`,
and wrap them once.

```fsharp
open Ranvier.Wire.Thoth.Json.Core
open Ranvier.Wire

type Person = { Name: string; Born: System.DateTimeOffset }

let person: Codec<Person> =
    objectCodec {
        let! name = Codec.field "name" _.Name Codec.string
        and! born = Codec.field "born" _.Born Codecs.dateTimeOffset
        return { Name = name; Born = born }
    }

let wire = WireCodec.ofCodec person
let text = wire.Encode { Name = "Ada"; Born = System.DateTimeOffset(1815, 12, 10, 0, 0, 0, System.TimeSpan.Zero) }
let back = wire.Decode text // Ok ..., or Error (WireError.Syntax | Limit | Shape)
```

`Decode` returns every failure as an `Error`: `Syntax` for text that is null, not JSON or holds a lone surrogate,
`Limit` for text over the codec's `WireLimits` (depth 64 and 100,000 array elements plus object members, checked before
parsing, and 4,300 digits for a `Codecs.bigint` value, by default), and `Shape` for JSON that does not fit the type,
including an exception raised inside a decoder. A `Shape` message quotes a number value in full and at most 256
characters of any other value's JSON text, followed by `…`. `WireLimits.MaxDepth` is between 1 and 1000.

`Encode` raises `ArgumentException` for a value holding a lone surrogate; under Fable a JS `Error` is thrown. On .NET,
`Encode` also raises for a value nested deeper than 1000 levels, System.Text.Json's writer limit.

## Wire contract

A codec rejects only text that is not its wire form or holds a value the type cannot hold. It does no domain
validation: a date far in the past or a negative quantity decodes when the type holds it.

| Type | Wire form | Codec |
|---|---|---|
| `string`, `char` | Unicode scalar values only: a lone surrogate raises on encode and is a `Syntax` error on decode, raw or escaped, in a value or an object key | `Codec.string`, `Codec.char` |
| `DateTimeOffset` | `"yyyy-MM-ddTHH:mm:ss.fff±HH:mm"`, the clock time in its own offset; millisecond precision, truncated on encode, always written with upper-case `T`. Decoding also reads `Z`, `-00:00`, 1–7 fraction digits (keeping the first three), lower-case `t` and `z`, and trailing spaces, tabs, carriage returns and line feeds; it rejects every other form and any instant outside 0001-01-01T00:00:00Z to 9999-12-31T23:59:59.999Z | `Codecs.dateTimeOffset` |
| `bigint` | a string of digits; decoding also reads a number written `-?digits`, exactly, with at most `WireLimits.MaxBigIntDigits` digits (4,300 by default, 1–100,000) | `Codecs.bigint` |
| `decimal` | a string keeping the scale (`"1.10"`); decoding reads `-?digits(.digits)?` held exactly, as a string or a number | `Codecs.decimal` |
| `int64`, `uint64` | a string of digits; decoding reads an integer string, or a number with the target's integer parse (see the differences below) | `Codec.int64`, `Codec.uint64` |
| `float` | a number in ECMAScript `Number::toString` form (`1e+21`, `1.5e-7`); `-0` is written `0`; NaN and the infinities are the strings `"NaN"`, `"Infinity"`, `"-Infinity"`, which decode. Decoding rejects a number outside the double range (`1e400`) | `Codec.float` |
| `float32` | as `float`; decoding also rejects a number outside the float32 range (`1e39`) | `Codec.float32` |
| `sbyte` … `uint32` | a number; decoding reads a number with neither a `.` nor a negative exponent (`1e3` is 1000; `1.0` and `10e-1` are `Shape` errors) within the type's range, or an integer string | `Codec.int` and the others |

On both targets, an integer string for the codecs from `sbyte` to `uint64` is exactly optional ASCII whitespace
(space, tab, line feed, vertical tab, form feed or carriage return), an optional `+` or `-`, one or more decimal digits
and optional ASCII whitespace, holding a value within the type's range: `" 42"`, `"+42"` and `"0042"` decode as 42, and
`"-0"` as 0. Any other string is a `Shape` error, including `"0x10"`, `"1_000"`, `"1e3"` and a string holding U+FEFF
or U+0000.

On both targets an object's keys are written, and listed by `Codec.keyValuePairs` and `Decode.keyValuePairs`, in
JavaScript's own-property order: the keys that are array indexes (the canonical decimal text of an integer from 0 to
4294967294) first in ascending numeric order, then the other keys in insertion order, with a repeated key at its first
position holding its last value.

Use `Codecs.dateTimeOffset`, `Codecs.bigint` and `Codecs.decimal` for those types. Thoth's members below write or read
differently on the two targets and are marked obsolete (warning FS0044):

- `Codec`, `Encode` and `Decode` `datetimeOffset`, `bigint` and `decimal`;
- `Encode.datetime` (seven fraction digits on .NET, three under Fable), `Decode.datetimeUtc` and `Decode.datetimeLocal`
  (the current culture on .NET, the local zone under Fable), which point to `Codecs.dateTimeOffset`;
- `Codec`, `Encode` and `Decode` `timespan` (see the differences below). Ranvier.Wire has no `TimeSpan` codec yet.

On `netstandard2.1`, the ECMAScript number form relies on `Double.ToString("R")` giving the shortest round-trip
digits, as .NET Core 3.0 and later do; an older runtime such as Mono can write longer digits for some doubles.

## Differences between .NET and Fable

| Input | .NET | Fable |
|---|---|---|
| A string holding a character outside the Basic Multilingual Plane, DEL, U+2028, U+00A0 or other characters System.Text.Json escapes | escaped (`\uD83C\uDF89`), upper-case hex | raw, and lower-case hex for control characters (`\u001f`) |
| An `int64`/`uint64` number in exponent form (`1e3`, `10e-1`) | decodes when the value is a whole number in range (`10e-1` is 1) | `Shape` error |
| A `Codec.guid` string in doubled or unmatched braces or parentheses (`"{{…}}"`, `"{…)"`), or led by U+FEFF | `Shape` error | decodes |
| A `TimeSpan` through Thoth's obsolete `timespan` codec | written to the tick, read with `TimeSpan.TryParse` in the current culture | written to the millisecond, read with fable-library's own pattern |

Both targets read each other's string escapes to the same value. Both read a `Codec.guid` string in upper case, without
hyphens, in one pair of braces or parentheses, or with surrounding spaces and tabs, and write it lower-case with hyphens.

Fable reads a number's source text through `JSON.parse` source-text access (Node 21+, Chrome 114+, Firefox 135+,
Safari 18.4+). On an older engine a number decodes only where its value is exact: integers within ±(2^53 − 1) and
floats decode, a number whose text matters (an `int64` or `bigint` past 2^53, a `decimal` with a fraction) is a
`Shape` error, and numbers written as strings decode exactly. A number on such an engine reads as the safe integer its
value is, whatever its text: `1e3` and `1.0` read as 1000 and 1, also for `Codecs.bigint` and `Codecs.decimal`, and
`1e-400` reads as 0.
