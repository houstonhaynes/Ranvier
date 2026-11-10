
type __ = Obj.t
let __ = let rec f _ = Obj.repr f in Obj.repr f

type unit0 =
| Tt

type bool =
| True
| False

(** val negb : bool -> bool **)

let negb = function
| True -> False
| False -> True

type nat =
| O
| S of nat

type 'a option =
| Some of 'a
| None

(** val option_map : ('a1 -> 'a2) -> 'a1 option -> 'a2 option **)

let option_map f = function
| Some a -> Some (f a)
| None -> None

type ('a, 'b) prod =
| Pair of 'a * 'b

(** val fst : ('a1, 'a2) prod -> 'a1 **)

let fst = function
| Pair (x, _) -> x

(** val snd : ('a1, 'a2) prod -> 'a2 **)

let snd = function
| Pair (_, y) -> y

type 'a list =
| Nil
| Cons of 'a * 'a list

(** val length : 'a1 list -> nat **)

let rec length = function
| Nil -> O
| Cons (_, l') -> S (length l')

(** val app : 'a1 list -> 'a1 list -> 'a1 list **)

let rec app l m0 =
  match l with
  | Nil -> m0
  | Cons (a, l1) -> Cons (a, (app l1 m0))

type comparison =
| Eq
| Lt
| Gt

(** val compOpp : comparison -> comparison **)

let compOpp = function
| Eq -> Eq
| Lt -> Gt
| Gt -> Lt

(** val id : __ -> __ **)

let id x =
  x

type sumbool =
| Left
| Right

(** val pred : nat -> nat **)

let pred n0 = match n0 with
| O -> n0
| S u -> u

module Coq__1 = struct
 (** val add : nat -> nat -> nat **)

 let rec add n0 m0 =
   match n0 with
   | O -> m0
   | S p -> S (add p m0)
end
include Coq__1

(** val sub : nat -> nat -> nat **)

let rec sub n0 m0 =
  match n0 with
  | O -> n0
  | S k -> (match m0 with
            | O -> n0
            | S l -> sub k l)

(** val compose : ('a2 -> 'a3) -> ('a1 -> 'a2) -> 'a1 -> 'a3 **)

let compose g f x =
  g (f x)

module Nat =
 struct
  (** val eqb : nat -> nat -> bool **)

  let rec eqb n0 m0 =
    match n0 with
    | O -> (match m0 with
            | O -> True
            | S _ -> False)
    | S n' -> (match m0 with
               | O -> False
               | S m' -> eqb n' m')

  (** val max : nat -> nat -> nat **)

  let rec max n0 m0 =
    match n0 with
    | O -> m0
    | S n' -> (match m0 with
               | O -> n0
               | S m' -> S (max n' m'))

  (** val eq_dec : nat -> nat -> sumbool **)

  let rec eq_dec n0 m0 =
    match n0 with
    | O -> (match m0 with
            | O -> Left
            | S _ -> Right)
    | S n1 -> (match m0 with
               | O -> Right
               | S n2 -> eq_dec n1 n2)
 end

(** val seq : nat -> nat -> nat list **)

let rec seq start = function
| O -> Nil
| S len0 -> Cons (start, (seq (S start) len0))

(** val firstn : nat -> 'a1 list -> 'a1 list **)

let rec firstn n0 l =
  match n0 with
  | O -> Nil
  | S n1 ->
    (match l with
     | Nil -> Nil
     | Cons (a, l0) -> Cons (a, (firstn n1 l0)))

(** val skipn : nat -> 'a1 list -> 'a1 list **)

let rec skipn n0 l =
  match n0 with
  | O -> l
  | S n1 -> (match l with
             | Nil -> Nil
             | Cons (_, l0) -> skipn n1 l0)

(** val tl : 'a1 list -> 'a1 list **)

let tl = function
| Nil -> Nil
| Cons (_, l') -> l'

(** val rev : 'a1 list -> 'a1 list **)

let rec rev = function
| Nil -> Nil
| Cons (x, l') -> app (rev l') (Cons (x, Nil))

(** val fold_left : ('a1 -> 'a2 -> 'a1) -> 'a2 list -> 'a1 -> 'a1 **)

let rec fold_left f l a0 =
  match l with
  | Nil -> a0
  | Cons (b, l0) -> fold_left f l0 (f a0 b)

(** val fold_right : ('a2 -> 'a1 -> 'a1) -> 'a1 -> 'a2 list -> 'a1 **)

let rec fold_right f a0 = function
| Nil -> a0
| Cons (b, l0) -> f b (fold_right f a0 l0)

(** val existsb : ('a1 -> bool) -> 'a1 list -> bool **)

let rec existsb f = function
| Nil -> False
| Cons (a, l0) -> (match f a with
                   | True -> True
                   | False -> existsb f l0)

(** val forallb : ('a1 -> bool) -> 'a1 list -> bool **)

let rec forallb f = function
| Nil -> True
| Cons (a, l0) -> (match f a with
                   | True -> forallb f l0
                   | False -> False)

(** val le_lt_dec : nat -> nat -> sumbool **)

let rec le_lt_dec n0 m0 =
  match n0 with
  | O -> Left
  | S n1 -> (match m0 with
             | O -> Right
             | S n2 -> le_lt_dec n1 n2)

(** val le_gt_dec : nat -> nat -> sumbool **)

let le_gt_dec =
  le_lt_dec

(** val le_dec : nat -> nat -> sumbool **)

let le_dec =
  le_gt_dec

(** val lt_dec : nat -> nat -> sumbool **)

let lt_dec n0 m0 =
  le_dec (S n0) m0

type decision = sumbool

(** val decide : decision -> sumbool **)

let decide decision0 =
  decision0

type ('a, 'b) relDecision = 'a -> 'b -> decision

(** val decide_rel : ('a1, 'a2) relDecision -> 'a1 -> 'a2 -> decision **)

let decide_rel relDecision0 =
  relDecision0

type 'a empty = 'a

(** val empty0 : 'a1 empty -> 'a1 **)

let empty0 empty1 =
  empty1

type 'a union = 'a -> 'a -> 'a

(** val union0 : 'a1 union -> 'a1 -> 'a1 -> 'a1 **)

let union0 union1 =
  union1

type 'a difference = 'a -> 'a -> 'a

(** val difference0 : 'a1 difference -> 'a1 -> 'a1 -> 'a1 **)

let difference0 difference1 =
  difference1

type ('a, 'b) singleton = 'a -> 'b

(** val singleton0 : ('a1, 'a2) singleton -> 'a1 -> 'a2 **)

let singleton0 singleton1 =
  singleton1

(** val list_to_set :
    ('a1, 'a2) singleton -> 'a2 empty -> 'a2 union -> 'a1 list -> 'a2 **)

let rec list_to_set h h0 h1 = function
| Nil -> empty0 h0
| Cons (x, l0) -> union0 h1 (singleton0 h x) (list_to_set h h0 h1 l0)

type ('a, 'b) filter = __ -> ('a -> decision) -> 'b -> 'b

(** val filter0 : ('a1, 'a2) filter -> ('a1 -> decision) -> 'a2 -> 'a2 **)

let filter0 filter1 h x =
  filter1 __ h x

type 'm mBind = __ -> __ -> (__ -> 'm) -> 'm -> 'm

(** val mbind : 'a1 mBind -> ('a2 -> 'a1) -> 'a1 -> 'a1 **)

let mbind mBind0 x x0 =
  Obj.magic mBind0 __ __ x x0

type 'm fMap = __ -> __ -> (__ -> __) -> 'm -> 'm

(** val fmap : 'a1 fMap -> ('a2 -> 'a3) -> 'a1 -> 'a1 **)

let fmap fMap0 x x0 =
  Obj.magic fMap0 __ __ x x0

type 'm oMap = __ -> __ -> (__ -> __ option) -> 'm -> 'm

(** val omap : 'a1 oMap -> ('a2 -> 'a3 option) -> 'a1 -> 'a1 **)

let omap oMap0 x x0 =
  Obj.magic oMap0 __ __ x x0

type ('k, 'a, 'm) lookup = 'k -> 'm -> 'a option

(** val lookup0 : ('a1, 'a2, 'a3) lookup -> 'a1 -> 'a3 -> 'a2 option **)

let lookup0 lookup1 =
  lookup1

type ('k, 'a, 'm) singletonM = 'k -> 'a -> 'm

(** val singletonM0 : ('a1, 'a2, 'a3) singletonM -> 'a1 -> 'a2 -> 'a3 **)

let singletonM0 singletonM1 =
  singletonM1

type ('k, 'a, 'm) insert = 'k -> 'a -> 'm -> 'm

(** val insert0 : ('a1, 'a2, 'a3) insert -> 'a1 -> 'a2 -> 'a3 -> 'a3 **)

let insert0 insert1 =
  insert1

type ('k, 'm) delete = 'k -> 'm -> 'm

(** val delete0 : ('a1, 'a2) delete -> 'a1 -> 'a2 -> 'a2 **)

let delete0 delete1 =
  delete1

type ('k, 'a, 'm) alter = ('a -> 'a) -> 'k -> 'm -> 'm

(** val alter0 :
    ('a1, 'a2, 'a3) alter -> ('a2 -> 'a2) -> 'a1 -> 'a3 -> 'a3 **)

let alter0 alter1 =
  alter1

type ('k, 'a, 'm) partialAlter = ('a option -> 'a option) -> 'k -> 'm -> 'm

(** val partial_alter :
    ('a1, 'a2, 'a3) partialAlter -> ('a2 option -> 'a2 option) -> 'a1 -> 'a3
    -> 'a3 **)

let partial_alter partialAlter0 =
  partialAlter0

type ('m, 'd) dom = 'm -> 'd

(** val dom0 : ('a1, 'a2) dom -> 'a1 -> 'a2 **)

let dom0 dom1 =
  dom1

type 'm merge =
  __ -> __ -> __ -> (__ option -> __ option -> __ option) -> 'm -> 'm -> 'm

(** val merge0 :
    'a1 merge -> ('a2 option -> 'a3 option -> 'a4 option) -> 'a1 -> 'a1 -> 'a1 **)

let merge0 merge1 x x0 x1 =
  Obj.magic merge1 __ __ __ x x0 x1

type ('a, 'm) unionWith = ('a -> 'a -> 'a option) -> 'm -> 'm -> 'm

(** val union_with :
    ('a1, 'a2) unionWith -> ('a1 -> 'a1 -> 'a1 option) -> 'a2 -> 'a2 -> 'a2 **)

let union_with unionWith0 =
  unionWith0

type ('a, 'm) differenceWith = ('a -> 'a -> 'a option) -> 'm -> 'm -> 'm

(** val difference_with :
    ('a1, 'a2) differenceWith -> ('a1 -> 'a1 -> 'a1 option) -> 'a2 -> 'a2 ->
    'a2 **)

let difference_with differenceWith0 =
  differenceWith0

type ('a, 'c) elements = 'c -> 'a list

(** val elements0 : ('a1, 'a2) elements -> 'a2 -> 'a1 list **)

let elements0 elements1 =
  elements1

type ('a, 'c) fresh = 'c -> 'a

(** val fresh0 : ('a1, 'a2) fresh -> 'a2 -> 'a1 **)

let fresh0 fresh1 =
  fresh1

type 'a infinite =
  ('a, 'a list) fresh
  (* singleton inductive, whose constructor was Build_Infinite *)

type positive =
| XI of positive
| XO of positive
| XH

type n =
| N0
| Npos of positive

type z =
| Z0
| Zpos of positive
| Zneg of positive

module Pos =
 struct
  (** val succ : positive -> positive **)

  let rec succ = function
  | XI p -> XO (succ p)
  | XO p -> XI p
  | XH -> XO XH

  (** val add : positive -> positive -> positive **)

  let rec add x y =
    match x with
    | XI p ->
      (match y with
       | XI q -> XO (add_carry p q)
       | XO q -> XI (add p q)
       | XH -> XO (succ p))
    | XO p ->
      (match y with
       | XI q -> XI (add p q)
       | XO q -> XO (add p q)
       | XH -> XI p)
    | XH -> (match y with
             | XI q -> XO (succ q)
             | XO q -> XI q
             | XH -> XO XH)

  (** val add_carry : positive -> positive -> positive **)

  and add_carry x y =
    match x with
    | XI p ->
      (match y with
       | XI q -> XI (add_carry p q)
       | XO q -> XO (add_carry p q)
       | XH -> XI (succ p))
    | XO p ->
      (match y with
       | XI q -> XO (add_carry p q)
       | XO q -> XI (add p q)
       | XH -> XO (succ p))
    | XH ->
      (match y with
       | XI q -> XI (succ q)
       | XO q -> XO (succ q)
       | XH -> XI XH)

  (** val pred_double : positive -> positive **)

  let rec pred_double = function
  | XI p -> XI (XO p)
  | XO p -> XI (pred_double p)
  | XH -> XH

  type mask =
  | IsNul
  | IsPos of positive
  | IsNeg

  (** val succ_double_mask : mask -> mask **)

  let succ_double_mask = function
  | IsNul -> IsPos XH
  | IsPos p -> IsPos (XI p)
  | IsNeg -> IsNeg

  (** val double_mask : mask -> mask **)

  let double_mask = function
  | IsPos p -> IsPos (XO p)
  | x0 -> x0

  (** val double_pred_mask : positive -> mask **)

  let double_pred_mask = function
  | XI p -> IsPos (XO (XO p))
  | XO p -> IsPos (XO (pred_double p))
  | XH -> IsNul

  (** val sub_mask : positive -> positive -> mask **)

  let rec sub_mask x y =
    match x with
    | XI p ->
      (match y with
       | XI q -> double_mask (sub_mask p q)
       | XO q -> succ_double_mask (sub_mask p q)
       | XH -> IsPos (XO p))
    | XO p ->
      (match y with
       | XI q -> succ_double_mask (sub_mask_carry p q)
       | XO q -> double_mask (sub_mask p q)
       | XH -> IsPos (pred_double p))
    | XH -> (match y with
             | XH -> IsNul
             | _ -> IsNeg)

  (** val sub_mask_carry : positive -> positive -> mask **)

  and sub_mask_carry x y =
    match x with
    | XI p ->
      (match y with
       | XI q -> succ_double_mask (sub_mask_carry p q)
       | XO q -> double_mask (sub_mask p q)
       | XH -> IsPos (pred_double p))
    | XO p ->
      (match y with
       | XI q -> double_mask (sub_mask_carry p q)
       | XO q -> succ_double_mask (sub_mask_carry p q)
       | XH -> double_pred_mask p)
    | XH -> IsNeg

  (** val mul : positive -> positive -> positive **)

  let rec mul x y =
    match x with
    | XI p -> add y (XO (mul p y))
    | XO p -> XO (mul p y)
    | XH -> y

  (** val compare_cont : comparison -> positive -> positive -> comparison **)

  let rec compare_cont r x y =
    match x with
    | XI p ->
      (match y with
       | XI q -> compare_cont r p q
       | XO q -> compare_cont Gt p q
       | XH -> Gt)
    | XO p ->
      (match y with
       | XI q -> compare_cont Lt p q
       | XO q -> compare_cont r p q
       | XH -> Gt)
    | XH -> (match y with
             | XH -> r
             | _ -> Lt)

  (** val compare : positive -> positive -> comparison **)

  let compare =
    compare_cont Eq

  (** val eqb : positive -> positive -> bool **)

  let rec eqb p q =
    match p with
    | XI p0 -> (match q with
                | XI q0 -> eqb p0 q0
                | _ -> False)
    | XO p0 -> (match q with
                | XO q0 -> eqb p0 q0
                | _ -> False)
    | XH -> (match q with
             | XH -> True
             | _ -> False)

  (** val iter_op : ('a1 -> 'a1 -> 'a1) -> positive -> 'a1 -> 'a1 **)

  let rec iter_op op p a =
    match p with
    | XI p0 -> op a (iter_op op p0 (op a a))
    | XO p0 -> iter_op op p0 (op a a)
    | XH -> a

  (** val to_nat : positive -> nat **)

  let to_nat x =
    iter_op Coq__1.add x (S O)

  (** val of_succ_nat : nat -> positive **)

  let rec of_succ_nat = function
  | O -> XH
  | S x -> succ (of_succ_nat x)
 end

module Coq_Pos =
 struct
  (** val succ : positive -> positive **)

  let rec succ = function
  | XI p -> XO (succ p)
  | XO p -> XI p
  | XH -> XO XH

  (** val pred_double : positive -> positive **)

  let rec pred_double = function
  | XI p -> XI (XO p)
  | XO p -> XI (pred_double p)
  | XH -> XH

  (** val pred : positive -> positive **)

  let pred = function
  | XI p -> XO p
  | XO p -> pred_double p
  | XH -> XH

  (** val eq_dec : positive -> positive -> sumbool **)

  let rec eq_dec p x0 =
    match p with
    | XI p0 -> (match x0 with
                | XI p1 -> eq_dec p0 p1
                | _ -> Right)
    | XO p0 -> (match x0 with
                | XO p1 -> eq_dec p0 p1
                | _ -> Right)
    | XH -> (match x0 with
             | XH -> Left
             | _ -> Right)
 end

module N =
 struct
  (** val succ_double : n -> n **)

  let succ_double = function
  | N0 -> Npos XH
  | Npos p -> Npos (XI p)

  (** val double : n -> n **)

  let double = function
  | N0 -> N0
  | Npos p -> Npos (XO p)

  (** val sub : n -> n -> n **)

  let sub n0 m0 =
    match n0 with
    | N0 -> N0
    | Npos n' ->
      (match m0 with
       | N0 -> n0
       | Npos m' ->
         (match Pos.sub_mask n' m' with
          | Pos.IsPos p -> Npos p
          | _ -> N0))

  (** val compare : n -> n -> comparison **)

  let compare n0 m0 =
    match n0 with
    | N0 -> (match m0 with
             | N0 -> Eq
             | Npos _ -> Lt)
    | Npos n' -> (match m0 with
                  | N0 -> Gt
                  | Npos m' -> Pos.compare n' m')

  (** val leb : n -> n -> bool **)

  let leb x y =
    match compare x y with
    | Gt -> False
    | _ -> True

  (** val pos_div_eucl : positive -> n -> (n, n) prod **)

  let rec pos_div_eucl a b =
    match a with
    | XI a' ->
      let Pair (q, r) = pos_div_eucl a' b in
      let r' = succ_double r in
      (match leb b r' with
       | True -> Pair ((succ_double q), (sub r' b))
       | False -> Pair ((double q), r'))
    | XO a' ->
      let Pair (q, r) = pos_div_eucl a' b in
      let r' = double r in
      (match leb b r' with
       | True -> Pair ((succ_double q), (sub r' b))
       | False -> Pair ((double q), r'))
    | XH ->
      (match b with
       | N0 -> Pair (N0, (Npos XH))
       | Npos p ->
         (match p with
          | XH -> Pair ((Npos XH), N0)
          | _ -> Pair (N0, (Npos XH))))
 end

module Coq_N =
 struct
  (** val to_nat : n -> nat **)

  let to_nat = function
  | N0 -> O
  | Npos p -> Pos.to_nat p

  (** val of_nat : nat -> n **)

  let of_nat = function
  | O -> N0
  | S n' -> Npos (Pos.of_succ_nat n')

  (** val eq_dec : n -> n -> sumbool **)

  let eq_dec n0 m0 =
    match n0 with
    | N0 -> (match m0 with
             | N0 -> Left
             | Npos _ -> Right)
    | Npos p -> (match m0 with
                 | N0 -> Right
                 | Npos p0 -> Coq_Pos.eq_dec p p0)
 end

module Z =
 struct
  (** val double : z -> z **)

  let double = function
  | Z0 -> Z0
  | Zpos p -> Zpos (XO p)
  | Zneg p -> Zneg (XO p)

  (** val succ_double : z -> z **)

  let succ_double = function
  | Z0 -> Zpos XH
  | Zpos p -> Zpos (XI p)
  | Zneg p -> Zneg (Pos.pred_double p)

  (** val pred_double : z -> z **)

  let pred_double = function
  | Z0 -> Zneg XH
  | Zpos p -> Zpos (Pos.pred_double p)
  | Zneg p -> Zneg (XI p)

  (** val pos_sub : positive -> positive -> z **)

  let rec pos_sub x y =
    match x with
    | XI p ->
      (match y with
       | XI q -> double (pos_sub p q)
       | XO q -> succ_double (pos_sub p q)
       | XH -> Zpos (XO p))
    | XO p ->
      (match y with
       | XI q -> pred_double (pos_sub p q)
       | XO q -> double (pos_sub p q)
       | XH -> Zpos (Pos.pred_double p))
    | XH ->
      (match y with
       | XI q -> Zneg (XO q)
       | XO q -> Zneg (Pos.pred_double q)
       | XH -> Z0)

  (** val add : z -> z -> z **)

  let add x y =
    match x with
    | Z0 -> y
    | Zpos x' ->
      (match y with
       | Z0 -> x
       | Zpos y' -> Zpos (Pos.add x' y')
       | Zneg y' -> pos_sub x' y')
    | Zneg x' ->
      (match y with
       | Z0 -> x
       | Zpos y' -> pos_sub y' x'
       | Zneg y' -> Zneg (Pos.add x' y'))

  (** val opp : z -> z **)

  let opp = function
  | Z0 -> Z0
  | Zpos x0 -> Zneg x0
  | Zneg x0 -> Zpos x0

  (** val sub : z -> z -> z **)

  let sub m0 n0 =
    add m0 (opp n0)

  (** val mul : z -> z -> z **)

  let mul x y =
    match x with
    | Z0 -> Z0
    | Zpos x' ->
      (match y with
       | Z0 -> Z0
       | Zpos y' -> Zpos (Pos.mul x' y')
       | Zneg y' -> Zneg (Pos.mul x' y'))
    | Zneg x' ->
      (match y with
       | Z0 -> Z0
       | Zpos y' -> Zneg (Pos.mul x' y')
       | Zneg y' -> Zpos (Pos.mul x' y'))

  (** val compare : z -> z -> comparison **)

  let compare x y =
    match x with
    | Z0 -> (match y with
             | Z0 -> Eq
             | Zpos _ -> Lt
             | Zneg _ -> Gt)
    | Zpos x' -> (match y with
                  | Zpos y' -> Pos.compare x' y'
                  | _ -> Gt)
    | Zneg x' ->
      (match y with
       | Zneg y' -> compOpp (Pos.compare x' y')
       | _ -> Lt)

  (** val ltb : z -> z -> bool **)

  let ltb x y =
    match compare x y with
    | Lt -> True
    | _ -> False

  (** val eqb : z -> z -> bool **)

  let eqb x y =
    match x with
    | Z0 -> (match y with
             | Z0 -> True
             | _ -> False)
    | Zpos p -> (match y with
                 | Zpos q -> Pos.eqb p q
                 | _ -> False)
    | Zneg p -> (match y with
                 | Zneg q -> Pos.eqb p q
                 | _ -> False)

  (** val to_nat : z -> nat **)

  let to_nat = function
  | Zpos p -> Pos.to_nat p
  | _ -> O

  (** val of_nat : nat -> z **)

  let of_nat = function
  | O -> Z0
  | S n1 -> Zpos (Pos.of_succ_nat n1)

  (** val of_N : n -> z **)

  let of_N = function
  | N0 -> Z0
  | Npos p -> Zpos p

  (** val quotrem : z -> z -> (z, z) prod **)

  let quotrem a b =
    match a with
    | Z0 -> Pair (Z0, Z0)
    | Zpos a0 ->
      (match b with
       | Z0 -> Pair (Z0, a)
       | Zpos b0 ->
         let Pair (q, r) = N.pos_div_eucl a0 (Npos b0) in
         Pair ((of_N q), (of_N r))
       | Zneg b0 ->
         let Pair (q, r) = N.pos_div_eucl a0 (Npos b0) in
         Pair ((opp (of_N q)), (of_N r)))
    | Zneg a0 ->
      (match b with
       | Z0 -> Pair (Z0, a)
       | Zpos b0 ->
         let Pair (q, r) = N.pos_div_eucl a0 (Npos b0) in
         Pair ((opp (of_N q)), (opp (of_N r)))
       | Zneg b0 ->
         let Pair (q, r) = N.pos_div_eucl a0 (Npos b0) in
         Pair ((of_N q), (opp (of_N r))))

  (** val quot : z -> z -> z **)

  let quot a b =
    fst (quotrem a b)

  (** val eq_dec : z -> z -> sumbool **)

  let eq_dec x y =
    match x with
    | Z0 -> (match y with
             | Z0 -> Left
             | _ -> Right)
    | Zpos p -> (match y with
                 | Zpos p0 -> Coq_Pos.eq_dec p p0
                 | _ -> Right)
    | Zneg p -> (match y with
                 | Zneg p0 -> Coq_Pos.eq_dec p p0
                 | _ -> Right)
 end

(** val true_dec : decision **)

let true_dec =
  Left

(** val not_dec : decision -> decision **)

let not_dec = function
| Left -> Right
| Right -> Left

(** val and_dec : decision -> decision -> decision **)

let and_dec p_dec q_dec =
  match p_dec with
  | Left -> q_dec
  | Right -> Right

(** val or_dec : decision -> decision -> decision **)

let or_dec p_dec q_dec =
  match p_dec with
  | Left -> Left
  | Right -> q_dec

(** val bool_eq_dec : (bool, bool) relDecision **)

let bool_eq_dec x y =
  match x with
  | True -> (match y with
             | True -> Left
             | False -> Right)
  | False -> (match y with
              | True -> Right
              | False -> Left)

(** val prod_eq_dec :
    ('a1, 'a1) relDecision -> ('a2, 'a2) relDecision -> (('a1, 'a2) prod,
    ('a1, 'a2) prod) relDecision **)

let prod_eq_dec eqDecision0 eqDecision1 x y =
  let Pair (a, b) = x in
  let Pair (a0, b0) = y in
  (match decide_rel eqDecision0 a a0 with
   | Left -> decide_rel eqDecision1 b b0
   | Right -> Right)

(** val bool_decide : decision -> bool **)

let bool_decide = function
| Left -> True
| Right -> False

(** val from_option : ('a1 -> 'a2) -> 'a2 -> 'a1 option -> 'a2 **)

let from_option f y = function
| Some x -> f x
| None -> y

(** val option_eq_dec :
    ('a1, 'a1) relDecision -> ('a1 option, 'a1 option) relDecision **)

let option_eq_dec dec mx my =
  match mx with
  | Some x ->
    (match my with
     | Some y -> decide (decide_rel dec x y)
     | None -> Right)
  | None -> (match my with
             | Some _ -> Right
             | None -> Left)

(** val option_bind : (__ -> __ option) -> __ option -> __ option **)

let option_bind f = function
| Some x -> f x
| None -> None

(** val option_fmap : (__ -> __) -> __ option -> __ option **)

let option_fmap =
  option_map

(** val option_union_with : ('a1, 'a1 option) unionWith **)

let option_union_with f mx my =
  match mx with
  | Some x -> (match my with
               | Some y -> f x y
               | None -> Some x)
  | None -> my

(** val option_difference_with : ('a1, 'a1 option) differenceWith **)

let option_difference_with f mx my =
  match mx with
  | Some x -> (match my with
               | Some y -> f x y
               | None -> Some x)
  | None -> None

(** val z_le_dec : z -> z -> sumbool **)

let z_le_dec x y =
  match Z.compare x y with
  | Gt -> Right
  | _ -> Left

module Coq_Nat =
 struct
  (** val eq_dec : (nat, nat) relDecision **)

  let eq_dec =
    Nat.eq_dec

  (** val le_dec : (nat, nat) relDecision **)

  let le_dec =
    le_dec

  (** val lt_dec : (nat, nat) relDecision **)

  let lt_dec =
    lt_dec
 end

module Coq0_Pos =
 struct
  (** val eq_dec : (positive, positive) relDecision **)

  let eq_dec =
    Coq_Pos.eq_dec

  (** val reverse_go : positive -> positive -> positive **)

  let rec reverse_go p1 = function
  | XI p3 -> reverse_go (XI p1) p3
  | XO p3 -> reverse_go (XO p1) p3
  | XH -> p1

  (** val reverse : positive -> positive **)

  let reverse =
    reverse_go XH
 end

module Coq0_N =
 struct
  (** val eq_dec : (n, n) relDecision **)

  let eq_dec =
    Coq_N.eq_dec
 end

module Coq_Z =
 struct
  (** val eq_dec : (z, z) relDecision **)

  let eq_dec =
    Z.eq_dec

  (** val le_dec : (z, z) relDecision **)

  let le_dec =
    z_le_dec
 end

module Coq_list =
 struct
  (** val list_lookup : (nat, 'a1, 'a1 list) lookup **)

  let rec list_lookup i = function
  | Nil -> None
  | Cons (x, l0) ->
    (match i with
     | O -> Some x
     | S i0 -> lookup0 list_lookup i0 l0)

  (** val list_alter : (nat, 'a1, 'a1 list) alter **)

  let rec list_alter f i = function
  | Nil -> Nil
  | Cons (x, l0) ->
    (match i with
     | O -> Cons ((f x), l0)
     | S i0 -> Cons (x, (list_alter f i0 l0)))

  (** val list_insert : (nat, 'a1, 'a1 list) insert **)

  let rec list_insert i y = function
  | Nil -> Nil
  | Cons (x, l0) ->
    (match i with
     | O -> Cons (y, l0)
     | S i0 -> Cons (x, (insert0 list_insert i0 y l0)))

  (** val list_filter : ('a1 -> decision) -> 'a1 list -> 'a1 list **)

  let rec list_filter x = function
  | Nil -> Nil
  | Cons (x0, l0) ->
    (match decide (x x0) with
     | Left -> Cons (x0, (filter0 (fun _ -> list_filter) x l0))
     | Right -> filter0 (fun _ -> list_filter) x l0)

  (** val list_elem_of_dec :
      ('a1, 'a1) relDecision -> ('a1, 'a1 list) relDecision **)

  let rec list_elem_of_dec dec x = function
  | Nil -> Right
  | Cons (y, l0) ->
    (match decide (decide_rel dec x y) with
     | Left -> Left
     | Right -> list_elem_of_dec dec x l0)
 end

module Coq0_list =
 struct
  (** val list_fmap : (__ -> __) -> __ list -> __ list **)

  let rec list_fmap f = function
  | Nil -> Nil
  | Cons (x, l0) -> Cons ((f x), (list_fmap f l0))

  (** val list_omap : (__ -> __ option) -> __ list -> __ list **)

  let rec list_omap f = function
  | Nil -> Nil
  | Cons (x, l0) ->
    (match f x with
     | Some y -> Cons (y, (list_omap f l0))
     | None -> list_omap f l0)
 end

type 'a countable = { encode : ('a -> positive);
                      decode : (positive -> 'a option) }

(** val prod_encode_fst : positive -> positive **)

let rec prod_encode_fst = function
| XI p0 -> XI (XO (prod_encode_fst p0))
| XO p0 -> XO (XO (prod_encode_fst p0))
| XH -> XH

(** val prod_encode_snd : positive -> positive **)

let rec prod_encode_snd = function
| XI p0 -> XO (XI (prod_encode_snd p0))
| XO p0 -> XO (XO (prod_encode_snd p0))
| XH -> XO XH

(** val prod_encode : positive -> positive -> positive **)

let rec prod_encode p q =
  match p with
  | XI p0 ->
    (match q with
     | XI q0 -> XI (XI (prod_encode p0 q0))
     | XO q0 -> XI (XO (prod_encode p0 q0))
     | XH -> XI (XI (prod_encode_fst p0)))
  | XO p0 ->
    (match q with
     | XI q0 -> XO (XI (prod_encode p0 q0))
     | XO q0 -> XO (XO (prod_encode p0 q0))
     | XH -> XO (XI (prod_encode_fst p0)))
  | XH ->
    (match q with
     | XI q0 -> XI (XI (prod_encode_snd q0))
     | XO q0 -> XI (XO (prod_encode_snd q0))
     | XH -> XI XH)

(** val prod_decode_fst : positive -> positive option **)

let rec prod_decode_fst = function
| XI p0 ->
  (match p0 with
   | XI p1 -> Some (match prod_decode_fst p1 with
                    | Some q -> XI q
                    | None -> XH)
   | XO p1 -> Some (match prod_decode_fst p1 with
                    | Some q -> XI q
                    | None -> XH)
   | XH -> Some XH)
| XO p0 ->
  (match p0 with
   | XI p1 ->
     fmap (Obj.magic (fun _ _ -> option_fmap)) (fun x -> XO x)
       (prod_decode_fst p1)
   | XO p1 ->
     fmap (Obj.magic (fun _ _ -> option_fmap)) (fun x -> XO x)
       (prod_decode_fst p1)
   | XH -> None)
| XH -> Some XH

(** val prod_decode_snd : positive -> positive option **)

let rec prod_decode_snd = function
| XI p0 ->
  (match p0 with
   | XI p1 -> Some (match prod_decode_snd p1 with
                    | Some q -> XI q
                    | None -> XH)
   | XO p1 ->
     fmap (Obj.magic (fun _ _ -> option_fmap)) (fun x -> XO x)
       (prod_decode_snd p1)
   | XH -> Some XH)
| XO p0 ->
  (match p0 with
   | XI p1 -> Some (match prod_decode_snd p1 with
                    | Some q -> XI q
                    | None -> XH)
   | XO p1 ->
     fmap (Obj.magic (fun _ _ -> option_fmap)) (fun x -> XO x)
       (prod_decode_snd p1)
   | XH -> Some XH)
| XH -> None

(** val prod_countable :
    ('a1, 'a1) relDecision -> 'a1 countable -> ('a2, 'a2) relDecision -> 'a2
    countable -> ('a1, 'a2) prod countable **)

let prod_countable _ h _ h0 =
  { encode = (fun xy ->
    prod_encode (h.encode (fst xy)) (h0.encode (snd xy))); decode = (fun p ->
    mbind (Obj.magic (fun _ _ -> option_bind)) (fun x ->
      mbind (Obj.magic (fun _ _ -> option_bind)) (fun y -> Some (Pair (x,
        y)))
        (mbind (Obj.magic (fun _ _ -> option_bind)) (Obj.magic h0).decode
          (Obj.magic prod_decode_snd p)))
      (mbind (Obj.magic (fun _ _ -> option_bind)) (Obj.magic h).decode
        (Obj.magic prod_decode_fst p))) }

(** val n_countable : n countable **)

let n_countable =
  { encode = (fun x -> match x with
                       | N0 -> XH
                       | Npos p -> Coq_Pos.succ p);
    decode = (fun p ->
    match decide (decide_rel Coq0_Pos.eq_dec p XH) with
    | Left -> Some N0
    | Right -> Some (Npos (Coq_Pos.pred p))) }

(** val nat_countable : nat countable **)

let nat_countable =
  { encode = (fun x -> n_countable.encode (Coq_N.of_nat x)); decode =
    (fun p ->
    fmap (Obj.magic (fun _ _ -> option_fmap)) Coq_N.to_nat
      ((Obj.magic n_countable).decode p)) }

(** val set_fresh :
    ('a1, 'a2) elements -> ('a1, 'a1 list) fresh -> ('a1, 'a2) fresh **)

let set_fresh h h0 =
  compose (fresh0 h0) (elements0 h)

(** val max_infinite : ('a1 -> 'a1 -> 'a1) -> 'a1 -> 'a1 infinite **)

let max_infinite =
  fold_right

(** val nat_infinite : nat infinite **)

let nat_infinite =
  max_infinite (compose Nat.max (fun x -> S x)) O

type ('k, 'a, 'm) mapFold = __ -> ('k -> 'a -> __ -> __) -> __ -> 'm -> __

(** val map_fold :
    ('a1, 'a2, 'a3) mapFold -> ('a1 -> 'a2 -> 'a4 -> 'a4) -> 'a4 -> 'a3 -> 'a4 **)

let map_fold mapFold0 x x0 x1 =
  Obj.magic mapFold0 __ x x0 x1

(** val map_insert :
    ('a1, 'a2, 'a3) partialAlter -> ('a1, 'a2, 'a3) insert **)

let map_insert h i x =
  partial_alter h (fun _ -> Some x) i

(** val map_delete : ('a1, 'a2, 'a3) partialAlter -> ('a1, 'a3) delete **)

let map_delete h =
  partial_alter h (fun _ -> None)

(** val map_singleton :
    ('a1, 'a2, 'a3) partialAlter -> 'a3 empty -> ('a1, 'a2, 'a3) singletonM **)

let map_singleton h h0 i x =
  insert0 (map_insert h) i x (empty0 h0)

(** val map_to_list :
    ('a1, 'a2, 'a3) mapFold -> 'a3 -> ('a1, 'a2) prod list **)

let map_to_list h =
  map_fold h (fun i x x0 -> Cons ((Pair (i, x)), x0)) Nil

(** val map_union_with : 'a1 merge -> ('a2, 'a1) unionWith **)

let map_union_with h f =
  merge0 h (union_with option_union_with f)

(** val map_difference_with : 'a1 merge -> ('a2, 'a1) differenceWith **)

let map_difference_with h f =
  merge0 h (difference_with option_difference_with f)

(** val map_union : 'a1 merge -> 'a1 union **)

let map_union h =
  union_with (map_union_with h) (fun x _ -> Some x)

(** val map_difference : 'a1 merge -> 'a1 difference **)

let map_difference h =
  difference_with (map_difference_with h) (fun _ _ -> None)

type 'munit mapset' =
  'munit
  (* singleton inductive, whose constructor was Mapset *)

(** val mapset_empty : (__ -> 'a1 empty) -> 'a1 mapset' empty **)

let mapset_empty h1 =
  empty0 (h1 __)

(** val mapset_singleton :
    (__ -> 'a2 empty) -> (__ -> ('a1, __, 'a2) partialAlter) -> ('a1, 'a2
    mapset') singleton **)

let mapset_singleton h1 h2 x =
  singletonM0 (map_singleton (Obj.magic h2 __) (h1 __)) x Tt

(** val mapset_union : 'a1 merge -> 'a1 mapset' union **)

let mapset_union h4 x1 x2 =
  union0 (map_union h4) x1 x2

(** val mapset_difference : 'a1 merge -> 'a1 mapset' difference **)

let mapset_difference h4 x1 x2 =
  difference0 (map_difference h4) x1 x2

(** val mapset_elements :
    (__ -> ('a1, __, 'a2) mapFold) -> ('a1, 'a2 mapset') elements **)

let mapset_elements h5 x =
  fmap (Obj.magic (fun _ _ -> Coq0_list.list_fmap)) fst
    (Obj.magic map_to_list (h5 __) x)

(** val mapset_dom : 'a1 fMap -> ('a1, 'a1 mapset') dom **)

let mapset_dom h m0 =
  fmap h (fun _ -> Tt) m0

type 'a gmap_dep_ne =
| GNode001 of 'a gmap_dep_ne
| GNode010 of 'a
| GNode011 of 'a * 'a gmap_dep_ne
| GNode100 of 'a gmap_dep_ne
| GNode101 of 'a gmap_dep_ne * 'a gmap_dep_ne
| GNode110 of 'a gmap_dep_ne * 'a
| GNode111 of 'a gmap_dep_ne * 'a * 'a gmap_dep_ne

type 'a gmap_dep =
| GEmpty
| GNodes of 'a gmap_dep_ne

type ('k, 'a) gmap =
  'a gmap_dep
  (* singleton inductive, whose constructor was GMap *)

(** val gNode :
    'a1 gmap_dep -> (__, 'a1) prod option -> 'a1 gmap_dep -> 'a1 gmap_dep **)

let gNode ml mx mr =
  match ml with
  | GEmpty ->
    (match mx with
     | Some p0 ->
       let Pair (_, x) = p0 in
       (match mr with
        | GEmpty -> GNodes (GNode010 x)
        | GNodes r -> GNodes (GNode011 (x, r)))
     | None ->
       (match mr with
        | GEmpty -> GEmpty
        | GNodes r -> GNodes (GNode001 r)))
  | GNodes l ->
    (match mx with
     | Some p0 ->
       let Pair (_, x) = p0 in
       (match mr with
        | GEmpty -> GNodes (GNode110 (l, x))
        | GNodes r -> GNodes (GNode111 (l, x, r)))
     | None ->
       (match mr with
        | GEmpty -> GNodes (GNode100 l)
        | GNodes r -> GNodes (GNode101 (l, r))))

(** val gmap_dep_ne_case :
    'a1 gmap_dep_ne -> ('a1 gmap_dep -> (__, 'a1) prod option -> 'a1 gmap_dep
    -> 'a2) -> 'a2 **)

let gmap_dep_ne_case t f =
  match t with
  | GNode001 r -> f GEmpty None (GNodes r)
  | GNode010 x -> f GEmpty (Some (Pair (__, x))) GEmpty
  | GNode011 (x, r) -> f GEmpty (Some (Pair (__, x))) (GNodes r)
  | GNode100 l -> f (GNodes l) None GEmpty
  | GNode101 (l, r) -> f (GNodes l) None (GNodes r)
  | GNode110 (l, x) -> f (GNodes l) (Some (Pair (__, x))) GEmpty
  | GNode111 (l, x, r) -> f (GNodes l) (Some (Pair (__, x))) (GNodes r)

(** val gmap_dep_ne_lookup : positive -> 'a1 gmap_dep_ne -> 'a1 option **)

let rec gmap_dep_ne_lookup i = function
| GNode001 r -> (match i with
                 | XI i0 -> gmap_dep_ne_lookup i0 r
                 | _ -> None)
| GNode010 x -> (match i with
                 | XH -> Some x
                 | _ -> None)
| GNode011 (x, r) ->
  (match i with
   | XI i0 -> gmap_dep_ne_lookup i0 r
   | XO _ -> None
   | XH -> Some x)
| GNode100 l -> (match i with
                 | XO i0 -> gmap_dep_ne_lookup i0 l
                 | _ -> None)
| GNode101 (l, r) ->
  (match i with
   | XI i0 -> gmap_dep_ne_lookup i0 r
   | XO i0 -> gmap_dep_ne_lookup i0 l
   | XH -> None)
| GNode110 (l, x) ->
  (match i with
   | XI _ -> None
   | XO i0 -> gmap_dep_ne_lookup i0 l
   | XH -> Some x)
| GNode111 (l, x, r) ->
  (match i with
   | XI i0 -> gmap_dep_ne_lookup i0 r
   | XO i0 -> gmap_dep_ne_lookup i0 l
   | XH -> Some x)

(** val gmap_dep_lookup : positive -> 'a1 gmap_dep -> 'a1 option **)

let gmap_dep_lookup i = function
| GEmpty -> None
| GNodes t -> gmap_dep_ne_lookup i t

(** val gmap_lookup :
    ('a1, 'a1) relDecision -> 'a1 countable -> ('a1, 'a2, ('a1, 'a2) gmap)
    lookup **)

let gmap_lookup _ h k mt =
  gmap_dep_lookup (h.encode k) mt

(** val gmap_empty :
    ('a1, 'a1) relDecision -> 'a1 countable -> ('a1, 'a2) gmap empty **)

let gmap_empty _ _ =
  GEmpty

(** val gmap_dep_ne_singleton : positive -> 'a1 -> 'a1 gmap_dep_ne **)

let rec gmap_dep_ne_singleton i x =
  match i with
  | XI i0 -> GNode001 (gmap_dep_ne_singleton i0 x)
  | XO i0 -> GNode100 (gmap_dep_ne_singleton i0 x)
  | XH -> GNode010 x

(** val gmap_partial_alter_aux :
    (positive -> __ -> 'a1 gmap_dep_ne -> 'a1 gmap_dep) -> ('a1 option -> 'a1
    option) -> positive -> 'a1 gmap_dep -> 'a1 gmap_dep **)

let gmap_partial_alter_aux go f i = function
| GEmpty ->
  (match f None with
   | Some x -> GNodes (gmap_dep_ne_singleton i x)
   | None -> GEmpty)
| GNodes t -> go i __ t

(** val gmap_dep_ne_partial_alter :
    ('a1 option -> 'a1 option) -> positive -> 'a1 gmap_dep_ne -> 'a1 gmap_dep **)

let rec gmap_dep_ne_partial_alter f i = function
| GNode001 r ->
  (match i with
   | XI i0 ->
     (match gmap_dep_ne_partial_alter f i0 r with
      | GEmpty -> GEmpty
      | GNodes r0 -> GNodes (GNode001 r0))
   | XO i0 ->
     (match f None with
      | Some x0 ->
        let l = gmap_dep_ne_singleton i0 x0 in GNodes (GNode101 (l, r))
      | None -> GNodes (GNode001 r))
   | XH ->
     (match f None with
      | Some a ->
        let p0 = Pair (__, a) in
        let Pair (_, x0) = p0 in GNodes (GNode011 (x0, r))
      | None -> GNodes (GNode001 r)))
| GNode010 x0 ->
  (match i with
   | XI i0 ->
     (match f None with
      | Some x1 ->
        let r = gmap_dep_ne_singleton i0 x1 in GNodes (GNode011 (x0, r))
      | None -> GNodes (GNode010 x0))
   | XO i0 ->
     (match f None with
      | Some x1 ->
        let l = gmap_dep_ne_singleton i0 x1 in GNodes (GNode110 (l, x0))
      | None -> GNodes (GNode010 x0))
   | XH ->
     (match f (Some x0) with
      | Some a ->
        let p0 = Pair (__, a) in let Pair (_, x1) = p0 in GNodes (GNode010 x1)
      | None -> GEmpty))
| GNode011 (x0, r) ->
  (match i with
   | XI i0 ->
     (match gmap_dep_ne_partial_alter f i0 r with
      | GEmpty -> GNodes (GNode010 x0)
      | GNodes r0 -> GNodes (GNode011 (x0, r0)))
   | XO i0 ->
     (match f None with
      | Some x1 ->
        let l = gmap_dep_ne_singleton i0 x1 in GNodes (GNode111 (l, x0, r))
      | None -> GNodes (GNode011 (x0, r)))
   | XH ->
     (match f (Some x0) with
      | Some a ->
        let p0 = Pair (__, a) in
        let Pair (_, x1) = p0 in GNodes (GNode011 (x1, r))
      | None -> GNodes (GNode001 r)))
| GNode100 l ->
  (match i with
   | XI i0 ->
     (match f None with
      | Some x0 ->
        let r = gmap_dep_ne_singleton i0 x0 in GNodes (GNode101 (l, r))
      | None -> GNodes (GNode100 l))
   | XO i0 ->
     (match gmap_dep_ne_partial_alter f i0 l with
      | GEmpty -> GEmpty
      | GNodes l0 -> GNodes (GNode100 l0))
   | XH ->
     (match f None with
      | Some a ->
        let p0 = Pair (__, a) in
        let Pair (_, x0) = p0 in GNodes (GNode110 (l, x0))
      | None -> GNodes (GNode100 l)))
| GNode101 (l, r) ->
  (match i with
   | XI i0 ->
     (match gmap_dep_ne_partial_alter f i0 r with
      | GEmpty -> GNodes (GNode100 l)
      | GNodes r0 -> GNodes (GNode101 (l, r0)))
   | XO i0 ->
     (match gmap_dep_ne_partial_alter f i0 l with
      | GEmpty -> GNodes (GNode001 r)
      | GNodes l0 -> GNodes (GNode101 (l0, r)))
   | XH ->
     (match f None with
      | Some a ->
        let p0 = Pair (__, a) in
        let Pair (_, x0) = p0 in GNodes (GNode111 (l, x0, r))
      | None -> GNodes (GNode101 (l, r))))
| GNode110 (l, x0) ->
  (match i with
   | XI i0 ->
     (match f None with
      | Some x1 ->
        let r = gmap_dep_ne_singleton i0 x1 in GNodes (GNode111 (l, x0, r))
      | None -> GNodes (GNode110 (l, x0)))
   | XO i0 ->
     (match gmap_dep_ne_partial_alter f i0 l with
      | GEmpty -> GNodes (GNode010 x0)
      | GNodes l0 -> GNodes (GNode110 (l0, x0)))
   | XH ->
     (match f (Some x0) with
      | Some a ->
        let p0 = Pair (__, a) in
        let Pair (_, x1) = p0 in GNodes (GNode110 (l, x1))
      | None -> GNodes (GNode100 l)))
| GNode111 (l, x0, r) ->
  (match i with
   | XI i0 ->
     (match gmap_dep_ne_partial_alter f i0 r with
      | GEmpty -> GNodes (GNode110 (l, x0))
      | GNodes r0 -> GNodes (GNode111 (l, x0, r0)))
   | XO i0 ->
     (match gmap_dep_ne_partial_alter f i0 l with
      | GEmpty -> GNodes (GNode011 (x0, r))
      | GNodes l0 -> GNodes (GNode111 (l0, x0, r)))
   | XH ->
     (match f (Some x0) with
      | Some a ->
        let p0 = Pair (__, a) in
        let Pair (_, x1) = p0 in GNodes (GNode111 (l, x1, r))
      | None -> GNodes (GNode101 (l, r))))

(** val gmap_dep_partial_alter :
    ('a1 option -> 'a1 option) -> positive -> 'a1 gmap_dep -> 'a1 gmap_dep **)

let gmap_dep_partial_alter f i x =
  gmap_partial_alter_aux (fun x0 _ -> gmap_dep_ne_partial_alter f x0) f i x

(** val gmap_partial_alter :
    ('a1, 'a1) relDecision -> 'a1 countable -> ('a1, 'a2, ('a1, 'a2) gmap)
    partialAlter **)

let gmap_partial_alter _ h f k pat =
  gmap_dep_partial_alter f (h.encode k) pat

(** val gmap_dep_ne_fmap :
    ('a1 -> 'a2) -> 'a1 gmap_dep_ne -> 'a2 gmap_dep_ne **)

let rec gmap_dep_ne_fmap f = function
| GNode001 r -> GNode001 (gmap_dep_ne_fmap f r)
| GNode010 x0 -> GNode010 (f x0)
| GNode011 (x0, r) -> GNode011 ((f x0), (gmap_dep_ne_fmap f r))
| GNode100 l -> GNode100 (gmap_dep_ne_fmap f l)
| GNode101 (l, r) -> GNode101 ((gmap_dep_ne_fmap f l), (gmap_dep_ne_fmap f r))
| GNode110 (l, x0) -> GNode110 ((gmap_dep_ne_fmap f l), (f x0))
| GNode111 (l, x0, r) ->
  GNode111 ((gmap_dep_ne_fmap f l), (f x0), (gmap_dep_ne_fmap f r))

(** val gmap_dep_fmap : ('a1 -> 'a2) -> 'a1 gmap_dep -> 'a2 gmap_dep **)

let gmap_dep_fmap f = function
| GEmpty -> GEmpty
| GNodes t -> GNodes (gmap_dep_ne_fmap f t)

(** val gmap_fmap :
    ('a1, 'a1) relDecision -> 'a1 countable -> (__ -> __) -> ('a1, __) gmap
    -> ('a1, __) gmap **)

let gmap_fmap _ _ =
  gmap_dep_fmap

(** val gmap_dep_omap_aux :
    ('a1 gmap_dep_ne -> 'a2 gmap_dep) -> 'a1 gmap_dep -> 'a2 gmap_dep **)

let gmap_dep_omap_aux go = function
| GEmpty -> GEmpty
| GNodes t' -> go t'

(** val gmap_dep_ne_omap :
    ('a1 -> 'a2 option) -> 'a1 gmap_dep_ne -> 'a2 gmap_dep **)

let rec gmap_dep_ne_omap f x =
  gmap_dep_ne_case x (fun ml mx mr ->
    gNode (gmap_dep_omap_aux (fun x0 -> gmap_dep_ne_omap f x0) ml)
      (mbind (Obj.magic (fun _ _ -> option_bind)) (fun pat ->
        let Pair (_, x0) = pat in
        fmap (Obj.magic (fun _ _ -> option_fmap)) (fun x1 -> Pair (__, x1))
          (Obj.magic f x0))
        (Obj.magic mx))
      (gmap_dep_omap_aux (fun x0 -> gmap_dep_ne_omap f x0) mr))

(** val gmap_merge_aux :
    ('a1 gmap_dep_ne -> 'a2 gmap_dep_ne -> 'a3 gmap_dep) -> ('a1 option ->
    'a2 option -> 'a3 option) -> 'a1 gmap_dep -> 'a2 gmap_dep -> 'a3 gmap_dep **)

let gmap_merge_aux go f mt1 mt2 =
  match mt1 with
  | GEmpty ->
    (match mt2 with
     | GEmpty -> GEmpty
     | GNodes t2' -> gmap_dep_ne_omap (fun x -> f None (Some x)) t2')
  | GNodes t1' ->
    (match mt2 with
     | GEmpty -> gmap_dep_ne_omap (fun x -> f (Some x) None) t1'
     | GNodes t2' -> go t1' t2')

(** val diag_None' :
    ('a1 option -> 'a2 option -> 'a3 option) -> (__, 'a1) prod option -> (__,
    'a2) prod option -> (__, 'a3) prod option **)

let diag_None' f mx my =
  match mx with
  | Some p0 ->
    let Pair (_, x) = p0 in
    (match my with
     | Some p1 ->
       let Pair (_, y) = p1 in
       fmap (Obj.magic (fun _ _ -> option_fmap)) (fun x0 -> Pair (__, x0))
         (Obj.magic f (Some x) (Some y))
     | None ->
       fmap (Obj.magic (fun _ _ -> option_fmap)) (fun x0 -> Pair (__, x0))
         (Obj.magic f (Some x) None))
  | None ->
    (match my with
     | Some p0 ->
       let Pair (_, y) = p0 in
       fmap (Obj.magic (fun _ _ -> option_fmap)) (fun x -> Pair (__, x))
         (Obj.magic f None (Some y))
     | None -> None)

(** val gmap_dep_ne_merge :
    ('a1 option -> 'a2 option -> 'a3 option) -> 'a1 gmap_dep_ne -> 'a2
    gmap_dep_ne -> 'a3 gmap_dep **)

let rec gmap_dep_ne_merge f x x0 =
  gmap_dep_ne_case x (fun ml1 mx1 mr1 ->
    gmap_dep_ne_case x0 (fun ml2 mx2 mr2 ->
      gNode
        (gmap_merge_aux (fun x1 x2 -> gmap_dep_ne_merge f x1 x2) f ml1 ml2)
        (diag_None' f mx1 mx2)
        (gmap_merge_aux (fun x1 x2 -> gmap_dep_ne_merge f x1 x2) f mr1 mr2)))

(** val gmap_dep_merge :
    ('a1 option -> 'a2 option -> 'a3 option) -> 'a1 gmap_dep -> 'a2 gmap_dep
    -> 'a3 gmap_dep **)

let gmap_dep_merge f =
  gmap_merge_aux (gmap_dep_ne_merge f) f

(** val gmap_merge :
    ('a1, 'a1) relDecision -> 'a1 countable -> (__ option -> __ option -> __
    option) -> ('a1, __) gmap -> ('a1, __) gmap -> ('a1, __) gmap **)

let gmap_merge _ _ =
  gmap_dep_merge

(** val gmap_fold_aux :
    (positive -> 'a2 -> 'a1 gmap_dep_ne -> 'a2) -> positive -> 'a2 -> 'a1
    gmap_dep -> 'a2 **)

let gmap_fold_aux go i y = function
| GEmpty -> y
| GNodes t -> go i y t

(** val gmap_dep_ne_fold :
    (positive -> 'a1 -> 'a2 -> 'a2) -> positive -> 'a2 -> 'a1 gmap_dep_ne ->
    'a2 **)

let rec gmap_dep_ne_fold f x x0 x1 =
  gmap_dep_ne_case x1 (fun ml mx mr ->
    gmap_fold_aux (fun x2 x3 x4 -> gmap_dep_ne_fold f x2 x3 x4) (XI x)
      (gmap_fold_aux (fun x2 x3 x4 -> gmap_dep_ne_fold f x2 x3 x4) (XO x)
        (match mx with
         | Some p0 -> let Pair (_, x2) = p0 in f (Coq0_Pos.reverse x) x2 x0
         | None -> x0)
        ml)
      mr)

(** val gmap_dep_fold :
    (positive -> 'a1 -> 'a2 -> 'a2) -> positive -> 'a2 -> 'a1 gmap_dep -> 'a2 **)

let gmap_dep_fold f =
  gmap_fold_aux (gmap_dep_ne_fold f)

(** val gmap_fold :
    ('a1, 'a1) relDecision -> 'a1 countable -> ('a1 -> 'a2 -> __ -> __) -> __
    -> ('a1, 'a2) gmap -> __ **)

let gmap_fold _ h f y pat =
  gmap_dep_fold (fun i x ->
    match h.decode i with
    | Some k -> f k x
    | None -> id) XH y pat

type 'k gset = ('k, unit0) gmap mapset'

(** val gset_empty :
    ('a1, 'a1) relDecision -> 'a1 countable -> 'a1 gset empty **)

let gset_empty eqDecision0 h =
  mapset_empty (fun _ -> gmap_empty eqDecision0 h)

(** val gset_singleton :
    ('a1, 'a1) relDecision -> 'a1 countable -> ('a1, 'a1 gset) singleton **)

let gset_singleton eqDecision0 h =
  mapset_singleton (fun _ -> gmap_empty eqDecision0 h)
    (Obj.magic (fun _ -> gmap_partial_alter eqDecision0 h))

(** val gset_union :
    ('a1, 'a1) relDecision -> 'a1 countable -> 'a1 gset union **)

let gset_union eqDecision0 h =
  mapset_union (Obj.magic (fun _ _ _ -> gmap_merge eqDecision0 h))

(** val gset_difference :
    ('a1, 'a1) relDecision -> 'a1 countable -> 'a1 gset difference **)

let gset_difference eqDecision0 h =
  mapset_difference (Obj.magic (fun _ _ _ -> gmap_merge eqDecision0 h))

(** val gset_elements :
    ('a1, 'a1) relDecision -> 'a1 countable -> ('a1, 'a1 gset) elements **)

let gset_elements eqDecision0 h =
  mapset_elements (Obj.magic (fun _ _ -> gmap_fold eqDecision0 h))

(** val gset_dom :
    ('a1, 'a1) relDecision -> 'a1 countable -> (('a1, 'a2) gmap, 'a1 gset) dom **)

let gset_dom eqDecision0 h m0 =
  mapset_dom (Obj.magic (fun _ _ -> gmap_fmap eqDecision0 h)) (Obj.magic m0)

type node_id = nat

type owner_id = nat

type val0 = z

type freshness =
| Clean
| Check
| Dirty

(** val stale : freshness -> bool **)

let stale = function
| Clean -> False
| _ -> True

(** val freshness_eq_dec : (freshness, freshness) relDecision **)

let freshness_eq_dec x y =
  match x with
  | Clean -> (match y with
              | Clean -> Left
              | _ -> Right)
  | Check -> (match y with
              | Check -> Left
              | _ -> Right)
  | Dirty -> (match y with
              | Dirty -> Left
              | _ -> Right)

type var = nat

type expr =
| EConst of val0
| EVar of var
| EPrev
| EAdd of expr * expr
| ESub of expr * expr
| EMul of expr * expr
| EDiv of expr * expr
| ELt of expr * expr
| EEq of expr * expr

type outcome =
| Ok of val0
| Fail of val0

type comparer =
| CmpEq
| CmpNever
| CmpThrows of val0

type scope_mode =
| Pure
| Owning

type cmd =
| CRet of expr
| CThrow of val0
| CLet of var * expr * cmd
| CIf of expr * cmd * cmd
| CRead of var * expr * cmd
| CTryRead of var * var * expr * cmd
| CReadUntracked of var * expr * cmd
| CWrite of expr * expr * cmd
| CSignal of var * expr * cmd
| CMemo of var * scope_mode * comparer * cmd * cmd
| CEffect of var * cmd * cmd
| CCleanup of cmd * cmd
| CDispose of expr * cmd
| CBatch of cmd * cmd
| CLog of expr * cmd

type env = (var, val0) prod list

(** val env_lookup : env -> var -> val0 option **)

let rec env_lookup g x =
  match g with
  | Nil -> None
  | Cons (p, g') ->
    let Pair (y, v) = p in
    (match Nat.eqb x y with
     | True -> Some v
     | False -> env_lookup g' x)

(** val bool_val : bool -> val0 **)

let bool_val = function
| True -> Zpos XH
| False -> Z0

(** val div_zero_failure : val0 **)

let div_zero_failure =
  Zneg XH

(** val eval_expr : env -> val0 option -> expr -> outcome option **)

let rec eval_expr g p e =
  let bin = fun f a b ->
    match eval_expr g p a with
    | Some o ->
      (match o with
       | Ok u ->
         (match eval_expr g p b with
          | Some o0 ->
            (match o0 with
             | Ok v -> Some (f u v)
             | Fail f0 -> Some (Fail f0))
          | None -> None)
       | Fail f0 -> Some (Fail f0))
    | None -> None
  in
  (match e with
   | EConst z0 -> Some (Ok z0)
   | EVar x ->
     fmap (Obj.magic (fun _ _ -> option_fmap)) (fun x0 -> Ok x0)
       (Obj.magic env_lookup g x)
   | EPrev ->
     fmap (Obj.magic (fun _ _ -> option_fmap)) (fun x -> Ok x) (Obj.magic p)
   | EAdd (a, b) -> bin (fun u v -> Ok (Z.add u v)) a b
   | ESub (a, b) -> bin (fun u v -> Ok (Z.sub u v)) a b
   | EMul (a, b) -> bin (fun u v -> Ok (Z.mul u v)) a b
   | EDiv (a, b) ->
     bin (fun u v ->
       match decide (decide_rel Coq_Z.eq_dec v Z0) with
       | Left -> Fail div_zero_failure
       | Right -> Ok (Z.quot u v)) a b
   | ELt (a, b) -> bin (fun u v -> Ok (bool_val (Z.ltb u v))) a b
   | EEq (a, b) -> bin (fun u v -> Ok (bool_val (Z.eqb u v))) a b)

type event =
| EvRun of node_id
| EvLog of node_id * val0
| EvCleanup of node_id
| EvFail of node_id * val0
| EvDispose of node_id
| EvHostRead of node_id * outcome

(** val scan_limit : nat **)

let scan_limit =
  S (S (S (S (S (S (S (S O)))))))

type observer_set = { observer_items : (node_id, nat) prod list;
                      observer_latest : nat }

(** val empty_observer_set : observer_set **)

let empty_observer_set =
  { observer_items = Nil; observer_latest = O }

type tried =
| TAdded of nat
| TUnlinked
| TUnknown

(** val observer_view : observer_set -> node_id list **)

let observer_view o =
  fmap (Obj.magic (fun _ _ -> Coq0_list.list_fmap)) fst
    (Obj.magic o.observer_items)

(** val observers_add :
    node_id -> nat -> observer_set -> (observer_set, nat) prod **)

let observers_add c slot o =
  let h = length o.observer_items in
  Pair ({ observer_items =
  (app o.observer_items (Cons ((Pair (c, slot)), Nil))); observer_latest =
  h }, h)

(** val observers_try_add :
    node_id -> nat -> observer_set -> (observer_set, tried) prod **)

let observers_try_add c slot o =
  let add0 = fun _ ->
    let Pair (o', h) = observers_add c slot o in Pair (o', (TAdded h))
  in
  (match decide (decide_rel Coq_Nat.eq_dec (length o.observer_items) O) with
   | Left -> add0 Tt
   | Right ->
     (match decide
              (decide_rel (Obj.magic option_eq_dec Coq_Nat.eq_dec)
                (fmap (Obj.magic (fun _ _ -> option_fmap)) fst
                  (lookup0 Coq_list.list_lookup o.observer_latest
                    o.observer_items))
                (Some c)) with
      | Left -> Pair (o, TUnlinked)
      | Right ->
        (match decide
                 (decide_rel Coq_Nat.le_dec (length o.observer_items)
                   scan_limit) with
         | Left ->
           (match decide
                    (decide_rel (Coq_list.list_elem_of_dec Coq_Nat.eq_dec) c
                      (observer_view o)) with
            | Left -> Pair (o, TUnlinked)
            | Right -> add0 Tt)
         | Right -> Pair (o, TUnknown))))

(** val observers_remove_at :
    node_id -> nat -> observer_set -> (observer_set, ((node_id, nat) prod,
    nat) prod option) prod option **)

let observers_remove_at c h o =
  let last = pred (length o.observer_items) in
  (match lookup0 Coq_list.list_lookup h o.observer_items with
   | Some y ->
     let Pair (c', _) = y in
     (match decide (decide_rel Coq_Nat.eq_dec c' c) with
      | Left ->
        (match decide (decide_rel Coq_Nat.eq_dec h last) with
         | Left ->
           Some (Pair ({ observer_items = (firstn last o.observer_items);
             observer_latest = o.observer_latest }, None))
         | Right ->
           (match lookup0 Coq_list.list_lookup last o.observer_items with
            | Some y0 ->
              Some (Pair ({ observer_items =
                (insert0 Coq_list.list_insert h y0
                  (firstn last o.observer_items));
                observer_latest =
                (match decide
                         (decide_rel Coq_Nat.eq_dec o.observer_latest last) with
                 | Left -> h
                 | Right -> o.observer_latest) },
                (Some (Pair (y0, h)))))
            | None -> None))
      | Right -> None)
   | None -> None)

type source_list = { source_slots : (node_id, nat option) prod list;
                     source_cursor : nat }

type edgeWorld = { world_observers : (node_id, observer_set) gmap;
                   world_sources : (node_id, source_list) gmap }

(** val empty_source_list : source_list **)

let empty_source_list =
  { source_slots = Nil; source_cursor = O }

(** val empty_world : edgeWorld **)

let empty_world =
  { world_observers = (empty0 (gmap_empty Coq_Nat.eq_dec nat_countable));
    world_sources = (empty0 (gmap_empty Coq_Nat.eq_dec nat_countable)) }

(** val observers_of : edgeWorld -> node_id -> observer_set **)

let observers_of w s =
  from_option (Obj.magic id) empty_observer_set
    (lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) s w.world_observers)

(** val sources_of : edgeWorld -> node_id -> source_list **)

let sources_of w c =
  from_option (Obj.magic id) empty_source_list
    (lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) c w.world_sources)

(** val slot_at :
    edgeWorld -> node_id -> nat -> (node_id, nat option) prod option **)

let slot_at w c i =
  lookup0 Coq_list.list_lookup i (sources_of w c).source_slots

(** val set_observers : node_id -> observer_set -> edgeWorld -> edgeWorld **)

let set_observers s o w =
  { world_observers =
    (insert0 (map_insert (gmap_partial_alter Coq_Nat.eq_dec nat_countable)) s
      o w.world_observers);
    world_sources = w.world_sources }

(** val set_sources : node_id -> source_list -> edgeWorld -> edgeWorld **)

let set_sources c l w =
  { world_observers = w.world_observers; world_sources =
    (insert0 (map_insert (gmap_partial_alter Coq_Nat.eq_dec nat_countable)) c
      l w.world_sources) }

(** val sources_begin_run : node_id -> edgeWorld -> edgeWorld **)

let sources_begin_run c w =
  set_sources c { source_slots = (sources_of w c).source_slots;
    source_cursor = O } w

(** val set_handle :
    node_id -> nat -> nat option -> edgeWorld -> edgeWorld **)

let set_handle c i mh w =
  let l = sources_of w c in
  set_sources c { source_slots =
    (alter0 Coq_list.list_alter (fun p -> Pair ((fst p), mh)) i
      l.source_slots);
    source_cursor = l.source_cursor } w

(** val sources_moved_in : node_id -> nat -> nat -> edgeWorld -> edgeWorld **)

let sources_moved_in c slot h w =
  set_handle c slot (Some h) w

(** val remove_edge :
    node_id -> node_id -> nat -> edgeWorld -> edgeWorld option **)

let remove_edge self s h w =
  match observers_remove_at self h (observers_of w s) with
  | Some p ->
    let Pair (o', n0) = p in
    let w1 = set_observers s o' w in
    Some
    (match n0 with
     | Some p0 ->
       let Pair (p1, h') = p0 in
       let Pair (m0, ms) = p1 in sources_moved_in m0 ms h' w1
     | None -> w1)
  | None -> None

(** val sources_remove_slot :
    node_id -> nat -> edgeWorld -> edgeWorld option **)

let sources_remove_slot self i w =
  match slot_at w self i with
  | Some p ->
    let Pair (s, o) = p in
    (match o with
     | Some h ->
       fmap (Obj.magic (fun _ _ -> option_fmap)) (set_handle self i None)
         (remove_edge self s h w)
     | None -> Some w)
  | None -> Some w

(** val trim_loop : node_id -> nat -> nat -> edgeWorld -> edgeWorld option **)

let rec trim_loop self i n0 w =
  match n0 with
  | O -> Some w
  | S n' ->
    mbind (Obj.magic (fun _ _ -> option_bind)) (trim_loop self (S i) n')
      (sources_remove_slot self i w)

(** val sources_trim_from :
    node_id -> nat -> edgeWorld -> edgeWorld option **)

let sources_trim_from self from w =
  mbind (Obj.magic (fun _ _ -> option_bind)) (fun w1 ->
    let l1 = sources_of w1 self in
    Some
    (set_sources self { source_slots = (firstn from l1.source_slots);
      source_cursor = l1.source_cursor } w1))
    (trim_loop self from (sub (length (sources_of w self).source_slots) from)
      w)

(** val sources_end_run : node_id -> edgeWorld -> edgeWorld option **)

let sources_end_run self w =
  let l = sources_of w self in
  (match decide
           (decide_rel Coq_Nat.lt_dec l.source_cursor (length l.source_slots)) with
   | Left -> sources_trim_from self l.source_cursor w
   | Right -> Some w)

(** val sources_clear : node_id -> edgeWorld -> edgeWorld option **)

let sources_clear self w =
  mbind (Obj.magic (fun _ _ -> option_bind)) (fun w1 -> Some
    (set_sources self { source_slots = (sources_of w1 self).source_slots;
      source_cursor = O } w1))
    (sources_trim_from self O w)

(** val sources_append :
    node_id -> node_id -> nat option -> observer_set -> edgeWorld -> edgeWorld **)

let sources_append self s mh o w =
  let l = sources_of w self in
  set_observers s o
    (set_sources self { source_slots =
      (app l.source_slots (Cons ((Pair (s, mh)), Nil))); source_cursor = (S
      (length l.source_slots)) } w)

(** val sources_link : node_id -> node_id -> edgeWorld -> edgeWorld **)

let sources_link self s w =
  let l = sources_of w self in
  let slot = length l.source_slots in
  let o = observers_of w s in
  let add0 = fun _ ->
    sources_append self s (Some (snd (observers_add self slot o)))
      (fst (observers_add self slot o)) w
  in
  (match decide (decide_rel Coq_Nat.eq_dec slot O) with
   | Left -> add0 Tt
   | Right ->
     let Pair (o', t) = observers_try_add self slot o in
     (match t with
      | TAdded h -> sources_append self s (Some h) o' w
      | TUnlinked -> sources_append self s None o' w
      | TUnknown ->
        (match decide
                 (decide_rel
                   (Obj.magic Coq_list.list_elem_of_dec Coq_Nat.eq_dec) s
                   (fmap (Obj.magic (fun _ _ -> Coq0_list.list_fmap)) fst
                     l.source_slots)) with
         | Left -> sources_append self s None o' w
         | Right -> add0 Tt)))

(** val sources_add : node_id -> node_id -> edgeWorld -> edgeWorld option **)

let sources_add self s w =
  let l = sources_of w self in
  let cur = l.source_cursor in
  (match decide
           (decide_rel (Obj.magic option_eq_dec Coq_Nat.eq_dec)
             (fmap (Obj.magic (fun _ _ -> option_fmap)) fst
               (lookup0 Coq_list.list_lookup cur l.source_slots))
             (Some s)) with
   | Left ->
     Some
       (set_sources self { source_slots = l.source_slots; source_cursor = (S
         cur) } w)
   | Right ->
     (match decide
              (and_dec (decide_rel Coq_Nat.lt_dec O cur)
                (decide_rel (Obj.magic option_eq_dec Coq_Nat.eq_dec)
                  (fmap (Obj.magic (fun _ _ -> option_fmap)) fst
                    (lookup0 Coq_list.list_lookup (pred cur) l.source_slots))
                  (Some s))) with
      | Left -> Some w
      | Right ->
        mbind (Obj.magic (fun _ _ -> option_bind)) (fun w1 -> Some
          (sources_link self s w1)) (sources_end_run self w)))

type kind =
| KSignal
| KMemo
| KEffect

type status =
| StNone
| StPending
| StError
| StUninitialized

(** val status_eq_dec : (status, status) relDecision **)

let status_eq_dec x y =
  match x with
  | StNone -> (match y with
               | StNone -> Left
               | _ -> Right)
  | StPending -> (match y with
                  | StPending -> Left
                  | _ -> Right)
  | StError -> (match y with
                | StError -> Left
                | _ -> Right)
  | StUninitialized -> (match y with
                        | StUninitialized -> Left
                        | _ -> Right)

type link = { l_child : node_id; l_owner : owner_id;
              l_generation : nat option; l_acquired : nat }

type owner_state = { o_children : link list; o_cleanups : cmd list;
                     o_cleanup_envs : env list; o_cleanup_order : nat list;
                     o_acquisitions : nat; o_generation : nat;
                     o_disposed : bool; o_sink : owner_id option;
                     o_errors : val0 list; o_host : node_id }

type node = { n_kind : kind; n_value : val0; n_failure : val0 option;
              n_status : status; n_freshness : freshness;
              n_unannounced : bool; n_runs : nat; n_published : bool;
              n_disposed : bool; n_queued : bool; n_in_cleanup : bool;
              n_scope : owner_id option; n_violated : bool; n_body : 
              cmd; n_env : env; n_mode : scope_mode; n_comparer : comparer;
              n_link : link option }

type flags = { f_unannounced : bool; f_reader_mark : bool }

(** val current_flags : flags **)

let current_flags =
  { f_unannounced = True; f_reader_mark = True }

type read_view = ((val0, status) prod, val0 option) prod

(** val read_view_of : node -> read_view **)

let read_view_of d =
  Pair ((Pair (d.n_value, d.n_status)), d.n_failure)

type ghost = { gh_published : (node_id, read_view) gmap;
               gh_seen : ((node_id, node_id) prod, read_view list) gmap;
               gh_unnoticed : node_id gset; gh_window : (node_id, nat) gmap;
               gh_reads : (node_id, node_id list) gmap;
               gh_passed : node_id gset; gh_over : node_id gset;
               gh_run_reads : (node_id, node_id list) gmap;
               gh_nested : node_id gset }

(** val empty_ghost : ghost **)

let empty_ghost =
  { gh_published = (empty0 (gmap_empty Coq_Nat.eq_dec nat_countable));
    gh_seen =
    (empty0
      (gmap_empty (prod_eq_dec Coq_Nat.eq_dec Coq_Nat.eq_dec)
        (prod_countable Coq_Nat.eq_dec nat_countable Coq_Nat.eq_dec
          nat_countable)));
    gh_unnoticed = (empty0 (gset_empty Coq_Nat.eq_dec nat_countable));
    gh_window = (empty0 (gmap_empty Coq_Nat.eq_dec nat_countable));
    gh_reads = (empty0 (gmap_empty Coq_Nat.eq_dec nat_countable));
    gh_passed = (empty0 (gset_empty Coq_Nat.eq_dec nat_countable)); gh_over =
    (empty0 (gset_empty Coq_Nat.eq_dec nat_countable)); gh_run_reads =
    (empty0 (gmap_empty Coq_Nat.eq_dec nat_countable)); gh_nested =
    (empty0 (gset_empty Coq_Nat.eq_dec nat_countable)) }

type skip_mode =
| SkipOff
| SkipLeaveDirty
| SkipUnchecked
| SkipAll

type state = { g_flags : flags; g_edges : edgeWorld;
               g_nodes : (node_id, node) gmap;
               g_owners : (owner_id, owner_state) gmap;
               g_queue : node_id list; g_queue_head : nat; g_flushing : 
               bool; g_flush_owed : bool; g_batch_depth : nat;
               g_pull_depth : nat; g_current : node_id option;
               g_untracked : bool; g_current_owner : owner_id option;
               g_next_id : nat; g_trace : event list;
               g_running : node_id list; g_ghost : ghost;
               g_registered : (node_id, nat) gmap; g_skip : skip_mode;
               g_epoch : nat; g_stamps : (node_id, nat) gmap }

(** val node_marked : freshness -> bool -> bool -> node -> node **)

let node_marked f u q d =
  { n_kind = d.n_kind; n_value = d.n_value; n_failure = d.n_failure;
    n_status = d.n_status; n_freshness = f; n_unannounced = u; n_runs =
    d.n_runs; n_published = d.n_published; n_disposed = d.n_disposed;
    n_queued = q; n_in_cleanup = d.n_in_cleanup; n_scope = d.n_scope;
    n_violated = d.n_violated; n_body = d.n_body; n_env = d.n_env; n_mode =
    d.n_mode; n_comparer = d.n_comparer; n_link = d.n_link }

(** val set_node : node_id -> node -> state -> state **)

let set_node n0 d s =
  { g_flags = s.g_flags; g_edges = s.g_edges; g_nodes =
    (insert0 (map_insert (gmap_partial_alter Coq_Nat.eq_dec nat_countable))
      n0 d s.g_nodes);
    g_owners = s.g_owners; g_queue = s.g_queue; g_queue_head =
    s.g_queue_head; g_flushing = s.g_flushing; g_flush_owed = s.g_flush_owed;
    g_batch_depth = s.g_batch_depth; g_pull_depth = s.g_pull_depth;
    g_current = s.g_current; g_untracked = s.g_untracked; g_current_owner =
    s.g_current_owner; g_next_id = s.g_next_id; g_trace = s.g_trace;
    g_running = s.g_running; g_ghost = s.g_ghost; g_registered =
    s.g_registered; g_skip = s.g_skip; g_epoch = s.g_epoch; g_stamps =
    s.g_stamps }

(** val enqueue : node_id -> state -> state **)

let enqueue n0 s =
  { g_flags = s.g_flags; g_edges = s.g_edges; g_nodes = s.g_nodes; g_owners =
    s.g_owners; g_queue = (app s.g_queue (Cons (n0, Nil))); g_queue_head =
    s.g_queue_head; g_flushing = s.g_flushing; g_flush_owed = s.g_flush_owed;
    g_batch_depth = s.g_batch_depth; g_pull_depth = s.g_pull_depth;
    g_current = s.g_current; g_untracked = s.g_untracked; g_current_owner =
    s.g_current_owner; g_next_id = s.g_next_id; g_trace = s.g_trace;
    g_running = s.g_running; g_ghost = s.g_ghost; g_registered =
    s.g_registered; g_skip = s.g_skip; g_epoch = s.g_epoch; g_stamps =
    s.g_stamps }

(** val walk_marks :
    (node_id -> state -> state option) -> node_id -> nat -> state -> state
    option **)

let rec walk_marks mark src n0 s =
  match n0 with
  | O -> Some s
  | S i ->
    (match decide
             (decide_rel Coq_Nat.le_dec
               (length (observers_of s.g_edges src).observer_items) i) with
     | Left -> walk_marks mark src i s
     | Right ->
       (match lookup0 Coq_list.list_lookup i
                (observers_of s.g_edges src).observer_items with
        | Some y ->
          let Pair (c, _) = y in
          mbind (Obj.magic (fun _ _ -> option_bind)) (walk_marks mark src i)
            (mark c s)
        | None -> None))

(** val notify_with :
    (node_id -> state -> state option) -> node_id -> state -> state option **)

let notify_with mark src s =
  walk_marks mark src (length (observers_of s.g_edges src).observer_items) s

(** val announce_due : state -> node -> bool **)

let announce_due s d =
  match s.g_flags.f_unannounced with
  | True -> d.n_unannounced
  | False -> False

(** val effect_marked : freshness -> node_id -> node -> state -> state **)

let effect_marked f n0 d s =
  match match d.n_disposed with
        | True -> True
        | False -> d.n_in_cleanup with
  | True -> s
  | False ->
    (match d.n_queued with
     | True -> set_node n0 (node_marked f d.n_unannounced True d) s
     | False ->
       enqueue n0 (set_node n0 (node_marked f d.n_unannounced True d) s))

(** val mark_check : nat -> node_id -> state -> state option **)

let rec mark_check fuel n0 s =
  match fuel with
  | O -> None
  | S f ->
    (match lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) n0 s.g_nodes with
     | Some d ->
       (match d.n_kind with
        | KSignal -> Some s
        | KMemo ->
          (match decide (decide_rel freshness_eq_dec d.n_freshness Clean) with
           | Left ->
             notify_with (mark_check f) n0
               (set_node n0 (node_marked Check d.n_unannounced d.n_queued d)
                 s)
           | Right ->
             (match announce_due s d with
              | True ->
                notify_with (mark_check f) n0
                  (set_node n0 (node_marked d.n_freshness False d.n_queued d)
                    s)
              | False -> Some s))
        | KEffect ->
          Some
            (effect_marked
              (match decide (decide_rel freshness_eq_dec d.n_freshness Clean) with
               | Left -> Check
               | Right -> d.n_freshness)
              n0 d s))
     | None -> Some s)

(** val mark_dirty : nat -> node_id -> state -> state option **)

let mark_dirty fuel n0 s =
  match fuel with
  | O -> None
  | S f ->
    (match lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) n0 s.g_nodes with
     | Some d ->
       (match d.n_kind with
        | KSignal -> Some s
        | KMemo ->
          (match decide (decide_rel freshness_eq_dec d.n_freshness Clean) with
           | Left ->
             notify_with (mark_check f) n0
               (set_node n0 (node_marked Dirty d.n_unannounced d.n_queued d)
                 s)
           | Right ->
             (match announce_due s d with
              | True ->
                notify_with (mark_check f) n0
                  (set_node n0 (node_marked Dirty False d.n_queued d) s)
              | False ->
                (match decide
                         (decide_rel freshness_eq_dec d.n_freshness Dirty) with
                 | Left -> Some s
                 | Right ->
                   Some
                     (set_node n0
                       (node_marked Dirty d.n_unannounced d.n_queued d) s))))
        | KEffect -> Some (effect_marked Dirty n0 d s))
     | None -> Some s)

(** val notify_dirty : nat -> node_id -> state -> state option **)

let notify_dirty fuel src s =
  notify_with (mark_dirty fuel) src s

(** val mark_except :
    nat -> node_id option -> node_id -> state -> state option **)

let mark_except fuel r c s =
  match decide (decide_rel (option_eq_dec Coq_Nat.eq_dec) r (Some c)) with
  | Left -> Some s
  | Right -> mark_dirty fuel c s

(** val notify_dirty_except :
    nat -> node_id -> node_id option -> state -> state option **)

let notify_dirty_except fuel src r s =
  notify_with (mark_except fuel r) src s

(** val pending : state -> node_id list **)

let pending s =
  skipn s.g_queue_head s.g_queue

type 'a m = state -> (state, 'a) prod option

(** val m_return : 'a1 -> 'a1 m **)

let m_return a s =
  Some (Pair (s, a))

(** val m_bind : 'a1 m -> ('a1 -> 'a2 m) -> 'a2 m **)

let m_bind m0 k s =
  match m0 s with
  | Some p -> let Pair (s', a) = p in k a s'
  | None -> None

(** val stuck : 'a1 m **)

let stuck _ =
  None

(** val m_get : state m **)

let m_get s =
  Some (Pair (s, s))

(** val m_modify : (state -> state) -> unit0 m **)

let m_modify f s =
  Some (Pair ((f s), Tt))

(** val m_step : (state -> state option) -> unit0 m **)

let m_step f s =
  match f s with
  | Some s' -> Some (Pair (s', Tt))
  | None -> None

(** val with_edges : edgeWorld -> state -> state **)

let with_edges v s =
  { g_flags = s.g_flags; g_edges = v; g_nodes = s.g_nodes; g_owners =
    s.g_owners; g_queue = s.g_queue; g_queue_head = s.g_queue_head;
    g_flushing = s.g_flushing; g_flush_owed = s.g_flush_owed; g_batch_depth =
    s.g_batch_depth; g_pull_depth = s.g_pull_depth; g_current = s.g_current;
    g_untracked = s.g_untracked; g_current_owner = s.g_current_owner;
    g_next_id = s.g_next_id; g_trace = s.g_trace; g_running = s.g_running;
    g_ghost = s.g_ghost; g_registered = s.g_registered; g_skip = s.g_skip;
    g_epoch = s.g_epoch; g_stamps = s.g_stamps }

(** val with_owners : (owner_id, owner_state) gmap -> state -> state **)

let with_owners v s =
  { g_flags = s.g_flags; g_edges = s.g_edges; g_nodes = s.g_nodes; g_owners =
    v; g_queue = s.g_queue; g_queue_head = s.g_queue_head; g_flushing =
    s.g_flushing; g_flush_owed = s.g_flush_owed; g_batch_depth =
    s.g_batch_depth; g_pull_depth = s.g_pull_depth; g_current = s.g_current;
    g_untracked = s.g_untracked; g_current_owner = s.g_current_owner;
    g_next_id = s.g_next_id; g_trace = s.g_trace; g_running = s.g_running;
    g_ghost = s.g_ghost; g_registered = s.g_registered; g_skip = s.g_skip;
    g_epoch = s.g_epoch; g_stamps = s.g_stamps }

(** val with_queue : node_id list -> state -> state **)

let with_queue v s =
  { g_flags = s.g_flags; g_edges = s.g_edges; g_nodes = s.g_nodes; g_owners =
    s.g_owners; g_queue = v; g_queue_head = s.g_queue_head; g_flushing =
    s.g_flushing; g_flush_owed = s.g_flush_owed; g_batch_depth =
    s.g_batch_depth; g_pull_depth = s.g_pull_depth; g_current = s.g_current;
    g_untracked = s.g_untracked; g_current_owner = s.g_current_owner;
    g_next_id = s.g_next_id; g_trace = s.g_trace; g_running = s.g_running;
    g_ghost = s.g_ghost; g_registered = s.g_registered; g_skip = s.g_skip;
    g_epoch = s.g_epoch; g_stamps = s.g_stamps }

(** val with_queue_head : nat -> state -> state **)

let with_queue_head v s =
  { g_flags = s.g_flags; g_edges = s.g_edges; g_nodes = s.g_nodes; g_owners =
    s.g_owners; g_queue = s.g_queue; g_queue_head = v; g_flushing =
    s.g_flushing; g_flush_owed = s.g_flush_owed; g_batch_depth =
    s.g_batch_depth; g_pull_depth = s.g_pull_depth; g_current = s.g_current;
    g_untracked = s.g_untracked; g_current_owner = s.g_current_owner;
    g_next_id = s.g_next_id; g_trace = s.g_trace; g_running = s.g_running;
    g_ghost = s.g_ghost; g_registered = s.g_registered; g_skip = s.g_skip;
    g_epoch = s.g_epoch; g_stamps = s.g_stamps }

(** val with_flushing : bool -> state -> state **)

let with_flushing v s =
  { g_flags = s.g_flags; g_edges = s.g_edges; g_nodes = s.g_nodes; g_owners =
    s.g_owners; g_queue = s.g_queue; g_queue_head = s.g_queue_head;
    g_flushing = v; g_flush_owed = s.g_flush_owed; g_batch_depth =
    s.g_batch_depth; g_pull_depth = s.g_pull_depth; g_current = s.g_current;
    g_untracked = s.g_untracked; g_current_owner = s.g_current_owner;
    g_next_id = s.g_next_id; g_trace = s.g_trace; g_running = s.g_running;
    g_ghost = s.g_ghost; g_registered = s.g_registered; g_skip = s.g_skip;
    g_epoch = s.g_epoch; g_stamps = s.g_stamps }

(** val with_flush_owed : bool -> state -> state **)

let with_flush_owed v s =
  { g_flags = s.g_flags; g_edges = s.g_edges; g_nodes = s.g_nodes; g_owners =
    s.g_owners; g_queue = s.g_queue; g_queue_head = s.g_queue_head;
    g_flushing = s.g_flushing; g_flush_owed = v; g_batch_depth =
    s.g_batch_depth; g_pull_depth = s.g_pull_depth; g_current = s.g_current;
    g_untracked = s.g_untracked; g_current_owner = s.g_current_owner;
    g_next_id = s.g_next_id; g_trace = s.g_trace; g_running = s.g_running;
    g_ghost = s.g_ghost; g_registered = s.g_registered; g_skip = s.g_skip;
    g_epoch = s.g_epoch; g_stamps = s.g_stamps }

(** val with_batch_depth : nat -> state -> state **)

let with_batch_depth v s =
  { g_flags = s.g_flags; g_edges = s.g_edges; g_nodes = s.g_nodes; g_owners =
    s.g_owners; g_queue = s.g_queue; g_queue_head = s.g_queue_head;
    g_flushing = s.g_flushing; g_flush_owed = s.g_flush_owed; g_batch_depth =
    v; g_pull_depth = s.g_pull_depth; g_current = s.g_current; g_untracked =
    s.g_untracked; g_current_owner = s.g_current_owner; g_next_id =
    s.g_next_id; g_trace = s.g_trace; g_running = s.g_running; g_ghost =
    s.g_ghost; g_registered = s.g_registered; g_skip = s.g_skip; g_epoch =
    s.g_epoch; g_stamps = s.g_stamps }

(** val with_pull_depth : nat -> state -> state **)

let with_pull_depth v s =
  { g_flags = s.g_flags; g_edges = s.g_edges; g_nodes = s.g_nodes; g_owners =
    s.g_owners; g_queue = s.g_queue; g_queue_head = s.g_queue_head;
    g_flushing = s.g_flushing; g_flush_owed = s.g_flush_owed; g_batch_depth =
    s.g_batch_depth; g_pull_depth = v; g_current = s.g_current; g_untracked =
    s.g_untracked; g_current_owner = s.g_current_owner; g_next_id =
    s.g_next_id; g_trace = s.g_trace; g_running = s.g_running; g_ghost =
    s.g_ghost; g_registered = s.g_registered; g_skip = s.g_skip; g_epoch =
    s.g_epoch; g_stamps = s.g_stamps }

(** val with_current : node_id option -> state -> state **)

let with_current v s =
  { g_flags = s.g_flags; g_edges = s.g_edges; g_nodes = s.g_nodes; g_owners =
    s.g_owners; g_queue = s.g_queue; g_queue_head = s.g_queue_head;
    g_flushing = s.g_flushing; g_flush_owed = s.g_flush_owed; g_batch_depth =
    s.g_batch_depth; g_pull_depth = s.g_pull_depth; g_current = v;
    g_untracked = s.g_untracked; g_current_owner = s.g_current_owner;
    g_next_id = s.g_next_id; g_trace = s.g_trace; g_running = s.g_running;
    g_ghost = s.g_ghost; g_registered = s.g_registered; g_skip = s.g_skip;
    g_epoch = s.g_epoch; g_stamps = s.g_stamps }

(** val with_untracked : bool -> state -> state **)

let with_untracked v s =
  { g_flags = s.g_flags; g_edges = s.g_edges; g_nodes = s.g_nodes; g_owners =
    s.g_owners; g_queue = s.g_queue; g_queue_head = s.g_queue_head;
    g_flushing = s.g_flushing; g_flush_owed = s.g_flush_owed; g_batch_depth =
    s.g_batch_depth; g_pull_depth = s.g_pull_depth; g_current = s.g_current;
    g_untracked = v; g_current_owner = s.g_current_owner; g_next_id =
    s.g_next_id; g_trace = s.g_trace; g_running = s.g_running; g_ghost =
    s.g_ghost; g_registered = s.g_registered; g_skip = s.g_skip; g_epoch =
    s.g_epoch; g_stamps = s.g_stamps }

(** val with_current_owner : owner_id option -> state -> state **)

let with_current_owner v s =
  { g_flags = s.g_flags; g_edges = s.g_edges; g_nodes = s.g_nodes; g_owners =
    s.g_owners; g_queue = s.g_queue; g_queue_head = s.g_queue_head;
    g_flushing = s.g_flushing; g_flush_owed = s.g_flush_owed; g_batch_depth =
    s.g_batch_depth; g_pull_depth = s.g_pull_depth; g_current = s.g_current;
    g_untracked = s.g_untracked; g_current_owner = v; g_next_id =
    s.g_next_id; g_trace = s.g_trace; g_running = s.g_running; g_ghost =
    s.g_ghost; g_registered = s.g_registered; g_skip = s.g_skip; g_epoch =
    s.g_epoch; g_stamps = s.g_stamps }

(** val with_next_id : nat -> state -> state **)

let with_next_id v s =
  { g_flags = s.g_flags; g_edges = s.g_edges; g_nodes = s.g_nodes; g_owners =
    s.g_owners; g_queue = s.g_queue; g_queue_head = s.g_queue_head;
    g_flushing = s.g_flushing; g_flush_owed = s.g_flush_owed; g_batch_depth =
    s.g_batch_depth; g_pull_depth = s.g_pull_depth; g_current = s.g_current;
    g_untracked = s.g_untracked; g_current_owner = s.g_current_owner;
    g_next_id = v; g_trace = s.g_trace; g_running = s.g_running; g_ghost =
    s.g_ghost; g_registered = s.g_registered; g_skip = s.g_skip; g_epoch =
    s.g_epoch; g_stamps = s.g_stamps }

(** val with_trace : event list -> state -> state **)

let with_trace v s =
  { g_flags = s.g_flags; g_edges = s.g_edges; g_nodes = s.g_nodes; g_owners =
    s.g_owners; g_queue = s.g_queue; g_queue_head = s.g_queue_head;
    g_flushing = s.g_flushing; g_flush_owed = s.g_flush_owed; g_batch_depth =
    s.g_batch_depth; g_pull_depth = s.g_pull_depth; g_current = s.g_current;
    g_untracked = s.g_untracked; g_current_owner = s.g_current_owner;
    g_next_id = s.g_next_id; g_trace = v; g_running = s.g_running; g_ghost =
    s.g_ghost; g_registered = s.g_registered; g_skip = s.g_skip; g_epoch =
    s.g_epoch; g_stamps = s.g_stamps }

(** val with_running : node_id list -> state -> state **)

let with_running v s =
  { g_flags = s.g_flags; g_edges = s.g_edges; g_nodes = s.g_nodes; g_owners =
    s.g_owners; g_queue = s.g_queue; g_queue_head = s.g_queue_head;
    g_flushing = s.g_flushing; g_flush_owed = s.g_flush_owed; g_batch_depth =
    s.g_batch_depth; g_pull_depth = s.g_pull_depth; g_current = s.g_current;
    g_untracked = s.g_untracked; g_current_owner = s.g_current_owner;
    g_next_id = s.g_next_id; g_trace = s.g_trace; g_running = v; g_ghost =
    s.g_ghost; g_registered = s.g_registered; g_skip = s.g_skip; g_epoch =
    s.g_epoch; g_stamps = s.g_stamps }

(** val with_ghost : ghost -> state -> state **)

let with_ghost v s =
  { g_flags = s.g_flags; g_edges = s.g_edges; g_nodes = s.g_nodes; g_owners =
    s.g_owners; g_queue = s.g_queue; g_queue_head = s.g_queue_head;
    g_flushing = s.g_flushing; g_flush_owed = s.g_flush_owed; g_batch_depth =
    s.g_batch_depth; g_pull_depth = s.g_pull_depth; g_current = s.g_current;
    g_untracked = s.g_untracked; g_current_owner = s.g_current_owner;
    g_next_id = s.g_next_id; g_trace = s.g_trace; g_running = s.g_running;
    g_ghost = v; g_registered = s.g_registered; g_skip = s.g_skip; g_epoch =
    s.g_epoch; g_stamps = s.g_stamps }

(** val with_registered : (node_id, nat) gmap -> state -> state **)

let with_registered v s =
  { g_flags = s.g_flags; g_edges = s.g_edges; g_nodes = s.g_nodes; g_owners =
    s.g_owners; g_queue = s.g_queue; g_queue_head = s.g_queue_head;
    g_flushing = s.g_flushing; g_flush_owed = s.g_flush_owed; g_batch_depth =
    s.g_batch_depth; g_pull_depth = s.g_pull_depth; g_current = s.g_current;
    g_untracked = s.g_untracked; g_current_owner = s.g_current_owner;
    g_next_id = s.g_next_id; g_trace = s.g_trace; g_running = s.g_running;
    g_ghost = s.g_ghost; g_registered = v; g_skip = s.g_skip; g_epoch =
    s.g_epoch; g_stamps = s.g_stamps }

(** val with_skip : skip_mode -> state -> state **)

let with_skip v s =
  { g_flags = s.g_flags; g_edges = s.g_edges; g_nodes = s.g_nodes; g_owners =
    s.g_owners; g_queue = s.g_queue; g_queue_head = s.g_queue_head;
    g_flushing = s.g_flushing; g_flush_owed = s.g_flush_owed; g_batch_depth =
    s.g_batch_depth; g_pull_depth = s.g_pull_depth; g_current = s.g_current;
    g_untracked = s.g_untracked; g_current_owner = s.g_current_owner;
    g_next_id = s.g_next_id; g_trace = s.g_trace; g_running = s.g_running;
    g_ghost = s.g_ghost; g_registered = s.g_registered; g_skip = v; g_epoch =
    s.g_epoch; g_stamps = s.g_stamps }

(** val with_epoch : nat -> state -> state **)

let with_epoch v s =
  { g_flags = s.g_flags; g_edges = s.g_edges; g_nodes = s.g_nodes; g_owners =
    s.g_owners; g_queue = s.g_queue; g_queue_head = s.g_queue_head;
    g_flushing = s.g_flushing; g_flush_owed = s.g_flush_owed; g_batch_depth =
    s.g_batch_depth; g_pull_depth = s.g_pull_depth; g_current = s.g_current;
    g_untracked = s.g_untracked; g_current_owner = s.g_current_owner;
    g_next_id = s.g_next_id; g_trace = s.g_trace; g_running = s.g_running;
    g_ghost = s.g_ghost; g_registered = s.g_registered; g_skip = s.g_skip;
    g_epoch = v; g_stamps = s.g_stamps }

(** val with_stamps : (node_id, nat) gmap -> state -> state **)

let with_stamps v s =
  { g_flags = s.g_flags; g_edges = s.g_edges; g_nodes = s.g_nodes; g_owners =
    s.g_owners; g_queue = s.g_queue; g_queue_head = s.g_queue_head;
    g_flushing = s.g_flushing; g_flush_owed = s.g_flush_owed; g_batch_depth =
    s.g_batch_depth; g_pull_depth = s.g_pull_depth; g_current = s.g_current;
    g_untracked = s.g_untracked; g_current_owner = s.g_current_owner;
    g_next_id = s.g_next_id; g_trace = s.g_trace; g_running = s.g_running;
    g_ghost = s.g_ghost; g_registered = s.g_registered; g_skip = s.g_skip;
    g_epoch = s.g_epoch; g_stamps = v }

(** val node_with_value : val0 -> node -> node **)

let node_with_value v d =
  { n_kind = d.n_kind; n_value = v; n_failure = d.n_failure; n_status =
    d.n_status; n_freshness = d.n_freshness; n_unannounced = d.n_unannounced;
    n_runs = d.n_runs; n_published = d.n_published; n_disposed =
    d.n_disposed; n_queued = d.n_queued; n_in_cleanup = d.n_in_cleanup;
    n_scope = d.n_scope; n_violated = d.n_violated; n_body = d.n_body;
    n_env = d.n_env; n_mode = d.n_mode; n_comparer = d.n_comparer; n_link =
    d.n_link }

(** val node_with_failure : val0 option -> node -> node **)

let node_with_failure v d =
  { n_kind = d.n_kind; n_value = d.n_value; n_failure = v; n_status =
    d.n_status; n_freshness = d.n_freshness; n_unannounced = d.n_unannounced;
    n_runs = d.n_runs; n_published = d.n_published; n_disposed =
    d.n_disposed; n_queued = d.n_queued; n_in_cleanup = d.n_in_cleanup;
    n_scope = d.n_scope; n_violated = d.n_violated; n_body = d.n_body;
    n_env = d.n_env; n_mode = d.n_mode; n_comparer = d.n_comparer; n_link =
    d.n_link }

(** val node_with_status : status -> node -> node **)

let node_with_status v d =
  { n_kind = d.n_kind; n_value = d.n_value; n_failure = d.n_failure;
    n_status = v; n_freshness = d.n_freshness; n_unannounced =
    d.n_unannounced; n_runs = d.n_runs; n_published = d.n_published;
    n_disposed = d.n_disposed; n_queued = d.n_queued; n_in_cleanup =
    d.n_in_cleanup; n_scope = d.n_scope; n_violated = d.n_violated; n_body =
    d.n_body; n_env = d.n_env; n_mode = d.n_mode; n_comparer = d.n_comparer;
    n_link = d.n_link }

(** val node_with_freshness : freshness -> node -> node **)

let node_with_freshness v d =
  { n_kind = d.n_kind; n_value = d.n_value; n_failure = d.n_failure;
    n_status = d.n_status; n_freshness = v; n_unannounced = d.n_unannounced;
    n_runs = d.n_runs; n_published = d.n_published; n_disposed =
    d.n_disposed; n_queued = d.n_queued; n_in_cleanup = d.n_in_cleanup;
    n_scope = d.n_scope; n_violated = d.n_violated; n_body = d.n_body;
    n_env = d.n_env; n_mode = d.n_mode; n_comparer = d.n_comparer; n_link =
    d.n_link }

(** val node_with_unannounced : bool -> node -> node **)

let node_with_unannounced v d =
  { n_kind = d.n_kind; n_value = d.n_value; n_failure = d.n_failure;
    n_status = d.n_status; n_freshness = d.n_freshness; n_unannounced = v;
    n_runs = d.n_runs; n_published = d.n_published; n_disposed =
    d.n_disposed; n_queued = d.n_queued; n_in_cleanup = d.n_in_cleanup;
    n_scope = d.n_scope; n_violated = d.n_violated; n_body = d.n_body;
    n_env = d.n_env; n_mode = d.n_mode; n_comparer = d.n_comparer; n_link =
    d.n_link }

(** val node_with_runs : nat -> node -> node **)

let node_with_runs v d =
  { n_kind = d.n_kind; n_value = d.n_value; n_failure = d.n_failure;
    n_status = d.n_status; n_freshness = d.n_freshness; n_unannounced =
    d.n_unannounced; n_runs = v; n_published = d.n_published; n_disposed =
    d.n_disposed; n_queued = d.n_queued; n_in_cleanup = d.n_in_cleanup;
    n_scope = d.n_scope; n_violated = d.n_violated; n_body = d.n_body;
    n_env = d.n_env; n_mode = d.n_mode; n_comparer = d.n_comparer; n_link =
    d.n_link }

(** val node_with_published : bool -> node -> node **)

let node_with_published v d =
  { n_kind = d.n_kind; n_value = d.n_value; n_failure = d.n_failure;
    n_status = d.n_status; n_freshness = d.n_freshness; n_unannounced =
    d.n_unannounced; n_runs = d.n_runs; n_published = v; n_disposed =
    d.n_disposed; n_queued = d.n_queued; n_in_cleanup = d.n_in_cleanup;
    n_scope = d.n_scope; n_violated = d.n_violated; n_body = d.n_body;
    n_env = d.n_env; n_mode = d.n_mode; n_comparer = d.n_comparer; n_link =
    d.n_link }

(** val node_with_disposed : bool -> node -> node **)

let node_with_disposed v d =
  { n_kind = d.n_kind; n_value = d.n_value; n_failure = d.n_failure;
    n_status = d.n_status; n_freshness = d.n_freshness; n_unannounced =
    d.n_unannounced; n_runs = d.n_runs; n_published = d.n_published;
    n_disposed = v; n_queued = d.n_queued; n_in_cleanup = d.n_in_cleanup;
    n_scope = d.n_scope; n_violated = d.n_violated; n_body = d.n_body;
    n_env = d.n_env; n_mode = d.n_mode; n_comparer = d.n_comparer; n_link =
    d.n_link }

(** val node_with_queued : bool -> node -> node **)

let node_with_queued v d =
  { n_kind = d.n_kind; n_value = d.n_value; n_failure = d.n_failure;
    n_status = d.n_status; n_freshness = d.n_freshness; n_unannounced =
    d.n_unannounced; n_runs = d.n_runs; n_published = d.n_published;
    n_disposed = d.n_disposed; n_queued = v; n_in_cleanup = d.n_in_cleanup;
    n_scope = d.n_scope; n_violated = d.n_violated; n_body = d.n_body;
    n_env = d.n_env; n_mode = d.n_mode; n_comparer = d.n_comparer; n_link =
    d.n_link }

(** val node_with_in_cleanup : bool -> node -> node **)

let node_with_in_cleanup v d =
  { n_kind = d.n_kind; n_value = d.n_value; n_failure = d.n_failure;
    n_status = d.n_status; n_freshness = d.n_freshness; n_unannounced =
    d.n_unannounced; n_runs = d.n_runs; n_published = d.n_published;
    n_disposed = d.n_disposed; n_queued = d.n_queued; n_in_cleanup = v;
    n_scope = d.n_scope; n_violated = d.n_violated; n_body = d.n_body;
    n_env = d.n_env; n_mode = d.n_mode; n_comparer = d.n_comparer; n_link =
    d.n_link }

(** val node_with_scope : owner_id option -> node -> node **)

let node_with_scope v d =
  { n_kind = d.n_kind; n_value = d.n_value; n_failure = d.n_failure;
    n_status = d.n_status; n_freshness = d.n_freshness; n_unannounced =
    d.n_unannounced; n_runs = d.n_runs; n_published = d.n_published;
    n_disposed = d.n_disposed; n_queued = d.n_queued; n_in_cleanup =
    d.n_in_cleanup; n_scope = v; n_violated = d.n_violated; n_body =
    d.n_body; n_env = d.n_env; n_mode = d.n_mode; n_comparer = d.n_comparer;
    n_link = d.n_link }

(** val node_with_violated : bool -> node -> node **)

let node_with_violated v d =
  { n_kind = d.n_kind; n_value = d.n_value; n_failure = d.n_failure;
    n_status = d.n_status; n_freshness = d.n_freshness; n_unannounced =
    d.n_unannounced; n_runs = d.n_runs; n_published = d.n_published;
    n_disposed = d.n_disposed; n_queued = d.n_queued; n_in_cleanup =
    d.n_in_cleanup; n_scope = d.n_scope; n_violated = v; n_body = d.n_body;
    n_env = d.n_env; n_mode = d.n_mode; n_comparer = d.n_comparer; n_link =
    d.n_link }

(** val node_with_link : link option -> node -> node **)

let node_with_link v d =
  { n_kind = d.n_kind; n_value = d.n_value; n_failure = d.n_failure;
    n_status = d.n_status; n_freshness = d.n_freshness; n_unannounced =
    d.n_unannounced; n_runs = d.n_runs; n_published = d.n_published;
    n_disposed = d.n_disposed; n_queued = d.n_queued; n_in_cleanup =
    d.n_in_cleanup; n_scope = d.n_scope; n_violated = d.n_violated; n_body =
    d.n_body; n_env = d.n_env; n_mode = d.n_mode; n_comparer = d.n_comparer;
    n_link = v }

(** val emit : event -> unit0 m **)

let emit e =
  m_modify (fun s -> with_trace (app s.g_trace (Cons (e, Nil))) s)

(** val get_node : node_id -> node m **)

let get_node n0 s =
  match lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) n0 s.g_nodes with
  | Some d -> Some (Pair (s, d))
  | None -> None

(** val modify_node : node_id -> (node -> node) -> unit0 m **)

let modify_node n0 u s =
  match lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) n0 s.g_nodes with
  | Some d -> Some (Pair ((set_node n0 (u d) s), Tt))
  | None -> None

(** val get_owner : owner_id -> owner_state m **)

let get_owner o s =
  match lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) o s.g_owners with
  | Some x -> Some (Pair (s, x))
  | None -> None

(** val put_owner : owner_id -> owner_state -> unit0 m **)

let put_owner o x =
  m_modify (fun s ->
    with_owners
      (insert0 (map_insert (gmap_partial_alter Coq_Nat.eq_dec nat_countable))
        o x s.g_owners)
      s)

(** val edges : (edgeWorld -> edgeWorld) -> unit0 m **)

let edges u =
  m_modify (fun s -> with_edges (u s.g_edges) s)

(** val edges_opt : (edgeWorld -> edgeWorld option) -> unit0 m **)

let edges_opt u s =
  match u s.g_edges with
  | Some w -> Some (Pair ((with_edges w s), Tt))
  | None -> None

(** val push_running : node_id -> unit0 m **)

let push_running n0 =
  m_modify (fun s -> with_running (Cons (n0, s.g_running)) s)

(** val pop_running : unit0 m **)

let pop_running =
  m_modify (fun s -> with_running (tl s.g_running) s)

(** val stale_live : node -> bool **)

let stale_live d =
  match stale d.n_freshness with
  | True -> negb d.n_disposed
  | False -> False

(** val read_prefix : edgeWorld -> node_id -> node_id list **)

let read_prefix w c =
  fmap (Obj.magic (fun _ _ -> Coq0_list.list_fmap)) fst
    (firstn (sources_of w c).source_cursor
      (Obj.magic (sources_of w c).source_slots))

(** val ghost_read : node_id -> node_id -> node_id list -> state -> state **)

let ghost_read c n0 pre s =
  match lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) n0 s.g_nodes with
  | Some d ->
    let g = s.g_ghost in
    let older =
      match decide
              (decide_rel (Coq_list.list_elem_of_dec Coq_Nat.eq_dec) n0 pre) with
      | Left ->
        from_option (Obj.magic id) Nil
          (lookup0
            (gmap_lookup (prod_eq_dec Coq_Nat.eq_dec Coq_Nat.eq_dec)
              (prod_countable Coq_Nat.eq_dec nat_countable Coq_Nat.eq_dec
                nat_countable))
            (Pair (c, n0)) g.gh_seen)
      | Right -> Nil
    in
    with_ghost { gh_published = g.gh_published; gh_seen =
      (insert0
        (map_insert
          (gmap_partial_alter (prod_eq_dec Coq_Nat.eq_dec Coq_Nat.eq_dec)
            (prod_countable Coq_Nat.eq_dec nat_countable Coq_Nat.eq_dec
              nat_countable)))
        (Pair (c, n0)) (Cons ((read_view_of d), older)) g.gh_seen);
      gh_unnoticed =
      (match decide
               (decide_rel (Coq_list.list_elem_of_dec Coq_Nat.eq_dec) n0
                 s.g_running) with
       | Left ->
         union0 (gset_union Coq_Nat.eq_dec nat_countable)
           (singleton0 (gset_singleton Coq_Nat.eq_dec nat_countable) c)
           g.gh_unnoticed
       | Right -> g.gh_unnoticed);
      gh_window = g.gh_window; gh_reads =
      (insert0 (map_insert (gmap_partial_alter Coq_Nat.eq_dec nat_countable))
        c
        (app
          (from_option (Obj.magic id) Nil
            (lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) c g.gh_reads))
          (Cons (n0, Nil)))
        g.gh_reads);
      gh_passed = g.gh_passed; gh_over = g.gh_over; gh_run_reads =
      (insert0 (map_insert (gmap_partial_alter Coq_Nat.eq_dec nat_countable))
        c
        (app
          (from_option (Obj.magic id) Nil
            (lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) c
              g.gh_run_reads))
          (Cons (n0, Nil)))
        g.gh_run_reads);
      gh_nested = g.gh_nested } s
  | None -> s

(** val ghost_run_start : node_id -> state -> state **)

let ghost_run_start n0 s =
  let g = s.g_ghost in
  with_ghost { gh_published = g.gh_published; gh_seen = g.gh_seen;
    gh_unnoticed =
    (difference0 (gset_difference Coq_Nat.eq_dec nat_countable)
      g.gh_unnoticed
      (singleton0 (gset_singleton Coq_Nat.eq_dec nat_countable) n0));
    gh_window = g.gh_window; gh_reads =
    (insert0 (map_insert (gmap_partial_alter Coq_Nat.eq_dec nat_countable))
      n0 Nil g.gh_reads);
    gh_passed =
    (difference0 (gset_difference Coq_Nat.eq_dec nat_countable) g.gh_passed
      (singleton0 (gset_singleton Coq_Nat.eq_dec nat_countable) n0));
    gh_over =
    (difference0 (gset_difference Coq_Nat.eq_dec nat_countable) g.gh_over
      (singleton0 (gset_singleton Coq_Nat.eq_dec nat_countable) n0));
    gh_run_reads =
    (match decide
             (decide_rel (Coq_list.list_elem_of_dec Coq_Nat.eq_dec) n0
               s.g_running) with
     | Left -> g.gh_run_reads
     | Right ->
       insert0 (map_insert (gmap_partial_alter Coq_Nat.eq_dec nat_countable))
         n0 Nil g.gh_run_reads);
    gh_nested =
    (match decide
             (decide_rel (Coq_list.list_elem_of_dec Coq_Nat.eq_dec) n0
               s.g_running) with
     | Left ->
       union0 (gset_union Coq_Nat.eq_dec nat_countable)
         (singleton0 (gset_singleton Coq_Nat.eq_dec nat_countable) n0)
         g.gh_nested
     | Right ->
       difference0 (gset_difference Coq_Nat.eq_dec nat_countable) g.gh_nested
         (singleton0 (gset_singleton Coq_Nat.eq_dec nat_countable) n0)) }
    s

(** val ghost_effect_start : node_id -> state -> state **)

let ghost_effect_start n0 s =
  let g = s.g_ghost in
  with_ghost { gh_published = g.gh_published; gh_seen = g.gh_seen;
    gh_unnoticed = g.gh_unnoticed; gh_window = g.gh_window; gh_reads =
    (insert0 (map_insert (gmap_partial_alter Coq_Nat.eq_dec nat_countable))
      n0 Nil g.gh_reads);
    gh_passed =
    (difference0 (gset_difference Coq_Nat.eq_dec nat_countable) g.gh_passed
      (singleton0 (gset_singleton Coq_Nat.eq_dec nat_countable) n0));
    gh_over =
    (difference0 (gset_difference Coq_Nat.eq_dec nat_countable) g.gh_over
      (singleton0 (gset_singleton Coq_Nat.eq_dec nat_countable) n0));
    gh_run_reads =
    (insert0 (map_insert (gmap_partial_alter Coq_Nat.eq_dec nat_countable))
      n0 Nil g.gh_run_reads);
    gh_nested =
    (difference0 (gset_difference Coq_Nat.eq_dec nat_countable) g.gh_nested
      (singleton0 (gset_singleton Coq_Nat.eq_dec nat_countable) n0)) }
    s

(** val ghost_publish : node_id -> state -> state **)

let ghost_publish n0 s =
  match lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) n0 s.g_nodes with
  | Some d ->
    let g = s.g_ghost in
    with_ghost { gh_published =
      (insert0 (map_insert (gmap_partial_alter Coq_Nat.eq_dec nat_countable))
        n0 (read_view_of d) g.gh_published);
      gh_seen = g.gh_seen; gh_unnoticed = g.gh_unnoticed; gh_window =
      g.gh_window; gh_reads = g.gh_reads; gh_passed = g.gh_passed; gh_over =
      g.gh_over; gh_run_reads = g.gh_run_reads; gh_nested = g.gh_nested } s
  | None -> s

(** val run_end_unnoticed :
    node_id -> node_id option -> bool -> node -> state -> node_id gset **)

let run_end_unnoticed n0 r moved d s =
  match moved with
  | True ->
    (match r with
     | Some c ->
       (match decide
                (decide_rel (Coq_list.list_elem_of_dec Coq_Nat.eq_dec) n0
                  (read_prefix s.g_edges c)) with
        | Left -> singleton0 (gset_singleton Coq_Nat.eq_dec nat_countable) c
        | Right -> empty0 (gset_empty Coq_Nat.eq_dec nat_countable))
     | None -> empty0 (gset_empty Coq_Nat.eq_dec nat_countable))
  | False ->
    (match decide
             (decide_rel
               (option_eq_dec
                 (prod_eq_dec (prod_eq_dec Coq_Z.eq_dec status_eq_dec)
                   (option_eq_dec Coq_Z.eq_dec)))
               (lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) n0
                 s.g_ghost.gh_published)
               (Some (read_view_of d))) with
     | Left -> empty0 (gset_empty Coq_Nat.eq_dec nat_countable)
     | Right ->
       list_to_set (gset_singleton Coq_Nat.eq_dec nat_countable)
         (gset_empty Coq_Nat.eq_dec nat_countable)
         (gset_union Coq_Nat.eq_dec nat_countable)
         (observer_view (observers_of s.g_edges n0)))

(** val ghost_run_end :
    node_id -> node_id option -> bool -> state -> state **)

let ghost_run_end n0 r moved s =
  match lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) n0 s.g_nodes with
  | Some d ->
    let g = s.g_ghost in
    with_ghost { gh_published =
      (insert0 (map_insert (gmap_partial_alter Coq_Nat.eq_dec nat_countable))
        n0 (read_view_of d) g.gh_published);
      gh_seen = g.gh_seen; gh_unnoticed =
      (union0 (gset_union Coq_Nat.eq_dec nat_countable)
        (run_end_unnoticed n0 r moved d s) g.gh_unnoticed);
      gh_window = g.gh_window; gh_reads = g.gh_reads; gh_passed =
      g.gh_passed; gh_over = g.gh_over; gh_run_reads = g.gh_run_reads;
      gh_nested = g.gh_nested } s
  | None -> s

(** val ghost_dirty_cleared : node_id -> nat -> state -> state **)

let ghost_dirty_cleared n0 runs s =
  let g = s.g_ghost in
  with_ghost { gh_published = g.gh_published; gh_seen = g.gh_seen;
    gh_unnoticed =
    (union0 (gset_union Coq_Nat.eq_dec nat_countable)
      (singleton0 (gset_singleton Coq_Nat.eq_dec nat_countable) n0)
      g.gh_unnoticed);
    gh_window =
    (insert0 (map_insert (gmap_partial_alter Coq_Nat.eq_dec nat_countable))
      n0 runs g.gh_window);
    gh_reads = g.gh_reads; gh_passed = g.gh_passed; gh_over = g.gh_over;
    gh_run_reads = g.gh_run_reads; gh_nested = g.gh_nested } s

(** val slot_holds : (node -> bool) -> state -> node_id -> bool **)

let slot_holds p s n0 =
  existsb (fun src ->
    match lookup0 (Obj.magic gmap_lookup Coq_Nat.eq_dec nat_countable) src
            s.g_nodes with
    | Some e -> p e
    | None -> False)
    (fmap (Obj.magic (fun _ _ -> Coq0_list.list_fmap)) fst
      (sources_of s.g_edges n0).source_slots)

(** val add_if : bool -> node_id -> node_id gset -> node_id gset **)

let add_if b n0 x =
  match b with
  | True ->
    union0 (gset_union Coq_Nat.eq_dec nat_countable)
      (singleton0 (gset_singleton Coq_Nat.eq_dec nat_countable) n0) x
  | False -> x

(** val ghost_resolved : node_id -> state -> state **)

let ghost_resolved n0 s =
  let g = s.g_ghost in
  with_ghost { gh_published = g.gh_published; gh_seen = g.gh_seen;
    gh_unnoticed = g.gh_unnoticed; gh_window = g.gh_window; gh_reads =
    g.gh_reads; gh_passed =
    (add_if
      (slot_holds (fun e ->
        match stale_live e with
        | True -> negb e.n_unannounced
        | False -> False) s n0)
      n0 g.gh_passed);
    gh_over = (add_if (slot_holds stale_live s n0) n0 g.gh_over);
    gh_run_reads = g.gh_run_reads; gh_nested = g.gh_nested } s

(** val ghost_registered : node_id -> state -> state **)

let ghost_registered h s =
  with_registered
    (insert0 (map_insert (gmap_partial_alter Coq_Nat.eq_dec nat_countable)) h
      (S
      (from_option (Obj.magic id) O
        (lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) h s.g_registered)))
      s.g_registered)
    s

(** val root_owner_id : owner_id **)

let root_owner_id =
  O

(** val new_owner : node_id -> owner_id option -> bool -> owner_state **)

let new_owner host sink disposed =
  { o_children = Nil; o_cleanups = Nil; o_cleanup_envs = Nil;
    o_cleanup_order = Nil; o_acquisitions = O; o_generation =
    (match disposed with
     | True -> S O
     | False -> O);
    o_disposed = disposed; o_sink = sink; o_errors = Nil; o_host = host }

(** val owner_with_child : link -> owner_state -> owner_state **)

let owner_with_child l x =
  { o_children = (app x.o_children (Cons (l, Nil))); o_cleanups =
    x.o_cleanups; o_cleanup_envs = x.o_cleanup_envs; o_cleanup_order =
    x.o_cleanup_order; o_acquisitions = (S x.o_acquisitions); o_generation =
    x.o_generation; o_disposed = x.o_disposed; o_sink = x.o_sink; o_errors =
    x.o_errors; o_host = x.o_host }

(** val owner_with_cleanup : cmd -> env -> owner_state -> owner_state **)

let owner_with_cleanup b e x =
  { o_children = x.o_children; o_cleanups =
    (app x.o_cleanups (Cons (b, Nil))); o_cleanup_envs =
    (app x.o_cleanup_envs (Cons (e, Nil))); o_cleanup_order =
    (app x.o_cleanup_order (Cons (x.o_acquisitions, Nil))); o_acquisitions =
    (S x.o_acquisitions); o_generation = x.o_generation; o_disposed =
    x.o_disposed; o_sink = x.o_sink; o_errors = x.o_errors; o_host =
    x.o_host }

(** val zip_cleanups :
    cmd list -> env list -> nat list -> ((cmd, env) prod, nat) prod list **)

let rec zip_cleanups bs es os =
  match bs with
  | Nil -> Nil
  | Cons (b, bs') ->
    (match es with
     | Nil -> Nil
     | Cons (e, es') ->
       (match os with
        | Nil -> Nil
        | Cons (o, os') ->
          Cons ((Pair ((Pair (b, e)), o)), (zip_cleanups bs' es' os'))))

(** val owner_cleanups : owner_state -> ((cmd, env) prod, nat) prod list **)

let owner_cleanups x =
  rev (zip_cleanups x.o_cleanups x.o_cleanup_envs x.o_cleanup_order)

(** val owner_detached : owner_state -> owner_state **)

let owner_detached x =
  { o_children = Nil; o_cleanups = Nil; o_cleanup_envs = Nil;
    o_cleanup_order = Nil; o_acquisitions = O; o_generation = (S
    x.o_generation); o_disposed = x.o_disposed; o_sink = x.o_sink; o_errors =
    x.o_errors; o_host = x.o_host }

(** val owner_retired : owner_state -> owner_state **)

let owner_retired x =
  { o_children = x.o_children; o_cleanups = x.o_cleanups; o_cleanup_envs =
    x.o_cleanup_envs; o_cleanup_order = x.o_cleanup_order; o_acquisitions =
    x.o_acquisitions; o_generation = x.o_generation; o_disposed = True;
    o_sink = x.o_sink; o_errors = x.o_errors; o_host = x.o_host }

(** val owner_with_error : val0 -> owner_state -> owner_state **)

let owner_with_error f x =
  { o_children = x.o_children; o_cleanups = x.o_cleanups; o_cleanup_envs =
    x.o_cleanup_envs; o_cleanup_order = x.o_cleanup_order; o_acquisitions =
    x.o_acquisitions; o_generation = x.o_generation; o_disposed =
    x.o_disposed; o_sink = x.o_sink; o_errors =
    (app x.o_errors (Cons (f, Nil))); o_host = x.o_host }

(** val owner_without : link -> owner_state -> owner_state **)

let owner_without l x =
  { o_children =
    (filter0 (fun _ -> Coq_list.list_filter) (fun x0 ->
      not_dec
        (and_dec (decide_rel Coq_Nat.eq_dec x0.l_child l.l_child)
          (decide_rel Coq_Nat.eq_dec x0.l_acquired l.l_acquired)))
      x.o_children);
    o_cleanups = x.o_cleanups; o_cleanup_envs = x.o_cleanup_envs;
    o_cleanup_order = x.o_cleanup_order; o_acquisitions = x.o_acquisitions;
    o_generation = x.o_generation; o_disposed = x.o_disposed; o_sink =
    x.o_sink; o_errors = x.o_errors; o_host = x.o_host }

(** val unlink : link -> unit0 m **)

let unlink l =
  m_bind (get_owner l.l_owner) (fun x ->
    match decide
            (decide_rel (option_eq_dec Coq_Nat.eq_dec) l.l_generation (Some
              x.o_generation)) with
    | Left -> put_owner l.l_owner (owner_without l x)
    | Right -> m_return Tt)

(** val record_error : owner_id -> val0 -> unit0 m **)

let record_error o f =
  m_bind (get_owner o) (fun x ->
    match x.o_sink with
    | Some p ->
      m_bind (get_owner p) (fun y -> put_owner p (owner_with_error f y))
    | None -> put_owner o (owner_with_error f x))

(** val log_label : node_id m **)

let log_label s =
  match s.g_current with
  | Some n0 -> Some (Pair (s, n0))
  | None ->
    (match s.g_current_owner with
     | Some o ->
       Some (Pair (s,
         (from_option (Obj.magic id) O
           (fmap (Obj.magic (fun _ _ -> option_fmap)) (fun o0 -> o0.o_host)
             (lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) o s.g_owners)))))
     | None -> Some (Pair (s, O)))

(** val pure_scope_failure : val0 **)

let pure_scope_failure =
  Zneg (XO XH)

(** val prev_var : var **)

let prev_var =
  S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    (S (S (S (S (S (S (S (S (S (S (S (S (S (S (S
    O)))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))))

(** val without_prev : env -> env **)

let without_prev e =
  filter0 (fun _ -> Coq_list.list_filter) (fun x ->
    not_dec (decide_rel Coq_Nat.eq_dec (fst x) prev_var)) e

(** val detached : owner_id -> unit0 m -> unit0 m **)

let detached o body s0 =
  m_bind
    (m_modify (fun s ->
      with_batch_depth (S s.g_batch_depth)
        (with_current_owner (Some o) (with_current None s))))
    (fun _ ->
    m_bind body (fun _ ->
      m_modify (fun s ->
        with_batch_depth (pred s.g_batch_depth)
          (with_current_owner s0.g_current_owner
            (with_current s0.g_current s)))))
    s0

(** val hosted : node_id -> 'a1 m -> 'a1 m **)

let hosted n0 body s0 =
  m_bind
    (m_modify (fun s -> with_current_owner None (with_current (Some n0) s)))
    (fun _ ->
    m_bind body (fun r ->
      m_bind
        (m_modify (fun s ->
          with_current_owner s0.g_current_owner (with_current s0.g_current s)))
        (fun _ -> m_return r)))
    s0

(** val untracked : 'a1 m -> 'a1 m **)

let untracked body s0 =
  let flag = match s0.g_current with
             | Some _ -> True
             | None -> s0.g_untracked
  in
  m_bind (m_modify (fun s -> with_untracked flag (with_current None s)))
    (fun _ ->
    m_bind body (fun r ->
      m_bind
        (m_modify (fun s ->
          with_untracked s0.g_untracked (with_current s0.g_current s)))
        (fun _ -> m_return r)))
    s0

(** val deferring : state -> bool **)

let deferring s =
  match s.g_current with
  | Some _ -> True
  | None -> bool_decide (decide_rel Coq_Nat.lt_dec O s.g_pull_depth)

(** val live_queue : state -> bool **)

let live_queue s =
  bool_decide (decide_rel Coq_Nat.lt_dec s.g_queue_head (length s.g_queue))

(** val outside_bodies : state -> bool **)

let outside_bodies s =
  match bool_decide (decide_rel Coq_Nat.eq_dec s.g_pull_depth O) with
  | True ->
    (match s.g_current with
     | Some _ -> False
     | None -> negb s.g_untracked)
  | False -> False

(** val track : node_id -> unit0 m **)

let track n0 s =
  match s.g_current with
  | Some c ->
    (match sources_add c n0 s.g_edges with
     | Some w ->
       Some (Pair
         ((ghost_read c n0 (read_prefix s.g_edges c) (with_edges w s)), Tt))
     | None -> None)
  | None -> Some (Pair (s, Tt))

(** val mark_reader : nat -> unit0 m **)

let mark_reader fuel s =
  match s.g_current with
  | Some c ->
    (match s.g_flags.f_reader_mark with
     | True ->
       (match mark_check fuel c s with
        | Some s' -> Some (Pair (s', Tt))
        | None -> None)
     | False -> Some (Pair (s, Tt)))
  | None -> Some (Pair (s, Tt))

(** val on_value : env -> expr -> (val0 -> outcome m) -> outcome m **)

let on_value e x k s =
  match eval_expr e (env_lookup e prev_var) x with
  | Some o ->
    (match o with
     | Ok v -> k v s
     | Fail f -> Some (Pair (s, (Fail f))))
  | None -> None

(** val on_node : env -> expr -> (node_id -> outcome m) -> outcome m **)

let on_node e x k =
  on_value e x (fun z0 ->
    match decide (decide_rel Coq_Z.le_dec Z0 z0) with
    | Left -> k (Z.to_nat z0)
    | Right -> stuck)

(** val announce : node_id -> unit0 m **)

let announce n0 s =
  match s.g_flags.f_unannounced with
  | True -> modify_node n0 (node_with_unannounced True) s
  | False -> Some (Pair (s, Tt))

(** val note_host_read : node_id -> outcome -> unit0 m **)

let note_host_read n0 o s =
  match s.g_current with
  | Some _ -> Some (Pair (s, Tt))
  | None ->
    (match s.g_current_owner with
     | Some r ->
       (match decide (decide_rel Coq_Nat.eq_dec r root_owner_id) with
        | Left -> emit (EvHostRead (n0, o)) s
        | Right -> Some (Pair (s, Tt)))
     | None -> Some (Pair (s, Tt)))

(** val next_id : node_id m **)

let next_id s =
  Some (Pair ((with_next_id (S s.g_next_id) s), (S s.g_next_id)))

(** val signal_node : val0 -> node **)

let signal_node v =
  { n_kind = KSignal; n_value = v; n_failure = None; n_status = StNone;
    n_freshness = Clean; n_unannounced = False; n_runs = O; n_published =
    False; n_disposed = False; n_queued = False; n_in_cleanup = False;
    n_scope = None; n_violated = False; n_body = (CRet (EConst Z0)); n_env =
    Nil; n_mode = Pure; n_comparer = CmpEq; n_link = None }

(** val memo_node : scope_mode -> comparer -> cmd -> env -> node **)

let memo_node m0 c b e =
  { n_kind = KMemo; n_value = Z0; n_failure = None; n_status =
    StUninitialized; n_freshness = Dirty; n_unannounced = False; n_runs = O;
    n_published = False; n_disposed = False; n_queued = False; n_in_cleanup =
    False; n_scope = None; n_violated = False; n_body = b; n_env = e;
    n_mode = m0; n_comparer = c; n_link = None }

(** val effect_node : cmd -> env -> node **)

let effect_node b e =
  { n_kind = KEffect; n_value = Z0; n_failure = None; n_status =
    StUninitialized; n_freshness = Clean; n_unannounced = False; n_runs = O;
    n_published = False; n_disposed = False; n_queued = False; n_in_cleanup =
    False; n_scope = None; n_violated = False; n_body = b; n_env = e;
    n_mode = Owning; n_comparer = CmpEq; n_link = None }

(** val add_node : node_id -> node -> unit0 m **)

let add_node n0 d =
  m_modify (fun s -> ghost_publish n0 (set_node n0 d s))

(** val current_owner : owner_id option m **)

let current_owner s =
  match s.g_current_owner with
  | Some o -> Some (Pair (s, (Some o)))
  | None ->
    (match s.g_current with
     | Some h ->
       (match lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) h s.g_nodes with
        | Some d ->
          (match d.n_scope with
           | Some o -> Some (Pair ((with_current_owner (Some o) s), (Some o)))
           | None ->
             (match d.n_kind with
              | KMemo ->
                (match d.n_mode with
                 | Pure ->
                   Some (Pair ((set_node h (node_with_violated True d) s),
                     None))
                 | Owning ->
                   let o =
                     fresh0
                       (set_fresh
                         (gset_elements Coq_Nat.eq_dec nat_countable)
                         nat_infinite)
                       (dom0 (gset_dom Coq_Nat.eq_dec nat_countable)
                         s.g_owners)
                   in
                   let s1 =
                     with_owners
                       (insert0
                         (map_insert
                           (gmap_partial_alter Coq_Nat.eq_dec nat_countable))
                         o (new_owner h (Some root_owner_id) d.n_disposed)
                         s.g_owners)
                       s
                   in
                   Some (Pair
                   ((with_current_owner (Some o)
                      (set_node h (node_with_scope (Some o) d) s1)),
                   (Some o))))
              | _ ->
                let o =
                  fresh0
                    (set_fresh (gset_elements Coq_Nat.eq_dec nat_countable)
                      nat_infinite)
                    (dom0 (gset_dom Coq_Nat.eq_dec nat_countable) s.g_owners)
                in
                let s1 =
                  with_owners
                    (insert0
                      (map_insert
                        (gmap_partial_alter Coq_Nat.eq_dec nat_countable))
                      o (new_owner h (Some root_owner_id) d.n_disposed)
                      s.g_owners)
                    s
                in
                Some (Pair
                ((with_current_owner (Some o)
                   (set_node h (node_with_scope (Some o) d) s1)),
                (Some o)))))
        | None -> None)
     | None -> None)

(** val exec : nat -> env -> cmd -> outcome m **)

let rec exec fuel e c =
  match fuel with
  | O -> stuck
  | S f ->
    (match c with
     | CRet x -> on_value e x (fun v -> m_return (Ok v))
     | CThrow y -> m_return (Fail y)
     | CLet (x, v, k) ->
       on_value e v (fun z0 -> exec f (Cons ((Pair (x, z0)), e)) k)
     | CIf (v, a, b) ->
       on_value e v (fun z0 ->
         match decide (decide_rel Coq_Z.eq_dec z0 Z0) with
         | Left -> exec f e b
         | Right -> exec f e a)
     | CRead (x, n0, k) ->
       on_node e n0 (fun t ->
         m_bind (read f t True) (fun o ->
           m_bind (note_host_read t o) (fun _ ->
             match o with
             | Ok v -> exec f (Cons ((Pair (x, v)), e)) k
             | Fail y -> m_return (Fail y))))
     | CTryRead (x, ok, n0, k) ->
       on_node e n0 (fun t ->
         m_bind (read f t True) (fun o ->
           m_bind (note_host_read t o) (fun _ ->
             match o with
             | Ok v ->
               exec f (Cons ((Pair (ok, (Zpos XH))), (Cons ((Pair (x, v)),
                 e)))) k
             | Fail y ->
               exec f (Cons ((Pair (ok, Z0)), (Cons ((Pair (x, y)), e)))) k)))
     | CReadUntracked (x, n0, k) ->
       on_node e n0 (fun t ->
         m_bind (read f t False) (fun o ->
           m_bind (note_host_read t o) (fun _ ->
             match o with
             | Ok v -> exec f (Cons ((Pair (x, v)), e)) k
             | Fail y -> m_return (Fail y))))
     | CWrite (n0, v, k) ->
       on_node e n0 (fun t ->
         on_value e v (fun z0 ->
           m_bind (write_signal f t z0) (fun _ -> exec f e k)))
     | CSignal (x, v, k) ->
       on_value e v (fun z0 ->
         m_bind next_id (fun n0 ->
           m_bind (add_node n0 (signal_node z0)) (fun _ ->
             exec f (Cons ((Pair (x, (Z.of_nat n0))), e)) k)))
     | CMemo (x, m0, q, b, k) ->
       m_bind next_id (fun n0 ->
         let e' = Cons ((Pair (x, (Z.of_nat n0))), e) in
         m_bind (add_node n0 (memo_node m0 q b e')) (fun _ ->
           m_bind current_owner (fun owner ->
             match owner with
             | Some o -> m_bind (attach f n0 o) (fun _ -> exec f e' k)
             | None -> m_return (Fail pure_scope_failure))))
     | CEffect (x, b, k) ->
       m_bind next_id (fun n0 ->
         let e' = Cons ((Pair (x, (Z.of_nat n0))), e) in
         m_bind (add_node n0 (effect_node b e')) (fun _ ->
           m_bind current_owner (fun owner ->
             match owner with
             | Some o ->
               m_bind (attach f n0 o) (fun _ ->
                 m_bind (m_step (mark_dirty f n0)) (fun _ ->
                   m_bind (request_flush f) (fun _ -> exec f e' k)))
             | None -> m_return (Fail pure_scope_failure))))
     | CCleanup (b, k) ->
       m_bind current_owner (fun owner ->
         match owner with
         | Some o ->
           m_bind (get_owner o) (fun x ->
             m_bind (m_modify (ghost_registered x.o_host)) (fun _ ->
               m_bind
                 (match x.o_disposed with
                  | True ->
                    m_bind (detached o (run_cleanup f o b e)) (fun _ ->
                      request_flush f)
                  | False -> put_owner o (owner_with_cleanup b e x))
                 (fun _ -> exec f e k)))
         | None -> m_return (Fail pure_scope_failure))
     | CDispose (n0, k) ->
       on_node e n0 (fun t ->
         m_bind (get_node t) (fun d ->
           match d.n_kind with
           | KSignal -> stuck
           | _ ->
             m_bind (dispose_node f t) (fun _ ->
               m_bind
                 (match d.n_disposed with
                  | True -> m_return Tt
                  | False -> emit (EvDispose t))
                 (fun _ -> exec f e k))))
     | CBatch (b, k) ->
       m_bind (m_modify (fun s -> with_batch_depth (S s.g_batch_depth) s))
         (fun _ ->
         m_bind (exec f e b) (fun r ->
           m_bind
             (m_modify (fun s -> with_batch_depth (pred s.g_batch_depth) s))
             (fun _ ->
             match r with
             | Ok _ -> m_bind (request_flush f) (fun _ -> exec f e k)
             | Fail y -> m_return (Fail y))))
     | CLog (v, k) ->
       on_value e v (fun z0 ->
         m_bind log_label (fun l ->
           m_bind (emit (EvLog (l, z0))) (fun _ -> exec f e k))))

(** val read : nat -> node_id -> bool -> outcome m **)

and read fuel n0 tracked =
  match fuel with
  | O -> stuck
  | S f ->
    (match tracked with
     | True -> read_node f n0
     | False -> untracked (read_node f n0))

(** val read_node : nat -> node_id -> outcome m **)

and read_node fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_node n0) (fun d ->
      match d.n_kind with
      | KSignal -> m_bind (track n0) (fun _ -> m_return (Ok d.n_value))
      | KMemo ->
        m_bind
          (match stale_live d with
           | True ->
             m_bind (refresh_or_pull f n0) (fun _ ->
               m_bind (track n0) (fun _ ->
                 m_bind (get_node n0) (fun d' ->
                   match stale_live d' with
                   | True -> mark_reader f
                   | False -> m_return Tt)))
           | False -> track n0)
          (fun _ ->
          m_bind (get_node n0) (fun d' ->
            match d'.n_status with
            | StError ->
              m_return (Fail (from_option (Obj.magic id) Z0 d'.n_failure))
            | _ -> m_return (Ok d'.n_value)))
      | KEffect -> stuck)

(** val update_if_necessary : nat -> node_id -> unit0 m **)

and update_if_necessary fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_node n0) (fun d ->
      match d.n_kind with
      | KMemo ->
        (match stale_live d with
         | True -> refresh_or_pull f n0
         | False -> m_return Tt)
      | _ -> m_return Tt)

(** val refresh_or_pull : nat -> node_id -> unit0 m **)

and refresh_or_pull fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind m_get (fun s ->
      match deferring s with
      | True -> refresh f n0
      | False -> pull f n0)

(** val pull : nat -> node_id -> unit0 m **)

and pull fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (m_modify (fun s -> with_pull_depth (S s.g_pull_depth) s))
      (fun _ ->
      m_bind (refresh f n0) (fun _ ->
        m_bind (m_modify (fun s -> with_pull_depth (pred s.g_pull_depth) s))
          (fun _ ->
          m_bind m_get (fun s ->
            match match bool_decide
                          (decide_rel Coq_Nat.eq_dec s.g_pull_depth O) with
                  | True -> s.g_flush_owed
                  | False -> False with
            | True -> settle_flush f
            | False -> m_return Tt))))

(** val refresh : nat -> node_id -> unit0 m **)

and refresh fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_node n0) (fun d ->
      m_bind
        (match decide (decide_rel freshness_eq_dec d.n_freshness Check) with
         | Left ->
           m_bind (resolve_walk f n0 O) (fun _ ->
             m_bind (get_node n0) (fun d' ->
               match decide (decide_rel freshness_eq_dec d'.n_freshness Check) with
               | Left ->
                 m_bind (m_modify (ghost_resolved n0)) (fun _ ->
                   modify_node n0 (fun x ->
                     node_with_unannounced False (node_with_freshness Clean x)))
               | Right -> m_return Tt))
         | Right -> m_return Tt)
        (fun _ ->
        m_bind (get_node n0) (fun d' ->
          match decide (decide_rel freshness_eq_dec d'.n_freshness Dirty) with
          | Left -> recompute f n0
          | Right -> m_return Tt)))

(** val resolve_walk : nat -> node_id -> nat -> unit0 m **)

and resolve_walk fuel n0 i =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_node n0) (fun d ->
      m_bind m_get (fun s ->
        match decide (decide_rel freshness_eq_dec d.n_freshness Check) with
        | Left ->
          (match lookup0 Coq_list.list_lookup i
                   (sources_of s.g_edges n0).source_slots with
           | Some y ->
             let Pair (src, _) = y in
             m_bind (update_if_necessary f src) (fun _ ->
               resolve_walk f n0 (S i))
           | None -> m_return Tt)
        | Right -> m_return Tt))

(** val recompute : nat -> node_id -> unit0 m **)

and recompute fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_node n0) (fun d ->
      match d.n_scope with
      | Some o ->
        m_bind (modify_node n0 (node_with_freshness Clean)) (fun _ ->
          m_bind (m_modify (ghost_dirty_cleared n0 d.n_runs)) (fun _ ->
            m_bind (discharge f o) (fun _ ->
              m_bind (get_node n0) (fun d' ->
                match d'.n_disposed with
                | True -> m_return Tt
                | False ->
                  (match decide (decide_rel Coq_Nat.eq_dec d'.n_runs d.n_runs) with
                   | Left -> run_memo f n0
                   | Right ->
                     (match decide
                              (decide_rel freshness_eq_dec d'.n_freshness
                                Dirty) with
                      | Left -> recompute f n0
                      | Right -> m_return Tt))))))
      | None -> run_memo f n0)

(** val run_memo : nat -> node_id -> unit0 m **)

and run_memo fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_node n0) (fun d ->
      let previous = d.n_value in
      let previous_status = d.n_status in
      let previous_failure = d.n_failure in
      m_bind
        (modify_node n0 (fun x ->
          node_with_runs (S x.n_runs)
            (node_with_failure None
              (node_with_status StNone
                (node_with_unannounced False (node_with_freshness Clean x))))))
        (fun _ ->
        m_bind (edges (sources_begin_run n0)) (fun _ ->
          m_bind (m_modify (ghost_run_start n0)) (fun _ ->
            m_bind (push_running n0) (fun _ ->
              let body_env =
                match d.n_published with
                | True -> Cons ((Pair (prev_var, previous)), d.n_env)
                | False -> without_prev d.n_env
              in
              m_bind (hosted n0 (exec f body_env d.n_body)) (fun r ->
                m_bind (get_node n0) (fun d1 ->
                  let result =
                    match d1.n_violated with
                    | True -> Fail pure_scope_failure
                    | False -> r
                  in
                  m_bind
                    (match result with
                     | Ok v ->
                       modify_node n0 (fun x ->
                         node_with_published True (node_with_value v x))
                     | Fail _ -> m_return Tt)
                    (fun _ ->
                    m_bind
                      (edges_opt
                        (match d1.n_disposed with
                         | True -> sources_clear n0
                         | False -> sources_end_run n0))
                      (fun _ ->
                      m_bind pop_running (fun _ ->
                        m_bind
                          (match result with
                           | Ok _ -> m_return Tt
                           | Fail y ->
                             modify_node n0 (fun x ->
                               node_with_violated False
                                 (node_with_status StError
                                   (node_with_failure (Some y) x))))
                          (fun _ ->
                          m_bind (get_node n0) (fun d2 ->
                            m_bind
                              (match decide
                                       (or_dec
                                         (not_dec
                                           (decide_rel status_eq_dec
                                             d2.n_status previous_status))
                                         (not_dec
                                           (decide_rel
                                             (option_eq_dec Coq_Z.eq_dec)
                                             d2.n_failure previous_failure))) with
                               | Left -> m_return True
                               | Right ->
                                 (match d2.n_comparer with
                                  | CmpEq ->
                                    m_return
                                      (bool_decide
                                        (not_dec
                                          (decide_rel Coq_Z.eq_dec d2.n_value
                                            previous)))
                                  | CmpNever -> m_return True
                                  | CmpThrows y ->
                                    m_bind
                                      (modify_node n0 (fun x ->
                                        node_with_status StError
                                          (node_with_failure (Some y)
                                            (node_with_value previous x))))
                                      (fun _ -> m_return True)))
                              (fun moved ->
                              m_bind m_get (fun s ->
                                m_bind
                                  (m_modify
                                    (ghost_run_end n0 s.g_current moved))
                                  (fun _ ->
                                  m_bind
                                    (match moved with
                                     | True ->
                                       m_step
                                         (notify_dirty_except f n0
                                           s.g_current)
                                     | False -> m_return Tt)
                                    (fun _ ->
                                    m_bind (get_node n0) (fun d3 ->
                                      match decide
                                              (decide_rel freshness_eq_dec
                                                d3.n_freshness Clean) with
                                      | Left -> m_return Tt
                                      | Right -> announce n0)))))))))))))))))

(** val execute_effect : nat -> node_id -> unit0 m **)

and execute_effect fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_node n0) (fun d ->
      match d.n_disposed with
      | True -> modify_node n0 (node_with_queued False)
      | False ->
        m_bind
          (match decide (decide_rel freshness_eq_dec d.n_freshness Check) with
           | Left ->
             m_bind (resolve_walk f n0 O) (fun _ ->
               m_bind (get_node n0) (fun d' ->
                 match decide
                         (decide_rel freshness_eq_dec d'.n_freshness Check) with
                 | Left ->
                   m_bind (m_modify (ghost_resolved n0)) (fun _ ->
                     modify_node n0 (node_with_freshness Clean))
                 | Right -> m_return Tt))
           | Right -> m_return Tt)
          (fun _ ->
          m_bind (get_node n0) (fun d' ->
            m_bind
              (modify_node n0 (fun x ->
                node_with_freshness Clean (node_with_queued False x)))
              (fun _ ->
              match decide (decide_rel freshness_eq_dec d'.n_freshness Dirty) with
              | Left -> run_effect f n0
              | Right -> m_return Tt))))

(** val run_effect : nat -> node_id -> unit0 m **)

and run_effect fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (push_running n0) (fun _ ->
      m_bind (edges (sources_begin_run n0)) (fun _ ->
        m_bind (m_modify (ghost_effect_start n0)) (fun _ ->
          m_bind (get_node n0) (fun d ->
            m_bind
              (match d.n_scope with
               | Some o ->
                 m_bind (modify_node n0 (node_with_in_cleanup True))
                   (fun _ ->
                   m_bind (discharge f o) (fun _ ->
                     modify_node n0 (node_with_in_cleanup False)))
               | None -> m_return Tt)
              (fun _ ->
              m_bind (get_node n0) (fun d' ->
                m_bind
                  (match d'.n_disposed with
                   | True -> m_return Tt
                   | False -> run_effect_body f n0)
                  (fun _ -> pop_running)))))))

(** val run_effect_body : nat -> node_id -> unit0 m **)

and run_effect_body fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_node n0) (fun d ->
      m_bind
        (modify_node n0 (fun x ->
          node_with_runs (S x.n_runs)
            (node_with_failure None (node_with_status StNone x))))
        (fun _ ->
        m_bind (emit (EvRun n0)) (fun _ ->
          m_bind (hosted n0 (exec f d.n_env d.n_body)) (fun r ->
            m_bind (get_node n0) (fun d' ->
              m_bind
                (edges_opt
                  (match d'.n_disposed with
                   | True -> sources_clear n0
                   | False -> sources_end_run n0))
                (fun _ ->
                match r with
                | Ok _ -> m_return Tt
                | Fail y ->
                  m_bind
                    (modify_node n0 (fun x ->
                      node_with_status StError (node_with_failure (Some y) x)))
                    (fun _ -> emit (EvFail (n0, y)))))))))

(** val discharge : nat -> owner_id -> unit0 m **)

and discharge fuel o =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (detached o (dispose_scope f o)) (fun _ ->
      m_bind m_get (fun s ->
        match match match bool_decide
                            (decide_rel Coq_Nat.eq_dec s.g_batch_depth O) with
                    | True -> negb s.g_flushing
                    | False -> False with
              | True -> live_queue s
              | False -> False with
        | True -> m_modify (with_flush_owed True)
        | False -> m_return Tt))

(** val retire : nat -> owner_id -> unit0 m **)

and retire fuel o =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind
      (detached o
        (m_bind (get_owner o) (fun x ->
          match x.o_disposed with
          | True -> m_return Tt
          | False ->
            m_bind (put_owner o (owner_retired x)) (fun _ ->
              dispose_scope f o))))
      (fun _ ->
      m_bind m_get (fun s ->
        match live_queue s with
        | True -> request_flush f
        | False -> m_return Tt))

(** val dispose_scope : nat -> owner_id -> unit0 m **)

and dispose_scope fuel o =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_owner o) (fun x ->
      m_bind (put_owner o (owner_detached x)) (fun _ ->
        teardown f o (rev x.o_children) (owner_cleanups x)))

(** val teardown :
    nat -> owner_id -> link list -> ((cmd, env) prod, nat) prod list -> unit0
    m **)

and teardown fuel o ls cs =
  match fuel with
  | O -> stuck
  | S f ->
    (match ls with
     | Nil ->
       (match cs with
        | Nil -> m_return Tt
        | Cons (p, cs') ->
          let Pair (p0, _) = p in
          let Pair (b, e) = p0 in
          m_bind (run_cleanup f o b e) (fun _ -> teardown f o Nil cs'))
     | Cons (l, ls') ->
       (match cs with
        | Nil -> m_bind (release f l.l_child) (fun _ -> teardown f o ls' Nil)
        | Cons (p, cs') ->
          let Pair (p0, a) = p in
          let Pair (b, e) = p0 in
          (match decide (decide_rel Coq_Nat.lt_dec a l.l_acquired) with
           | Left ->
             m_bind (release f l.l_child) (fun _ -> teardown f o ls' cs)
           | Right ->
             m_bind (run_cleanup f o b e) (fun _ -> teardown f o ls cs'))))

(** val run_cleanup : nat -> owner_id -> cmd -> env -> unit0 m **)

and run_cleanup fuel o b e =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_owner o) (fun x ->
      m_bind (emit (EvCleanup x.o_host)) (fun _ ->
        m_bind (exec f e b) (fun r ->
          match r with
          | Ok _ -> m_return Tt
          | Fail y ->
            m_bind (record_error o y) (fun _ -> emit (EvFail (x.o_host, y))))))

(** val release : nat -> node_id -> unit0 m **)

and release fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (modify_node n0 (node_with_link None)) (fun _ -> dispose_node f n0)

(** val dispose_node : nat -> node_id -> unit0 m **)

and dispose_node fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_node n0) (fun d ->
      match d.n_disposed with
      | True -> m_return Tt
      | False ->
        m_bind
          (modify_node n0 (fun x ->
            node_with_link None (node_with_disposed True x)))
          (fun _ ->
          m_bind
            (match d.n_link with
             | Some l -> unlink l
             | None -> m_return Tt)
            (fun _ ->
            m_bind (edges_opt (sources_clear n0)) (fun _ ->
              match d.n_scope with
              | Some o -> retire f o
              | None -> m_return Tt))))

(** val attach : nat -> node_id -> owner_id -> unit0 m **)

and attach fuel n0 o =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_owner o) (fun x ->
      match x.o_disposed with
      | True ->
        m_bind (release f n0) (fun _ ->
          modify_node n0
            (node_with_link (Some { l_child = n0; l_owner = o; l_generation =
              None; l_acquired = O })))
      | False ->
        let l = { l_child = n0; l_owner = o; l_generation = (Some
          x.o_generation); l_acquired = x.o_acquisitions }
        in
        m_bind (put_owner o (owner_with_child l x)) (fun _ ->
          modify_node n0 (node_with_link (Some l))))

(** val write_signal : nat -> node_id -> val0 -> unit0 m **)

and write_signal fuel n0 v =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_node n0) (fun d ->
      match d.n_kind with
      | KSignal ->
        (match decide (decide_rel Coq_Z.eq_dec d.n_value v) with
         | Left -> m_return Tt
         | Right ->
           m_bind (modify_node n0 (node_with_value v)) (fun _ ->
             m_bind (m_modify (ghost_publish n0)) (fun _ ->
               m_bind (m_step (notify_dirty f n0)) (fun _ -> request_flush f))))
      | _ -> stuck)

(** val request_flush : nat -> unit0 m **)

and request_flush = function
| O -> stuck
| S f ->
  m_bind m_get (fun s ->
    match match live_queue s with
          | True -> bool_decide (decide_rel Coq_Nat.eq_dec s.g_batch_depth O)
          | False -> False with
    | True ->
      (match outside_bodies s with
       | True -> run_flush f
       | False ->
         (match s.g_flushing with
          | True -> m_return Tt
          | False -> m_modify (with_flush_owed True)))
    | False -> m_return Tt)

(** val settle_flush : nat -> unit0 m **)

and settle_flush = function
| O -> stuck
| S f ->
  m_bind m_get (fun s ->
    match match match s.g_flush_owed with
                | True ->
                  bool_decide (decide_rel Coq_Nat.eq_dec s.g_batch_depth O)
                | False -> False with
          | True -> outside_bodies s
          | False -> False with
    | True -> run_flush f
    | False -> m_return Tt)

(** val run_flush : nat -> unit0 m **)

and run_flush = function
| O -> stuck
| S f ->
  m_bind m_get (fun s ->
    match s.g_flushing with
    | True -> m_return Tt
    | False ->
      m_bind
        (m_modify (fun s0 -> with_flush_owed False (with_flushing True s0)))
        (fun _ -> m_bind (drain f) (fun _ -> m_modify (with_flushing False))))

(** val drain : nat -> unit0 m **)

and drain = function
| O -> stuck
| S f ->
  m_bind m_get (fun s ->
    match lookup0 Coq_list.list_lookup s.g_queue_head s.g_queue with
    | Some n0 ->
      m_bind (m_modify (with_queue_head (S s.g_queue_head))) (fun _ ->
        m_bind (execute_effect f n0) (fun _ -> drain f))
    | None -> m_modify (fun s0 -> with_queue Nil (with_queue_head O s0)))

(** val init : flags -> state **)

let init fl =
  { g_flags = fl; g_edges = empty_world; g_nodes =
    (empty0 (gmap_empty Coq_Nat.eq_dec nat_countable)); g_owners =
    (insert0 (map_insert (gmap_partial_alter Coq_Nat.eq_dec nat_countable))
      root_owner_id (new_owner O None False)
      (empty0 (gmap_empty Coq_Nat.eq_dec nat_countable)));
    g_queue = Nil; g_queue_head = O; g_flushing = False; g_flush_owed =
    False; g_batch_depth = O; g_pull_depth = O; g_current = None;
    g_untracked = False; g_current_owner = (Some root_owner_id); g_next_id =
    O; g_trace = Nil; g_running = Nil; g_ghost = empty_ghost; g_registered =
    (empty0 (gmap_empty Coq_Nat.eq_dec nat_countable)); g_skip = SkipOff;
    g_epoch = O; g_stamps =
    (empty0 (gmap_empty Coq_Nat.eq_dec nat_countable)) }

(** val run_top : nat -> cmd -> outcome m **)

let run_top fuel c =
  exec fuel Nil c

(** val trace_of : nat -> flags -> cmd -> event list option **)

let trace_of fuel fl c =
  match run_top fuel c (init fl) with
  | Some p -> let Pair (s, _) = p in Some s.g_trace
  | None -> None

type advance_point =
| A4191
| A4207
| A4290
| A4807
| A4825
| ANewEffect

(** val advances : skip_mode -> advance_point -> bool **)

let advances m0 p =
  match m0 with
  | SkipOff -> False
  | SkipLeaveDirty ->
    (match p with
     | A4191 -> True
     | A4207 -> True
     | A4807 -> True
     | _ -> False)
  | _ -> True

(** val advance : advance_point -> unit0 m **)

let advance p =
  m_modify (fun s ->
    match advances s.g_skip p with
    | True -> with_epoch (S s.g_epoch) s
    | False -> s)

(** val modify_node_adv :
    advance_point -> node_id -> (node -> node) -> unit0 m **)

let modify_node_adv p n0 u =
  m_bind (modify_node n0 u) (fun _ -> advance p)

(** val add_node_adv : node_id -> node -> unit0 m **)

let add_node_adv n0 d =
  m_bind (add_node n0 d) (fun _ -> advance ANewEffect)

(** val skip_on : skip_mode -> bool **)

let skip_on = function
| SkipOff -> False
| _ -> True

(** val stamp_current_dec : state -> node_id -> decision **)

let stamp_current_dec s src =
  decide_rel (option_eq_dec Coq_Nat.eq_dec)
    (lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) src s.g_stamps) (Some
    s.g_epoch)

(** val saturated_dec : state -> node_id -> decision **)

let saturated_dec s n0 =
  let o = lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) n0 s.g_nodes in
  (match o with
   | Some n1 ->
     let k = n1.n_kind in
     (match k with
      | KSignal -> true_dec
      | KMemo ->
        and_dec (decide_rel freshness_eq_dec n1.n_freshness Dirty)
          (decide_rel bool_eq_dec (announce_due s n1) False)
      | KEffect ->
        or_dec (decide_rel bool_eq_dec n1.n_disposed True)
          (or_dec (decide_rel bool_eq_dec n1.n_in_cleanup True)
            (and_dec (decide_rel freshness_eq_dec n1.n_freshness Dirty)
              (decide_rel bool_eq_dec n1.n_queued True))))
   | None -> true_dec)

(** val all_saturated : node_id -> state -> bool **)

let all_saturated src s =
  forallb (fun c -> bool_decide (saturated_dec s c))
    (observer_view (observers_of s.g_edges src))

(** val record_due : node_id -> state -> bool **)

let record_due src s =
  match s.g_skip with
  | SkipOff -> False
  | SkipUnchecked -> True
  | _ -> all_saturated src s

(** val skip_due : nat -> node_id -> state -> bool **)

let skip_due fuel src s =
  match fuel with
  | O -> False
  | S _ ->
    (match skip_on s.g_skip with
     | True -> bool_decide (stamp_current_dec s src)
     | False -> False)

(** val record : node_id -> state -> state **)

let record src s =
  match record_due src s with
  | True ->
    with_stamps
      (insert0 (map_insert (gmap_partial_alter Coq_Nat.eq_dec nat_countable))
        src s.g_epoch s.g_stamps)
      s
  | False -> s

(** val notify_write : nat -> node_id -> state -> state option **)

let notify_write fuel src s =
  match skip_due fuel src s with
  | True -> Some s
  | False ->
    fmap (Obj.magic (fun _ _ -> option_fmap)) (record src)
      (notify_dirty fuel src s)

(** val unstamp : node_id -> state -> state **)

let unstamp src s =
  match skip_on s.g_skip with
  | True ->
    with_stamps
      (delete0 (map_delete (gmap_partial_alter Coq_Nat.eq_dec nat_countable))
        src s.g_stamps)
      s
  | False -> s

(** val links : node_id -> node_id -> edgeWorld -> bool **)

let links self s w =
  let l = sources_of w self in
  let cur = l.source_cursor in
  (match negb
           (bool_decide
             (decide_rel (Obj.magic option_eq_dec Coq_Nat.eq_dec)
               (fmap (Obj.magic (fun _ _ -> option_fmap)) fst
                 (lookup0 Coq_list.list_lookup cur l.source_slots))
               (Some s))) with
   | True ->
     negb
       (bool_decide
         (and_dec (decide_rel Coq_Nat.lt_dec O cur)
           (decide_rel (Obj.magic option_eq_dec Coq_Nat.eq_dec)
             (fmap (Obj.magic (fun _ _ -> option_fmap)) fst
               (lookup0 Coq_list.list_lookup (pred cur) l.source_slots))
             (Some s))))
   | False -> False)

(** val otrack : node_id -> unit0 m **)

let otrack n0 s =
  match s.g_current with
  | Some c ->
    (match sources_add c n0 s.g_edges with
     | Some w ->
       let s' = ghost_read c n0 (read_prefix s.g_edges c) (with_edges w s) in
       Some (Pair
       ((match links c n0 s.g_edges with
         | True -> unstamp n0 s'
         | False -> s'),
       Tt))
     | None -> None)
  | None -> Some (Pair (s, Tt))

(** val oannounce : node_id -> unit0 m **)

let oannounce n0 s =
  match s.g_flags.f_unannounced with
  | True -> modify_node_adv A4290 n0 (node_with_unannounced True) s
  | False -> Some (Pair (s, Tt))

(** val oexec : nat -> env -> cmd -> outcome m **)

let rec oexec fuel e c =
  match fuel with
  | O -> stuck
  | S f ->
    (match c with
     | CRet x -> on_value e x (fun v -> m_return (Ok v))
     | CThrow y -> m_return (Fail y)
     | CLet (x, v, k) ->
       on_value e v (fun z0 -> oexec f (Cons ((Pair (x, z0)), e)) k)
     | CIf (v, a, b) ->
       on_value e v (fun z0 ->
         match decide (decide_rel Coq_Z.eq_dec z0 Z0) with
         | Left -> oexec f e b
         | Right -> oexec f e a)
     | CRead (x, n0, k) ->
       on_node e n0 (fun t ->
         m_bind (oread f t True) (fun o ->
           m_bind (note_host_read t o) (fun _ ->
             match o with
             | Ok v -> oexec f (Cons ((Pair (x, v)), e)) k
             | Fail y -> m_return (Fail y))))
     | CTryRead (x, ok, n0, k) ->
       on_node e n0 (fun t ->
         m_bind (oread f t True) (fun o ->
           m_bind (note_host_read t o) (fun _ ->
             match o with
             | Ok v ->
               oexec f (Cons ((Pair (ok, (Zpos XH))), (Cons ((Pair (x, v)),
                 e)))) k
             | Fail y ->
               oexec f (Cons ((Pair (ok, Z0)), (Cons ((Pair (x, y)), e)))) k)))
     | CReadUntracked (x, n0, k) ->
       on_node e n0 (fun t ->
         m_bind (oread f t False) (fun o ->
           m_bind (note_host_read t o) (fun _ ->
             match o with
             | Ok v -> oexec f (Cons ((Pair (x, v)), e)) k
             | Fail y -> m_return (Fail y))))
     | CWrite (n0, v, k) ->
       on_node e n0 (fun t ->
         on_value e v (fun z0 ->
           m_bind (owrite_signal f t z0) (fun _ -> oexec f e k)))
     | CSignal (x, v, k) ->
       on_value e v (fun z0 ->
         m_bind next_id (fun n0 ->
           m_bind (add_node n0 (signal_node z0)) (fun _ ->
             oexec f (Cons ((Pair (x, (Z.of_nat n0))), e)) k)))
     | CMemo (x, m0, q, b, k) ->
       m_bind next_id (fun n0 ->
         let e' = Cons ((Pair (x, (Z.of_nat n0))), e) in
         m_bind (add_node n0 (memo_node m0 q b e')) (fun _ ->
           m_bind current_owner (fun owner ->
             match owner with
             | Some o -> m_bind (oattach f n0 o) (fun _ -> oexec f e' k)
             | None -> m_return (Fail pure_scope_failure))))
     | CEffect (x, b, k) ->
       m_bind next_id (fun n0 ->
         let e' = Cons ((Pair (x, (Z.of_nat n0))), e) in
         m_bind (add_node_adv n0 (effect_node b e')) (fun _ ->
           m_bind current_owner (fun owner ->
             match owner with
             | Some o ->
               m_bind (oattach f n0 o) (fun _ ->
                 m_bind (m_step (mark_dirty f n0)) (fun _ ->
                   m_bind (orequest_flush f) (fun _ -> oexec f e' k)))
             | None -> m_return (Fail pure_scope_failure))))
     | CCleanup (b, k) ->
       m_bind current_owner (fun owner ->
         match owner with
         | Some o ->
           m_bind (get_owner o) (fun x ->
             m_bind (m_modify (ghost_registered x.o_host)) (fun _ ->
               m_bind
                 (match x.o_disposed with
                  | True ->
                    m_bind (detached o (orun_cleanup f o b e)) (fun _ ->
                      orequest_flush f)
                  | False -> put_owner o (owner_with_cleanup b e x))
                 (fun _ -> oexec f e k)))
         | None -> m_return (Fail pure_scope_failure))
     | CDispose (n0, k) ->
       on_node e n0 (fun t ->
         m_bind (get_node t) (fun d ->
           match d.n_kind with
           | KSignal -> stuck
           | _ ->
             m_bind (odispose_node f t) (fun _ ->
               m_bind
                 (match d.n_disposed with
                  | True -> m_return Tt
                  | False -> emit (EvDispose t))
                 (fun _ -> oexec f e k))))
     | CBatch (b, k) ->
       m_bind (m_modify (fun s -> with_batch_depth (S s.g_batch_depth) s))
         (fun _ ->
         m_bind (oexec f e b) (fun r ->
           m_bind
             (m_modify (fun s -> with_batch_depth (pred s.g_batch_depth) s))
             (fun _ ->
             match r with
             | Ok _ -> m_bind (orequest_flush f) (fun _ -> oexec f e k)
             | Fail y -> m_return (Fail y))))
     | CLog (v, k) ->
       on_value e v (fun z0 ->
         m_bind log_label (fun l ->
           m_bind (emit (EvLog (l, z0))) (fun _ -> oexec f e k))))

(** val oread : nat -> node_id -> bool -> outcome m **)

and oread fuel n0 tracked =
  match fuel with
  | O -> stuck
  | S f ->
    (match tracked with
     | True -> oread_node f n0
     | False -> untracked (oread_node f n0))

(** val oread_node : nat -> node_id -> outcome m **)

and oread_node fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_node n0) (fun d ->
      match d.n_kind with
      | KSignal -> m_bind (otrack n0) (fun _ -> m_return (Ok d.n_value))
      | KMemo ->
        m_bind
          (match stale_live d with
           | True ->
             m_bind (orefresh_or_pull f n0) (fun _ ->
               m_bind (otrack n0) (fun _ ->
                 m_bind (get_node n0) (fun d' ->
                   match stale_live d' with
                   | True -> mark_reader f
                   | False -> m_return Tt)))
           | False -> otrack n0)
          (fun _ ->
          m_bind (get_node n0) (fun d' ->
            match d'.n_status with
            | StError ->
              m_return (Fail (from_option (Obj.magic id) Z0 d'.n_failure))
            | _ -> m_return (Ok d'.n_value)))
      | KEffect -> stuck)

(** val oupdate_if_necessary : nat -> node_id -> unit0 m **)

and oupdate_if_necessary fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_node n0) (fun d ->
      match d.n_kind with
      | KMemo ->
        (match stale_live d with
         | True -> orefresh_or_pull f n0
         | False -> m_return Tt)
      | _ -> m_return Tt)

(** val orefresh_or_pull : nat -> node_id -> unit0 m **)

and orefresh_or_pull fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind m_get (fun s ->
      match deferring s with
      | True -> orefresh f n0
      | False -> opull f n0)

(** val opull : nat -> node_id -> unit0 m **)

and opull fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (m_modify (fun s -> with_pull_depth (S s.g_pull_depth) s))
      (fun _ ->
      m_bind (orefresh f n0) (fun _ ->
        m_bind (m_modify (fun s -> with_pull_depth (pred s.g_pull_depth) s))
          (fun _ ->
          m_bind m_get (fun s ->
            match match bool_decide
                          (decide_rel Coq_Nat.eq_dec s.g_pull_depth O) with
                  | True -> s.g_flush_owed
                  | False -> False with
            | True -> osettle_flush f
            | False -> m_return Tt))))

(** val orefresh : nat -> node_id -> unit0 m **)

and orefresh fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_node n0) (fun d ->
      m_bind
        (match decide (decide_rel freshness_eq_dec d.n_freshness Check) with
         | Left ->
           m_bind (oresolve_walk f n0 O) (fun _ ->
             m_bind (get_node n0) (fun d' ->
               match decide (decide_rel freshness_eq_dec d'.n_freshness Check) with
               | Left ->
                 m_bind (m_modify (ghost_resolved n0)) (fun _ ->
                   modify_node n0 (fun x ->
                     node_with_unannounced False (node_with_freshness Clean x)))
               | Right -> m_return Tt))
         | Right -> m_return Tt)
        (fun _ ->
        m_bind (get_node n0) (fun d' ->
          match decide (decide_rel freshness_eq_dec d'.n_freshness Dirty) with
          | Left -> orecompute f n0
          | Right -> m_return Tt)))

(** val oresolve_walk : nat -> node_id -> nat -> unit0 m **)

and oresolve_walk fuel n0 i =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_node n0) (fun d ->
      m_bind m_get (fun s ->
        match decide (decide_rel freshness_eq_dec d.n_freshness Check) with
        | Left ->
          (match lookup0 Coq_list.list_lookup i
                   (sources_of s.g_edges n0).source_slots with
           | Some y ->
             let Pair (src, _) = y in
             m_bind (oupdate_if_necessary f src) (fun _ ->
               oresolve_walk f n0 (S i))
           | None -> m_return Tt)
        | Right -> m_return Tt))

(** val orecompute : nat -> node_id -> unit0 m **)

and orecompute fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_node n0) (fun d ->
      match d.n_scope with
      | Some o ->
        m_bind (modify_node_adv A4191 n0 (node_with_freshness Clean))
          (fun _ ->
          m_bind (m_modify (ghost_dirty_cleared n0 d.n_runs)) (fun _ ->
            m_bind (odischarge f o) (fun _ ->
              m_bind (get_node n0) (fun d' ->
                match d'.n_disposed with
                | True -> m_return Tt
                | False ->
                  (match decide (decide_rel Coq_Nat.eq_dec d'.n_runs d.n_runs) with
                   | Left -> orun_memo f n0
                   | Right ->
                     (match decide
                              (decide_rel freshness_eq_dec d'.n_freshness
                                Dirty) with
                      | Left -> orecompute f n0
                      | Right -> m_return Tt))))))
      | None -> orun_memo f n0)

(** val orun_memo : nat -> node_id -> unit0 m **)

and orun_memo fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_node n0) (fun d ->
      let previous = d.n_value in
      let previous_status = d.n_status in
      let previous_failure = d.n_failure in
      m_bind
        (modify_node_adv A4207 n0 (fun x ->
          node_with_runs (S x.n_runs)
            (node_with_failure None
              (node_with_status StNone
                (node_with_unannounced False (node_with_freshness Clean x))))))
        (fun _ ->
        m_bind (edges (sources_begin_run n0)) (fun _ ->
          m_bind (m_modify (ghost_run_start n0)) (fun _ ->
            m_bind (push_running n0) (fun _ ->
              let body_env =
                match d.n_published with
                | True -> Cons ((Pair (prev_var, previous)), d.n_env)
                | False -> without_prev d.n_env
              in
              m_bind (hosted n0 (oexec f body_env d.n_body)) (fun r ->
                m_bind (get_node n0) (fun d1 ->
                  let result =
                    match d1.n_violated with
                    | True -> Fail pure_scope_failure
                    | False -> r
                  in
                  m_bind
                    (match result with
                     | Ok v ->
                       modify_node n0 (fun x ->
                         node_with_published True (node_with_value v x))
                     | Fail _ -> m_return Tt)
                    (fun _ ->
                    m_bind
                      (edges_opt
                        (match d1.n_disposed with
                         | True -> sources_clear n0
                         | False -> sources_end_run n0))
                      (fun _ ->
                      m_bind pop_running (fun _ ->
                        m_bind
                          (match result with
                           | Ok _ -> m_return Tt
                           | Fail y ->
                             modify_node n0 (fun x ->
                               node_with_violated False
                                 (node_with_status StError
                                   (node_with_failure (Some y) x))))
                          (fun _ ->
                          m_bind (get_node n0) (fun d2 ->
                            m_bind
                              (match decide
                                       (or_dec
                                         (not_dec
                                           (decide_rel status_eq_dec
                                             d2.n_status previous_status))
                                         (not_dec
                                           (decide_rel
                                             (option_eq_dec Coq_Z.eq_dec)
                                             d2.n_failure previous_failure))) with
                               | Left -> m_return True
                               | Right ->
                                 (match d2.n_comparer with
                                  | CmpEq ->
                                    m_return
                                      (bool_decide
                                        (not_dec
                                          (decide_rel Coq_Z.eq_dec d2.n_value
                                            previous)))
                                  | CmpNever -> m_return True
                                  | CmpThrows y ->
                                    m_bind
                                      (modify_node n0 (fun x ->
                                        node_with_status StError
                                          (node_with_failure (Some y)
                                            (node_with_value previous x))))
                                      (fun _ -> m_return True)))
                              (fun moved ->
                              m_bind m_get (fun s ->
                                m_bind
                                  (m_modify
                                    (ghost_run_end n0 s.g_current moved))
                                  (fun _ ->
                                  m_bind
                                    (match moved with
                                     | True ->
                                       m_step
                                         (notify_dirty_except f n0
                                           s.g_current)
                                     | False -> m_return Tt)
                                    (fun _ ->
                                    m_bind (get_node n0) (fun d3 ->
                                      match decide
                                              (decide_rel freshness_eq_dec
                                                d3.n_freshness Clean) with
                                      | Left -> m_return Tt
                                      | Right -> oannounce n0)))))))))))))))))

(** val oexecute_effect : nat -> node_id -> unit0 m **)

and oexecute_effect fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_node n0) (fun d ->
      match d.n_disposed with
      | True -> modify_node n0 (node_with_queued False)
      | False ->
        m_bind
          (match decide (decide_rel freshness_eq_dec d.n_freshness Check) with
           | Left ->
             m_bind (oresolve_walk f n0 O) (fun _ ->
               m_bind (get_node n0) (fun d' ->
                 match decide
                         (decide_rel freshness_eq_dec d'.n_freshness Check) with
                 | Left ->
                   m_bind (m_modify (ghost_resolved n0)) (fun _ ->
                     modify_node n0 (node_with_freshness Clean))
                 | Right -> m_return Tt))
           | Right -> m_return Tt)
          (fun _ ->
          m_bind (get_node n0) (fun d' ->
            m_bind
              (modify_node_adv A4807 n0 (fun x ->
                node_with_freshness Clean (node_with_queued False x)))
              (fun _ ->
              match decide (decide_rel freshness_eq_dec d'.n_freshness Dirty) with
              | Left -> orun_effect f n0
              | Right -> m_return Tt))))

(** val orun_effect : nat -> node_id -> unit0 m **)

and orun_effect fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (push_running n0) (fun _ ->
      m_bind (edges (sources_begin_run n0)) (fun _ ->
        m_bind (m_modify (ghost_effect_start n0)) (fun _ ->
          m_bind (get_node n0) (fun d ->
            m_bind
              (match d.n_scope with
               | Some o ->
                 m_bind (modify_node n0 (node_with_in_cleanup True))
                   (fun _ ->
                   m_bind (odischarge f o) (fun _ ->
                     modify_node_adv A4825 n0 (node_with_in_cleanup False)))
               | None -> m_return Tt)
              (fun _ ->
              m_bind (get_node n0) (fun d' ->
                m_bind
                  (match d'.n_disposed with
                   | True -> m_return Tt
                   | False -> orun_effect_body f n0)
                  (fun _ -> pop_running)))))))

(** val orun_effect_body : nat -> node_id -> unit0 m **)

and orun_effect_body fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_node n0) (fun d ->
      m_bind
        (modify_node n0 (fun x ->
          node_with_runs (S x.n_runs)
            (node_with_failure None (node_with_status StNone x))))
        (fun _ ->
        m_bind (emit (EvRun n0)) (fun _ ->
          m_bind (hosted n0 (oexec f d.n_env d.n_body)) (fun r ->
            m_bind (get_node n0) (fun d' ->
              m_bind
                (edges_opt
                  (match d'.n_disposed with
                   | True -> sources_clear n0
                   | False -> sources_end_run n0))
                (fun _ ->
                match r with
                | Ok _ -> m_return Tt
                | Fail y ->
                  m_bind
                    (modify_node n0 (fun x ->
                      node_with_status StError (node_with_failure (Some y) x)))
                    (fun _ -> emit (EvFail (n0, y)))))))))

(** val odischarge : nat -> owner_id -> unit0 m **)

and odischarge fuel o =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (detached o (odispose_scope f o)) (fun _ ->
      m_bind m_get (fun s ->
        match match match bool_decide
                            (decide_rel Coq_Nat.eq_dec s.g_batch_depth O) with
                    | True -> negb s.g_flushing
                    | False -> False with
              | True -> live_queue s
              | False -> False with
        | True -> m_modify (with_flush_owed True)
        | False -> m_return Tt))

(** val oretire : nat -> owner_id -> unit0 m **)

and oretire fuel o =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind
      (detached o
        (m_bind (get_owner o) (fun x ->
          match x.o_disposed with
          | True -> m_return Tt
          | False ->
            m_bind (put_owner o (owner_retired x)) (fun _ ->
              odispose_scope f o))))
      (fun _ ->
      m_bind m_get (fun s ->
        match live_queue s with
        | True -> orequest_flush f
        | False -> m_return Tt))

(** val odispose_scope : nat -> owner_id -> unit0 m **)

and odispose_scope fuel o =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_owner o) (fun x ->
      m_bind (put_owner o (owner_detached x)) (fun _ ->
        oteardown f o (rev x.o_children) (owner_cleanups x)))

(** val oteardown :
    nat -> owner_id -> link list -> ((cmd, env) prod, nat) prod list -> unit0
    m **)

and oteardown fuel o ls cs =
  match fuel with
  | O -> stuck
  | S f ->
    (match ls with
     | Nil ->
       (match cs with
        | Nil -> m_return Tt
        | Cons (p, cs') ->
          let Pair (p0, _) = p in
          let Pair (b, e) = p0 in
          m_bind (orun_cleanup f o b e) (fun _ -> oteardown f o Nil cs'))
     | Cons (l, ls') ->
       (match cs with
        | Nil ->
          m_bind (orelease f l.l_child) (fun _ -> oteardown f o ls' Nil)
        | Cons (p, cs') ->
          let Pair (p0, a) = p in
          let Pair (b, e) = p0 in
          (match decide (decide_rel Coq_Nat.lt_dec a l.l_acquired) with
           | Left ->
             m_bind (orelease f l.l_child) (fun _ -> oteardown f o ls' cs)
           | Right ->
             m_bind (orun_cleanup f o b e) (fun _ -> oteardown f o ls cs'))))

(** val orun_cleanup : nat -> owner_id -> cmd -> env -> unit0 m **)

and orun_cleanup fuel o b e =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_owner o) (fun x ->
      m_bind (emit (EvCleanup x.o_host)) (fun _ ->
        m_bind (oexec f e b) (fun r ->
          match r with
          | Ok _ -> m_return Tt
          | Fail y ->
            m_bind (record_error o y) (fun _ -> emit (EvFail (x.o_host, y))))))

(** val orelease : nat -> node_id -> unit0 m **)

and orelease fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (modify_node n0 (node_with_link None)) (fun _ ->
      odispose_node f n0)

(** val odispose_node : nat -> node_id -> unit0 m **)

and odispose_node fuel n0 =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_node n0) (fun d ->
      match d.n_disposed with
      | True -> m_return Tt
      | False ->
        m_bind
          (modify_node n0 (fun x ->
            node_with_link None (node_with_disposed True x)))
          (fun _ ->
          m_bind
            (match d.n_link with
             | Some l -> unlink l
             | None -> m_return Tt)
            (fun _ ->
            m_bind (edges_opt (sources_clear n0)) (fun _ ->
              match d.n_scope with
              | Some o -> oretire f o
              | None -> m_return Tt))))

(** val oattach : nat -> node_id -> owner_id -> unit0 m **)

and oattach fuel n0 o =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_owner o) (fun x ->
      match x.o_disposed with
      | True ->
        m_bind (orelease f n0) (fun _ ->
          modify_node n0
            (node_with_link (Some { l_child = n0; l_owner = o; l_generation =
              None; l_acquired = O })))
      | False ->
        let l = { l_child = n0; l_owner = o; l_generation = (Some
          x.o_generation); l_acquired = x.o_acquisitions }
        in
        m_bind (put_owner o (owner_with_child l x)) (fun _ ->
          modify_node n0 (node_with_link (Some l))))

(** val owrite_signal : nat -> node_id -> val0 -> unit0 m **)

and owrite_signal fuel n0 v =
  match fuel with
  | O -> stuck
  | S f ->
    m_bind (get_node n0) (fun d ->
      match d.n_kind with
      | KSignal ->
        (match decide (decide_rel Coq_Z.eq_dec d.n_value v) with
         | Left -> m_return Tt
         | Right ->
           m_bind (modify_node n0 (node_with_value v)) (fun _ ->
             m_bind (m_modify (ghost_publish n0)) (fun _ ->
               m_bind (m_step (notify_write f n0)) (fun _ -> orequest_flush f))))
      | _ -> stuck)

(** val orequest_flush : nat -> unit0 m **)

and orequest_flush = function
| O -> stuck
| S f ->
  m_bind m_get (fun s ->
    match match live_queue s with
          | True -> bool_decide (decide_rel Coq_Nat.eq_dec s.g_batch_depth O)
          | False -> False with
    | True ->
      (match outside_bodies s with
       | True -> orun_flush f
       | False ->
         (match s.g_flushing with
          | True -> m_return Tt
          | False -> m_modify (with_flush_owed True)))
    | False -> m_return Tt)

(** val osettle_flush : nat -> unit0 m **)

and osettle_flush = function
| O -> stuck
| S f ->
  m_bind m_get (fun s ->
    match match match s.g_flush_owed with
                | True ->
                  bool_decide (decide_rel Coq_Nat.eq_dec s.g_batch_depth O)
                | False -> False with
          | True -> outside_bodies s
          | False -> False with
    | True -> orun_flush f
    | False -> m_return Tt)

(** val orun_flush : nat -> unit0 m **)

and orun_flush = function
| O -> stuck
| S f ->
  m_bind m_get (fun s ->
    match s.g_flushing with
    | True -> m_return Tt
    | False ->
      m_bind
        (m_modify (fun s0 -> with_flush_owed False (with_flushing True s0)))
        (fun _ -> m_bind (odrain f) (fun _ -> m_modify (with_flushing False))))

(** val odrain : nat -> unit0 m **)

and odrain = function
| O -> stuck
| S f ->
  m_bind m_get (fun s ->
    match lookup0 Coq_list.list_lookup s.g_queue_head s.g_queue with
    | Some n0 ->
      m_bind (m_modify (with_queue_head (S s.g_queue_head))) (fun _ ->
        m_bind (oexecute_effect f n0) (fun _ -> odrain f))
    | None -> m_modify (fun s0 -> with_queue Nil (with_queue_head O s0)))

type node_view = { v_id : node_id; v_kind : kind; v_value : val0;
                   v_status : status; v_failure : val0 option;
                   v_freshness : freshness; v_unannounced : bool;
                   v_runs : nat; v_disposed : bool; v_queued : bool;
                   v_sources : nat; v_observers : nat }

type state_view = { v_nodes : node_view list; v_queue : nat; v_errors : nat }

(** val view_node : state -> node_id -> node -> node_view **)

let view_node s n0 d =
  { v_id = n0; v_kind = d.n_kind; v_value = d.n_value; v_status = d.n_status;
    v_failure = d.n_failure; v_freshness = d.n_freshness; v_unannounced =
    d.n_unannounced; v_runs = d.n_runs; v_disposed = d.n_disposed; v_queued =
    d.n_queued; v_sources = (length (sources_of s.g_edges n0).source_slots);
    v_observers = (length (observers_of s.g_edges n0).observer_items) }

(** val node_ids : state -> node_id list **)

let node_ids s =
  seq (S O) s.g_next_id

(** val view_of : state -> state_view **)

let view_of s =
  { v_nodes =
    (omap (Obj.magic (fun _ _ -> Coq0_list.list_omap)) (fun n0 ->
      fmap (Obj.magic (fun _ _ -> option_fmap)) (view_node s n0)
        (lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) n0 s.g_nodes))
      (Obj.magic node_ids s));
    v_queue = (length (pending s)); v_errors =
    (from_option (Obj.magic id) O
      (fmap (Obj.magic (fun _ _ -> option_fmap))
        (compose length (fun o -> o.o_errors))
        (lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) root_owner_id
          s.g_owners))) }

(** val memo_ids : state -> node_id list **)

let memo_ids s =
  omap (Obj.magic (fun _ _ -> Coq0_list.list_omap)) (fun n0 ->
    match fmap (Obj.magic (fun _ _ -> option_fmap)) (fun n1 -> n1.n_kind)
            (lookup0 (gmap_lookup Coq_Nat.eq_dec nat_countable) n0 s.g_nodes) with
    | Some y ->
      (match Obj.magic y with
       | KSignal -> None
       | KMemo -> Some n0
       | KEffect -> None)
    | None -> None) (node_ids s)

(** val closing : nat -> unit0 m **)

let closing fuel s =
  match fold_left (fun m0 n0 ->
          m_bind m0 (fun _ ->
            m_bind
              (exec fuel Nil (CTryRead (O, (S O), (EConst (Z.of_nat n0)),
                (CRet (EConst Z0)))))
              (fun _ -> m_return Tt)))
          (memo_ids s) (m_return Tt) s with
  | Some p -> let Pair (s', _) = p in run_flush fuel s'
  | None -> None

(** val observed :
    nat -> flags -> cmd -> ((event list, state_view) prod, state_view) prod
    option **)

let observed fuel fl c =
  match run_top fuel c (init fl) with
  | Some p ->
    let Pair (s, _) = p in
    (match closing fuel s with
     | Some p0 ->
       let Pair (s', _) = p0 in
       Some (Pair ((Pair (s'.g_trace, (view_of s))), (view_of s')))
     | None -> None)
  | None -> None

(** val oclosing : nat -> unit0 m **)

let oclosing fuel s =
  match fold_left (fun m0 n0 ->
          m_bind m0 (fun _ ->
            m_bind
              (oexec fuel Nil (CTryRead (O, (S O), (EConst (Z.of_nat n0)),
                (CRet (EConst Z0)))))
              (fun _ -> m_return Tt)))
          (memo_ids s) (m_return Tt) s with
  | Some p -> let Pair (s', _) = p in orun_flush fuel s'
  | None -> None

(** val observed_mode :
    nat -> flags -> skip_mode -> cmd -> ((event list, state_view) prod,
    state_view) prod option **)

let observed_mode fuel fl m0 c =
  match oexec fuel Nil c (with_skip m0 (init fl)) with
  | Some p ->
    let Pair (s, _) = p in
    (match oclosing fuel s with
     | Some p0 ->
       let Pair (s', _) = p0 in
       Some (Pair ((Pair (s'.g_trace, (view_of s))), (view_of s')))
     | None -> None)
  | None -> None
