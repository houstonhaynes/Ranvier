/// The F# samples of docs/content/server/state-transfer.md, in page order; a sample that is an excerpt is wrapped in a function.
module Shared

open Counter

open Ranvier.Wire
open Ranvier.Wire.Thoth.Json.Core

let counterPropsCodec: Codec<CounterProps> =
    objectCodec {
        let! start = Codec.field "start" _.Start Codec.int
        and! label = Codec.field "label" _.Label Codec.string
        return { Start = start; Label = label }
    }

let counterProps = WireCodec.ofCodec counterPropsCodec

/// The id of the script element that carries the props from server to client.
let stateId = "counter-props"
