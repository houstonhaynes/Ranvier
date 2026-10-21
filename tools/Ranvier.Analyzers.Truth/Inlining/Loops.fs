module Loops

let mutable childSetups = 0

let setup () =
    childSetups <- childSetups + 1
