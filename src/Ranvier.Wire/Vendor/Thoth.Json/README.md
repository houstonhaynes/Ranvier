# Vendored Thoth.Json

Sources of [Thoth.Json](https://github.com/thoth-org/Thoth.Json) by Maxime Mangel, MIT licence ([LICENSE.txt](LICENSE.txt)),
compiled into Ranvier.Wire.

| | |
|---|---|
| Upstream commit | `de668a4a6378245d86a96427cc90c5da23d195e2` ("chore: release multiple projects (#248)"; Thoth.Json.Core 0.10.0, Thoth.Json.JavaScript 0.10.0, Thoth.Json.System.Text.Json 0.10.0 sources) |
| Packages | `Thoth.Json.Core` (both targets), `Thoth.Json.System.Text.Json` (.NET), `Thoth.Json.JavaScript` (Fable), under the namespaces `Ranvier.Wire.Thoth.Json.Core`, `Ranvier.Wire.Thoth.Json.System.Text.Json` and `Ranvier.Wire.Thoth.Json.JavaScript` |

## Patches

Applied in name order by `refresh.ps1`. CI runs `refresh.ps1 -Check`, which fails when a file here differs from the commit plus these patches, byte for byte
(CRLF read as LF, a byte order mark included), when a package directory holds another file, or when the SHA-256 table
below differs from the patch files. The script fetches only the pinned commit.

| Patch | Change | Drop when |
|---|---|---|
| `0001-thoth-pr-265-nativeaot.patch` | [thoth-org/Thoth.Json#265](https://github.com/thoth-org/Thoth.Json/pull/265) at `e22fd43`: the two typed interpolations become concatenation and the generic `integral` decoder takes `float` bounds, so both assemblies publish with NativeAOT, rooted, with no IL diagnostic | #265 is released and this directory is replaced by the released packages |
| `0002-target-guards.patch` | wraps the System.Text.Json files in `#if !FABLE_COMPILER` and the JavaScript files in `#if FABLE_COMPILER` | this directory is replaced by package references |
| `0003-linear-list-decode.patch` | `Decode.list` conses and reverses instead of appending, so a list decodes in linear time | the same change is released upstream |
| `0004-ranvier-namespaces.patch` | prefixes `Ranvier.Wire.` to every namespace, `open` and `cref` of `Thoth.Json.Core`, `Thoth.Json.System.Text.Json` and `Thoth.Json.JavaScript`, so an app that also references the upstream packages gets no duplicate types | this directory is replaced by package references (callers then open `Thoth.Json.*`) |
| `0005-obsolete-target-dependent-codecs.patch` | marks `Codec`, `Encode` and `Decode` `bigint`, `decimal`, `datetimeOffset` and `timespan`, and `Encode.datetime`, `Decode.datetimeUtc` and `Decode.datetimeLocal`, `[<Obsolete>]`, pointing to `Ranvier.Wire.Codecs` (for `timespan`, to the Ranvier.Wire README's target differences), and adds `#nowarn "44"` to `Codec.fs`, whose obsolete codecs use the obsolete members | this directory is replaced by package references |
| `0006-finite-float-decode.patch` | `Decode.float` returns an error for a number outside the double range (`1e400`), and `Decode.float32` for a number whose double value rounds to ±∞ as a float32 (`1e39`, and `3.4028235677973366e38` upward), instead of ±∞; the strings `"Infinity"` and `"-Infinity"` still decode | the same change is released upstream |
| `0007-decimal-integer-strings.patch` | on both targets, the integer decoders up to `uint32` and `Decode.int64`/`Decode.uint64` read a string only when it is optional ASCII whitespace, an optional sign, decimal digits and optional ASCII whitespace. Without it, fable-library's parse also reads `0x`/`0o`/`0b` prefixes, `_` separators and other whitespace and wraps signed values (`"0xFF"` as an sbyte is -1), and .NET's reads trailing NUL characters and, for `int64`/`uint64`, exponents (`"1e3"`) | the same change is released upstream |

SHA-256 of each patch file, as committed (LF line ends, kept by `.gitattributes`):

| Patch | SHA-256 |
|---|---|
| 0001 | `9629a7e2e081e17dd68d3f0eab89693900b7b6f21d8453ac84af11e7ed6bee66` |
| 0002 | `482e9341ef350e00a176202e7aad0adab32a636f818ad2096d002955b1843e0e` |
| 0003 | `f8bb71680fe91eb0d0883150f72e75035d63733887a8b15241a4417dbfdcdfa9` |
| 0004 | `0d468cd5d839eaeaca24acc30c76331ac492e4ca45f29a64cb4b636fd04a65d8` |
| 0005 | `13863b44372d67f98bbaf97acfb84ab4c968a6d3b8c86e6528ebdffc2817cc1a` |
| 0006 | `5277419dddb815c9185311f1f593c4531dfcf3a64da2dea22297269f2e6ef763` |
| 0007 | `457ca53745eef7d42e11cc895a3d9170614f5818a20880b3f89bd02aae97dede` |

## Changing the vendored files

Edit only through a patch: add or change a file in `patches/`, run `pwsh refresh.ps1`, review `git diff`, and update
the SHA-256 table. Fantomas and the repository's comment rules do not apply here (`.fantomasignore`).
