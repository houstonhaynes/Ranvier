// Compares two assemblies method by method: names, signatures and IL, with each ldstr operand
// replaced by its string and the argument prefix stripped from that string (source roots).
open System
open System.IO
open System.Reflection
open System.Reflection.Emit
open System.Reflection.Metadata
open System.Reflection.Metadata.Ecma335
open System.Reflection.PortableExecutable

let opcodes =
    typeof<OpCodes>.GetFields(BindingFlags.Public ||| BindingFlags.Static)
    |> Array.map (fun f -> f.GetValue null :?> OpCode)
    |> Array.map (fun o -> uint16 o.Value, o)
    |> dict

let operandSize (o: OpCode) (il: byte[]) (at: int) =
    match o.OperandType with
    | OperandType.InlineNone -> 0
    | OperandType.ShortInlineBrTarget
    | OperandType.ShortInlineI
    | OperandType.ShortInlineVar -> 1
    | OperandType.InlineVar -> 2
    | OperandType.InlineI8
    | OperandType.InlineR -> 8
    | OperandType.InlineSwitch -> 4 + 4 * BitConverter.ToInt32 (il, at)
    | _ -> 4

let summary (path: string) (root: string) =
    use stream = File.OpenRead path
    use pe = new PEReader (stream)
    let md = pe.GetMetadataReader ()

    let name (h: StringHandle) =
        md.GetString h

    let normalise (il: byte[]) =
        let out = Text.StringBuilder ()
        let mutable i = 0

        while i < il.Length do
            let code, width =
                if il[i] = 0xFEuy then
                    0xFE00us ||| uint16 il[i + 1], 2
                else
                    uint16 il[i], 1

            let o = opcodes[code]
            let at = i + width
            let size = operandSize o il at

            if o = OpCodes.Ldstr then
                let token = BitConverter.ToInt32 (il, at)
                let text = md.GetUserString (MetadataTokens.UserStringHandle (token &&& 0xFFFFFF))

                out.Append(o.Name).Append(" \"").Append(text.Replace (root, "<root>")).Append("\" ")
                |> ignore
            else
                out.Append(o.Name).Append(' ').Append(Convert.ToHexString (il, at, size)).Append(' ')
                |> ignore

            i <- at + size

        out.ToString ()

    [
        for th in md.TypeDefinitions do
            let t = md.GetTypeDefinition th
            let tn = name t.Namespace + "." + name t.Name

            for mh in t.GetMethods () do
                let m = md.GetMethodDefinition mh

                let body =
                    if m.RelativeVirtualAddress = 0 then
                        ""
                    else
                        normalise ((pe.GetMethodBody m.RelativeVirtualAddress).GetILBytes())

                tn
                + "::"
                + name m.Name
                + " "
                + Convert.ToBase64String (md.GetBlobBytes m.Signature),
                body
    ]

let a = summary fsi.CommandLineArgs[1] fsi.CommandLineArgs[2]
let b = summary fsi.CommandLineArgs[3] fsi.CommandLineArgs[4]
let diff = List.zip a b |> List.filter (fun (x, y) -> x <> y)
printfn "methods: %d vs %d; differing after ldstr resolution: %d" a.Length b.Length (if a.Length = b.Length then diff.Length else -1)

for (x, _) in diff |> List.truncate 10 do
    printfn "  %s" (fst x)
