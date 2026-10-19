namespace Ranvier.Wire

/// <summary>Why a wire text failed to decode.</summary>
[<RequireQualifiedAccess>]
type WireError =
    /// <summary>The text is null or not JSON, or one of its strings holds a lone surrogate.</summary>
    | Syntax of message: string
    /// <summary>The text exceeds one of the codec's <see cref="T:Ranvier.Wire.WireLimits"/>.</summary>
    | Limit of message: string
    /// <summary>The JSON does not have the shape of the decoded type.</summary>
    | Shape of message: string

    /// <summary>The text carried by the case.</summary>
    member this.Message =
        match this with
        | Syntax message
        | Limit message
        | Shape message -> message

/// <summary>
/// The bounds on a decoded wire text: its nesting depth and item count, checked before parsing, and the digits of each
/// <c>Codecs.bigint</c> value.
/// </summary>
type WireLimits =
    {
        /// <summary>
        /// The deepest nesting of arrays and objects, between 1 and 1000 (System.Text.Json's writer limit); checked before
        /// parsing.
        /// </summary>
        MaxDepth: int
        /// <summary>The total number of array elements and object members, at least 1; checked before parsing.</summary>
        MaxItems: int
        /// <summary>The most digits a <c>Codecs.bigint</c> value has, between 1 and 100,000.</summary>
        MaxBigIntDigits: int
    }

    /// <summary>A depth of 64, as System.Text.Json's default, 100,000 items and 4,300 bigint digits.</summary>
    static member Default =
        {
            MaxDepth = 64
            MaxItems = 100_000
            MaxBigIntDigits = 4_300
        }

/// <summary>Writes a <c>'T</c> as wire text and reads it back, with the same text on .NET and Fable.</summary>
type WireCodec<'T> =
    /// <summary>
    /// The wire text of the value; a null string is written as JSON <c>null</c>. Raises <c>ArgumentException</c>,
    /// naming the index, when a string, char or object key in the value holds a lone surrogate; under Fable a JS
    /// <c>Error</c> is thrown. On .NET, also raises for a value nested deeper than 1000 levels.
    /// </summary>
    abstract Encode: value: 'T -> string
    /// <summary>
    /// The value the text denotes, or the reason it denotes none; every failure is an <c>Error</c>, and a null text
    /// is <c>WireError.Syntax</c>.
    /// </summary>
    abstract Decode: text: string -> Result<'T, WireError>

module internal TestAccess =
    [<assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Ranvier.Tests.Fable")>]
    do ()
