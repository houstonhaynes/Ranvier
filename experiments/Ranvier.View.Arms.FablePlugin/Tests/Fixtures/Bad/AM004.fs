module Fixtures.Bad.AM004

open global.Ranvier
open Ranvier.View.Template
open Ranvier.View.Sugar
open Ranvier.View.Arms
open Fixtures.Types

#if BAD_AM004_INT
let site = Component.template (fun _ (n: int) -> n) (div () { Arms.Match ((fun (n: int) -> n), function 0 -> span () { "zero" } | _ -> span () { "other" }) })
#endif
#if BAD_AM004_STRING
let site = Component.template (fun _ (s: string) -> s) (div () { Arms.Match ((fun (s: string) -> s.Length), function 0 -> span () { "zero" } | _ -> span () { "other" }) })
#endif
#if BAD_AM004_UNIT
let site = Component.template (fun _ (u: unit) -> u) (div () { Arms.Match ((fun (_: unit) -> 0), function 0 -> span () { "zero" } | _ -> span () { "other" }) })
#endif
#if BAD_AM004_OPTION
let site = Component.template (fun _ (o: int option) -> o) (div () { Arms.Match ((fun (o: int option) -> defaultArg o 0), function 0 -> span () { "zero" } | _ -> span () { "other" }) })
#endif
#if BAD_AM004_STRUCT
let site = Component.template (fun _ (p: Point) -> p) (div () { Arms.Match ((fun (p: Point) -> p.X), function 0 -> span () { "zero" } | _ -> span () { "other" }) })
#endif
