/// The page sample of docs/content/server/deployment.md.
module ServerPage

open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Counter
open Shared

let options = { Server.Options.defaults with Policy = Omit }

let page (props: CounterProps) : string =
    let rendered = Server.renderPage options counter props

    $"""<!doctype html>
<html>
<head><meta charset="utf-8">{rendered.Head}</head>
<body>
<div id="app">{rendered.Body}</div>
{Server.stateScript stateId (counterProps.Encode props)}
<script type="module">import "/main.js";</script>
</body>
</html>"""
