(* The OCaml half of the differential check. Reads programs from stdin, one S-expression per line, runs the extracted
   [observed_mode] under [current_flags] in the skip mode of the second argument ([off], the default, [leave-dirty],
   [unchecked] or [all]), and writes one line per program, tab-separated: index, status, trace, end state and closed state. The status
   is [ok], or [none] when [observed_mode] has no result: exhausted fuel or a stuck body. In mode [off],
   [observed_mode] is [observed] ([observed_mode_off]).

     driver <fuel> [mode] < programs.txt > ocaml.txt *)

open Engine

type sexp = Atom of string | Node of sexp Stdlib.List.t

let parse_sexp (text : string) : sexp =
  let i = ref 0 in
  let skip () = while text.[!i] = ' ' do incr i done in
  let rec one () =
    skip ();
    if text.[!i] = '(' then begin
      incr i;
      let items = ref [] in
      skip ();
      while text.[!i] <> ')' do
        items := one () :: !items;
        skip ()
      done;
      incr i;
      Node (Stdlib.List.rev !items)
    end else begin
      let start = !i in
      while text.[!i] <> ' ' && text.[!i] <> ')' && text.[!i] <> '(' do incr i done;
      Atom (Stdlib.String.sub text start (!i - start))
    end
  in
  one ()

(* The extracted [nat] and [Z] are inductive. *)
let rec nat_of_int (n : int) : nat = if n <= 0 then O else S (nat_of_int (n - 1))

let int_of_nat (n : nat) : int =
  let rec go acc = function O -> acc | S m -> go (acc + 1) m in
  go 0 n

let rec positive_of_int (n : int) : positive =
  if n = 1 then XH
  else if n land 1 = 1 then XI (positive_of_int (n lsr 1))
  else XO (positive_of_int (n lsr 1))

let rec int_of_positive (p : positive) : int =
  match p with
  | XH -> 1
  | XO q -> 2 * int_of_positive q
  | XI q -> (2 * int_of_positive q) + 1

let z_of_int (n : int) : z =
  if n = 0 then Z0 else if n > 0 then Zpos (positive_of_int n) else Zneg (positive_of_int (-n))

let int_of_z (v : z) : int =
  match v with
  | Z0 -> 0
  | Zpos p -> int_of_positive p
  | Zneg p -> -int_of_positive p

let atom_int = function
  | Atom a -> int_of_string a
  | Node _ -> failwith "expected an integer"

let var x = nat_of_int (atom_int x)
let num x = z_of_int (atom_int x)

let rec read_expr (s : sexp) : expr =
  match s with
  | Node [ Atom "k"; z ] -> EConst (num z)
  | Node [ Atom "v"; x ] -> EVar (var x)
  | Node [ Atom "prev" ] -> EPrev
  | Node [ Atom "add"; a; b ] -> EAdd (read_expr a, read_expr b)
  | Node [ Atom "sub"; a; b ] -> ESub (read_expr a, read_expr b)
  | Node [ Atom "mul"; a; b ] -> EMul (read_expr a, read_expr b)
  | Node [ Atom "div"; a; b ] -> EDiv (read_expr a, read_expr b)
  | Node [ Atom "lt"; a; b ] -> ELt (read_expr a, read_expr b)
  | Node [ Atom "eq"; a; b ] -> EEq (read_expr a, read_expr b)
  | _ -> failwith "not an expression"

let read_cmp (s : sexp) : comparer =
  match s with
  | Atom "eq" -> CmpEq
  | Atom "never" -> CmpNever
  | Node [ Atom "throws"; f ] -> CmpThrows (num f)
  | _ -> failwith "not a comparer"

let rec read_cmd (s : sexp) : cmd =
  match s with
  | Node [ Atom "ret"; e ] -> CRet (read_expr e)
  | Node [ Atom "throw"; f ] -> CThrow (num f)
  | Node [ Atom "let"; x; e; k ] -> CLet (var x, read_expr e, read_cmd k)
  | Node [ Atom "if"; e; a; b ] -> CIf (read_expr e, read_cmd a, read_cmd b)
  | Node [ Atom "read"; x; n; k ] -> CRead (var x, read_expr n, read_cmd k)
  | Node [ Atom "tryread"; x; ok; n; k ] -> CTryRead (var x, var ok, read_expr n, read_cmd k)
  | Node [ Atom "readu"; x; n; k ] -> CReadUntracked (var x, read_expr n, read_cmd k)
  | Node [ Atom "write"; n; e; k ] -> CWrite (read_expr n, read_expr e, read_cmd k)
  | Node [ Atom "signal"; x; e; k ] -> CSignal (var x, read_expr e, read_cmd k)
  | Node [ Atom "memo"; x; Atom mode; q; b; k ] ->
    let m = if mode = "owning" then Owning else Pure in
    CMemo (var x, m, read_cmp q, read_cmd b, read_cmd k)
  | Node [ Atom "effect"; x; b; k ] -> CEffect (var x, read_cmd b, read_cmd k)
  | Node [ Atom "cleanup"; b; k ] -> CCleanup (read_cmd b, read_cmd k)
  | Node [ Atom "dispose"; n; k ] -> CDispose (read_expr n, read_cmd k)
  | Node [ Atom "batch"; b; k ] -> CBatch (read_cmd b, read_cmd k)
  | Node [ Atom "log"; e; k ] -> CLog (read_expr e, read_cmd k)
  | _ -> failwith "not a command"

let render_outcome = function
  | Ok v -> Printf.sprintf "(Ok %d)" (int_of_z v)
  | Fail f -> Printf.sprintf "(Fail %d)" (int_of_z f)

let render (e : event) : string =
  match e with
  | EvRun n -> Printf.sprintf "EvRun %d" (int_of_nat n)
  | EvLog (n, v) -> Printf.sprintf "EvLog %d %d" (int_of_nat n) (int_of_z v)
  | EvCleanup n -> Printf.sprintf "EvCleanup %d" (int_of_nat n)
  | EvFail (n, f) -> Printf.sprintf "EvFail %d %d" (int_of_nat n) (int_of_z f)
  | EvDispose n -> Printf.sprintf "EvDispose %d" (int_of_nat n)
  | EvHostRead (n, o) -> Printf.sprintf "EvHostRead %d %s" (int_of_nat n) (render_outcome o)

let events (l : event list) : string Stdlib.List.t =
  let rec go acc = function
    | Nil -> Stdlib.List.rev acc
    | Cons (e, rest) -> go (render e :: acc) rest
  in
  go [] l

let flag (b : bool) = match b with True -> 1 | False -> 0

let render_status = function
  | StNone -> "None"
  | StPending -> "Pending"
  | StError -> "Error"
  | StUninitialized -> "Uninitialized"

let render_fresh = function Clean -> "Clean" | Check -> "Check" | Dirty -> "Dirty"

let render_failure = function
  | Some f -> string_of_int (int_of_z f)
  | None -> "-"

let render_node (v : node_view) : string =
  let id = int_of_nat v.v_id in
  match v.v_kind with
  | KSignal -> Printf.sprintf "S%d v=%d o=%d" id (int_of_z v.v_value) (int_of_nat v.v_observers)
  | KMemo ->
    Printf.sprintf "M%d v=%d st=%s fail=%s fr=%s u=%d r=%d d=%d s=%d o=%d" id (int_of_z v.v_value)
      (render_status v.v_status) (render_failure v.v_failure) (render_fresh v.v_freshness) (flag v.v_unannounced)
      (int_of_nat v.v_runs) (flag v.v_disposed) (int_of_nat v.v_sources) (int_of_nat v.v_observers)
  | KEffect ->
    Printf.sprintf "E%d st=%s fail=%s fr=%s r=%d d=%d q=%d s=%d" id (render_status v.v_status)
      (render_failure v.v_failure) (render_fresh v.v_freshness) (int_of_nat v.v_runs) (flag v.v_disposed)
      (flag v.v_queued) (int_of_nat v.v_sources)

let render_view (v : state_view) : string =
  let rec nodes acc = function
    | Nil -> Stdlib.List.rev acc
    | Cons (n, rest) -> nodes (render_node n :: acc) rest
  in
  let tail = Printf.sprintf "queue=%d errors=%d" (int_of_nat v.v_queue) (int_of_nat v.v_errors) in
  Stdlib.String.concat ", " (nodes [] v.v_nodes @ [ tail ])

let read_mode (s : string) : skip_mode =
  match s with
  | "off" -> SkipOff
  | "leave-dirty" -> SkipLeaveDirty
  | "unchecked" -> SkipUnchecked
  | "all" -> SkipAll
  | _ -> failwith "the skip mode is off, leave-dirty, unchecked or all"

let () =
  let fuel = nat_of_int (int_of_string Sys.argv.(1)) in
  let mode = read_mode (if Array.length Sys.argv > 2 then Sys.argv.(2) else "off") in
  let index = ref 0 in
  try
    while true do
      let line = input_line stdin in
      let program = read_cmd (parse_sexp line) in
      (match observed_mode fuel current_flags mode program with
       | Some (Pair (Pair (trace, ended), closed)) ->
         Printf.printf "%d\tok\t%s\t%s\t%s\n" !index
           (Stdlib.String.concat "; " (events trace))
           (render_view ended) (render_view closed)
       | None -> Printf.printf "%d\tnone\t\t\t\n" !index
       | exception Stack_overflow -> Printf.printf "%d\tnone\t\t\t\n" !index);
      incr index
    done
  with End_of_file -> ()
