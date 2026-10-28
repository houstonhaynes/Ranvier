# Ranvier.View.WebCore

Runs [Ranvier.View](../Ranvier.View/README.md) components on .NET against a native WebCore page from
[Ranvier.WebCore](https://github.com/ranvier-org/Ranvier.WebCore), with no browser. `WebCoreHost` owns the page:
its `Document` takes the same `Mounting.mount` as the browser renderer, and the app forwards window input to it and
paints it into its window.

Experimental, Windows x64 only for now: the native library comes from `Ranvier.WebCore.Native.win-x64`.

```fsharp
open Ranvier
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.WebCore

use host = WebCoreHost.Create (320, 200)
use graph = new Graph ()
let container = host.Document.createElement "div"
host.Document.body.appendChild container |> ignore

let mounted =
    Mounting.mount graph None (fun errors -> raise (AggregateException errors)) container None (fun context ->
        Program.run (Component.render counter 1) context.Backend)

host.Click (40, 30) |> ignore // input in page pixels
host.Cycle () // run WebCore's timers and the posted callbacks
host.Frame () // lay out and paint

let struct (width, height) = host.PaintSize
let pixels = Array.zeroCreate<byte> (width * height * 4)
host.Paint pixels // RGBA, row by row
```

All calls go on the thread that created the host.
