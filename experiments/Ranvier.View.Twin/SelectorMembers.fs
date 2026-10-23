module internal Ranvier.View.Twin.SelectorMembers

open Ranvier.View.Testing

let private registered =
    lazy
        (Bridge.ensure ()

         Bridge.registerMember (Bridge.declaredBy [ "ParentNode" ]) "querySelector" 1 (fun node args ->
             match Selector.query node.Native (string args[0]) with
             | Some element -> box (Bridge.nodeOf element)
             | None -> null)

         Bridge.registerMember (Bridge.declaredBy [ "ParentNode" ]) "querySelectorAll" 1 (fun node args ->
             let found =
                 Selector.queryAll node.Native (string args[0])
                 |> Array.map (fun e -> Bridge.nodeOf e)
                 |> List.ofArray

             // A static list: a fresh owner per call, so each call returns its own object.
             box (ListValue (obj (), "querySelectorAll", (fun () -> Seq.ofList found), (fun () -> 0))))

         Bridge.registerMember (Bridge.declaredBy [ "Element" ]) "matches" 1 (fun node args ->
             box (Selector.matches (Selector.parse (string args[0])) (unbox node.Native)))

         Bridge.registerMember (Bridge.declaredBy [ "Element" ]) "closest" 1 (fun node args ->
             let parsed = Selector.parse (string args[0])
             let mutable cursor = node

             while not (isNull cursor)
                   && not (
                       cursor :? TwinElement
                       && Selector.matchesIn node.Native parsed (unbox cursor.Native)
                   ) do
                 cursor <- cursor.Parent

             box cursor))

let ensure () =
    registered.Force ()
