namespace Broadside.Graphics.Functions;

/// <summary>The instructions a Type 4 program compiles to: the operators of Table 42 plus pushes and jumps.</summary>
/// <remarks>ISO 32000-2 §7.10.5.2, Table 42, Annex B. <c>true</c> and <c>false</c> compile to <see cref="PushBoolean"/>.</remarks>
internal enum PostScriptOpCode : byte
{
    /// <summary>Push the literal as a real.</summary>
    PushReal,

    /// <summary>Push the literal as an integer.</summary>
    PushInteger,

    /// <summary>Push the literal (0 or 1) as a boolean.</summary>
    PushBoolean,

    /// <summary>Pop a boolean; jump to the target when it is false (the start of <c>{…} if</c> or <c>{…} {…} ifelse</c>).</summary>
    JumpIfFalse,

    /// <summary>Jump to the target (the end of the first body of <c>ifelse</c>).</summary>
    Jump,

    /// <summary><c>abs</c> (B.2).</summary>
    Abs,

    /// <summary><c>add</c> (B.2).</summary>
    Add,

    /// <summary><c>atan</c> (B.2).</summary>
    Atan,

    /// <summary><c>ceiling</c> (B.2).</summary>
    Ceiling,

    /// <summary><c>cos</c> (B.2).</summary>
    Cos,

    /// <summary><c>cvi</c> (B.2).</summary>
    Cvi,

    /// <summary><c>cvr</c> (B.2).</summary>
    Cvr,

    /// <summary><c>div</c> (B.2).</summary>
    Div,

    /// <summary><c>exp</c> (B.2).</summary>
    Exp,

    /// <summary><c>floor</c> (B.2).</summary>
    Floor,

    /// <summary><c>idiv</c> (B.2).</summary>
    Idiv,

    /// <summary><c>ln</c> (B.2).</summary>
    Ln,

    /// <summary><c>log</c> (B.2).</summary>
    Log,

    /// <summary><c>mod</c> (B.2).</summary>
    Mod,

    /// <summary><c>mul</c> (B.2).</summary>
    Mul,

    /// <summary><c>neg</c> (B.2).</summary>
    Neg,

    /// <summary><c>round</c> (B.2).</summary>
    Round,

    /// <summary><c>sin</c> (B.2).</summary>
    Sin,

    /// <summary><c>sqrt</c> (B.2).</summary>
    Sqrt,

    /// <summary><c>sub</c> (B.2).</summary>
    Sub,

    /// <summary><c>truncate</c> (B.2).</summary>
    Truncate,

    /// <summary><c>and</c> (B.3).</summary>
    And,

    /// <summary><c>bitshift</c> (B.3).</summary>
    Bitshift,

    /// <summary><c>eq</c> (B.3).</summary>
    Eq,

    /// <summary><c>ge</c> (B.3).</summary>
    Ge,

    /// <summary><c>gt</c> (B.3).</summary>
    Gt,

    /// <summary><c>le</c> (B.3).</summary>
    Le,

    /// <summary><c>lt</c> (B.3).</summary>
    Lt,

    /// <summary><c>ne</c> (B.3).</summary>
    Ne,

    /// <summary><c>not</c> (B.3).</summary>
    Not,

    /// <summary><c>or</c> (B.3).</summary>
    Or,

    /// <summary><c>xor</c> (B.3).</summary>
    Xor,

    /// <summary><c>copy</c> (B.5).</summary>
    Copy,

    /// <summary><c>dup</c> (B.5).</summary>
    Dup,

    /// <summary><c>exch</c> (B.5).</summary>
    Exch,

    /// <summary><c>index</c> (B.5).</summary>
    Index,

    /// <summary><c>pop</c> (B.5).</summary>
    Pop,

    /// <summary><c>roll</c> (B.5).</summary>
    Roll,
}

/// <summary>One instruction of a compiled Type 4 program.</summary>
/// <param name="OpCode">What to do.</param>
/// <param name="Target">The instruction index a jump goes to.</param>
/// <param name="Literal">The value a push pushes.</param>
internal readonly record struct PostScriptInstruction(PostScriptOpCode OpCode, int Target = 0, double Literal = 0);
