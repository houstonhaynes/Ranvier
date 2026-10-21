module B

let usesTester (p: A.Policy) =
    p.IsQueue, p.IsDrop, p.IsUserDefined, (Some 1).IsSome

let snap () =
    let s = A.counter
    A.counter <- 2
    A.counter - s

let t () =
    task {
        let x = 1
        do! System.Threading.Tasks.Task.Yield ()
        return x + A.counter
    }
