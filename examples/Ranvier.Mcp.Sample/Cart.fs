/// A shopping cart with a percentage discount, held in an Mvu and exposed through the Ranvier MCP adapter.
module Ranvier.Mcp.Sample.Cart

open System.Text.Json
open System.Text.Json.Nodes
open Ranvier
open Ranvier.Elmish
open Ranvier.Mcp

/// A cart line. Prices are in cents.
type Line =
    {
        Sku: string
        UnitPrice: int
        Quantity: int
    }

type Model =
    {
        Lines: Line list
        /// <summary>From 0 to 100.</summary>
        DiscountPercent: int
    }

type Msg =
    /// <summary>Adds <c>quantity</c> units of <c>sku</c>; a line already holding <c>sku</c> keeps its price.</summary>
    | AddItem of sku: string * unitPrice: int * quantity: int
    | RemoveItem of sku: string
    | SetDiscount of percent: int

let init = { Lines = []; DiscountPercent = 0 }

let update (msg: Msg) (model: Model) =
    match msg with
    | AddItem (sku, unitPrice, quantity) ->
        if
            model.Lines
            |> List.exists (fun line -> line.Sku = sku)
        then
            { model with
                Lines =
                    model.Lines
                    |> List.map (fun line ->
                        if line.Sku = sku then
                            { line with
                                Quantity = line.Quantity + quantity
                            }
                        else
                            line)
            }
        else
            { model with
                Lines =
                    model.Lines
                    @ [
                        {
                            Sku = sku
                            UnitPrice = unitPrice
                            Quantity = quantity
                        }
                    ]
            }
    | RemoveItem sku ->
        { model with
            Lines =
                model.Lines
                |> List.filter (fun line -> line.Sku <> sku)
        }
    | SetDiscount percent -> { model with DiscountPercent = percent }

/// The sum of the line prices.
let subtotalOf (lines: Line list) =
    lines
    |> List.sumBy (fun line -> line.UnitPrice * line.Quantity)

/// The subtotal less <c>discountPercent</c> percent, rounded towards zero to a cent.
let discounted (subtotal: int) (discountPercent: int) =
    subtotal - subtotal * discountPercent / 100

/// The decoders of the messages a client may dispatch: <c>add_item</c>, <c>remove_item</c> and <c>set_discount</c>.
let decoders: (string * MessageDecoder<Msg>) list =
    let text (args: JsonElement) (name: string) =
        args.GetProperty(name).GetString()

    let number (args: JsonElement) (name: string) =
        args.GetProperty(name).GetInt32()

    [
        "add_item",
        fun args ->
            let sku = text args "sku"
            let unitPrice = number args "unitPrice"
            let quantity = number args "quantity"

            if System.String.IsNullOrWhiteSpace sku then
                Result.Error "add_item needs a non-empty 'sku'."
            elif unitPrice < 0 then
                Result.Error "add_item needs a 'unitPrice' of at least 0 cents."
            elif quantity < 1 then
                Result.Error "add_item needs a 'quantity' of at least 1."
            else
                Result.Ok (AddItem (sku, unitPrice, quantity))
        "remove_item", fun args -> Result.Ok (RemoveItem (text args "sku"))
        "set_discount",
        fun args ->
            let percent = number args "percent"

            if percent < 0 || percent > 100 then
                Result.Error "set_discount needs a 'percent' from 0 to 100."
            else
                Result.Ok (SetDiscount percent)
    ]

/// The running cart: its Mvu, the derived memos, and the adapter serving them.
type App =
    {
        Mvu: Mvu<Model, Msg>
        Subtotal: Memo<int>
        Discount: Memo<int>
        Total: Memo<int>
        Adapter: McpAdapter
    }

let private encodeLines (lines: Line list) : JsonNode =
    let array = JsonArray ()

    for line in lines do
        let item = JsonObject ()
        item["sku"] <- JsonValue.Create line.Sku
        item["unitPrice"] <- JsonValue.Create line.UnitPrice
        item["quantity"] <- JsonValue.Create line.Quantity
        array.Add item

    array

let private encodeInt (value: int) : JsonNode =
    JsonValue.Create value

/// <summary>
/// Builds the cart on the ambient graph and exposes <c>lines</c>, <c>subtotal</c>, <c>discount</c> and <c>total</c>,
/// the messages of <c>decoders</c>, and the nodes <c>subtotal</c>, <c>discount</c> and <c>total</c>.
/// </summary>
/// <remarks>
/// With <c>defect</c>, <c>total</c> reads the discount untracked: a discount change leaves the total stale until the
/// next line change. This is the seeded defect of the validation experiment.
/// </remarks>
let create (defect: bool) : App =
    let mvu = Mvu.create init update
    let lines = mvu.Select _.Lines
    let discount = mvu.Select _.DiscountPercent
    let subtotal = createMemo (fun _ -> subtotalOf lines.Value)

    let total =
        if defect then
            createMemo (fun _ -> discounted subtotal.Value discount.Peek)
        else
            createMemo (fun _ -> discounted subtotal.Value discount.Value)

    let exposure = Exposure ()

    exposure
    |> Expose.value "lines" encodeLines (fun () -> lines.Value)

    exposure
    |> Expose.value "subtotal" encodeInt (fun () -> subtotal.Value)

    exposure
    |> Expose.value "discount" encodeInt (fun () -> discount.Value)

    exposure
    |> Expose.value "total" encodeInt (fun () -> total.Value)

    exposure |> Expose.messages mvu decoders
    exposure |> Expose.node "subtotal" subtotal
    exposure |> Expose.node "discount" discount
    exposure |> Expose.node "total" total
    exposure.Start ()

    {
        Mvu = mvu
        Subtotal = subtotal
        Discount = discount
        Total = total
        Adapter = new McpAdapter (exposure)
    }
