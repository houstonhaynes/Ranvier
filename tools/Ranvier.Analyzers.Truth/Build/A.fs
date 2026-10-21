module A

let mutable counter = 0

let bump () =
    counter <- counter + 1
