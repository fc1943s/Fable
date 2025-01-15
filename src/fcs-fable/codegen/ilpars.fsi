// Signature file for parser generated by fsyacc
module internal FSharp.Compiler.AbstractIL.AsciiParser
open FSharp.Compiler.AbstractIL.AsciiConstants
open FSharp.Compiler.AbstractIL.IL
type token = 
  | VOID
  | VARARG
  | VALUETYPE
  | VALUE
  | UNSIGNED
  | UNMANAGED
  | UINT8
  | UINT64
  | UINT32
  | UINT16
  | UINT
  | STRING
  | STAR
  | SLASH
  | RPAREN
  | RBRACK
  | PLUS
  | OBJECT
  | NATIVE
  | METHOD
  | LPAREN
  | LESS
  | LBRACK
  | INT8
  | INT64
  | INT32
  | INT16
  | INT
  | INSTANCE
  | GREATER
  | FLOAT64
  | FLOAT32
  | FIELD
  | EXPLICIT
  | EOF
  | ELLIPSES
  | DOT
  | DEFAULT
  | DCOLON
  | COMMA
  | CLASS
  | CHAR
  | BYTEARRAY
  | BOOL
  | BANG
  | AMP
  | VAL_SQSTRING of (string)
  | VAL_QSTRING of (string)
  | VAL_DOTTEDNAME of (string)
  | VAL_ID of (string)
  | VAL_HEXBYTE of (int)
  | INSTR_VALUETYPE of (ValueTypeInstr)
  | INSTR_INT_TYPE of (IntTypeInstr)
  | INSTR_TYPE of (TypeInstr)
  | INSTR_TOK of (TokenInstr)
  | INSTR_STRING of (StringInstr)
  | INSTR_NONE of (NoArgInstr)
  | INSTR_R of (DoubleInstr)
  | INSTR_I8 of (Int64Instr)
  | INSTR_I32_I32 of (Int32Int32Instr)
  | INSTR_I of (Int32Instr)
  | VAL_FLOAT64 of (double)
  | VAL_INT32_ELLIPSES of (int32)
  | VAL_INT64 of (int64)
type tokenId = 
    | TOKEN_VOID
    | TOKEN_VARARG
    | TOKEN_VALUETYPE
    | TOKEN_VALUE
    | TOKEN_UNSIGNED
    | TOKEN_UNMANAGED
    | TOKEN_UINT8
    | TOKEN_UINT64
    | TOKEN_UINT32
    | TOKEN_UINT16
    | TOKEN_UINT
    | TOKEN_STRING
    | TOKEN_STAR
    | TOKEN_SLASH
    | TOKEN_RPAREN
    | TOKEN_RBRACK
    | TOKEN_PLUS
    | TOKEN_OBJECT
    | TOKEN_NATIVE
    | TOKEN_METHOD
    | TOKEN_LPAREN
    | TOKEN_LESS
    | TOKEN_LBRACK
    | TOKEN_INT8
    | TOKEN_INT64
    | TOKEN_INT32
    | TOKEN_INT16
    | TOKEN_INT
    | TOKEN_INSTANCE
    | TOKEN_GREATER
    | TOKEN_FLOAT64
    | TOKEN_FLOAT32
    | TOKEN_FIELD
    | TOKEN_EXPLICIT
    | TOKEN_EOF
    | TOKEN_ELLIPSES
    | TOKEN_DOT
    | TOKEN_DEFAULT
    | TOKEN_DCOLON
    | TOKEN_COMMA
    | TOKEN_CLASS
    | TOKEN_CHAR
    | TOKEN_BYTEARRAY
    | TOKEN_BOOL
    | TOKEN_BANG
    | TOKEN_AMP
    | TOKEN_VAL_SQSTRING
    | TOKEN_VAL_QSTRING
    | TOKEN_VAL_DOTTEDNAME
    | TOKEN_VAL_ID
    | TOKEN_VAL_HEXBYTE
    | TOKEN_INSTR_VALUETYPE
    | TOKEN_INSTR_INT_TYPE
    | TOKEN_INSTR_TYPE
    | TOKEN_INSTR_TOK
    | TOKEN_INSTR_STRING
    | TOKEN_INSTR_NONE
    | TOKEN_INSTR_R
    | TOKEN_INSTR_I8
    | TOKEN_INSTR_I32_I32
    | TOKEN_INSTR_I
    | TOKEN_VAL_FLOAT64
    | TOKEN_VAL_INT32_ELLIPSES
    | TOKEN_VAL_INT64
    | TOKEN_end_of_input
    | TOKEN_error
type nonTerminalId = 
    | NONTERM__startilInstrs
    | NONTERM__startilType
    | NONTERM_ilType
    | NONTERM_ilInstrs
    | NONTERM_compQstring
    | NONTERM_methodName
    | NONTERM_instrs2
    | NONTERM_instr
    | NONTERM_name1
    | NONTERM_className
    | NONTERM_slashedName
    | NONTERM_typeNameInst
    | NONTERM_typeName
    | NONTERM_typSpec
    | NONTERM_callConv
    | NONTERM_callKind
    | NONTERM_typ
    | NONTERM_bounds1
    | NONTERM_bound
    | NONTERM_id
    | NONTERM_int32
    | NONTERM_int64
    | NONTERM_float64
    | NONTERM_opt_actual_tyargs
    | NONTERM_actual_tyargs
    | NONTERM_actualTypSpecs
/// This function maps tokens to integer indexes
val tagOfToken: token -> int

/// This function maps integer indexes to symbolic token ids
val tokenTagToTokenId: int -> tokenId

/// This function maps production indexes returned in syntax errors to strings representing the non terminal that would be produced by that production
val prodIdxToNonTerminal: int -> nonTerminalId

/// This function gets the name of a token as a string
val token_to_string: token -> string
val ilInstrs : (Internal.Utilities.Text.Lexing.LexBuffer<char> -> token) -> Internal.Utilities.Text.Lexing.LexBuffer<char> -> (ILInstr array) 
val ilType : (Internal.Utilities.Text.Lexing.LexBuffer<char> -> token) -> Internal.Utilities.Text.Lexing.LexBuffer<char> -> (ILType) 
