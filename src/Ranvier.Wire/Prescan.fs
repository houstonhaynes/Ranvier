module internal Ranvier.Wire.Prescan

let private isHigh (code: int) =
    code >= 0xD800 && code <= 0xDBFF

let private isLow (code: int) =
    code >= 0xDC00 && code <= 0xDFFF

/// <summary>The value of the four hex digits at <c>start</c>, or -1.</summary>
let private hex4 (text: string) (start: int) =
    let mutable result = if start + 4 <= text.Length then 0 else -1
    let mutable i = start

    while result >= 0 && i < start + 4 do
        let c = text[i]

        let digit =
            if c >= '0' && c <= '9' then int c - int '0'
            elif c >= 'a' && c <= 'f' then int c - int 'a' + 10
            elif c >= 'A' && c <= 'F' then int c - int 'A' + 10
            else -1

        result <- if digit < 0 then -1 else result * 16 + digit
        i <- i + 1

    result

let private loneSurrogate = "A string in the wire text holds a lone surrogate."

/// <summary>
/// The first problem found in the text before parsing: nesting past <c>MaxDepth</c> or more than <c>MaxItems</c>
/// items (<c>WireError.Limit</c>), or a null text, a closing bracket without its opening one, or a string holding a
/// lone surrogate, raw or escaped (<c>WireError.Syntax</c>). Brackets and commas inside strings are skipped; other
/// malformed text is left to the parser.
/// </summary>
let check (limits: WireLimits) (text: string) : WireError option =
    let mutable depth = 0
    let mutable items = 0
    let mutable inString = false
    // 0: no high surrogate pending; 1: a raw one; 2: an escaped one. A pair is two raw or two escaped code units.
    let mutable pendingHigh = 0
    let mutable opened = false

    let mutable failure =
        if isNull text then
            Some (WireError.Syntax "The wire text is null.")
        else
            None

    let mutable i = 0

    while failure.IsNone && i < text.Length do
        let c = text[i]

        if inString then
            // code: the code unit of a raw character or a \u escape, 0 for any other escape, -1 at the closing quote.
            // form: 1 for a raw character, 2 for an escape.
            let mutable form = 1

            let code =
                if c = '\\' then
                    let escaped =
                        if i + 1 < text.Length && text[i + 1] = 'u' then
                            hex4 text (i + 2)
                        else
                            -1

                    i <- i + (if escaped >= 0 then 5 else 1)
                    form <- 2
                    max escaped 0
                elif c = '"' then
                    inString <- false
                    -1
                else
                    int c

            if pendingHigh <> 0 then
                if isLow code && form = pendingHigh then
                    pendingHigh <- 0
                else
                    failure <- Some (WireError.Syntax loneSurrogate)
            elif isHigh code then
                pendingHigh <- form
            elif isLow code then
                failure <- Some (WireError.Syntax loneSurrogate)
        elif c <> ' ' && c <> '\t' && c <> '\n' && c <> '\r' then
            if opened then
                opened <- false

                if c <> ']' && c <> '}' then
                    items <- items + 1

            match c with
            | '"' -> inString <- true
            | '['
            | '{' ->
                depth <- depth + 1
                opened <- true

                if depth > limits.MaxDepth then
                    failure <-
                        Some (
                            WireError.Limit (
                                "The wire text nests deeper than "
                                + string limits.MaxDepth
                                + " levels."
                            )
                        )
            | ']'
            | '}' ->
                depth <- depth - 1

                if depth < 0 then
                    failure <- Some (WireError.Syntax "The wire text closes more brackets than it opens.")
            | ',' -> items <- items + 1
            | _ -> ()

            if failure.IsNone && items > limits.MaxItems then
                failure <-
                    Some (
                        WireError.Limit (
                            "The wire text holds more than "
                            + string limits.MaxItems
                            + " items."
                        )
                    )

        i <- i + 1

    failure
