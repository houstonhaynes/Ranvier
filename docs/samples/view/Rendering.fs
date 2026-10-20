/// The F# samples of docs/content/server/rendering.md, in page order; a sample that is an excerpt is wrapped in a function.
module Rendering

open global.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar
open Counter

let options = { Server.Options.defaults with Policy = Omit }

let html = Server.render options counter { Start = 0; Label = "Clicks" }

let writeCounter (output: System.IO.TextWriter) =
    Server.renderTo options counter { Start = 0; Label = "Clicks" } output.Write

let tryRender (definition: Component<unit>) =
    try
        Ok (Server.render options definition ())
    with Server.ServerPendingException pending ->
        Error pending.Message
