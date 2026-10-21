module Probe

let snapshot () =
    let setups = Loops.childSetups
    Loops.setup ()
    Loops.childSetups - setups

let closure () =
    let setups = Loops.childSetups
    Loops.setup ()
    fun () -> Loops.childSetups - setups

let tupled () =
    let a, b = Loops.childSetups, 1
    Loops.setup ()
    Loops.childSetups - a + b

let kept () =
    let mutable held = Loops.childSetups
    Loops.setup ()
    Loops.childSetups - held
