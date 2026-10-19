module internal Ranvier.Wire.Numbers

#if !FABLE_COMPILER
open System
open System.Globalization

/// <summary>
/// ECMAScript's <c>Number::toString</c> of a finite double: the text <c>JSON.stringify</c> writes, with
/// <c>-0</c> written as <c>0</c>.
/// </summary>
let ecmaString (value: float) : string =
    if value = 0.0 then
        "0"
    else
        let shortest = value.ToString ("R", CultureInfo.InvariantCulture)
        let negative = shortest[0] = '-'
        let body = if negative then shortest.Substring 1 else shortest
        let e = body.IndexOf 'E'
        let mantissa = if e < 0 then body else body.Substring (0, e)

        let exponent =
            if e < 0 then
                0
            else
                Int32.Parse (body.Substring (e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)

        let point = mantissa.IndexOf '.'

        let whole =
            if point < 0 then
                mantissa
            else
                mantissa.Substring (0, point)

        let fraction = if point < 0 then "" else mantissa.Substring (point + 1)
        let all = whole + fraction
        let mutable lead = 0

        while all[lead] = '0' do
            lead <- lead + 1

        let digits = all.Substring(lead).TrimEnd '0'
        let k = digits.Length
        let n = whole.Length - lead + exponent

        let text =
            if k <= n && n <= 21 then
                digits + String.replicate (n - k) "0"
            elif 0 < n && n <= 21 then
                digits.Substring (0, n) + "." + digits.Substring n
            elif -6 < n && n <= 0 then
                "0." + String.replicate -n "0" + digits
            else
                let shown = n - 1

                let head =
                    if k = 1 then
                        digits
                    else
                        digits.Substring (0, 1) + "." + digits.Substring 1

                head
                + "e"
                + (if shown < 0 then "-" else "+")
                + string (abs shown)

        if negative then "-" + text else text
#endif
