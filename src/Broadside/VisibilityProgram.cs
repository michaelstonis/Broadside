using System.Buffers;

namespace Broadside;

/// <summary>
/// A membership dictionary's visibility compiled once into postfix instructions over group ordinals, so evaluating it per marked-content
/// section or XObject allocates nothing (ISO 32000-2 §8.11.2.2). Values are three-valued: ON, OFF, or no effect (a group whose intent
/// the configuration does not use, §8.11.2.3); operators skip operands without effect, and a result without effect is visible.
/// </summary>
internal sealed class VisibilityProgram
{
    private const int StackAllocLimit = 256;

    private readonly Instruction[] _code;
    private readonly int _maxStack;

    private VisibilityProgram(Instruction[] code, int maxStack)
    {
        _code = code;
        _maxStack = maxStack;
    }

    /// <summary>Gets a program for content that is not optional, or whose optionality has no usable group: always visible.</summary>
    public static VisibilityProgram AlwaysVisible { get; } = new([new Instruction(Operation.And, 0)], 1);

    /// <summary>The operations.</summary>
    internal enum Operation : byte
    {
        /// <summary>Push the state of group <see cref="Instruction.Operand"/>.</summary>
        Group,

        /// <summary>Pop <see cref="Instruction.Operand"/> values, push their conjunction.</summary>
        And,

        /// <summary>Pop <see cref="Instruction.Operand"/> values, push their disjunction.</summary>
        Or,

        /// <summary>Negate the top value.</summary>
        Not,
    }

    /// <summary>Compiles a parsed expression.</summary>
    public static VisibilityProgram Compile(PdfVisibilityExpression expression)
    {
        var code = new List<Instruction>();
        int depth = 0;
        int max = 0;
        Emit(expression);
        return new VisibilityProgram([.. code], Math.Max(max, 1));

        void Emit(PdfVisibilityExpression node)
        {
            switch (node.Operator)
            {
                case PdfVisibilityOperator.Group:
                    code.Add(new Instruction(Operation.Group, node.Group!.Ordinal));
                    max = Math.Max(max, ++depth);
                    break;
                case PdfVisibilityOperator.Not when node.Operands.Count == 1:
                    Emit(node.Operands[0]);
                    code.Add(new Instruction(Operation.Not, 0));
                    break;
                default:
                    var operation = node.Operator == PdfVisibilityOperator.Or ? Operation.Or : Operation.And;
                    int count = node.Operator == PdfVisibilityOperator.Not ? 0 : node.Operands.Count;
                    for (int index = 0; index < count; index++)
                    {
                        Emit(node.Operands[index]);
                    }

                    code.Add(new Instruction(operation, count));
                    depth -= count;
                    max = Math.Max(max, ++depth);
                    break;
            }
        }
    }

    /// <summary>Compiles a policy over a group list (§8.11.2.2 Table 97 <c>P</c>).</summary>
    public static VisibilityProgram Compile(IReadOnlyList<PdfOptionalContentGroup> groups, PdfVisibilityPolicy policy)
    {
        if (groups.Count == 0)
        {
            return AlwaysVisible;
        }

        bool negate = policy is PdfVisibilityPolicy.AnyOff or PdfVisibilityPolicy.AllOff;
        var code = new List<Instruction>(groups.Count * 2 + 1);
        foreach (PdfOptionalContentGroup group in groups)
        {
            code.Add(new Instruction(Operation.Group, group.Ordinal));
            if (negate)
            {
                code.Add(new Instruction(Operation.Not, 0));
            }
        }

        code.Add(new Instruction(policy is PdfVisibilityPolicy.AllOn or PdfVisibilityPolicy.AllOff ? Operation.And : Operation.Or, groups.Count));
        return new VisibilityProgram([.. code], groups.Count);
    }

    /// <summary>Evaluates the program: <see langword="true"/> unless the result is OFF.</summary>
    public bool Evaluate(ReadOnlySpan<bool> on, ReadOnlySpan<bool> effective)
    {
        sbyte[]? rented = null;
        Span<sbyte> stack = _maxStack <= StackAllocLimit
            ? stackalloc sbyte[StackAllocLimit]
            : (rented = ArrayPool<sbyte>.Shared.Rent(_maxStack));
        try
        {
            int top = 0;
            foreach (Instruction instruction in _code)
            {
                switch (instruction.Operation)
                {
                    case Operation.Group:
                        int ordinal = instruction.Operand;
                        stack[top++] = !effective[ordinal] ? (sbyte)-1 : on[ordinal] ? (sbyte)1 : (sbyte)0;
                        break;
                    case Operation.Not:
                        if (stack[top - 1] >= 0)
                        {
                            stack[top - 1] = (sbyte)(1 - stack[top - 1]);
                        }

                        break;
                    default:
                        int count = instruction.Operand;
                        sbyte result = -1;
                        for (int index = top - count; index < top; index++)
                        {
                            sbyte value = stack[index];
                            if (value < 0)
                            {
                                continue;
                            }

                            result = result < 0 ? value
                                : instruction.Operation == Operation.And ? (sbyte)(result & value) : (sbyte)(result | value);
                        }

                        top -= count;
                        stack[top++] = result;
                        break;
                }
            }

            return stack[0] != 0;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<sbyte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>One instruction.</summary>
    internal readonly record struct Instruction(Operation Operation, int Operand);
}
