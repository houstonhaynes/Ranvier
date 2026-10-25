module Ranvier.View.Twin.Tests.NativeTests

open Expecto
open Ranvier.View
open Ranvier.View.Twin

type private Row = { Id: int }

[<Tests>]
let tests =
    testList
        "Native"
        [
            testCase "replaceWith puts the next element in the node's place"
            <| fun () ->
                use document = new Ranvier.View.Twin.TwinDocument ()
                let first = document.CreateElement "a"
                document.Head.AppendChild first |> ignore

                document.Head.AppendChild (document.CreateElement "i")
                |> ignore

                let next = document.CreateElement "b"
                Native.replaceWith (first.Native :?> Fable.Core.TS.Dom.Element) (next.Native :?> Fable.Core.TS.Dom.Element)
                Expect.equal (Ranvier.View.Twin.Html.inner document.Head) "<b></b><i></i>" "replaced in place"

            testCase "defaultValueOf reads an input's and a textarea's default value"
            <| fun () ->
                use document = new TwinDocument ()
                let input = document.CreateElement "input"
                input.SetAttribute ("value", "a")
                let area = document.CreateElement "textarea"
                area.TextContent <- "b"
                Expect.equal (Native.defaultValueOf input.Native, Native.defaultValueOf area.Native) ("a", "b") "default values"

            testCase "a map keys strings, numbers and booleans by value"
            <| fun () ->
                let map = Native.newMap ()
                Native.set map "a" 1
                Native.set map 2 2
                Native.set map true 3
                Expect.equal (Native.getIndex map ("a" + "")) 1 "string key"
                Expect.equal (Native.getIndex map 2.0) 2 "an int and a float of equal value are one key"
                Expect.equal (Native.getIndex map true) 3 "boolean key"
                Expect.equal (Native.getIndex map "missing") -1 "absent key"

            testCase "SameValueZero keys erased Fable primitives by value"
            <| fun () ->
                let map = Native.newMap ()
                Native.set map (Some 7) 1
                Native.set map 'x' 2
                Native.set map 9L 3
                Expect.equal (Native.getIndex map 7) 1 "Some 7 is the key 7"
                Expect.equal (Native.getIndex map "x") 2 "a char is a one-character string"
                Expect.equal (Native.getIndex map 9L) 3 "an int64 keys by value"
                Expect.equal (Native.getIndex map nan) -1 "NaN is absent"
                Native.set map nan 4
                Expect.equal (Native.getIndex map nan) 4 "NaN equals NaN"
                Native.set map -0.0 5
                Expect.equal (Native.getIndex map 0.0) 5 "-0 equals +0"

            testCase "records key by reference"
            <| fun () ->
                let map = Native.newMap ()
                Native.set map { Id = 1 } 1
                Expect.equal (Native.getIndex map { Id = 1 }) -1 "a structurally equal record is another key"

            testCase "a set iterates in insertion order over a snapshot"
            <| fun () ->
                let set = Native.newSet ()

                for name in [ "click"; "input"; "focusin" ] do
                    Native.add set name

                let seen = ResizeArray<string>()

                Native.forEach set (fun (name: string) ->
                    seen.Add name
                    Native.add set (name + "!"))

                Expect.equal (List.ofSeq seen) [ "click"; "input"; "focusin" ] "insertion order; entries added during the walk are not visited"

            testCase "a weak map holds a reachable key until it is deleted"
            <| fun () ->
                let key = obj ()
                let table = Native.newWeakMap ()
                Native.set table key "v"
                Expect.isTrue (Native.has table key) "held"
                Expect.equal (Native.get<obj, string> table key) "v" "value"
                Native.delete table key
                Expect.isFalse (Native.has table key) "deleted"

            testCase "list reads past the end are null and writes at the end append"
            <| fun () ->
                let items = ResizeArray<string> [ "a" ]
                Expect.isNull (Native.listAt items 3) "past the end"
                Native.listSet items 1 "b"
                Expect.equal (List.ofSeq items) [ "a"; "b" ] "appended"
                Native.listSplice items 0 1 [| "x"; "y" |]
                Expect.equal (List.ofSeq items) [ "x"; "y"; "b" ] "spliced"
                Native.listTruncate items 1
                Expect.equal (List.ofSeq items) [ "x" ] "truncated"

            testCase "array slices clamp and reads past the end are null"
            <| fun () ->
                let rows = [| "a"; "b"; "c" |]
                Expect.equal (Native.arraySlice rows 1 10) [| "b"; "c" |] "clamped"
                Expect.isNull (Native.arrayAt rows 5) "past the end"
                Expect.equal (Native.allocate 3).Length 3 "allocated"

            testCase "expandos round trip per object"
            <| fun () ->
                let a, b = obj (), obj ()
                Native.setExpando a "$$ranvierWidget" true
                Expect.equal (Native.expando<bool> a "$$ranvierWidget") true "set"
                Expect.isNull (Native.expando<obj> b "$$ranvierWidget") "another object"

            testCase "a twin node's expandos live on its bridged object"
            <| fun () ->
                let host = TwinHost.Create ()
                use _ = host.Document
                let a = (host.Document.CreateElement "a").Native
                let b = (host.Document.CreateElement "b").Native
                Native.setExpando a "$$ranvier" [| box 1 |]
                Native.setExpando a "$$ranvierScope" "scope"
                Native.setExpando a "$$ranvier" [| box 2 |]
                Expect.equal (Native.expando<obj array> a "$$ranvier") [| box 2 |] "the first property, overwritten"
                Expect.equal (Native.expando<string> a "$$ranvierScope") "scope" "a second property"
                Expect.isNotNull (a :?> NodeProxy).Expandos "held by the bridged object"
                Expect.isNull (Native.expando<obj> b "$$ranvier") "another node"
                Expect.isNull (b :?> NodeProxy).Expandos "a node without properties has no slots"
                Expect.isFalse (Native.expando<bool> b "$$ranvierWidget") "an unset value reads as its default"
                let plain = obj ()
                Native.setExpando plain "$$ranvierWidget" true
                Expect.isTrue (Native.expando<bool> plain "$$ranvierWidget") "an object outside the twin"

            testCase "jsString writes values as Fable's erased primitives"
            <| fun () ->
                Expect.equal (Native.jsString (box (Some "x"))) "x" "Some is erased"
                Expect.equal (Native.jsString (box (Some 2.5))) "2.5" "Some of a number"
                Expect.equal (Native.jsString (box (ValueSome 3))) "3" "ValueSome is erased"
                Expect.equal (Native.jsString (box 'c')) "c" "a char"
                Expect.equal (Native.jsString (box true)) "true" "a bool"
                Expect.equal (Native.jsString (box 0.1f)) "0.1" "a float32 as its shortest decimal"
                Expect.equal (Native.jsString (box 1.50M)) "1.5" "a decimal as a number"
                Expect.equal (Native.jsString (box System.DayOfWeek.Monday)) "1" "an enum as its number"
                Expect.equal (Native.jsString (box 1e21)) "1e+21" "the ECMAScript exponent form"

            testCase "a map's forEach passes values and a set's passes its members"
            <| fun () ->
                let map = Native.newMap ()
                Native.set map "k" "v"
                let seen = ResizeArray<string>()
                Native.forEach map (fun (value: string) -> seen.Add value)
                Expect.equal (List.ofSeq seen) [ "v" ] "values"

            testCase "list and array writes past the end follow JavaScript's clamping"
            <| fun () ->
                let items = ResizeArray<string> [ "a" ]
                Native.listSplice items 5 1 [| "z" |]
                Expect.equal (List.ofSeq items) [ "a"; "z" ] "a splice past the end appends"
                Expect.equal (Native.arraySlice [| "a"; "b"; "c" |] -2 3) [| "b"; "c" |] "a negative start counts from the end"
        ]
