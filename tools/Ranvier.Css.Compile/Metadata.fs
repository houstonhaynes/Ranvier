namespace Ranvier.Css.Compile

open System
open System.IO
open System.Globalization
open System.Text.Json
open Ranvier.Css

module internal Metadata =
    type CssValue = { Value: Value<unit>; Marker: string }

    type Primitive =
        {
            Marker: string
            Production: string
            Method: string
            Argument: string
        }

    type Property =
        {
            Name: string
            Keywords: Set<string>
            Paths: Map<string, ((float * string) option * (float * string) option) list>
        }

    type Registry =
        {
            Namespace: string
            Primitives: Map<string, Primitive>
            Keywords: Map<string, string>
            Properties: Map<string, Property>
        }

    let private string (name: string) (item: JsonElement) : string =
        item.GetProperty(name).GetString()

    let load path =
        use document = JsonDocument.Parse (File.ReadAllText path)
        let sugar = document.RootElement.GetProperty "sugar"

        if sugar.GetProperty("schemaVersion").GetInt32() <> 2 then
            failwith "CSS sugar metadata schema 2 is required. Regenerate the specification bindings."

        let bound item =
            if string "kind" item = "finite" then
                Some (Double.Parse (string "value" item, CultureInfo.InvariantCulture), string "unitName" item)
            else
                None

        let range (item: JsonElement) =
            bound (item.GetProperty "minimum"), bound (item.GetProperty "maximum")

        {
            Namespace = string "namespaceName" sugar
            Primitives =
                sugar.GetProperty("primitives").EnumerateArray()
                |> Seq.map (fun item ->
                    string "memberName" item,
                    {
                        Marker = string "marker" item
                        Production = string "production" item
                        Method = string "methodName" item
                        Argument = string "argumentType" item
                    })
                |> Map.ofSeq
            Keywords =
                sugar.GetProperty("keywords").EnumerateArray()
                |> Seq.map (fun item -> string "memberName" item, string "token" item)
                |> Map.ofSeq
            Properties =
                sugar.GetProperty("properties").EnumerateArray()
                |> Seq.filter (fun item ->
                    item.GetProperty("scopes").GetArrayLength() = 0
                    && not (item.GetProperty("obsolete").GetBoolean()))
                |> Seq.map (fun item ->
                    let definition =
                        {
                            Name = string "cssName" item
                            Keywords =
                                item.GetProperty("acceptedKeywords").EnumerateArray()
                                |> Seq.map _.GetString()
                                |> Set.ofSeq
                            Paths =
                                item.GetProperty("acceptedValues").EnumerateArray()
                                |> Seq.map (fun path ->
                                    string "marker" path,
                                    path.GetProperty("ranges").EnumerateArray()
                                    |> Seq.map range
                                    |> Seq.toList)
                                |> Map.ofSeq
                        }

                    string "memberName" item, definition)
                |> Map.ofSeq
        }

    let primitive (definition: Primitive) (argument: obj) =
        let value: Value<unit> =
            match definition.Argument, definition.Production with
            | "float", _ ->
                let unitName =
                    if definition.Production = "percentage" then "%"
                    elif definition.Production = "number" then ""
                    else definition.Method

                Generated.number (Convert.ToDouble (argument, CultureInfo.InvariantCulture)) unitName
            | "int", _ -> Generated.number (float (Convert.ToInt32 (argument, CultureInfo.InvariantCulture))) ""
            | "none", "zero" -> Generated.value "0"
            | "string", "string" -> Generated.stringValue (unbox argument)
            | "string", "custom-ident" -> Generated.identifier false true (unbox argument)
            | "string", "dashed-ident"
            | "string", "custom-property-name" -> Generated.identifier true false (unbox argument)
            | "string", "ident" -> Generated.identifier false false (unbox argument)
            | _ -> invalidOp ("Unsupported primitive: " + definition.Production)

        {
            Value = value
            Marker = definition.Marker
        }

    let declaration (definition: Property) (value: CssValue) =
        match definition.Paths.TryFind value.Marker with
        | Some paths ->
            let checkedValue =
                paths
                |> List.fold (fun value (minimum, maximum) -> Generated.range minimum maximum value: Value<unit>) value.Value

            Declaration.create definition.Name (Value.render checkedValue)
        | None when definition.Keywords.Contains (Value.render value.Value) -> Declaration.create definition.Name (Value.render value.Value)
        | None ->
            invalidOp (
                "Unsupported typed conversion for "
                + definition.Name
                + " from "
                + value.Marker
            )
