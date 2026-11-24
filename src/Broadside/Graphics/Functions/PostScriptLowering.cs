namespace Broadside.Graphics.Functions;

/// <summary>
/// Lowers a compiled Type 4 program to a register program (<see cref="PostScriptRegisterProgram"/>) when the depth and the kind of
/// every stack entry are known at every instruction; <see langword="null"/> otherwise, and the program is interpreted.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.10.5, Annex B. The stack is followed through the program at compile time, one entry per register: a push names a
/// constant register, an operator writes a new register, and <c>dup</c>, <c>exch</c>, <c>pop</c>, <c>copy</c>, <c>index</c> and
/// <c>roll</c> (with literal integer operands) only rename, so they cost nothing when the program runs. Where paths join (the end of
/// an <c>if</c> body, or of either body of <c>ifelse</c>), each incoming path moves its entries into the join's registers.
/// </para>
/// <para>
/// The results and the <see cref="FunctionStatus"/> are those of <see cref="PostScriptEvaluator"/> for every input. A program is not
/// lowered when the interpreter's result depends on something only known at run time that the register program does not follow:
/// a stack overflow or underflow (the interpreter fails the evaluation), a <c>copy</c>, <c>index</c> or <c>roll</c> operand that is
/// not a literal integer, paths that join with different depths or with a boolean and a number in one entry, or an integer operand
/// (of <c>idiv</c>, <c>mod</c>, <c>and</c>, <c>or</c>, <c>xor</c>, <c>not</c>, <c>bitshift</c>) that is an integer or a real
/// depending on the run (the result of integer arithmetic that may overflow, or of joined paths).
/// </para>
/// </remarks>
internal static class PostScriptLowering
{
    /// <summary>The most registers a lowered program may use.</summary>
    public const int MaxRegisters = 1024;

    /// <summary>A real.</summary>
    internal const byte Real = 0;

    /// <summary>A 32-bit integer.</summary>
    internal const byte Integer = 1;

    /// <summary>A boolean (1 or 0).</summary>
    internal const byte Boolean = 2;

    /// <summary>An integer or a real, depending on the run.</summary>
    internal const byte Number = 3;

    /// <summary>Lowers <paramref name="code"/>, or returns <see langword="null"/> when it must be interpreted.</summary>
    /// <param name="code">The compiled program.</param>
    /// <param name="inputs">m, the number of inputs, pushed as reals.</param>
    /// <param name="outputs">n, the number of outputs.</param>
    /// <returns>The register program.</returns>
    public static PostScriptRegisterProgram? Lower(PostScriptInstruction[] code, int inputs, int outputs)
    {
        if (inputs > PostScriptEvaluator.StackLimit)
        {
            return null;
        }

        var lowering = new Lowerer(code);
        return lowering.Run(inputs, outputs);
    }

    /// <summary>The stack entries and their kinds at one point.</summary>
    private sealed class Stack
    {
        public Stack(List<int> registers, List<byte> kinds)
        {
            Registers = registers;
            Kinds = kinds;
        }

        public List<int> Registers { get; }

        public List<byte> Kinds { get; }

        public int Depth => Registers.Count;

        public void Push(int register, byte kind)
        {
            Registers.Add(register);
            Kinds.Add(kind);
        }

        public (int Register, byte Kind) Pop()
        {
            int last = Registers.Count - 1;
            (int register, byte kind) = (Registers[last], Kinds[last]);
            Registers.RemoveAt(last);
            Kinds.RemoveAt(last);
            return (register, kind);
        }
    }

    /// <summary>Where paths join: the registers the entries are in after the join, and the kinds seen so far.</summary>
    private sealed class Join(int[] registers, byte[] kinds)
    {
        public int[] Registers { get; } = registers;

        public byte[] Kinds { get; } = kinds;
    }

    private sealed class Lowerer(PostScriptInstruction[] code)
    {
        private readonly List<PostScriptRegisterInstruction> _out = [];
        private readonly Dictionary<int, Join> _joins = [];
        private readonly Dictionary<int, int> _labels = [];
        private readonly List<(int At, int Target)> _jumps = [];
        private readonly Dictionary<long, int> _constantIds = [];
        private readonly List<double> _constants = [];
        private int _registers;
        private int _protectedCopy = int.MinValue;

        public PostScriptRegisterProgram? Run(int inputs, int outputs)
        {
            var registers = new List<int>(inputs);
            var kinds = new List<byte>(inputs);
            for (int i = 0; i < inputs; i++)
            {
                registers.Add(_registers++);
                kinds.Add(Real);
            }

            Stack? stack = new(registers, kinds);
            for (int pc = 0; pc <= code.Length; pc++)
            {
                if (_joins.TryGetValue(pc, out Join? join))
                {
                    if (stack is not null && !Edge(stack, pc))
                    {
                        return null;
                    }

                    stack = new Stack([.. join.Registers], [.. join.Kinds]);
                }

                _labels[pc] = _out.Count;
                if (pc == code.Length)
                {
                    break;
                }

                if (stack is null)
                {
                    continue;
                }

                if (!Step(code[pc], ref stack))
                {
                    return null;
                }

                if (_registers > MaxRegisters)
                {
                    return null;
                }
            }

            if (stack is null)
            {
                return null;
            }

            return Finish(stack, inputs, outputs);
        }

        private PostScriptRegisterProgram Finish(Stack stack, int inputs, int outputs)
        {
            int constants = _constants.Count;
            int sp = stack.Depth;
            bool repairs = sp != outputs;
            int first = Math.Max(sp - outputs, 0);
            int[] results = new int[outputs];
            for (int j = 0; j < outputs; j++)
            {
                int at = first + j;
                if (at < sp)
                {
                    repairs |= stack.Kinds[at] == Boolean;
                    results[j] = Index(stack.Registers[at], constants);
                }
                else
                {
                    results[j] = -1;
                }
            }

            var program = new PostScriptRegisterInstruction[_out.Count];
            for (int i = 0; i < program.Length; i++)
            {
                PostScriptRegisterInstruction instruction = _out[i];
                program[i] = instruction with
                {
                    Destination = Index(instruction.Destination, constants),
                    Left = Index(instruction.Left, constants),
                    Right = Index(instruction.Right, constants),
                };
            }

            foreach ((int at, int target) in _jumps)
            {
                program[at] = program[at] with { Target = _labels[target] };
            }

            return new PostScriptRegisterProgram(program, [.. _constants], constants, inputs, constants + _registers, results, repairs);
        }

        /// <summary>Constants take ids −1, −2, …, and registers 0 to <see cref="PostScriptRegisterProgram.RegisterCount"/>: constants come first.</summary>
        private static int Index(int id, int constants) => id < 0 ? -id - 1 : id + constants;

        private int Fresh() => _registers++;

        private int Constant(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            if (!_constantIds.TryGetValue(bits, out int id))
            {
                _constants.Add(value);
                id = -_constants.Count;
                _constantIds[bits] = id;
            }

            return id;
        }

        private bool TryConstant(int register, byte kind, out int value)
        {
            value = 0;
            if (register >= 0 || kind != Integer)
            {
                return false;
            }

            value = (int)_constants[-register - 1];
            return true;
        }

        private void Emit(PostScriptRegisterOpCode opCode, int destination = 0, int left = 0, int right = 0, byte leftKind = Real, byte rightKind = Real, bool repairs = false) =>
            _out.Add(new PostScriptRegisterInstruction(opCode, destination, left, right, leftKind, rightKind, repairs));

        /// <summary>The path from here reaches <paramref name="target"/>: moves its entries into the join's registers.</summary>
        private bool Edge(Stack stack, int target, int protectedRegister = int.MinValue)
        {
            if (!_joins.TryGetValue(target, out Join? join))
            {
                int[] registers = new int[stack.Depth];
                var seen = new HashSet<int>();
                for (int p = 0; p < registers.Length; p++)
                {
                    int register = stack.Registers[p];
                    registers[p] = register < 0 || !seen.Add(register) ? Fresh() : register;
                }

                join = new Join(registers, [.. stack.Kinds]);
                _joins[target] = join;
            }
            else
            {
                if (join.Registers.Length != stack.Depth)
                {
                    return false;
                }

                for (int p = 0; p < join.Kinds.Length; p++)
                {
                    byte merged = Merge(join.Kinds[p], stack.Kinds[p]);
                    if (merged == byte.MaxValue)
                    {
                        return false;
                    }

                    join.Kinds[p] = merged;
                }
            }

            Move(stack, join.Registers, protectedRegister);
            return true;
        }

        /// <summary>Moves every entry into <paramref name="targets"/> at once; a source another move overwrites is read first.</summary>
        private void Move(Stack stack, int[] targets, int protectedRegister)
        {
            var written = new HashSet<int>();
            for (int p = 0; p < targets.Length; p++)
            {
                if (stack.Registers[p] != targets[p])
                {
                    written.Add(targets[p]);
                }
            }

            if (written.Count == 0)
            {
                return;
            }

            var saved = new Dictionary<int, int>();
            if (written.Contains(protectedRegister))
            {
                int copy = Fresh();
                Emit(PostScriptRegisterOpCode.Move, copy, protectedRegister);
                saved[protectedRegister] = copy;
            }

            for (int p = 0; p < targets.Length; p++)
            {
                int source = stack.Registers[p];
                if (source != targets[p] && written.Contains(source) && !saved.ContainsKey(source))
                {
                    int copy = Fresh();
                    Emit(PostScriptRegisterOpCode.Move, copy, source);
                    saved[source] = copy;
                }
            }

            for (int p = 0; p < targets.Length; p++)
            {
                int source = stack.Registers[p];
                if (source != targets[p])
                {
                    Emit(PostScriptRegisterOpCode.Move, targets[p], saved.TryGetValue(source, out int copy) ? copy : source);
                }
            }

            for (int p = 0; p < targets.Length; p++)
            {
                stack.Registers[p] = targets[p];
            }

            if (saved.TryGetValue(protectedRegister, out int kept))
            {
                _protectedCopy = kept;
            }
        }

        private static byte Merge(byte left, byte right)
        {
            if (left == right)
            {
                return left;
            }

            return left == Boolean || right == Boolean ? byte.MaxValue : Number;
        }

        private bool Step(PostScriptInstruction instruction, ref Stack? stack)
        {
            Stack s = stack!;
            switch (instruction.OpCode)
            {
                case PostScriptOpCode.PushReal:
                case PostScriptOpCode.PushInteger:
                case PostScriptOpCode.PushBoolean:
                    if (s.Depth == PostScriptEvaluator.StackLimit)
                    {
                        return false;
                    }

                    s.Push(Constant(instruction.Literal), instruction.OpCode switch
                    {
                        PostScriptOpCode.PushReal => Real,
                        PostScriptOpCode.PushInteger => Integer,
                        _ => Boolean,
                    });
                    return true;

                case PostScriptOpCode.Jump:
                    if (!Edge(s, instruction.Target))
                    {
                        return false;
                    }

                    _jumps.Add((_out.Count, instruction.Target));
                    Emit(PostScriptRegisterOpCode.Jump);
                    stack = null;
                    return true;

                case PostScriptOpCode.JumpIfFalse:
                    {
                        if (s.Depth < 1)
                        {
                            return false;
                        }

                        (int condition, byte kind) = s.Pop();
                        _protectedCopy = int.MinValue;
                        if (!Edge(s, instruction.Target, condition))
                        {
                            return false;
                        }

                        if (_protectedCopy != int.MinValue)
                        {
                            condition = _protectedCopy;
                        }

                        _jumps.Add((_out.Count, instruction.Target));
                        Emit(PostScriptRegisterOpCode.JumpIfFalse, left: condition, repairs: kind != Boolean);
                        return true;
                    }

                case PostScriptOpCode.Dup:
                    {
                        if (s.Depth < 1 || s.Depth == PostScriptEvaluator.StackLimit)
                        {
                            return false;
                        }

                        int top = s.Depth - 1;
                        s.Push(s.Registers[top], s.Kinds[top]);
                        return true;
                    }

                case PostScriptOpCode.Exch:
                    {
                        if (s.Depth < 2)
                        {
                            return false;
                        }

                        int a = s.Depth - 2;
                        int b = s.Depth - 1;
                        (s.Registers[a], s.Registers[b]) = (s.Registers[b], s.Registers[a]);
                        (s.Kinds[a], s.Kinds[b]) = (s.Kinds[b], s.Kinds[a]);
                        return true;
                    }

                case PostScriptOpCode.Pop:
                    if (s.Depth < 1)
                    {
                        return false;
                    }

                    s.Pop();
                    return true;

                case PostScriptOpCode.Copy:
                    {
                        if (s.Depth < 1)
                        {
                            return false;
                        }

                        (int register, byte kind) = s.Pop();
                        if (!TryConstant(register, kind, out int count) || count < 0 || count > s.Depth || s.Depth + count > PostScriptEvaluator.StackLimit)
                        {
                            return false;
                        }

                        int start = s.Depth - count;
                        for (int i = 0; i < count; i++)
                        {
                            s.Push(s.Registers[start + i], s.Kinds[start + i]);
                        }

                        return true;
                    }

                case PostScriptOpCode.Index:
                    {
                        if (s.Depth < 1)
                        {
                            return false;
                        }

                        (int register, byte kind) = s.Pop();
                        if (!TryConstant(register, kind, out int index) || index < 0 || index >= s.Depth)
                        {
                            return false;
                        }

                        int at = s.Depth - 1 - index;
                        s.Push(s.Registers[at], s.Kinds[at]);
                        return true;
                    }

                case PostScriptOpCode.Roll:
                    {
                        if (s.Depth < 2)
                        {
                            return false;
                        }

                        (int shiftRegister, byte shiftKind) = s.Pop();
                        (int countRegister, byte countKind) = s.Pop();
                        if (!TryConstant(countRegister, countKind, out int count) || !TryConstant(shiftRegister, shiftKind, out int shift)
                            || count < 0 || count > s.Depth)
                        {
                            return false;
                        }

                        if (count > 1)
                        {
                            int by = (int)(((shift % (long)count) + count) % count);
                            Rotate(s.Registers, s.Depth - count, count, by);
                            Rotate(s.Kinds, s.Depth - count, count, by);
                        }

                        return true;
                    }

                default:
                    return Operator(instruction.OpCode, s);
            }
        }

        /// <summary>Rotates <paramref name="count"/> entries from <paramref name="start"/> by <paramref name="shift"/> toward the top, as the interpreter's roll does.</summary>
        private static void Rotate<T>(List<T> list, int start, int count, int shift)
        {
            if (shift == 0)
            {
                return;
            }

            list.Reverse(start, count);
            list.Reverse(start, shift);
            list.Reverse(start + shift, count - shift);
        }

        private bool Operator(PostScriptOpCode opCode, Stack s)
        {
            switch (opCode)
            {
                case PostScriptOpCode.Abs:
                case PostScriptOpCode.Neg:
                case PostScriptOpCode.Ceiling:
                case PostScriptOpCode.Floor:
                case PostScriptOpCode.Round:
                case PostScriptOpCode.Truncate:
                case PostScriptOpCode.Sqrt:
                case PostScriptOpCode.Sin:
                case PostScriptOpCode.Cos:
                case PostScriptOpCode.Ln:
                case PostScriptOpCode.Log:
                case PostScriptOpCode.Cvr:
                case PostScriptOpCode.Cvi:
                case PostScriptOpCode.Not:
                    return Unary(opCode, s);
                default:
                    return Binary(opCode, s);
            }
        }

        private bool Unary(PostScriptOpCode opCode, Stack s)
        {
            if (s.Depth < 1)
            {
                return false;
            }

            (int x, byte kind) = s.Pop();
            bool repairs = false;
            switch (opCode)
            {
                case PostScriptOpCode.Cvr:
                    if (kind == Boolean)
                    {
                        Emit(PostScriptRegisterOpCode.Repair, repairs: true);
                    }

                    s.Push(x, Real);
                    return true;

                case PostScriptOpCode.Cvi:
                    if (kind is Integer or Boolean)
                    {
                        if (kind == Boolean)
                        {
                            Emit(PostScriptRegisterOpCode.Repair, repairs: true);
                        }

                        s.Push(x, Integer);
                        return true;
                    }

                    return Write(s, PostScriptRegisterOpCode.Cvi, x, 0, Real, Real, repairs: false, Integer);

                case PostScriptOpCode.Not:
                    if (kind == Boolean)
                    {
                        return Write(s, PostScriptRegisterOpCode.Not, x, 0, Boolean, Real, repairs: false, Boolean);
                    }

                    if (!IntegerOperand(kind, booleanAllowed: false, ref repairs))
                    {
                        return false;
                    }

                    return Write(s, PostScriptRegisterOpCode.Not, x, 0, kind, Real, repairs, Integer);
            }

            if (kind == Boolean)
            {
                kind = Integer;
                repairs = true;
            }

            (PostScriptRegisterOpCode op, byte result) = opCode switch
            {
                PostScriptOpCode.Abs => (PostScriptRegisterOpCode.Abs, kind == Real ? Real : Number),
                PostScriptOpCode.Neg => (PostScriptRegisterOpCode.Neg, kind == Real ? Real : Number),
                PostScriptOpCode.Ceiling => (PostScriptRegisterOpCode.Ceiling, kind),
                PostScriptOpCode.Floor => (PostScriptRegisterOpCode.Floor, kind),
                PostScriptOpCode.Round => (PostScriptRegisterOpCode.Round, kind),
                PostScriptOpCode.Truncate => (PostScriptRegisterOpCode.Truncate, kind),
                PostScriptOpCode.Sqrt => (PostScriptRegisterOpCode.Sqrt, Real),
                PostScriptOpCode.Sin => (PostScriptRegisterOpCode.Sin, Real),
                PostScriptOpCode.Cos => (PostScriptRegisterOpCode.Cos, Real),
                PostScriptOpCode.Ln => (PostScriptRegisterOpCode.Ln, Real),
                _ => (PostScriptRegisterOpCode.Log, Real),
            };
            return Write(s, op, x, 0, kind, Real, repairs, result);
        }

        private bool Binary(PostScriptOpCode opCode, Stack s)
        {
            if (s.Depth < 2)
            {
                return false;
            }

            (int y, byte right) = s.Pop();
            (int x, byte left) = s.Pop();
            bool repairs = false;
            switch (opCode)
            {
                case PostScriptOpCode.Add:
                case PostScriptOpCode.Sub:
                case PostScriptOpCode.Mul:
                    Numeric(ref left, ref repairs);
                    Numeric(ref right, ref repairs);
                    return Write(s, Map(opCode), x, y, left, right, repairs, left == Real || right == Real ? Real : Number);

                case PostScriptOpCode.Div:
                case PostScriptOpCode.Atan:
                case PostScriptOpCode.Exp:
                    Numeric(ref left, ref repairs);
                    Numeric(ref right, ref repairs);
                    return Write(s, Map(opCode), x, y, left, right, repairs, Real);

                case PostScriptOpCode.Idiv:
                case PostScriptOpCode.Mod:
                case PostScriptOpCode.Bitshift:
                    if (!IntegerOperand(left, booleanAllowed: false, ref repairs) || !IntegerOperand(right, booleanAllowed: false, ref repairs))
                    {
                        return false;
                    }

                    return Write(s, Map(opCode), x, y, left, right, repairs, opCode == PostScriptOpCode.Idiv ? Number : Integer);

                case PostScriptOpCode.Eq:
                case PostScriptOpCode.Ne:
                    return Write(s, Map(opCode), x, y, left, right, repairs: false, Boolean);

                case PostScriptOpCode.Ge:
                case PostScriptOpCode.Gt:
                case PostScriptOpCode.Le:
                case PostScriptOpCode.Lt:
                    Numeric(ref left, ref repairs);
                    Numeric(ref right, ref repairs);
                    return Write(s, Map(opCode), x, y, left, right, repairs, Boolean);

                case PostScriptOpCode.And:
                case PostScriptOpCode.Or:
                case PostScriptOpCode.Xor:
                    {
                        bool logical = left == Boolean && right == Boolean;
                        if (!IntegerOperand(left, logical, ref repairs) || !IntegerOperand(right, logical, ref repairs))
                        {
                            return false;
                        }

                        return Write(s, Map(opCode), x, y, left, right, repairs, logical ? Boolean : Integer);
                    }

                default:
                    return false;
            }
        }

        private static PostScriptRegisterOpCode Map(PostScriptOpCode opCode) => opCode switch
        {
            PostScriptOpCode.Add => PostScriptRegisterOpCode.Add,
            PostScriptOpCode.Sub => PostScriptRegisterOpCode.Sub,
            PostScriptOpCode.Mul => PostScriptRegisterOpCode.Mul,
            PostScriptOpCode.Div => PostScriptRegisterOpCode.Div,
            PostScriptOpCode.Atan => PostScriptRegisterOpCode.Atan,
            PostScriptOpCode.Exp => PostScriptRegisterOpCode.Exp,
            PostScriptOpCode.Idiv => PostScriptRegisterOpCode.Idiv,
            PostScriptOpCode.Mod => PostScriptRegisterOpCode.Mod,
            PostScriptOpCode.Bitshift => PostScriptRegisterOpCode.Bitshift,
            PostScriptOpCode.Eq => PostScriptRegisterOpCode.Eq,
            PostScriptOpCode.Ne => PostScriptRegisterOpCode.Ne,
            PostScriptOpCode.Ge => PostScriptRegisterOpCode.Ge,
            PostScriptOpCode.Gt => PostScriptRegisterOpCode.Gt,
            PostScriptOpCode.Le => PostScriptRegisterOpCode.Le,
            PostScriptOpCode.Lt => PostScriptRegisterOpCode.Lt,
            PostScriptOpCode.And => PostScriptRegisterOpCode.And,
            PostScriptOpCode.Or => PostScriptRegisterOpCode.Or,
            _ => PostScriptRegisterOpCode.Xor,
        };

        /// <summary>The interpreter's Numeric: a boolean becomes the integer 1 or 0, which is a repair.</summary>
        private static void Numeric(ref byte kind, ref bool repairs)
        {
            if (kind == Boolean)
            {
                kind = Integer;
                repairs = true;
            }
        }

        /// <summary>The interpreter's ToInteger, known at compile time: false when it depends on the run.</summary>
        private static bool IntegerOperand(byte kind, bool booleanAllowed, ref bool repairs)
        {
            switch (kind)
            {
                case Integer:
                    return true;
                case Boolean:
                    repairs |= !booleanAllowed;
                    return true;
                case Real:
                    repairs = true;
                    return true;
                default:
                    return false;
            }
        }

        private bool Write(Stack s, PostScriptRegisterOpCode op, int left, int right, byte leftKind, byte rightKind, bool repairs, byte result)
        {
            int destination = Fresh();
            Emit(op, destination, left, right, leftKind, rightKind, repairs);
            s.Push(destination, result);
            return true;
        }
    }
}
