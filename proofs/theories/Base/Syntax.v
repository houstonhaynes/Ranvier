From stdpp Require Import prelude.
From Ranvier.Base Require Import Prelude.
From Stdlib Require Import ZArith.
Open Scope Z_scope.

Definition var := nat.

(** Integer expressions over local variables and the previous-value argument. *)
Inductive expr :=
  | EConst (z : val)
  | EVar (x : var)
  | EPrev
  | EAdd (a b : expr)
  | ESub (a b : expr)
  | EMul (a b : expr)
  | EDiv (a b : expr)
  | ELt (a b : expr)
  | EEq (a b : expr).

(** The result of a body: a value, or a failure carrying its identity. *)
Inductive outcome := Ok (v : val) | Fail (f : val).

(** Comparer behaviours of a memo. *)
Inductive comparer := CmpEq | CmpNever | CmpThrows (f : val).

(**md
Whether a memo body opens an owner of its own (`Owning`) or runs in the enclosing scope (`Pure`).
*)
Inductive scope_mode := Pure | Owning.

(**md
The command language of node bodies. Each constructor with a continuation `k` runs it after the
command. `CRead` is a tracked read, `CTryRead` a tracked read that reports failure through a flag
variable, `CReadUntracked` a read that records no edge, `CWrite` a write to a signal, and
`CSignal`, `CMemo` and `CEffect` create a node and bind its id to a variable. `CCleanup`
registers a cleanup, `CDispose` disposes a node, `CBatch` runs a block as a batch and `CLog` emits
a log event.
*)
Inductive cmd :=
  | CRet (e : expr)
  | CThrow (f : val)
  | CLet (x : var) (e : expr) (k : cmd)
  | CIf (e : expr) (t f : cmd)
  | CRead (x : var) (n : expr) (k : cmd)
  | CTryRead (x ok : var) (n : expr) (k : cmd)
  | CReadUntracked (x : var) (n : expr) (k : cmd)
  | CWrite (n e : expr) (k : cmd)
  | CSignal (x : var) (e : expr) (k : cmd)
  | CMemo (x : var) (m : scope_mode) (c : comparer) (b : cmd) (k : cmd)
  | CEffect (x : var) (b : cmd) (k : cmd)
  | CCleanup (b : cmd) (k : cmd)
  | CDispose (n : expr) (k : cmd)
  | CBatch (b : cmd) (k : cmd)
  | CLog (e : expr) (k : cmd).

Arguments EVar _%_nat.
Arguments CLet _%_nat _ _.
Arguments CRead _%_nat _ _.
Arguments CTryRead _%_nat _%_nat _ _.
Arguments CReadUntracked _%_nat _ _.
Arguments CSignal _%_nat _ _.
Arguments CMemo _%_nat _ _ _ _.
Arguments CEffect _%_nat _ _.

(**md
Local variable bindings, searched from the head.
*)
Definition env := list (var * val).

(**md
The value bound to the first entry for `x`, or `None` when unbound.

- [g:] the environment searched.
- [x:] the variable looked up.
*)
Fixpoint env_lookup (g : env) (x : var) : option val :=
  match g with
  | nil => None
  | cons (y, v) g' => if Nat.eqb x y then Some v else env_lookup g' x
  end.

(**md
`1` for `true` and `0` for `false`.
*)
Definition bool_val (b : bool) : val := if b then 1 else 0.

(** The failure of a zero divisor. *)
Definition div_zero_failure : val := -1.

(**md
Operands evaluate left to right, and the first failure is the result. Integer division truncates
toward zero. `None` marks an unbound variable or an absent previous value.

- [g:] the local variable environment.
- [p:] the previous value that `EPrev` reads, when there is one.
- [e:] the expression evaluated.
*)
Fixpoint eval_expr (g : env) (p : option val) (e : expr) : option outcome :=
  let bin f a b :=
    match eval_expr g p a with
    | Some (Ok u) =>
        match eval_expr g p b with
        | Some (Ok v) => Some (f u v)
        | r => r
        end
    | r => r
    end in
  match e with
  | EConst z => Some (Ok z)
  | EVar x => Ok <$> env_lookup g x
  | EPrev => Ok <$> p
  | EAdd a b => bin (fun u v => Ok (u + v)) a b
  | ESub a b => bin (fun u v => Ok (u - v)) a b
  | EMul a b => bin (fun u v => Ok (u * v)) a b
  | EDiv a b =>
      bin (fun u v => if decide (v = 0) then Fail div_zero_failure else Ok (Z.quot u v)) a b
  | ELt a b => bin (fun u v => Ok (bool_val (Z.ltb u v))) a b
  | EEq a b => bin (fun u v => Ok (bool_val (Z.eqb u v))) a b
  end.

(**md
The observable trace of a program: node runs, logs, cleanups, failures, disposals and host reads.
*)
Inductive event :=
  | EvRun (n : node_id)
  | EvLog (n : node_id) (v : val)
  | EvCleanup (n : node_id)
  | EvFail (n : node_id) (f : val)
  | EvDispose (n : node_id)
  | EvHostRead (n : node_id) (o : outcome).

(**md
`true` when `e` contains no `EPrev`.
*)
Fixpoint expr_no_prev (e : expr) : bool :=
  match e with
  | EPrev => false
  | EConst _ | EVar _ => true
  | EAdd a b | ESub a b | EMul a b | EDiv a b | ELt a b | EEq a b =>
      expr_no_prev a && expr_no_prev b
  end.

(** A body that reads only through tracked reads and has no effect other than [CLog]. *)
Fixpoint well_behaved_memo (c : cmd) : bool :=
  match c with
  | CRet e => expr_no_prev e
  | CThrow _ => true
  | CLet _ e k => expr_no_prev e && well_behaved_memo k
  | CIf e t f => expr_no_prev e && well_behaved_memo t && well_behaved_memo f
  | CRead _ n k => expr_no_prev n && well_behaved_memo k
  | CTryRead _ _ n k => expr_no_prev n && well_behaved_memo k
  | CBatch b k => well_behaved_memo b && well_behaved_memo k
  | CLog e k => expr_no_prev e && well_behaved_memo k
  | CReadUntracked _ _ _ | CWrite _ _ _ | CSignal _ _ _ | CMemo _ _ _ _ _
  | CEffect _ _ _ | CCleanup _ _ | CDispose _ _ => false
  end.

Local Notation g0 := [(0%nat, 7); (1%nat, 0)].

Example eval_const : eval_expr [] None (EConst 5) = Some (Ok 5).
Proof. reflexivity. Qed.
Example eval_arith :
  eval_expr g0 None (EAdd (EMul (EConst 3) (EVar 0)) (ESub (EConst 1) (EConst 4))) = Some (Ok 18).
Proof. reflexivity. Qed.
Example eval_unbound : eval_expr g0 None (EVar 9) = None.
Proof. reflexivity. Qed.
Example eval_unbound_nested : eval_expr g0 None (EAdd (EConst 1) (EVar 9)) = None.
Proof. reflexivity. Qed.
Example eval_div : eval_expr g0 None (EDiv (EVar 0) (EConst 2)) = Some (Ok 3).
Proof. reflexivity. Qed.
Example eval_div_truncates : eval_expr [] None (EDiv (EConst (-7)) (EConst 2)) = Some (Ok (-3)).
Proof. reflexivity. Qed.
Example eval_div_zero : eval_expr g0 None (EDiv (EVar 0) (EVar 1)) = Some (Fail div_zero_failure).
Proof. reflexivity. Qed.
Example eval_div_zero_nested :
  eval_expr g0 None (EAdd (EConst 1) (EDiv (EVar 0) (EVar 1))) = Some (Fail div_zero_failure).
Proof. reflexivity. Qed.
Example eval_div_zero_before_unbound :
  eval_expr g0 None (EAdd (EDiv (EVar 0) (EVar 1)) (EVar 9)) = Some (Fail div_zero_failure).
Proof. reflexivity. Qed.
Example eval_unbound_before_div_zero :
  eval_expr g0 None (EAdd (EVar 9) (EDiv (EVar 0) (EVar 1))) = None.
Proof. reflexivity. Qed.
Example eval_prev_none : eval_expr [] None EPrev = None.
Proof. reflexivity. Qed.
Example eval_prev_some : eval_expr [] (Some 4) (EAdd EPrev (EConst 1)) = Some (Ok 5).
Proof. reflexivity. Qed.
Example eval_lt : eval_expr [] None (ELt (EConst 1) (EConst 2)) = Some (Ok 1).
Proof. reflexivity. Qed.
Example eval_lt_false : eval_expr [] None (ELt (EConst 2) (EConst 2)) = Some (Ok 0).
Proof. reflexivity. Qed.
Example eval_eq : eval_expr [] None (EEq (EConst 2) (EConst 2)) = Some (Ok 1).
Proof. reflexivity. Qed.
Example eval_eq_false : eval_expr [] None (EEq (EConst 2) (EConst 3)) = Some (Ok 0).
Proof. reflexivity. Qed.

Local Notation ret := (CRet (EConst 0)).

Example well_behaved_memo_accepts :
  well_behaved_memo
    (CLet 0 (EConst 1) (CRead 1 (EVar 0) (CTryRead 2 3 (EVar 0)
      (CIf (EVar 3) (CBatch ret (CLog (EVar 1) ret)) (CThrow 1))))) = true.
Proof. reflexivity. Qed.
Example well_behaved_memo_rejects_prev : well_behaved_memo (CRet EPrev) = false.
Proof. reflexivity. Qed.
Example well_behaved_memo_rejects_prev_nested : well_behaved_memo (CIf (EAdd (EConst 1) EPrev) ret ret) = false.
Proof. reflexivity. Qed.
Example well_behaved_memo_rejects_reading_untracked : well_behaved_memo (CReadUntracked 0 (EConst 0) ret) = false.
Proof. reflexivity. Qed.
Example well_behaved_memo_rejects_write : well_behaved_memo (CWrite (EConst 0) (EConst 1) ret) = false.
Proof. reflexivity. Qed.
Example well_behaved_memo_rejects_signal : well_behaved_memo (CSignal 0 (EConst 1) ret) = false.
Proof. reflexivity. Qed.
Example well_behaved_memo_rejects_memo : well_behaved_memo (CMemo 0 Pure CmpEq ret ret) = false.
Proof. reflexivity. Qed.
Example well_behaved_memo_rejects_effect : well_behaved_memo (CEffect 0 ret ret) = false.
Proof. reflexivity. Qed.
Example well_behaved_memo_rejects_cleanup : well_behaved_memo (CCleanup ret ret) = false.
Proof. reflexivity. Qed.
Example well_behaved_memo_rejects_dispose : well_behaved_memo (CDispose (EConst 0) ret) = false.
Proof. reflexivity. Qed.
Example well_behaved_memo_rejects_deep : well_behaved_memo (CLet 0 (EConst 1) (CBatch ret (CDispose (EConst 0) ret))) = false.
Proof. reflexivity. Qed.
