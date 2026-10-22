/// <summary>The texts of the Arms.Match diagnostics, shared by the runtime and the Fable plugin.</summary>
module internal Ranvier.View.Arms.Diagnostics

[<Literal>]
let reflection =
    "Arms.Match lowers a quotation through reflection and System.Linq.Expressions. Use matchWith with armOf in trimmed or NativeAOT apps."

/// <summary>AM001.</summary>
let outsideReader (variable: string) =
    "Arms.Match: '"
    + variable
    + "' is read outside a reader of the instance. Read it inside a lambda over the instance, such as (fun _ -> "
    + variable
    + ")."

/// <summary>AM001b: the compiler replaced the pattern variable by a read of the case field.</summary>
let fieldOutsideReader (field: string) (case: string) =
    "Arms.Match: the field '"
    + field
    + "' of '"
    + case
    + "' is read outside a reader of the instance. Read it inside a lambda over the instance, such as (fun _ -> "
    + field
    + ")."

/// <summary>AM002.</summary>
let otherLambda (variable: string) (lambda: string) (instance: string) =
    "Arms.Match: '"
    + variable
    + "' is read inside a lambda over '"
    + lambda
    + "', which is not the instance type '"
    + instance
    + "'. Pass the value to the nested view through its items or props."

/// <summary>AM003.</summary>
[<Literal>]
let notLiteral =
    "Arms.Match takes a function literal as its second argument, such as (function | Case x -> ...)."

/// <summary>AM004.</summary>
let notObject (instance: string) =
    "Arms.Match: the instance type '"
    + instance
    + "' is not an object type. An arm site keys each arm's payload by its instance; use a record, class, union or function instance."

/// <summary>AM005.</summary>
[<Literal>]
let noPlugin =
    "Arms.Match needs the Ranvier.View.Arms Fable plugin; import Ranvier.View.Arms.targets in the project compiled by Fable."

/// <summary>AM006.</summary>
[<Literal>]
let noArm =
    "Arms.Match: no arm of this site is mounted for the instance this reader received. A lambda over the instance type inside a case body runs with the arm's instance."

/// <summary>The AM006 text of a Release Fable build: the code and the docs entry holding the full text.</summary>
[<Literal>]
let noArmCode = "Ranvier AM006: shayanhabibi.github.io/Ranvier/view/arms/#am006"

/// <summary>AM007.</summary>
[<Literal>]
let unsupported = "Arms.Match: this pattern form is not supported."

/// <summary>A call with no source location; the plugin derives the names of a site's emitted locals from it.</summary>
[<Literal>]
let noRange =
    "Arms.Match: the call has no source location; the plugin derives the names of a site's emitted locals from it."

/// <summary>The F# abbreviation of a .NET numeric type name (<c>SByte</c> is <c>sbyte</c>), or the name itself.</summary>
let numberName (dotnetName: string) =
    match dotnetName with
    | "SByte" -> "sbyte"
    | "Byte" -> "byte"
    | "Int16" -> "int16"
    | "UInt16" -> "uint16"
    | "Int32" -> "int"
    | "UInt32" -> "uint32"
    | "Int64" -> "int64"
    | "UInt64" -> "uint64"
    | "IntPtr" -> "nativeint"
    | "UIntPtr" -> "unativeint"
    | "BigInteger" -> "bigint"
    | "Single" -> "float32"
    | "Double" -> "float"
    | "Decimal" -> "decimal"
    | other -> other
