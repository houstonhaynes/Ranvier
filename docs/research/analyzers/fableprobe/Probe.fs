module Probe

let arith () =
    let s = Loops.childSetups + 0
    Loops.setup ()
    Loops.childSetups - s

let listLen () =
    let s = Loops.items
    Loops.setup ()
    List.length s

let field () =
    let s = Loops.state.Count
    Loops.setup ()
    Loops.state.Count - s

let staticProp () =
    let s = Loops.Box.Shared
    Loops.setup ()
    Loops.Box.Shared - s

let tupleSnap () =
    let a, b = Loops.childSetups, 1
    Loops.setup ()
    Loops.childSetups - a + b
