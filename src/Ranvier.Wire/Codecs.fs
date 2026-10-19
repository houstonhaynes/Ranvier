namespace Ranvier.Wire

open System
open System.Globalization
open System.Numerics
open Ranvier.Wire.Thoth.Json.Core

/// <summary>
/// Codecs with the same wire text and the same decoding on .NET and Fable, for the types whose Thoth.Json codecs
/// differ between the two.
/// </summary>
[<RequireQualifiedAccess>]
module Codecs =

    let private millisecondsPerDay = 86400000L
    let private minUtcMilliseconds = -62135596800000L
    let private maxUtcMilliseconds = 253402300799999L

    let private floorDiv (a: int64) (b: int64) =
        let q = a / b
        if a % b <> 0L && ((a < 0L) <> (b < 0L)) then q - 1L else q

    /// <summary>The proleptic Gregorian year, month and day of a day count from 1970-01-01.</summary>
    let private civilFromDays (days: int64) =
        let z = days + 719468L
        let era = floorDiv z 146097L
        let dayOfEra = z - era * 146097L

        let yearOfEra =
            (dayOfEra - dayOfEra / 1460L + dayOfEra / 36524L
             - dayOfEra / 146096L)
            / 365L

        let dayOfYear =
            dayOfEra
            - (365L * yearOfEra + yearOfEra / 4L
               - yearOfEra / 100L)

        let shiftedMonth = (5L * dayOfYear + 2L) / 153L
        let day = dayOfYear - (153L * shiftedMonth + 2L) / 5L + 1L

        let month =
            if shiftedMonth < 10L then
                shiftedMonth + 3L
            else
                shiftedMonth - 9L

        let year = yearOfEra + era * 400L
        (if month <= 2L then year + 1L else year), month, day

    /// <summary>The day count from 1970-01-01 of a proleptic Gregorian date.</summary>
    let private daysFromCivil (year: int64) (month: int64) (day: int64) =
        let y = if month <= 2L then year - 1L else year
        let era = floorDiv y 400L
        let yearOfEra = y - era * 400L
        let shiftedMonth = if month > 2L then month - 3L else month + 9L
        let dayOfYear = (153L * shiftedMonth + 2L) / 5L + day - 1L

        let dayOfEra =
            yearOfEra * 365L + yearOfEra / 4L
            - yearOfEra / 100L
            + dayOfYear

        era * 146097L + dayOfEra - 719468L

    let private daysInMonth (year: int64) (month: int64) =
        match month with
        | 2L ->
            if
                (year % 4L = 0L && year % 100L <> 0L)
                || year % 400L = 0L
            then
                29L
            else
                28L
        | 4L
        | 6L
        | 9L
        | 11L -> 30L
        | _ -> 31L

    let private padded (width: int) (value: int64) =
        let text = string value

        if text.Length >= width then
            text
        else
            String.replicate (width - text.Length) "0" + text

    /// <summary>
    /// The value's clock time in its own offset as <c>yyyy-MM-ddTHH:mm:ss.fff±HH:mm</c>, truncated to the
    /// millisecond at or before the instant.
    /// </summary>
    let private formatDateTimeOffset (value: DateTimeOffset) =
        let offset = int64 value.Offset.TotalMinutes
        let local = value.ToUnixTimeMilliseconds () + offset * 60000L
        let days = floorDiv local millisecondsPerDay
        let ofDay = local - days * millisecondsPerDay
        let year, month, day = civilFromDays days
        let size = abs offset

        padded 4 year
        + "-"
        + padded 2 month
        + "-"
        + padded 2 day
        + "T"
        + padded 2 (ofDay / 3600000L)
        + ":"
        + padded 2 (ofDay / 60000L % 60L)
        + ":"
        + padded 2 (ofDay / 1000L % 60L)
        + "."
        + padded 3 (ofDay % 1000L)
        + (if offset < 0L then "-" else "+")
        + padded 2 (size / 60L)
        + ":"
        + padded 2 (size % 60L)

    /// <summary>The value of <c>count</c> ASCII digits from <c>start</c>, or -1 when any is not a digit.</summary>
    let private digitsAt (text: string) (start: int) (count: int) =
        let mutable result = 0L
        let mutable i = start

        while result >= 0L && i < start + count do
            let c = text[i]

            result <-
                if c >= '0' && c <= '9' then
                    result * 10L + int64 (int c - int '0')
                else
                    -1L

            i <- i + 1

        result

    /// <summary>The text without its trailing spaces, tabs, carriage returns and line feeds.</summary>
    let private trimTrailingWhitespace (text: string) =
        let mutable length = text.Length

        while length > 0
              && (text[length - 1] = ' '
                  || text[length - 1] = '\t'
                  || text[length - 1] = '\r'
                  || text[length - 1] = '\n') do
            length <- length - 1

        text.Substring (0, length)

    /// <summary>
    /// The UTC Unix milliseconds and the offset in minutes of a <c>yyyy-MM-dd(T|t)HH:mm:ss[.f{1,7}](Z|z|±HH:mm)</c>
    /// text followed by optional trailing whitespace, or <c>None</c>. Fraction digits after the third are dropped.
    /// </summary>
    let private parseDateTimeOffset (wire: string) : (int64 * int64) option =
        let text = trimTrailingWhitespace wire

        let at (i: int) (c: char) =
            text.Length > i && text[i] = c

        if
            text.Length < 20
            || not (
                at 4 '-'
                && at 7 '-'
                && (at 10 'T' || at 10 't')
                && at 13 ':'
                && at 16 ':'
            )
        then
            None
        else
            let year = digitsAt text 0 4
            let month = digitsAt text 5 2
            let day = digitsAt text 8 2
            let hour = digitsAt text 11 2
            let minute = digitsAt text 14 2
            let second = digitsAt text 17 2
            let mutable i = 19
            let mutable fraction = 0L
            let mutable fractionDigits = 0
            let mutable fractionValid = true

            if at 19 '.' then
                i <- 20

                while i < text.Length
                      && text[i] >= '0'
                      && text[i] <= '9' do
                    if fractionDigits < 3 then
                        fraction <- fraction * 10L + int64 (int text[i] - int '0')

                    fractionDigits <- fractionDigits + 1
                    i <- i + 1

                fractionValid <- fractionDigits >= 1 && fractionDigits <= 7

            let milliseconds =
                if fractionDigits = 1 then fraction * 100L
                elif fractionDigits = 2 then fraction * 10L
                else fraction

            let offset =
                if not fractionValid then
                    None
                elif
                    text.Length = i + 1
                    && (text[i] = 'Z' || text[i] = 'z')
                then
                    Some 0L
                elif
                    text.Length = i + 6
                    && (text[i] = '+' || text[i] = '-')
                    && text[i + 3] = ':'
                then
                    let hours = digitsAt text (i + 1) 2
                    let minutes = digitsAt text (i + 4) 2

                    if
                        hours < 0L
                        || minutes < 0L
                        || minutes > 59L
                        || hours * 60L + minutes > 840L
                    then
                        None
                    elif text[i] = '-' then
                        Some -(hours * 60L + minutes)
                    else
                        Some (hours * 60L + minutes)
                else
                    None

            match offset with
            | Some offset when
                year >= 1L
                && month >= 1L
                && month <= 12L
                && day >= 1L
                && day <= daysInMonth year month
                && hour >= 0L
                && hour <= 23L
                && minute >= 0L
                && minute <= 59L
                && second >= 0L
                && second <= 59L
                ->
                let local =
                    daysFromCivil year month day * millisecondsPerDay
                    + hour * 3600000L
                    + minute * 60000L
                    + second * 1000L
                    + milliseconds

                let utc = local - offset * 60000L

                if
                    utc < minUtcMilliseconds
                    || utc > maxUtcMilliseconds
                then
                    None
                else
                    Some (utc, offset)
            | _ -> None

    let private dateTimeOffsetDecoder: Decoder<DateTimeOffset> =
        { new Decoder<DateTimeOffset> with
            member _.Decode(helpers, value) =
                let parsed =
                    if helpers.isString value then
                        parseDateTimeOffset (helpers.asString value)
                    else
                        None

                match parsed with
                | Some (utc, offset) -> Ok (DateTimeOffset.FromUnixTimeMilliseconds(utc).ToOffset(TimeSpan.FromMinutes (float offset)))
                | None -> Error ("", BadPrimitive ("a datetimeoffset written yyyy-MM-dd(T|t)HH:mm:ss[.fffffff](Z|z|+HH:mm|-HH:mm)", value))
        }

    /// <summary>
    /// A DateTimeOffset as the string <c>yyyy-MM-ddTHH:mm:ss.fff±HH:mm</c> of its clock time in its own offset.
    /// Encoding truncates to the millisecond. Decoding reads an optional fraction of 1 to 7 digits, <c>Z</c> and
    /// <c>-00:00</c> as well, reads <c>t</c> and <c>z</c> as <c>T</c> and <c>Z</c>, ignores trailing spaces, tabs,
    /// carriage returns and line feeds, and rejects instants outside 0001-01-01T00:00:00Z to
    /// 9999-12-31T23:59:59.999Z.
    /// </summary>
    let dateTimeOffset: Codec<DateTimeOffset> =
        Codec.create (fun value -> Encode.string (formatDateTimeOffset value)) dateTimeOffsetDecoder

    let private allDigits (text: string) =
        let mutable result = text.Length > 0
        let mutable i = 0

        while result && i < text.Length do
            result <- text[i] >= '0' && text[i] <= '9'
            i <- i + 1

        result

    let private unsigned (text: string) =
        if text.Length > 0 && text[0] = '-' then
            text.Substring 1
        else
            text

    /// <summary>Whether the text is <c>-?digits(.digits)?</c>.</summary>
    let private isDecimalText (text: string) =
        let body = unsigned text
        let point = body.IndexOf '.'

        if point < 0 then
            allDigits body
        else
            allDigits (body.Substring (0, point))
            && allDigits (body.Substring (point + 1))

    /// <summary>Whether a decimal text has at most 28 fraction digits and a mantissa of at most Decimal.MaxValue's.</summary>
    let private fitsDecimal (text: string) =
        let body = unsigned text
        let point = body.IndexOf '.'
        let fractionDigits = if point < 0 then 0 else body.Length - point - 1
        let mantissa = body.Replace(".", "").TrimStart '0'

        fractionDigits <= 28
        && (mantissa.Length < 29
            || (mantissa.Length = 29
                && String.CompareOrdinal (mantissa, "79228162514264337593543950335")
                   <= 0))

    /// <summary>The text of a JSON string, or the source text of a JSON number.</summary>
    let private numericText (helpers: IDecoderHelpers<'J>) (value: 'J) =
        if helpers.isString value then
            Some (helpers.asString value)
        elif helpers.isNumber value then
            Some (helpers.numberToString value)
        else
            None

    let private bigintDecoder: Decoder<BigInteger> =
        { new Decoder<BigInteger> with
            member _.Decode(helpers, value) =
                match numericText helpers value with
                | Some text when allDigits (unsigned text) ->
                    let digits = (unsigned text).Length

                    let cap, raises =
                        match box helpers with
                        | :? Helpers.DecodeScope as scope -> scope.MaxBigIntDigits, not scope.LimitIsError
                        | _ -> WireLimits.Default.MaxBigIntDigits, false

                    if digits <= cap then
#if FABLE_COMPILER
                        Ok (BigInteger.Parse text)
#else
                        Ok (BigInteger.Parse (text, CultureInfo.InvariantCulture))
#endif
                    else
                        let message =
                            "The bigint has "
                            + string digits
                            + " digits, more than "
                            + string cap
                            + "."

                        if raises then
                            raise (Helpers.LimitExceeded message)
                        else
                            Error ("", BadPrimitiveExtra ("a bigint", value, message))
                | _ -> Error ("", BadPrimitive ("a bigint written as digits", value))
        }

    let private decimalDecoder: Decoder<decimal> =
        { new Decoder<decimal> with
            member _.Decode(helpers, value) =
                match numericText helpers value with
                | Some text when isDecimalText text ->
                    if fitsDecimal text then
#if FABLE_COMPILER
                        Ok (Decimal.Parse text)
#else
                        Ok (
                            Decimal.Parse (
                                text,
                                NumberStyles.AllowLeadingSign
                                ||| NumberStyles.AllowDecimalPoint,
                                CultureInfo.InvariantCulture
                            )
                        )
#endif
                    else
                        Error ("", BadPrimitiveExtra ("a decimal", value, "Value is outside the decimal range or has more than 28 fraction digits"))
                | _ -> Error ("", BadPrimitive ("a decimal written as digits with an optional fraction", value))
        }

    /// <summary>
    /// A BigInteger as a string of digits. Decoding reads a string or a number written as <c>-?digits</c>, exactly,
    /// with at most <c>WireLimits.MaxBigIntDigits</c> digits; a <c>WireCodec</c> gives more as <c>WireError.Limit</c>
    /// naming the value's path. Outside a <c>WireCodec</c> the cap is <c>WireLimits.Default</c>'s, and more is a
    /// decoder error.
    /// </summary>
    let bigint: Codec<BigInteger> =
        Codec.create (fun (value: BigInteger) -> Encode.string (string value)) bigintDecoder

    /// <summary>
    /// A decimal as a string, keeping its scale. Decoding reads a string or a number written as
    /// <c>-?digits(.digits)?</c> that a decimal holds exactly.
    /// </summary>
    let decimal: Codec<decimal> =
        Codec.create (fun (value: decimal) -> Encode.string (value.ToString (CultureInfo.InvariantCulture))) decimalDecoder
