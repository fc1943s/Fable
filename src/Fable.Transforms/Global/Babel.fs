namespace rec Fable.AST.Babel

open Fable.AST
open PrinterExtensions

type Printer =
    abstract Line: int
    abstract Column: int
    abstract PushIndentation: unit -> unit
    abstract PopIndentation: unit -> unit
    abstract Print: string * ?loc: SourceLocation -> unit
    abstract PrintNewLine: unit -> unit
    abstract AddLocation: SourceLocation option -> unit
    abstract EscapeJsStringLiteral: string -> string
    abstract MakeImportPath: string -> string

module PrinterExtensions =
    type Printer with
        member printer.Print(node: #IPrinter) =
            node.Print(printer)

        member printer.PrintBlock(nodes: 'a array, printNode: Printer -> 'a -> unit, printSeparator: Printer -> unit, ?skipNewLineAtEnd) =
            let skipNewLineAtEnd = defaultArg skipNewLineAtEnd false
            printer.Print("{")
            printer.PrintNewLine()
            printer.PushIndentation()
            for node in nodes do
                printNode printer node
                printSeparator printer
            printer.PopIndentation()
            printer.Print("}")
            if not skipNewLineAtEnd then
                printer.PrintNewLine()

        member printer.PrintStatementSeparator() =
            if printer.Column > 0 then
                printer.Print(";")
                printer.PrintNewLine()

        member _.IsProductiveStatement(s: Statement) =
            let rec hasNoSideEffects (e: Expression) =
                match e with
                | Undefined(_)
                | Literal(NullLiteral(_))
                | Literal(StringLiteral(_))
                | Literal(BooleanLiteral(_))
                | Literal(NumericLiteral(_)) -> true
                // Constructors of classes deriving from System.Object add an empty object at the end
                | ObjectExpression(expr) -> expr.Properties.Length = 0
                | UnaryExpression(expr) when expr.Operator = "void" -> hasNoSideEffects expr.Argument
                // Some identifiers may be stranded as the result of imports
                // intended only for side effects, see #2228
                | Identifier(_) -> true
                | _ -> false

            match s with
            | ExpressionStatement(stmt) -> hasNoSideEffects stmt.Expression |> not
            | _ -> true

        member printer.PrintProductiveStatement(s: Statement, ?printSeparator) =
            if printer.IsProductiveStatement(s) then
                printer.Print(s)
                printSeparator |> Option.iter (fun f -> f printer)

        member printer.PrintProductiveStatements(statements: Statement[]) =
            for s in statements do
                printer.PrintProductiveStatement(s, (fun p -> p.PrintStatementSeparator()))

        member printer.PrintBlock(nodes: Statement array, ?skipNewLineAtEnd) =
            printer.PrintBlock(nodes,
                               (fun p s -> p.PrintProductiveStatement(s)),
                               (fun p -> p.PrintStatementSeparator()),
                               ?skipNewLineAtEnd=skipNewLineAtEnd)

        member printer.PrintOptional(before: string, node: #IPrinter option) =
            match node with
            | None -> ()
            | Some node ->
                printer.Print(before)
                printer.Print(node)

        member printer.PrintOptional(node: #IPrinter option) =
            match node with
            | None -> ()
            | Some node -> printer.Print(node)

        member printer.PrintArray(nodes: 'a array, printNode: Printer -> 'a -> unit, printSeparator: Printer -> unit) =
            for i = 0 to nodes.Length - 1 do
                printNode printer nodes.[i]
                if i < nodes.Length - 1 then
                    printSeparator printer

        member printer.PrintCommaSeparatedArray(nodes: Expression array) =
            printer.PrintArray(nodes, (fun p x -> p.SequenceExpressionWithParens(x)), (fun p -> p.Print(", ")))

        member printer.PrintCommaSeparatedArray(nodes: #IPrinter array) =
            printer.PrintArray(nodes, (fun p x -> p.Print(x)), (fun p -> p.Print(", ")))

        // TODO: (super) type parameters, implements
        member printer.PrintClass(id: Identifier option, superClass: Expression option,
                superTypeParameters: TypeParameterInstantiation option,
                typeParameters: TypeParameterDeclaration option,
                implements: ClassImplements array option, body: ClassBody, loc) =
            printer.Print("class", ?loc=loc)
            printer.PrintOptional(" ", id)
            printer.PrintOptional(typeParameters)
            match superClass with
            | Some (Identifier(id)) when id.TypeAnnotation.IsSome ->
                printer.Print(" extends ");
                printer.Print(id.TypeAnnotation.Value.TypeAnnotation)
            | _ -> printer.PrintOptional(" extends ", superClass)
            // printer.PrintOptional(superTypeParameters)
            match implements with
            | Some implements when not (Array.isEmpty implements) ->
                printer.Print(" implements ")
                printer.PrintArray(implements, (fun p x -> p.Print(x)), (fun p -> p.Print(", ")))
            | _ -> ()
            printer.Print(" ")
            printer.Print(body)

        member printer.PrintFunction(id: Identifier option, parameters: Pattern array, body: BlockStatement,
                typeParameters: TypeParameterDeclaration option, returnType: TypeAnnotation option, loc, ?isDeclaration, ?isArrow) =
            let areEqualPassedAndAppliedArgs (passedArgs: Pattern[]) (appliedAgs: Expression[]) =
                Array.zip passedArgs appliedAgs
                |> Array.forall (function
                    | RestElement(p), Identifier(a) -> p.Name = a.Name
                    | _ -> false)

            let isDeclaration = defaultArg isDeclaration false
            let isArrow = defaultArg isArrow false

            printer.AddLocation(loc)

            // Check if we can remove the function
            let skipExpr =
                match body.Body with
                | [| ReturnStatement(r) |] when not isDeclaration ->
                    match r.Argument with
                    | CallExpression(c) when parameters.Length = c.Arguments.Length ->
                        // To be sure we're not running side effects when deleting the function,
                        // check the callee is an identifier (accept non-computed member expressions too?)
                        match c.Callee with
                        | Identifier(id) when areEqualPassedAndAppliedArgs parameters c.Arguments ->
                            Some c.Callee
                        | _ -> None
                    | _ -> None
                | _ -> None

            match skipExpr with
            | Some e -> printer.Print(e)
            | None ->
                if isArrow then
                    // Remove parens if we only have one argument? (and no annotation)
                    printer.PrintOptional(typeParameters)
                    printer.Print("(")
                    printer.PrintCommaSeparatedArray(parameters)
                    printer.Print(")")
                    printer.PrintOptional(returnType)
                    printer.Print(" => ")
                    match body.Body with
                    | [| ReturnStatement(r) |] ->
                        match r.Argument with
                        | ObjectExpression(e) -> printer.WithParens(e)
                        | MemberExpression(e) ->
                            match e.Object with
                            | ObjectExpression(o) -> e.Print(printer, objectWithParens=true)
                            | _ -> e.Print(printer)
                        | _ -> printer.ComplexExpressionWithParens(r.Argument)
                    | _ -> printer.PrintBlock(body.Body, skipNewLineAtEnd=true)
                else
                    printer.Print("function ")
                    printer.PrintOptional(id)
                    printer.PrintOptional(typeParameters)
                    printer.Print("(")
                    printer.PrintCommaSeparatedArray(parameters)
                    printer.Print(")")
                    printer.PrintOptional(returnType)
                    printer.Print(" ")
                    printer.PrintBlock(body.Body, skipNewLineAtEnd=true)

        member printer.WithParens(expr: IPrinter) =
            printer.Print("(")
            printer.Print(expr)
            printer.Print(")")

        member printer.SequenceExpressionWithParens(expr: Expression) =
            match expr with
            | SequenceExpression(_) -> printer.WithParens(expr)
            | _ -> printer.Print(expr)

        /// Surround with parens anything that can potentially conflict with operator precedence
        member printer.ComplexExpressionWithParens(expr: Expression) =
            match expr with
            | Undefined(_)
            | Literal(NullLiteral(_))
            | Literal(StringLiteral(_))
            | Literal(BooleanLiteral(_))
            | Literal(NumericLiteral(_))
            | Identifier(_)
            | MemberExpression(_)
            | CallExpression(_)
            | ThisExpression(_)
            | Super(_)
            | SpreadElement(_)
            | ArrayExpression(_)
            | ObjectExpression(_) -> printer.Print(expr)
            | _ -> printer.WithParens(expr)

        member printer.PrintOperation(left, operator, right, loc) =
            printer.AddLocation(loc)
            printer.ComplexExpressionWithParens(left)
            printer.Print(" " + operator + " ")
            printer.ComplexExpressionWithParens(right)

/// The type field is a string representing the AST variant type.
/// Each subtype of Node is documented below with the specific string of its type field.
/// You can use this field to determine which interface a node implements.
/// The loc field represents the source location information of the node.
/// If the node contains no information about the source location, the field is null;
/// otherwise it is an object consisting of a start position (the position of the first character of the parsed source region)
/// and an end position (the position of the first character after the parsed source region):
type IPrinter =
    abstract Print: Printer -> unit

type Node =
    | Pattern of Pattern
    | Program of Program
    | Statement of Statement
    | Directive of Directive
    | ClassBody of ClassBody
    | Expression of Expression
    | SwitchCase of SwitchCase
    | CatchClause of CatchClause
    | ObjectMember of ObjectMember
    | TypeParameter of TypeParameter
    | TypeAnnotation of TypeAnnotation
    | ExportSpecifier of ExportSpecifier
    | ImportSpecifier of ImportSpecifier
    | InterfaceExtends of InterfaceExtends
    | ObjectTypeIndexer of ObjectTypeIndexer
    | FunctionTypeParam of FunctionTypeParam
    | ModuleDeclaration of ModuleDeclaration
    | VariableDeclarator of VariableDeclarator
    | TypeAnnotationInfo of TypeAnnotationInfo
    | ObjectTypeProperty of ObjectTypeProperty
    | ObjectTypeCallProperty of ObjectTypeCallProperty
    | ObjectTypeInternalSlot of ObjectTypeInternalSlot
    | TypeParameterDeclaration of TypeParameterDeclaration
    | TypeParameterInstantiation of TypeParameterInstantiation

    interface IPrinter with
        member this.Print(printer) =
            failwith "Not implemented"

/// Since the left-hand side of an assignment may be any expression in general, an expression can also be a pattern.
type Expression =
    | Super of Super
    | Literal of Literal
    | Undefined of Undefined
    | Identifier of Identifier
    | NewExpression of NewExpression
    | SpreadElement of SpreadElement
    | ThisExpression of ThisExpression
    | CallExpression of CallExpression
    | EmitExpression of EmitExpression
    | ArrayExpression of ArrayExpression
    | ClassExpression of ClassExpression
    | ClassImplements of ClassImplements
    | UnaryExpression of UnaryExpression
    | UpdateExpression of UpdateExpression
    | ObjectExpression of ObjectExpression
    | BinaryExpression of BinaryExpression
    | MemberExpression of MemberExpression
    | LogicalExpression of LogicalExpression
    | SequenceExpression of SequenceExpression
    | FunctionExpression of FunctionExpression
    | AssignmentExpression of AssignmentExpression
    | ConditionalExpression of ConditionalExpression
    | ArrowFunctionExpression of ArrowFunctionExpression

    interface IPrinter with
        member this.Print(printer) =
            failwith "Not implemented"

type Pattern =
    | RestElement of RestElement

    interface IPrinter with
        member this.Print(printer) =
            failwith "Not implemented"

type Literal =
    | RegExp of RegExpLiteral
    | NullLiteral of NullLiteral
    | StringLiteral of StringLiteral
    | BooleanLiteral of BooleanLiteral
    | NumericLiteral of NumericLiteral
    | DirectiveLiteral of DirectiveLiteral

    interface IPrinter with
        member this.Print(printer) =
            failwith "Not implemented"

type Statement =
    | Declaration of Declaration
    | ExpressionStatement of ExpressionStatement
    | BlockStatement of BlockStatement
    | DebuggerStatement of DebuggerStatement
    | LabeledStatement of LabeledStatement
    | BreakStatement of BreakStatement
    | ContinueStatement of ContinueStatement
    | ReturnStatement of ReturnStatement
    | IfStatement of IfStatement
    | SwitchStatement of SwitchStatement
    | TryStatement of TryStatement
    | WhileStatement of WhileStatement
    | ForStatement of ForStatement
    | ThrowStatement of ThrowStatement

    interface IPrinter with
        member this.Print(printer) =
            failwith "Not implemented"

/// Note that declarations are considered statements; this is because declarations can appear in any statement context.
type Declaration =
    | VariableDeclaration of VariableDeclaration
    | FunctionDeclaration of FunctionDeclaration
    | ClassDeclaration of ClassDeclaration
    | InterfaceDeclaration of InterfaceDeclaration

    interface IPrinter with
        member this.Print(printer) =
            failwith "Not implemented"

/// A module import or export declaration.
type ModuleDeclaration =
    | PrivateModuleDeclaration of PrivateModuleDeclaration
    | ImportDeclaration of ImportDeclaration
    | ExportNamedDeclaration of ExportNamedDeclaration
    | ExportDefaultDeclaration of ExportDefaultDeclaration
    | ExportAllDeclaration of ExportAllDeclaration
    | ExportNamedReferences of ExportNamedReferences

    interface IPrinter with
        member this.Print(printer) =
            failwith "Not implemented"

/// Not in Babel specs
type EmitExpression =
    {
        Value: string
        Args: Expression array
        Loc: SourceLocation option
    }

    static member Create(value, args, ?loc) : Expression =
        {
            Value = value
            Args = args
            Loc = loc
        } |> EmitExpression

    interface IPrinter with
        member this.Print(printer) =
            printer.AddLocation(this.Loc)

            let inline replace pattern (f: System.Text.RegularExpressions.Match -> string) input =
                System.Text.RegularExpressions.Regex.Replace(input, pattern, f)

            let printSegment (printer: Printer) (value: string) segmentStart segmentEnd =
                let segmentLength = segmentEnd - segmentStart
                if segmentLength > 0 then
                    let segment = value.Substring(segmentStart, segmentLength)
                    let subSegments = System.Text.RegularExpressions.Regex.Split(segment, @"\r?\n")
                    for i = 1 to subSegments.Length do
                        let subSegment =
                            // Remove whitespace in front of new lines,
                            // indent will be automatically applied
                            if printer.Column = 0 then subSegments.[i - 1].TrimStart()
                            else subSegments.[i - 1]
                        if subSegment.Length > 0 then
                            printer.Print(subSegment)
                            if i < subSegments.Length then
                                printer.PrintNewLine()

            // Macro transformations
            // https://fable.io/docs/communicate/js-from-fable.html#Emit-when-F-is-not-enough
            let value =
                this.Value
                |> replace @"\$(\d+)\.\.\." (fun m ->
                    let rep = ResizeArray()
                    let i = int m.Groups.[1].Value
                    for j = i to this.Args.Length - 1 do
                        rep.Add("$" + string j)
                    String.concat ", " rep)

                |> replace @"\{\{\s*\$(\d+)\s*\?(.*?)\:(.*?)\}\}" (fun m ->
                    let i = int m.Groups.[1].Value
                    match this.Args.[i] with
                    | Literal(BooleanLiteral(b)) when b.Value -> m.Groups.[2].Value
                    | _ -> m.Groups.[3].Value)

                |> replace @"\{\{([^\}]*\$(\d+).*?)\}\}" (fun m ->
                    let i = int m.Groups.[2].Value
                    match Array.tryItem i this.Args with
                    | Some _ -> m.Groups.[1].Value
                    | None -> "")

                // This is to emit string literals as JS, I think it's no really
                // used and it shouldn't be necessary with the new emitJsExpr
    //            |> replace @"\$(\d+)!" (fun m ->
    //                let i = int m.Groups.[1].Value
    //                match Array.tryItem i args with
    //                | Some(:? StringLiteral as s) -> s.Value
    //                | _ -> "")

            let matches = System.Text.RegularExpressions.Regex.Matches(value, @"\$\d+")
            if matches.Count > 0 then
                for i = 0 to matches.Count - 1 do
                    let m = matches.[i]

                    let segmentStart =
                        if i > 0 then matches.[i-1].Index + matches.[i-1].Length
                        else 0

                    printSegment printer value segmentStart m.Index

                    let argIndex = int m.Value.[1..]
                    match Array.tryItem argIndex this.Args with
                    | Some e -> printer.ComplexExpressionWithParens(e)
                    | None -> printer.Print("undefined")

                let lastMatch = matches.[matches.Count - 1]
                printSegment printer value (lastMatch.Index + lastMatch.Length) value.Length
            else
                printSegment printer value 0 value.Length

// Template Literals
//type TemplateElement(value: string, tail, ?loc) =
//    inherit Node("TemplateElement", ?loc = loc)
//    member _.Tail: bool = tail
//    member _.Value = dict [ ("raw", value); ("cooked", value) ]
//
//type TemplateLiteral(quasis, expressions, ?loc) =
//    inherit Literal("TemplateLiteral", ?loc = loc)
//    member _.Quasis: TemplateElement array = quasis
//    member _.Expressions: Expression array = expressions
//
//type TaggedTemplateExpression(tag, quasi, ?loc) =
//    interface Expression with
//    member _.Tag: Expression = tag
//    member _.Quasi: TemplateLiteral = quasi

// Identifier
/// Note that an identifier may be an expression or a destructuring pattern.
type Identifier =
    {
        Name: string
        Optional: bool option
        TypeAnnotation: TypeAnnotation option
        Loc: SourceLocation option
    }
    static member Create(name, ?optional, ?typeAnnotation, ?loc) : Identifier =
        {
            Name = name
            Optional = optional
            TypeAnnotation = typeAnnotation
            Loc = loc
        }
    static member CreateExpression(name, ?optional, ?typeAnnotation, ?loc) : Expression =
        Identifier.Create(name, ?optional=optional, ?typeAnnotation=typeAnnotation, ?loc=loc) |> Identifier

    interface IPrinter with
        member this.Print(printer) =
            printer.Print(this.Name, ?loc=this.Loc)
            if this.Optional = Some true then
                printer.Print("?")
            printer.PrintOptional(this.TypeAnnotation)

// Literals
type RegExpLiteral =
    {
        Pattern: string
        Flags: string
        Loc: SourceLocation option
    }
    static member Create(pattern, flags_, ?loc) : Literal =
        let flags =
            flags_ |> Seq.map (function
                | RegexGlobal -> "g"
                | RegexIgnoreCase -> "i"
                | RegexMultiline -> "m"
                | RegexSticky -> "y") |> Seq.fold (+) ""
        {
            Pattern = pattern
            Flags = flags
            Loc = loc
        } |> RegExp
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("/", ?loc=this.Loc)
            printer.Print(this.Pattern)
            printer.Print("/")
            printer.Print(this.Flags)

type Undefined =
    {
        Loc: SourceLocation option
    }
    static member Create(?loc) : Expression =
        {
            Loc = loc
        } |> Undefined

    // TODO: Use `void 0` instead? Just remove this node?
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("undefined", ?loc=this.Loc)

type NullLiteral =
    {
        Loc: SourceLocation option
    }
    static member Create(?loc) : Literal =
        {
            Loc = loc
        } |> NullLiteral
    static member CreateExpression(?loc) : Expression =
        NullLiteral.Create(?loc=loc) |> Literal
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("null", ?loc=this.Loc)

type StringLiteral =
    {
        Value: string
        Loc: SourceLocation option
    }
    static member CreateLiteral(value, ?loc) : Literal =
        {
            Value = value
            Loc = loc
        } |> StringLiteral
    static member CreateExpression(value, ?loc) : Expression =
        StringLiteral.CreateLiteral(value, ?loc=loc) |> Literal
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("\"", ?loc=this.Loc)
            printer.Print(printer.EscapeJsStringLiteral(this.Value))
            printer.Print("\"")

type BooleanLiteral =
    {
        Value: bool
        Loc: SourceLocation option
    }
    static member Create(value, ?loc) : Literal =
        {
            Value = value
            Loc = loc
        } |> BooleanLiteral
    interface IPrinter with
        member this.Print(printer) =
            printer.Print((if this.Value then "true" else "false"), ?loc=this.Loc)

type NumericLiteral =
    {
        Value: float
        Loc: SourceLocation option
    }
    static member Create(value, ?loc) : Literal =
        {
            Value =value
            Loc = loc
        } |> NumericLiteral
    static member CreateExpression(value, ?loc) : Expression =
        NumericLiteral.Create(value, ?loc=loc) |> Literal
    interface IPrinter with
        member this.Print(printer) =
            let value =
                match this.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) with
                | "∞" -> "Infinity"
                | "-∞" -> "-Infinity"
                | value -> value
            printer.Print(value, ?loc=this.Loc)

// Misc
//type Decorator(value, ?loc) =
//    inherit Node("Decorator", ?loc = loc)
//    member _.Value = value
//
type DirectiveLiteral =
    {
        Value: string
    }
    static member Create(value) =
        {
            Value = value
        }
    interface IPrinter with
        member _.Print(_) = failwith "not implemented"

/// e.g. "use strict";
type Directive =
    {
        Value: DirectiveLiteral
    }
    static member Create(value) : Node =
        {
            Value = value
        } |> Directive
    interface IPrinter with
        member _.Print(_) = failwith "not implemented"

// Program

/// A complete program source tree.
/// Parsers must specify sourceType as "module" if the source has been parsed as an ES6 module.
/// Otherwise, sourceType must be "script".
type Program =
    {
        Body: ModuleDeclaration array
    }
    static member Create(body) = // ?directives_,
        {
            Body = body
        }

//    let sourceType = "module" // Don't use "script"
//    member _.Directives: Directive array = directives
//    member _.SourceType: string = sourceType

// Statements
/// An expression statement, i.e., a statement consisting of a single expression.
type ExpressionStatement =
    {
        Expression: Expression
    }
    static member Create(expression) : Statement =
        {
            Expression = expression
        } |> ExpressionStatement
    interface IPrinter with
        member this.Print(printer) =
            printer.Print(this.Expression)

/// A block statement, i.e., a sequence of statements surrounded by braces.
type BlockStatement =
    {
        Body: Statement array
    }

    static member Create(body) = // ?directives_,
        {
            Body = body
        }
    static member CreateStatement(body) =
        BlockStatement.Create(body) |> BlockStatement
//    let directives = [||] // defaultArg directives_ [||]
//    member _.Directives: Directive array = directives
    interface IPrinter with
        member this.Print(printer) =
            printer.PrintBlock(this.Body)

/// An empty statement, i.e., a solitary semicolon.
//type EmptyStatement(?loc) =
//    inherit Statement("EmptyStatement", ?loc = loc)
//    member _.Print(_) = ()

type DebuggerStatement =
    {
        Loc: SourceLocation option
    }
    static member Create(?loc) : Statement =
        {
            Loc = loc
        } |> DebuggerStatement
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("debugger", ?loc=this.Loc)

/// Statement (typically loop) prefixed with a label (for continue and break)
type LabeledStatement =
    {
        Body: Statement
        Label: Identifier
    }
    static member Create(label, body) : Statement =
        {
            Body = body
            Label = label
        } |> LabeledStatement
    interface IPrinter with
        member this.Print(printer) =
            printer.Print(this.Label)
            printer.Print(":")
            printer.PrintNewLine()
            // Don't push indent
            printer.Print(this.Body)

/// Break can optionally take a label of a loop to break
type BreakStatement =
    {
        Label: Identifier option
        Loc: SourceLocation option
    }
    static member Create(?label, ?loc) : Statement =
        {
            Label = label
            Loc = loc
        } |> BreakStatement

    interface IPrinter with
        member this.Print(printer) =
            printer.Print("break", ?loc=this.Loc)

/// Continue can optionally take a label of a loop to continue
type ContinueStatement =
    {
        Label: Identifier option
        Loc: SourceLocation option
    }
    static member Create(?label, ?loc) : Statement =
        {
            Label = label
            Loc = loc
        } |> ContinueStatement

    interface IPrinter with
        member this.Print(printer) =
            printer.Print("continue", ?loc=this.Loc)
            printer.PrintOptional(" ", this.Label)

// type WithStatement

// Control Flow
type ReturnStatement =
    {
        Argument: Expression
        Loc: SourceLocation option
    }
    static member Create(argument, ?loc) : Statement =
        {
            Argument = argument
            Loc = loc
        } |> ReturnStatement
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("return ", ?loc=this.Loc)
            printer.Print(this.Argument)

type IfStatement =
    {
        Test: Expression
        Consequent: BlockStatement
        Alternate: Statement option
        Loc: SourceLocation option
    }
    static member Create(test, consequent, ?alternate, ?loc) : Statement =
        {
            Test = test
            Consequent = consequent
            Alternate = alternate
            Loc = loc
        } |> IfStatement
    interface IPrinter with
        member this.Print(printer) =
            printer.AddLocation(this.Loc)
            printer.Print("if (", ?loc=this.Loc)
            printer.Print(this.Test)
            printer.Print(") ")
            printer.Print(this.Consequent)
            match this.Alternate with
            | None -> ()
            | Some alternate ->
                if printer.Column > 0 then printer.Print(" ")
                match alternate with
                | IfStatement(iff) ->
                    printer.Print("else ")
                    printer.Print(iff)
                | alternate ->
                    let statements =
                        match alternate with
                        | BlockStatement(b) -> b.Body
                        | alternate -> [|alternate|]
                    // Get productive statements and skip `else` if they're empty
                    statements
                    |> Array.filter printer.IsProductiveStatement
                    |> function
                        | [||] -> ()
                        | statements ->
                            printer.Print("else ")
                            printer.PrintBlock(statements)
            if printer.Column > 0 then
                printer.PrintNewLine()

/// A case (if test is an Expression) or default (if test === null) clause in the body of a switch statement.
type SwitchCase =
    {
        Test: Expression option
        Consequent: Statement array
        Loc: SourceLocation option
    }
    static member Create(consequent, ?test, ?loc) =
        {
            Test = test
            Consequent = consequent
            Loc = loc
        }
    interface IPrinter with
        member this.Print(printer) =
            printer.AddLocation(this.Loc)
            match this.Test with
            | None -> printer.Print("default")
            | Some test ->
                printer.Print("case ")
                printer.Print(test)
            printer.Print(":")
            match this.Consequent.Length with
            | 0 -> printer.PrintNewLine()
            | 1 ->
                printer.Print(" ")
                printer.Print(this.Consequent.[0])
            | _ ->
                printer.Print(" ")
                printer.PrintBlock(this.Consequent)

type SwitchStatement =
    {
        Discriminant: Expression
        Cases: SwitchCase array
        Loc: SourceLocation option
    }
    static member Create(discriminant, cases, ?loc) : Statement =
        {
            Discriminant = discriminant
            Cases = cases
            Loc = loc
        } |> SwitchStatement
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("switch (", ?loc=this.Loc)
            printer.Print(this.Discriminant)
            printer.Print(") ")
            printer.PrintBlock(this.Cases, (fun p x -> p.Print(x)), fun _ -> ())

// Exceptions
type ThrowStatement =
    {
        Argument: Expression
        Loc: SourceLocation option
    }
    static member Create(argument, ?loc) : Statement =
        {
            Argument = argument
            Loc = loc
        } |> ThrowStatement
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("throw ", ?loc=this.Loc)
            printer.Print(this.Argument)

/// A catch clause following a try block.
type CatchClause =
    {
        Param: Pattern
        Body: BlockStatement
        Loc: SourceLocation option
    }
    static member Create(param, body, ?loc) =
        {
            Param = param
            Body = body
            Loc = loc
        }
    interface IPrinter with
        member this.Print(printer) =
            // "catch" is being printed by TryStatement
            printer.Print("(", ?loc=this.Loc)
            printer.Print(this.Param)
            printer.Print(") ")
            printer.Print(this.Body)

/// If handler is null then finalizer must be a BlockStatement.
type TryStatement =
    {
        Block: BlockStatement
        Handler: CatchClause option
        Finalizer: BlockStatement option
        Loc: SourceLocation option
    }
    static member Create(block, ?handler, ?finalizer, ?loc) : Statement =
        {
            Block = block
            Handler = handler
            Finalizer = finalizer
            Loc = loc
        } |> TryStatement
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("try ", ?loc=this.Loc)
            printer.Print(this.Block)
            printer.PrintOptional("catch ", this.Handler)
            printer.PrintOptional("finally ", this.Finalizer)

// Declarations
type VariableDeclarator =
    {
        Id: Pattern
        Init: Expression option
    }
    static member Create(id, ?init)  =
        {
            Id = id
            Init = init
        }


type VariableDeclarationKind = Var | Let | Const

type VariableDeclaration =
    {
        Declarations: VariableDeclarator array
        Kind: string
        Loc: SourceLocation option
    }

    static member Create(kind_, declarations, ?loc) : Declaration =
        let kind = match kind_ with Var -> "var" | Let -> "let" | Const -> "const"
        {
            Declarations = declarations
            Kind = kind
            Loc = loc
        } |> VariableDeclaration
    static member Create(var, ?init, ?kind, ?loc) : Statement =
        VariableDeclaration.Create(defaultArg kind Let, [|VariableDeclarator.Create(var, ?init=init)|], ?loc=loc)
        |> Declaration
    interface IPrinter with
        member this.Print(printer) =
            printer.Print(this.Kind + " ", ?loc=this.Loc)
            let canConflict = this.Declarations.Length > 1
            for i = 0 to this.Declarations.Length - 1 do
                let decl = this.Declarations.[i]
                printer.Print(decl.Id)
                match decl.Init with
                | None -> ()
                | Some e ->
                    printer.Print(" = ")
                    if canConflict then printer.ComplexExpressionWithParens(e)
                    else printer.SequenceExpressionWithParens(e)
                if i < this.Declarations.Length - 1 then
                    printer.Print(", ")

// Loops
type WhileStatement =
    {
        Test: Expression
        Body: BlockStatement
        Loc: SourceLocation option
    }
    static member Create(test, body, ?loc) : Statement =
        {
            Test = test
            Body = body
            Loc = loc
        } |> WhileStatement
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("while (", ?loc=this.Loc)
            printer.Print(this.Test)
            printer.Print(") ")
            printer.Print(this.Body)

//type DoWhileStatement(body, test, ?loc) =
//    inherit Statement("DoWhileStatement", ?loc = loc)
//    member _.Body: BlockStatement = body
//    member _.Test: Expression = test

type ForStatement =
    {
        Body: BlockStatement
        // In JS this can be an expression too
        Init: VariableDeclaration option
        Test: Expression option
        Update: Expression option
        Loc: SourceLocation option
    }
    static member Create(body, ?init, ?test, ?update, ?loc) : Statement =
        {
            Body = body
            // In JS this can be an expression too
            Init = init
            Test = test
            Update = update
            Loc = loc
        } |> ForStatement
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("for (", ?loc=this.Loc)
            printer.PrintOptional(this.Init)
            printer.Print("; ")
            printer.PrintOptional(this.Test)
            printer.Print("; ")
            printer.PrintOptional(this.Update)
            printer.Print(") ")
            printer.Print(this.Body)

/// When passing a VariableDeclaration, the bound value must go through
/// the `right` parameter instead of `init` property in VariableDeclarator
//type ForInStatement(left, right, body, ?loc) =
//    inherit Statement("ForInStatement", ?loc = loc)
//    member _.Body: BlockStatement = body
//    member _.Left: Choice<VariableDeclaration, Expression> = left
//    member _.Right: Expression = right

/// When passing a VariableDeclaration, the bound value must go through
/// the `right` parameter instead of `init` property in VariableDeclarator
//type ForOfStatement(left, right, body, ?loc) =
//    inherit Statement("ForOfStatement", ?loc = loc)
//    member _.Body: BlockStatement = body
//    member _.Left: Choice<VariableDeclaration, Expression> = left
//    member _.Right: Expression = right

/// A function declaration. Note that id cannot be null.
type FunctionDeclaration =
    {
        Params: Pattern array
        Body: BlockStatement
        Id: Identifier
        ReturnType: TypeAnnotation option
        TypeParameters: TypeParameterDeclaration option
        Loc: SourceLocation option
    }
    static member Create(``params``, body, id, ?returnType, ?typeParameters, ?loc) : Declaration = // ?async_, ?generator_, ?declare,
        {
            Params = ``params``
            Body = body
            Id = id
            ReturnType = returnType
            TypeParameters = typeParameters
            Loc = loc
        } |> FunctionDeclaration
//    let async = defaultArg async_ false
//    let generator = defaultArg generator_ false
//    member _.Async: bool = async
//    member _.Generator: bool = generator
//    member _.Declare: bool option = declare
    interface IPrinter with
        member this.Print(printer) =
            printer.PrintFunction(Some this.Id, this.Params, this.Body, this.TypeParameters, this.ReturnType, this.Loc, isDeclaration=true)
            printer.PrintNewLine()

// Expressions

/// A super pseudo-expression.
type Super =
    {
        Loc: SourceLocation option
    }
    static member Create(?loc) : Expression =
        {
            Loc = loc
        } |> Super
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("super", ?loc=this.Loc)

type ThisExpression =
    {
        Loc: SourceLocation option
    }
    static member Create(?loc) : Expression =
        {
            Loc = loc
        } |> ThisExpression

    interface IPrinter with
        member this.Print(printer) =
            printer.Print("this", ?loc=this.Loc)

/// A fat arrow function expression, e.g., let foo = (bar) => { /* body */ }.
type ArrowFunctionExpression =
    {
        Params: Pattern array
        Body: BlockStatement
        ReturnType: TypeAnnotation option
        TypeParameters: TypeParameterDeclaration option
        Loc: SourceLocation option
    }
    static member Create(``params``, body: BlockStatement, ?returnType, ?typeParameters, ?loc) : Expression = //?async_, ?generator_,
        {
            Params = ``params``
            Body = body
            ReturnType = returnType
            TypeParameters = typeParameters
            Loc = loc
        } |> ArrowFunctionExpression
    static member Create(``params``, body: Expression, ?returnType, ?typeParameters, ?loc) : Expression =
        let body = { Body = [|ReturnStatement.Create(body) |] }
        {
            Params = ``params``
            Body = body
            ReturnType = returnType
            TypeParameters = typeParameters
            Loc = loc
        } |> ArrowFunctionExpression

//    let async = defaultArg async_ false
//    let generator = defaultArg generator_ false
//    member _.Async: bool = async
//    member _.Generator: bool = generator
    interface IPrinter with
        member this.Print(printer) =
            printer.PrintFunction(None, this.Params, this.Body, this.TypeParameters, this.ReturnType, this.Loc, isArrow=true)

type FunctionExpression =
    {
        Id: Identifier option
        Params: Pattern array
        Body: BlockStatement
        ReturnType: TypeAnnotation option
        TypeParameters: TypeParameterDeclaration option
        Loc: SourceLocation option
    }
    static member Create(``params``, body, ?id, ?returnType, ?typeParameters, ?loc) : Expression = //?generator_, ?async_
        {
            Id = id
            Params = ``params``
            Body = body
            ReturnType = returnType
            TypeParameters = typeParameters
            Loc = loc
        } |> FunctionExpression
//    let async = defaultArg async_ false
//    let generator = defaultArg generator_ false
//    member _.Async: bool = async
//    member _.Generator: bool = generator
    interface IPrinter with
        member this.Print(printer) =
            printer.PrintFunction(this.Id, this.Params, this.Body, this.TypeParameters, this.ReturnType, this.Loc)

///// e.g., x = do { var t = f(); t * t + 1 };
///// http://wiki.ecmascript.org/doku.php?id=strawman:do_expressions
///// Doesn't seem to work well with block-scoped variables (let, const)
//type DoExpression(body, ?loc) =
//    interface Expression with
//    member _.Body: BlockStatement = body

//type YieldExpression(argument, ``delegate``, ?loc) =
//    interface Expression with
//    member _.Argument: Expression option = argument
//    /// Delegates to another generator? (yield*)
//    member _.Delegate: bool = ``delegate``
//
//type AwaitExpression(argument, ?loc) =
//    interface Expression with
//    member _.Argument: Expression option = argument

//type RestProperty(argument, ?loc) =
//    inherit Node("RestProperty", ?loc = loc)
//    member _.Argument: Expression = argument

///// e.g., var z = { x: 1, ...y } // Copy all properties from y
//type SpreadProperty(argument, ?loc) =
//    inherit Node("SpreadProperty", ?loc = loc)
//    member _.Argument: Expression = argument

// Should derive from Node, but make it an expression for simplicity
type SpreadElement =
    {
        Argument: Expression
        Loc: SourceLocation option
    }
    static member Create(argument, ?loc) : Expression =
        {
            Argument = argument
            Loc = loc
        } |> SpreadElement
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("...", ?loc=this.Loc)
            printer.ComplexExpressionWithParens(this.Argument)

type ArrayExpression =
    {
        // Elements: Choice<Expression, SpreadElement> option array
        Elements: Expression array
        Loc: SourceLocation option
    }
    static member Create(elements, ?loc) : Expression =
        {
            // Elements: Choice<Expression, SpreadElement> option array
            Elements = elements
            Loc = loc
        } |> ArrayExpression
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("[", ?loc=this.Loc)
            printer.PrintCommaSeparatedArray(this.Elements)
            printer.Print("]")

type ObjectMember =
    | ObjectProperty of ObjectProperty
    | ObjectMethod of ObjectMethod

    interface IPrinter with
        member this.Print(printer) =
            match this with
            | ObjectProperty(op) -> printer.Print(op)
            | ObjectMethod(op) -> printer.Print(op)

type ObjectProperty =
    {
        Key: Expression
        Value: Expression
        Computed: bool
    }
    static member Create(key, value, ?computed_) : ObjectMember = // ?shorthand_,
        let computed = defaultArg computed_ false
        {
            Key = key
            Value = value
            Computed = computed
        } |> ObjectProperty
//    let shorthand = defaultArg shorthand_ false
//    member _.Shorthand: bool = shorthand
    interface IPrinter with
        member this.Print(printer) =
            if this.Computed then
                printer.Print("[")
                printer.Print(this.Key)
                printer.Print("]")
            else
                printer.Print(this.Key)
            printer.Print(": ")
            printer.SequenceExpressionWithParens(this.Value)

type ObjectMethodKind = ObjectGetter | ObjectSetter | ObjectMeth

type ObjectMethod =
    {
        Kind: string
        Key: Expression
        Params: Pattern array
        Body: BlockStatement
        Computed: bool
        ReturnType: TypeAnnotation option
        TypeParameters: TypeParameterDeclaration option
        Loc: SourceLocation option
    }
    static member Create(kind_, key, ``params``, body, ?computed_, ?returnType, ?typeParameters, ?loc) : ObjectMember = // ?async_, ?generator_,
        let kind =
            match kind_ with
            | ObjectGetter -> "get"
            | ObjectSetter -> "set"
            | ObjectMeth -> "method"
        let computed = defaultArg computed_ false
    //    let async = defaultArg async_ false
    //    let generator = defaultArg generator_ false
    //    member _.Async: bool = async
    //    member _.Generator: bool = generator
        {
            Kind = kind
            Key = key
            Params = ``params``
            Body = body
            Computed = computed
            ReturnType = returnType
            TypeParameters = typeParameters
            Loc = loc
        } |> ObjectMethod
    interface IPrinter with
        member this.Print(printer) =
            printer.AddLocation(this.Loc)

            if this.Kind <> "method" then
                printer.Print(this.Kind + " ")

            if this.Computed then
                printer.Print("[")
                printer.Print(this.Key)
                printer.Print("]")
            else
                printer.Print(this.Key)

            printer.PrintOptional(this.TypeParameters)
            printer.Print("(")
            printer.PrintCommaSeparatedArray(this.Params)
            printer.Print(")")
            printer.PrintOptional(this.ReturnType)
            printer.Print(" ")

            printer.PrintBlock(this.Body.Body, skipNewLineAtEnd=true)

/// If computed is true, the node corresponds to a computed (a[b]) member expression and property is an Expression.
/// If computed is false, the node corresponds to a static (a.b) member expression and property is an Identifier.
type MemberExpression =
    {
        Name: string
        Object: Expression
        Property: Expression
        Computed: bool
        Loc: SourceLocation option
    }
    static member Create(object, property, ?computed_, ?loc) : Expression =
        let computed = defaultArg computed_ false
        let name =
            match property with
            | Identifier(id) -> id.Name
            | _ -> ""

        {
            Name = name
            Object = object
            Property = property
            Computed = computed
            Loc = loc
        } |> MemberExpression
    member this.Print(printer, ?objectWithParens: bool) =
        printer.AddLocation(this.Loc)
        match objectWithParens, this.Object with
        | Some true, _ | _, Literal(NumericLiteral(_)) -> printer.WithParens(this.Object)
        | _ -> printer.ComplexExpressionWithParens(this.Object)
        if this.Computed then
            printer.Print("[")
            printer.Print(this.Property)
            printer.Print("]")
        else
            printer.Print(".")
            printer.Print(this.Property)

type ObjectExpression =
    {
        Properties: ObjectMember array
        Loc: SourceLocation option
    }
    static member Create(properties, ?loc) : Expression =
        {
            Properties = properties
            Loc = loc
        } |> ObjectExpression
    interface IPrinter with
        member this.Print(printer) =
            let printSeparator (p: Printer) =
                p.Print(",")
                p.PrintNewLine()

            printer.AddLocation(this.Loc)
            if Array.isEmpty this.Properties then printer.Print("{}")
            else printer.PrintBlock(this.Properties, (fun p x -> p.Print(x)), printSeparator, skipNewLineAtEnd=true)

/// A conditional expression, i.e., a ternary ?/: expression.
type ConditionalExpression =
    {
        Test: Expression
        Consequent: Expression
        Alternate: Expression
        Loc: SourceLocation option
    }
    static member Create(test, consequent, alternate, ?loc) : Expression =
        {
            Test = test
            Consequent = consequent
            Alternate = alternate
            Loc = loc
        } |> ConditionalExpression
    interface IPrinter with
        member this.Print(printer) =
            printer.AddLocation(this.Loc)
            match this.Test with
            // TODO: Move this optimization to Fable2Babel as with IfStatement?
            | Literal(BooleanLiteral(b)) ->
                if b.Value then printer.Print(this.Consequent)
                else printer.Print(this.Alternate)
            | _ ->
                printer.ComplexExpressionWithParens(this.Test)
                printer.Print(" ? ")
                printer.ComplexExpressionWithParens(this.Consequent)
                printer.Print(" : ")
                printer.ComplexExpressionWithParens(this.Alternate)

/// A function or method call expression.
type CallExpression =
    {
        Callee: Expression
        // Arguments: Choice<Expression, SpreadElement> array
        Arguments: Expression array
        Loc: SourceLocation option
    }
    static member Create(callee, arguments, ?loc) : Expression =
        {
            Callee = callee
            Arguments = arguments
            Loc = loc
        } |> CallExpression
    interface IPrinter with
        member this.Print(printer) =
            printer.AddLocation(this.Loc)
            printer.ComplexExpressionWithParens(this.Callee)
            printer.Print("(")
            printer.PrintCommaSeparatedArray(this.Arguments)
            printer.Print(")")

type NewExpression =
    {
        Callee: Expression
        // Arguments: Choice<Expression, SpreadElement> array = arguments
        Arguments: Expression array
        TypeArguments: TypeParameterInstantiation option
        Loc: SourceLocation option
    }
    static member Create(callee, arguments, ?typeArguments, ?loc) : Expression =
        {
            Callee = callee
            Arguments = arguments
            TypeArguments = typeArguments
            Loc = loc
        } |> NewExpression
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("new ", ?loc=this.Loc)
            printer.ComplexExpressionWithParens(this.Callee)
            printer.Print("(")
            printer.PrintCommaSeparatedArray(this.Arguments)
            printer.Print(")")

/// A comma-separated sequence of expressions.
type SequenceExpression =
    {
        Expressions: Expression array
        Loc: SourceLocation option
    }
    static member Create(expressions, ?loc) : Expression =
        {
            Expressions = expressions
            Loc = loc
        } |> SequenceExpression
    interface IPrinter with
        member this.Print(printer) =
            printer.AddLocation(this.Loc)
            printer.PrintCommaSeparatedArray(this.Expressions)

// Unary Operations
type UnaryExpression =
    {
        Prefix: bool
        Argument: Expression
        Operator: string
        Loc: SourceLocation option
    }
    static member Create(operator_, argument, ?loc) : Expression =
        let prefix = true
        let operator =
            match operator_ with
            | UnaryMinus -> "-"
            | UnaryPlus -> "+"
            | UnaryNot -> "!"
            | UnaryNotBitwise -> "~"
            | UnaryTypeof -> "typeof"
            | UnaryVoid -> "void"
            | UnaryDelete -> "delete"
        {
            Prefix = prefix
            Argument = argument
            Operator = operator
            Loc = loc
        } |> UnaryExpression
    interface IPrinter with
        member this.Print(printer) =
            printer.AddLocation(this.Loc)
            match this.Operator with
            | "-" | "+" | "!" | "~" -> printer.Print(this.Operator)
            | _ -> printer.Print(this.Operator + " ")
            printer.ComplexExpressionWithParens(this.Argument)

type UpdateExpression =
    {
        Prefix: bool
        Argument: Expression
        Operator: string
        Loc: SourceLocation option
    }
    static member Create(operator_, prefix, argument, ?loc) : Expression =
        let operator =
            match operator_ with
            | UpdateMinus -> "--"
            | UpdatePlus -> "++"
        {
            Prefix = prefix
            Argument = argument
            Operator = operator
            Loc = loc
        } |> UpdateExpression

    interface IPrinter with
        member this.Print(printer) =
            printer.AddLocation(this.Loc)
            if this.Prefix then
                printer.Print(this.Operator)
                printer.ComplexExpressionWithParens(this.Argument)
            else
                printer.ComplexExpressionWithParens(this.Argument)
                printer.Print(this.Operator)

// Binary Operations
type BinaryExpression =
    {
        Left: Expression
        Right: Expression
        Operator: string
        Loc: SourceLocation option
    }
    static member Create(operator_, left, right, ?loc) : Expression =
        let operator =
            match operator_ with
            | BinaryEqual -> "=="
            | BinaryUnequal -> "!="
            | BinaryEqualStrict -> "==="
            | BinaryUnequalStrict -> "!=="
            | BinaryLess -> "<"
            | BinaryLessOrEqual -> "<="
            | BinaryGreater -> ">"
            | BinaryGreaterOrEqual -> ">="
            | BinaryShiftLeft -> "<<"
            | BinaryShiftRightSignPropagating -> ">>"
            | BinaryShiftRightZeroFill -> ">>>"
            | BinaryMinus -> "-"
            | BinaryPlus -> "+"
            | BinaryMultiply -> "*"
            | BinaryDivide -> "/"
            | BinaryModulus -> "%"
            | BinaryExponent -> "**"
            | BinaryOrBitwise -> "|"
            | BinaryXorBitwise -> "^"
            | BinaryAndBitwise -> "&"
            | BinaryIn -> "in"
            | BinaryInstanceOf -> "instanceof"
        {
            Left = left
            Right = right
            Operator = operator
            Loc = loc
        } |> BinaryExpression
    interface IPrinter with
        member this.Print(printer) =
            printer.PrintOperation(this.Left, this.Operator, this.Right, this.Loc)

type AssignmentExpression =
    {
        Left: Expression
        Right: Expression
        Operator: string
        Loc: SourceLocation option
    }
    static member Create(operator_, left, right, ?loc) : Expression =
        let operator =
            match operator_ with
            | AssignEqual -> "="
            | AssignMinus -> "-="
            | AssignPlus -> "+="
            | AssignMultiply -> "*="
            | AssignDivide -> "/="
            | AssignModulus -> "%="
            | AssignShiftLeft -> "<<="
            | AssignShiftRightSignPropagating -> ">>="
            | AssignShiftRightZeroFill -> ">>>="
            | AssignOrBitwise -> "|="
            | AssignXorBitwise -> "^="
            | AssignAndBitwise -> "&="
        {
            Left = left
            Right = right
            Operator = operator
            Loc = loc
        } |> AssignmentExpression
    interface IPrinter with
        member this.Print(printer) =
            printer.PrintOperation(this.Left, this.Operator, this.Right, this.Loc)

type LogicalExpression =
    {
        Left: Expression
        Right: Expression
        Operator: string
        Loc: SourceLocation option
    }
    static member Create(operator_, left, right, ?loc) : Expression =
        let operator =
            match operator_ with
            | LogicalOr -> "||"
            | LogicalAnd-> "&&"
        {
            Left = left
            Right = right
            Operator = operator
            Loc = loc
        } |> LogicalExpression
    interface IPrinter with
        member this.Print(printer) =
            printer.PrintOperation(this.Left, this.Operator, this.Right, this.Loc)

// Patterns
// type AssignmentProperty(key, value, ?loc) =
//     inherit ObjectProperty("AssignmentProperty", ?loc = loc)
//     member _.Value: Pattern = value

// type ObjectPattern(properties, ?loc) =
//     inherit Node("ObjectPattern", ?loc = loc)
//     member _.Properties: Choice<AssignmentProperty, RestProperty> array = properties
//     interface Pattern

//type ArrayPattern(elements, ?typeAnnotation, ?loc) =
//    inherit Pattern("ArrayPattern", ?loc = loc)
//    member _.Elements: Pattern option array = elements
//    member _.TypeAnnotation: TypeAnnotation option = typeAnnotation

//type AssignmentPattern(left, right, ?typeAnnotation, ?loc) =
//    inherit Pattern("AssignmentPattern", ?loc = loc)
//    member _.Left: Pattern = left
//    member _.Right: Expression = right
//    member _.TypeAnnotation: TypeAnnotation option = typeAnnotation

type RestElement =
    {
        Name: string
        Argument: Pattern
        TypeAnnotation: TypeAnnotation option
        Loc: SourceLocation option
    }
    static member Create(argument: Pattern, ?typeAnnotation, ?loc) : Pattern =
        let (RestElement elem) = argument
        {
            Name = elem.Name
            Argument = argument
            TypeAnnotation = typeAnnotation
            Loc = loc
        } |> RestElement
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("...", ?loc=this.Loc)
            printer.Print(this.Argument)
            printer.PrintOptional(this.TypeAnnotation)

// Classes
type ClassMember =
    | ClassMethod of ClassMethod
    | ClassProperty of ClassProperty

    interface IPrinter with
        member this.Print(printer) =
            match this with
            | ClassMethod(cm) -> printer.Print(cm)
            | ClassProperty(cp) -> printer.Print(cp)

type ClassMethodKind =
    | ClassImplicitConstructor | ClassFunction | ClassGetter | ClassSetter

type ClassMethod =
    {
        Kind: string
        Key: Expression
        Params: Pattern array
        Body: BlockStatement
        Computed: bool
        Static: bool option
        Abstract: bool option
        ReturnType: TypeAnnotation option
        TypeParameters: TypeParameterDeclaration option
        Loc: SourceLocation option
    }
    static member Create(kind_, key, ``params``, body, ?computed_, ?``static``, ?``abstract``, ?returnType, ?typeParameters, ?loc) : ClassMember =
        let kind =
            match kind_ with
            | ClassImplicitConstructor -> "constructor"
            | ClassGetter -> "get"
            | ClassSetter -> "set"
            | ClassFunction -> "method"
        let computed = defaultArg computed_ false
        {
            Kind = kind
            Key = key
            Params = ``params``
            Body = body
            Computed = computed
            Static = ``static``
            Abstract = ``abstract``
            ReturnType = returnType
            TypeParameters = typeParameters
            Loc = loc
        } |> ClassMethod
    // This appears in astexplorer.net but it's not documented
    // member _.Expression: bool = false
    interface IPrinter with
        member this.Print(printer) =
            printer.AddLocation(this.Loc)

            let keywords = [
                if this.Static = Some true then yield "static"
                if this.Abstract = Some true then yield "abstract"
                if this.Kind = "get" || this.Kind = "set" then yield this.Kind
            ]

            if not (List.isEmpty keywords) then
                printer.Print((String.concat " " keywords) + " ")

            if this.Computed then
                printer.Print("[")
                printer.Print(this.Key)
                printer.Print("]")
            else
                printer.Print(this.Key)

            printer.PrintOptional(this.TypeParameters)
            printer.Print("(")
            printer.PrintCommaSeparatedArray(this.Params)
            printer.Print(")")
            printer.PrintOptional(this.ReturnType)
            printer.Print(" ")

            printer.Print(this.Body)

/// ES Class Fields & Static Properties
/// https://github.com/jeffmo/es-class-fields-and-static-properties
/// e.g, class MyClass { static myStaticProp = 5; myProp /* = 10 */; }
type ClassProperty =
    {
        Key: Expression
        Value: Expression option
        Computed: bool
        Static: bool
        Optional: bool
        TypeAnnotation: TypeAnnotation option
        Loc: SourceLocation option
    }
    static member Create(key, ?value, ?computed_, ?``static``, ?optional, ?typeAnnotation, ?loc) : ClassMember =
        let computed = defaultArg computed_ false
        {
            Key = key
            Value = value
            Computed = computed
            Static = defaultArg ``static`` false
            Optional = defaultArg optional false
            TypeAnnotation = typeAnnotation
            Loc = loc
        } |> ClassProperty
    interface IPrinter with
        member this.Print(printer) =
            printer.AddLocation(this.Loc)
            if this.Static then
                printer.Print("static ")
            if this.Computed then
                printer.Print("[")
                printer.Print(this.Key)
                printer.Print("]")
            else
                printer.Print(this.Key)
            if this.Optional then
                printer.Print("?")
            printer.PrintOptional(this.TypeAnnotation)
            printer.PrintOptional(": ", this.Value)

type ClassImplements =
    {
        Id: Identifier
        TypeParameters: TypeParameterInstantiation option
    }
    static member Create(id, ?typeParameters) : Expression =
        {
            Id = id
            TypeParameters = typeParameters
        } |> ClassImplements
    interface IPrinter with
        member this.Print(printer) =
            printer.Print(this.Id)
            printer.PrintOptional(this.TypeParameters)

type ClassBody =
    {
        Body: ClassMember array
        Loc: SourceLocation option
    }
    static member Create(body, ?loc) =
        {
            Body = body
            Loc = loc
        }
    interface IPrinter with
        member this.Print(printer) =
            printer.AddLocation(this.Loc)
            printer.PrintBlock(this.Body, (fun p x -> p.Print(x)), (fun p -> p.PrintStatementSeparator()))

type ClassDeclaration =
    {
        Body: ClassBody
        Id: Identifier option
        SuperClass: Expression option
        Implements: ClassImplements array option
        SuperTypeParameters: TypeParameterInstantiation option
        TypeParameters: TypeParameterDeclaration option
        Loc: SourceLocation option
    }
    static member Create(body, ?id, ?superClass, ?superTypeParameters, ?typeParameters, ?implements, ?loc) : Declaration =
        {
            Body = body
            Id = id
            SuperClass = superClass
            Implements = implements
            SuperTypeParameters = superTypeParameters
            TypeParameters = typeParameters
            Loc = loc
        } |> ClassDeclaration
    interface IPrinter with
        member this.Print(printer) =
            printer.PrintClass(this.Id, this.SuperClass, this.SuperTypeParameters, this.TypeParameters, this.Implements, this.Body, this.Loc)

/// Anonymous class: e.g., var myClass = class { }
type ClassExpression =
    {
        Body: ClassBody
        Id: Identifier option
        SuperClass: Expression option
        Implements: ClassImplements array option
        SuperTypeParameters: TypeParameterInstantiation option
        TypeParameters: TypeParameterDeclaration option
        Loc: SourceLocation option
    }
    static member Create(body, ?id, ?superClass, ?superTypeParameters, ?typeParameters, ?implements, ?loc) : Expression =
        {
            Body = body
            Id = id
            SuperClass = superClass
            Implements = implements
            SuperTypeParameters = superTypeParameters
            TypeParameters = typeParameters
            Loc = loc
        } |> ClassExpression
    interface IPrinter with
        member this.Print(printer) =
            printer.PrintClass(this.Id, this.SuperClass, this.SuperTypeParameters, this.TypeParameters, this.Implements, this.Body, this.Loc)

// type MetaProperty(meta, property, ?loc) =
//     interface Expression with
//     member _.Meta: Identifier = meta
//     member _.Property: Expression = property

// Modules
type PrivateModuleDeclaration =
    {
        Statement: Statement
    }
    static member Create(statement) : ModuleDeclaration =
        {
            Statement = statement
        } |> PrivateModuleDeclaration
    interface IPrinter with
        member this.Print(printer) =
            if printer.IsProductiveStatement(this.Statement) then
                printer.Print(this.Statement)

type ImportSpecifier =
    | ImportMemberSpecifier of ImportMemberSpecifier
    | ImportDefaultSpecifier of ImportDefaultSpecifier
    | ImportNamespaceSpecifier of ImportNamespaceSpecifier

    interface IPrinter with
        member this.Print(printer) =
            failwith "not implemented"

/// An imported variable binding, e.g., {foo} in import {foo} from "mod" or {foo as bar} in import {foo as bar} from "mod".
/// The imported field refers to the name of the export imported from the module.
/// The local field refers to the binding imported into the local module scope.
/// If it is a basic named import, such as in import {foo} from "mod", both imported and local are equivalent Identifier nodes; in this case an Identifier node representing foo.
/// If it is an aliased import, such as in import {foo as bar} from "mod", the imported field is an Identifier node representing foo, and the local field is an Identifier node representing bar.
type ImportMemberSpecifier =
    {
        Local: Identifier
        Imported: Identifier
    }
    static member Create(local, imported) : ImportSpecifier =
        {
            Local = local
            Imported = imported
        } |> ImportMemberSpecifier

    interface IPrinter with
        member this.Print(printer) =
            // Don't print the braces, this will be done in the import declaration
            printer.Print(this.Imported)
            if this.Imported.Name <> this.Local.Name then
                printer.Print(" as ")
                printer.Print(this.Local)

/// A default import specifier, e.g., foo in import foo from "mod".
type ImportDefaultSpecifier =
    {
        Local: Identifier
    }
    static member Create(local) : ImportSpecifier =
        {
            Local = local
        } |> ImportDefaultSpecifier

    interface IPrinter with
        member this.Print(printer) =
            printer.Print(this.Local)

/// A namespace import specifier, e.g., * as foo in import * as foo from "mod".
type ImportNamespaceSpecifier =
    {
        Local: Identifier
    }
    static member Create(local) : ImportSpecifier =
        {
            Local = local
        } |> ImportNamespaceSpecifier
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("* as ")
            printer.Print(this.Local)

/// e.g., import foo from "mod";.
type ImportDeclaration =
    {
        Specifiers: ImportSpecifier array
        Source: StringLiteral
    }
    static member Create(specifiers, source) : ModuleDeclaration =
        {
            Specifiers = specifiers
            Source = source
        } |> ImportDeclaration
    interface IPrinter with
        member this.Print(printer) =
            let members = this.Specifiers |> Array.choose (function ImportMemberSpecifier(x) -> Some x | _ -> None)
            let defaults = this.Specifiers|> Array.choose (function ImportDefaultSpecifier(x) -> Some x | _ -> None)
            let namespaces = this.Specifiers |> Array.choose (function ImportNamespaceSpecifier(x) -> Some x | _ -> None)

            printer.Print("import ")

            if not(Array.isEmpty defaults) then
                printer.PrintCommaSeparatedArray(defaults)
                if not(Array.isEmpty namespaces && Array.isEmpty members) then
                    printer.Print(", ")

            if not(Array.isEmpty namespaces) then
                printer.PrintCommaSeparatedArray(namespaces)
                if not(Array.isEmpty members) then
                    printer.Print(", ")

            if not(Array.isEmpty members) then
                printer.Print("{ ")
                printer.PrintCommaSeparatedArray(members)
                printer.Print(" }")

            if not(Array.isEmpty defaults && Array.isEmpty namespaces && Array.isEmpty members) then
                printer.Print(" from ")

            printer.Print("\"")
            printer.Print(printer.MakeImportPath(this.Source.Value))
            printer.Print("\"")

/// An exported variable binding, e.g., {foo} in export {foo} or {bar as foo} in export {bar as foo}.
/// The exported field refers to the name exported in the module.
/// The local field refers to the binding into the local module scope.
/// If it is a basic named export, such as in export {foo}, both exported and local are equivalent Identifier nodes;
/// in this case an Identifier node representing foo. If it is an aliased export, such as in export {bar as foo},
/// the exported field is an Identifier node representing foo, and the local field is an Identifier node representing bar.
type ExportSpecifier =
    {
        Local: Identifier
        Exported: Identifier
    }
    static member Create(local, exported) : Node =
        {
            Local = local
            Exported = exported
        } |> ExportSpecifier
    interface IPrinter with
        member this.Print(printer) =
            // Don't print the braces, this will be done in the export declaration
            printer.Print(this.Local)
            if this.Exported.Name <> this.Local.Name then
                printer.Print(" as ")
                printer.Print(this.Exported)

/// An export named declaration, e.g., export {foo, bar};, export {foo} from "mod"; or export var foo = 1;.
/// Note: Having declaration populated with non-empty specifiers or non-null source results in an invalid state.
type ExportNamedDeclaration =
    {
        Declaration: Declaration
    }
    static member Create(declaration) : ModuleDeclaration =
        {
            Declaration = declaration
        } |> ExportNamedDeclaration
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("export ")
            printer.Print(this.Declaration)

type ExportNamedReferences =
    {
        Specifiers: ExportSpecifier array
        Source: StringLiteral option
    }
    static member Create(specifiers, ?source) : ModuleDeclaration =
        {
            Specifiers = specifiers
            Source = source
        } |> ExportNamedReferences
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("export ")
            printer.Print("{ ")
            printer.PrintCommaSeparatedArray(this.Specifiers)
            printer.Print(" }")
            printer.PrintOptional(" from ", this.Source)

/// An export default declaration, e.g., export default function () {}; or export default 1;.
type ExportDefaultDeclaration =
    {
        Declaration: Choice<Declaration, Expression>
    }
    static member Create(declaration) : ModuleDeclaration =
        {
            Declaration = declaration
        } |> ExportDefaultDeclaration
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("export default ")
            match this.Declaration with
            | Choice1Of2 x -> printer.Print(x)
            | Choice2Of2 x -> printer.Print(x)

/// An export batch declaration, e.g., export * from "mod";.
type ExportAllDeclaration =
    {
        Source: Literal
        Loc: SourceLocation option
    }
    static member Create(source, ?loc) : ModuleDeclaration =
        {
            Source = source
            Loc = loc
        } |> ExportAllDeclaration
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("export * from ", ?loc=this.Loc)
            printer.Print(this.Source)

// Type Annotations
type TypeAnnotationInfo =
    | StringTypeAnnotation
    | NumberTypeAnnotation
    | TypeAnnotationInfo of TypeAnnotationInfo
    | BooleanTypeAnnotation
    | AnyTypeAnnotation
    | VoidTypeAnnotation
    | TupleTypeAnnotation of TupleTypeAnnotation
    | UnionTypeAnnotation of UnionTypeAnnotation
    | FunctionTypeAnnotation of FunctionTypeAnnotation
    | NullableTypeAnnotation of NullableTypeAnnotation
    | GenericTypeAnnotation of GenericTypeAnnotation
    | ObjectTypeAnnotation of ObjectTypeAnnotation

    interface IPrinter with
        member this.Print(printer) =
            match this with
            | StringTypeAnnotation -> printer.Print("string")
            | NumberTypeAnnotation -> printer.Print("number")
            | TypeAnnotationInfo(an) -> printer.Print(an)
            | BooleanTypeAnnotation -> printer.Print("boolean")
            | AnyTypeAnnotation -> printer.Print("any")
            | VoidTypeAnnotation -> printer.Print("void")
            | TupleTypeAnnotation(an) -> printer.Print(an)
            | UnionTypeAnnotation(an) -> printer.Print(an)
            | FunctionTypeAnnotation(an) -> printer.Print(an)
            | NullableTypeAnnotation(an) -> printer.Print(an)
            | GenericTypeAnnotation(an) -> printer.Print(an)
            | ObjectTypeAnnotation(an) -> printer.Print(an)

type TypeAnnotation =
    {
        TypeAnnotation: TypeAnnotationInfo
    }
    static member Create(typeAnnotation) =
        {
            TypeAnnotation = typeAnnotation
        }
    interface IPrinter with
        member this.Print(printer) =
            printer.Print(": ")
            printer.Print(this.TypeAnnotation)

type TypeParameter =
    {
        Name: string
        Bound: TypeAnnotation option
        Default: TypeAnnotationInfo option
    }
    static member Create(name, ?bound, ?``default``) =
        {
            Name = name
            Bound = bound
            Default = ``default``
        }
    interface IPrinter with
        member this.Print(printer) =
            printer.Print(this.Name)
            // printer.PrintOptional(bound)
            // printer.PrintOptional(``default``)

type TypeParameterDeclaration =
    {
        Params: TypeParameter array
    }
    static member Create(``params``) =
        {
            Params = ``params``
        }
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("<")
            printer.PrintCommaSeparatedArray(this.Params)
            printer.Print(">")

type TypeParameterInstantiation =
    {
        Params: TypeAnnotationInfo array
    }
    static member Create(``params``) =
        {
            Params = ``params``
        }
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("<")
            printer.PrintCommaSeparatedArray(this.Params)
            printer.Print(">")



type TupleTypeAnnotation =
    {
        Types: TypeAnnotationInfo array
    }
    static member Create(types) : TypeAnnotationInfo =
        {
            Types = types
        } |> TupleTypeAnnotation
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("[")
            printer.PrintCommaSeparatedArray(this.Types)
            printer.Print("]")

type UnionTypeAnnotation =
    {
        Types: TypeAnnotationInfo array
    }
    static member Create(types) : TypeAnnotationInfo =
        {
            Types = types
        } |> UnionTypeAnnotation
    interface IPrinter with
        member this.Print(printer) =
            printer.PrintArray(this.Types, (fun p x -> p.Print(x)), (fun p -> p.Print(" | ")))

type FunctionTypeParam =
    {
        Name: Identifier
        TypeAnnotation: TypeAnnotationInfo
        Optional: bool option
    }
    static member Create(name, typeInfo, ?optional) =
        {
            Name = name
            TypeAnnotation = typeInfo
            Optional = optional
        }
    interface IPrinter with
        member this.Print(printer) =
            printer.Print(this.Name)
            if this.Optional = Some true then
                printer.Print("?")
            printer.Print(": ")
            printer.Print(this.TypeAnnotation)

type FunctionTypeAnnotation =
    {
        Params: FunctionTypeParam array
        ReturnType: TypeAnnotationInfo
        TypeParameters: TypeParameterDeclaration option
        Rest: FunctionTypeParam option
    }
    static member Create(``params``, returnType, ?typeParameters, ?rest) : TypeAnnotationInfo =
        {
            Params = ``params``
            ReturnType = returnType
            TypeParameters = typeParameters
            Rest = rest
        } |> FunctionTypeAnnotation
    interface IPrinter with
        member this.Print(printer) =
            printer.PrintOptional(this.TypeParameters)
            printer.Print("(")
            printer.PrintCommaSeparatedArray(this.Params)
            if Option.isSome this.Rest then
                printer.Print("...")
                printer.Print(this.Rest.Value)
            printer.Print(") => ")
            printer.Print(this.ReturnType)

type NullableTypeAnnotation =
    {
        TypeAnnotation: TypeAnnotationInfo
    }
    static member Create(``type``) : TypeAnnotationInfo =
        {
            TypeAnnotation = ``type``
        } |> NullableTypeAnnotation
    interface IPrinter with
        member this.Print(printer) =
            printer.Print(this.TypeAnnotation)

type GenericTypeAnnotation =
    {
        Id: Identifier
        TypeParameters: TypeParameterInstantiation option
    }
    static member Create(id, ?typeParameters) : TypeAnnotationInfo =
        {
            Id = id
            TypeParameters = typeParameters
        } |> GenericTypeAnnotation
    interface IPrinter with
        member this.Print(printer) =
            printer.Print(this.Id)
            printer.PrintOptional(this.TypeParameters)

type ObjectTypeProperty =
    {
        Key: Expression
        Value: TypeAnnotationInfo
        Kind: string option
        Computed: bool
        Static: bool
        Optional: bool
        Proto: bool
        Method: bool
    }
    static member Create(key, value, ?computed_, ?kind, ?``static``, ?optional, ?proto, ?method) =
        let computed = defaultArg computed_ false
        {
            Key = key
            Value = value
            Kind = kind
            Computed = computed
            Static = defaultArg ``static`` false
            Optional = defaultArg optional false
            Proto = defaultArg proto false
            Method = defaultArg method false
        }
    interface IPrinter with
        member this.Print(printer) =
            if this.Static then
                printer.Print("static ")
            if Option.isSome this.Kind then
                printer.Print(this.Kind.Value + " ")
            if this.Computed then
                printer.Print("[")
                printer.Print(this.Key)
                printer.Print("]")
            else
                printer.Print(this.Key)
            if this.Optional then
                printer.Print("?")
            // TODO: proto, method
            printer.Print(": ")
            printer.Print(this.Value)

type ObjectTypeIndexer =
    {
        Id: Identifier option
        Key: Identifier
        Value: TypeAnnotationInfo
        Static: bool option
    }
    static member Create(key, value, ?id, ?``static``) : Node =
        {
            Id = id
            Key = key
            Value = value
            Static = ``static``
        } |> ObjectTypeIndexer
    interface IPrinter with
        member _.Print(_) = failwith "not implemented"

type ObjectTypeCallProperty =
    {
        Value: TypeAnnotationInfo
        Static: bool option
    }
    static member Create(value, ?``static``) : Node =
        {
            Value = value
            Static = ``static``
        } |> ObjectTypeCallProperty
    interface IPrinter with
        member _.Print(_) = failwith "not implemented"

type ObjectTypeInternalSlot =
    {
        Id: Identifier
        Value: TypeAnnotationInfo
        Optional: bool
        Static: bool
        Method: bool
    }
    static member Create(id, value, optional, ``static``, method) : Node =
        {
            Id = id
            Value = value
            Optional = optional
            Static = ``static``
            Method = method
        } |> ObjectTypeInternalSlot
    interface IPrinter with
        member _.Print(_) = failwith "not implemented"

type ObjectTypeAnnotation =
    {
        Properties: ObjectTypeProperty array
        Indexers: ObjectTypeIndexer array
        CallProperties: ObjectTypeCallProperty array
        InternalSlots: ObjectTypeInternalSlot array
        Exact: bool
    }
    static member Create(properties, ?indexers_, ?callProperties_, ?internalSlots_, ?exact_) : TypeAnnotationInfo =
        let exact = defaultArg exact_ false
        let indexers = defaultArg indexers_ [||]
        let callProperties = defaultArg callProperties_ [||]
        let internalSlots = defaultArg internalSlots_ [||]
        {
            Properties = properties
            Indexers = indexers
            CallProperties = callProperties
            InternalSlots = internalSlots
            Exact = exact
        } |> ObjectTypeAnnotation
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("{")
            printer.PrintNewLine()
            printer.PushIndentation()
            printer.PrintArray(this.Properties, (fun p x -> p.Print(x)), (fun p -> p.PrintStatementSeparator()))
            printer.PrintArray(this.Indexers, (fun p x -> p.Print(x)), (fun p -> p.PrintStatementSeparator()))
            printer.PrintArray(this.CallProperties, (fun p x -> p.Print(x)), (fun p -> p.PrintStatementSeparator()))
            printer.PrintArray(this.InternalSlots, (fun p x -> p.Print(x)), (fun p -> p.PrintStatementSeparator()))
            printer.PrintNewLine()
            printer.PopIndentation()
            printer.Print("}")
            printer.PrintNewLine()

type InterfaceExtends =
    {
        Id: Identifier
        TypeParameters: TypeParameterInstantiation option
    }
    static member Create(id, ?typeParameters) : Node =
        {
            Id = id
            TypeParameters = typeParameters
        } |> InterfaceExtends
    interface IPrinter with
        member this.Print(printer) =
            printer.Print(this.Id)
            printer.PrintOptional(this.TypeParameters)

type InterfaceDeclaration =
    {
        Id: Identifier
        Body: ObjectTypeAnnotation
        Extends: InterfaceExtends array
        Implements: ClassImplements array

        TypeParameters: TypeParameterDeclaration option
    }
    static member Create(id, body, ?extends_, ?typeParameters, ?implements_) : Declaration = // ?mixins_,
        let extends = defaultArg extends_ [||]
        let implements = defaultArg implements_ [||]
        {
            Id = id
            Body = body
            Extends = extends
            Implements = implements

            TypeParameters = typeParameters
        } |> InterfaceDeclaration
//    let mixins = defaultArg mixins_ [||]
//    member _.Mixins: InterfaceExtends array = mixins
    interface IPrinter with
        member this.Print(printer) =
            printer.Print("interface ")
            printer.Print(this.Id)
            printer.PrintOptional(this.TypeParameters)
            if not (Array.isEmpty this.Extends) then
                printer.Print(" extends ")
                printer.PrintArray(this.Extends, (fun p x -> p.Print(x)), (fun p -> p.Print(", ")))
            if not (Array.isEmpty this.Implements) then
                printer.Print(" implements ")
                printer.PrintArray(this.Implements, (fun p x -> p.Print(x)), (fun p -> p.Print(", ")))
            printer.Print(" ")
            printer.Print(this.Body)

