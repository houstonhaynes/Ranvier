namespace Ranvier.Wire

open Ranvier.Wire.Thoth.Json.Core

/// <summary>Builds a <see cref="T:Ranvier.Wire.WireCodec`1"/> from a Thoth.Json codec.</summary>
[<RequireQualifiedAccess>]
module WireCodec =

    /// <summary>
    /// The wire codec of a Thoth.Json codec, decoding only texts within the limits. Raises
    /// <c>ArgumentException</c> when a limit is outside its range.
    /// </summary>
    let ofCodecWith (limits: WireLimits) (codec: Codec<'T>) : WireCodec<'T> =
        if limits.MaxDepth < 1 || limits.MaxDepth > 1000 then
            invalidArg
                "limits"
                ("MaxDepth must be between 1 and 1000; it is "
                 + string limits.MaxDepth
                 + ".")

        if limits.MaxItems < 1 then
            invalidArg
                "limits"
                ("MaxItems must be at least 1; it is "
                 + string limits.MaxItems
                 + ".")

        if
            limits.MaxBigIntDigits < 1
            || limits.MaxBigIntDigits > 100_000
        then
            invalidArg
                "limits"
                ("MaxBigIntDigits must be between 1 and 100000; it is "
                 + string limits.MaxBigIntDigits
                 + ".")

        { new WireCodec<'T> with
            member _.Encode value =
                Backend.encode (codec.Encoder value)

            member _.Decode text =
                Backend.decode limits codec.Decoder text
        }

    /// <summary>The wire codec of a Thoth.Json codec, with <c>WireLimits.Default</c>.</summary>
    let ofCodec (codec: Codec<'T>) : WireCodec<'T> =
        ofCodecWith WireLimits.Default codec
