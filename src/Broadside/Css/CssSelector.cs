using System.Diagnostics.CodeAnalysis;

namespace Broadside.Css;

/// <summary>An element a <see cref="CssSelector"/> is matched against: its name, id, classes, attributes and place in the tree.</summary>
internal interface ICssElement
{
    /// <summary>Gets the element's local name, as written.</summary>
    string LocalName { get; }

    /// <summary>Gets the parent element, or <see langword="null"/> at the root.</summary>
    ICssElement? ParentElement { get; }

    /// <summary>Gets the element sibling just before this one, or <see langword="null"/> for the first child.</summary>
    ICssElement? PreviousElementSibling { get; }

    /// <summary>Returns the value of the attribute named <paramref name="name"/>, or <see langword="null"/> when it is absent.</summary>
    string? GetAttribute(string name);
}

/// <summary>
/// One complex selector: compound selectors joined by descendant (white space) and child (<c>&gt;</c>) combinators, matched right to
/// left. Compounds hold a type or <c>*</c>, ids, classes, attribute tests (<c>[a]</c>, <c>[a=v]</c>, <c>[a~=v]</c>, <c>[a|=v]</c>,
/// <c>[a^=v]</c>, <c>[a$=v]</c>, <c>[a*=v]</c>) and <c>:first-child</c>.
/// </summary>
/// <remarks>
/// Selectors Level 3 §5 (groups), §6 (simple selectors), §8 (combinators) and §9 (specificity). The sibling combinators (<c>+</c>,
/// <c>~</c>), other pseudo-classes and pseudo-elements are not read: a selector with one cannot be parsed, so its rule is dropped,
/// the way a browser drops a rule with a selector it does not know.
/// </remarks>
internal sealed class CssSelector
{
    private readonly Compound[] _compounds;
    private readonly bool[] _childCombinator;

    private CssSelector(Compound[] compounds, bool[] childCombinator)
    {
        _compounds = compounds;
        _childCombinator = childCombinator;
        int a = 0, b = 0, c = 0;
        foreach (Compound compound in compounds)
        {
            a += compound.Ids.Count;
            b += compound.Classes.Count + compound.Attributes.Count + (compound.FirstChild ? 1 : 0);
            c += compound.Type is null ? 0 : 1;
        }

        Specificity = (Math.Min(a, 255) << 16) | (Math.Min(b, 255) << 8) | Math.Min(c, 255);
    }

    /// <summary>Gets the specificity, (a, b, c) packed so that a larger value wins (§9).</summary>
    public int Specificity { get; }

    /// <summary>Parses one complex selector.</summary>
    /// <param name="text">The selector.</param>
    /// <param name="selector">The selector, when it can be read.</param>
    /// <returns><see langword="true"/> when <paramref name="text"/> is a selector of the supported subset.</returns>
    public static bool TryParse(ReadOnlySpan<char> text, [NotNullWhen(true)] out CssSelector? selector)
    {
        selector = null;
        var compounds = new List<Compound>();
        var child = new List<bool>();
        int position = 0;
        bool pendingChild = false;
        text = text.Trim();
        if (text.IsEmpty)
        {
            return false;
        }

        while (position < text.Length)
        {
            bool sawSpace = false;
            while (position < text.Length && char.IsWhiteSpace(text[position]))
            {
                position++;
                sawSpace = true;
            }

            if (position >= text.Length)
            {
                break;
            }

            char c = text[position];
            if (c == '>')
            {
                if (compounds.Count == 0 || pendingChild)
                {
                    return false;
                }

                pendingChild = true;
                position++;
                continue;
            }

            if (c is '+' or '~')
            {
                return false;
            }

            if (compounds.Count > 0 && !sawSpace && !pendingChild)
            {
                return false;
            }

            if (!TryParseCompound(text, ref position, out Compound? compound))
            {
                return false;
            }

            if (compounds.Count > 0)
            {
                child.Add(pendingChild);
            }

            compounds.Add(compound);
            pendingChild = false;
        }

        if (compounds.Count == 0 || pendingChild)
        {
            return false;
        }

        selector = new CssSelector([.. compounds], [.. child]);
        return true;
    }

    /// <summary>Returns whether <paramref name="element"/> matches this selector.</summary>
    /// <param name="element">The element.</param>
    /// <returns><see langword="true"/> on a match.</returns>
    public bool Matches(ICssElement element)
    {
        // Failed (element, compound) pairs are remembered, so descendant combinators cannot backtrack exponentially.
        HashSet<(ICssElement, int)>? failed = _compounds.Length > 2 ? new(ElementIndexComparer.Instance) : null;
        return Matches(element, _compounds.Length - 1, failed);
    }

    private bool Matches(ICssElement element, int index, HashSet<(ICssElement, int)>? failed)
    {
        if (failed is not null && failed.Contains((element, index)))
        {
            return false;
        }

        bool matched = MatchesUncached(element, index, failed);
        if (!matched)
        {
            _ = failed?.Add((element, index));
        }

        return matched;
    }

    private bool MatchesUncached(ICssElement element, int index, HashSet<(ICssElement, int)>? failed)
    {
        if (!_compounds[index].Matches(element))
        {
            return false;
        }

        if (index == 0)
        {
            return true;
        }

        if (_childCombinator[index - 1])
        {
            return element.ParentElement is { } parent && Matches(parent, index - 1, failed);
        }

        for (ICssElement? ancestor = element.ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
        {
            if (Matches(ancestor, index - 1, failed))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Compares (element, index) pairs by element reference.</summary>
    private sealed class ElementIndexComparer : IEqualityComparer<(ICssElement Element, int Index)>
    {
        public static ElementIndexComparer Instance { get; } = new();

        public bool Equals((ICssElement Element, int Index) x, (ICssElement Element, int Index) y) => ReferenceEquals(x.Element, y.Element) && x.Index == y.Index;

        public int GetHashCode((ICssElement Element, int Index) obj) => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Element), obj.Index);
    }

    private static bool TryParseCompound(ReadOnlySpan<char> text, ref int position, [NotNullWhen(true)] out Compound? compound)
    {
        compound = new Compound();
        bool any = false;
        if (text[position] == '*')
        {
            position++;
            any = true;
        }
        else if (IsNameStart(text[position]))
        {
            compound.Type = ReadName(text, ref position);
            any = true;
        }

        while (position < text.Length)
        {
            char c = text[position];
            if (c == '#' || c == '.')
            {
                position++;
                if (position >= text.Length || !IsNameCharacter(text[position]))
                {
                    compound = null;
                    return false;
                }

                string name = ReadName(text, ref position);
                (c == '#' ? compound.Ids : compound.Classes).Add(name);
                any = true;
            }
            else if (c == '[')
            {
                int close = text[position..].IndexOf(']');
                if (close < 0 || !AttributeTest.TryParse(text[(position + 1)..(position + close)], out AttributeTest? test))
                {
                    compound = null;
                    return false;
                }

                compound.Attributes.Add(test);
                position += close + 1;
                any = true;
            }
            else if (c == ':')
            {
                position++;
                string pseudo = position < text.Length && IsNameStart(text[position]) ? ReadName(text, ref position) : string.Empty;
                if (!pseudo.Equals("first-child", StringComparison.OrdinalIgnoreCase))
                {
                    compound = null;
                    return false;
                }

                compound.FirstChild = true;
                any = true;
            }
            else
            {
                break;
            }
        }

        if (!any)
        {
            compound = null;
        }

        return any;
    }

    private static bool IsNameStart(char c) => char.IsLetter(c) || c is '_' or '-' || c > 0x7F;

    private static bool IsNameCharacter(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' || c > 0x7F;

    private static string ReadName(ReadOnlySpan<char> text, ref int position)
    {
        int start = position;
        while (position < text.Length && IsNameCharacter(text[position]))
        {
            position++;
        }

        return text[start..position].ToString();
    }

    private sealed class Compound
    {
        public string? Type { get; set; }

        public List<string> Ids { get; } = [];

        public List<string> Classes { get; } = [];

        public List<AttributeTest> Attributes { get; } = [];

        public bool FirstChild { get; set; }

        public bool Matches(ICssElement element)
        {
            if (Type is not null && !string.Equals(Type, element.LocalName, StringComparison.Ordinal))
            {
                return false;
            }

            foreach (string id in Ids)
            {
                if (!string.Equals(element.GetAttribute("id"), id, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            if (Classes.Count > 0)
            {
                string? classes = element.GetAttribute("class");
                if (classes is null)
                {
                    return false;
                }

                foreach (string name in Classes)
                {
                    if (!ContainsWord(classes, name))
                    {
                        return false;
                    }
                }
            }

            foreach (AttributeTest test in Attributes)
            {
                if (!test.Matches(element))
                {
                    return false;
                }
            }

            return !FirstChild || element.PreviousElementSibling is null;
        }
    }

    /// <summary>Returns whether the white-space-separated list <paramref name="list"/> contains <paramref name="word"/>.</summary>
    internal static bool ContainsWord(string list, string word)
    {
        ReadOnlySpan<char> rest = list;
        while (!rest.IsEmpty)
        {
            rest = rest.TrimStart();
            int end = 0;
            while (end < rest.Length && !char.IsWhiteSpace(rest[end]))
            {
                end++;
            }

            if (rest[..end].Equals(word, StringComparison.Ordinal))
            {
                return true;
            }

            rest = rest[end..];
        }

        return false;
    }

    private sealed class AttributeTest
    {
        private static readonly System.Buffers.SearchValues<char> Operators = System.Buffers.SearchValues.Create("=~|^$*");

        private string _name = string.Empty;
        private string? _operator;
        private string _value = string.Empty;

        public static bool TryParse(ReadOnlySpan<char> text, [NotNullWhen(true)] out AttributeTest? test)
        {
            test = null;
            text = text.Trim();
            int operatorAt = text.IndexOfAny(Operators);
            ReadOnlySpan<char> name = (operatorAt < 0 ? text : text[..operatorAt]).Trim();
            if (name.IsEmpty)
            {
                return false;
            }

            var result = new AttributeTest { _name = name.ToString() };
            if (operatorAt >= 0)
            {
                int valueAt = text[operatorAt] == '=' ? operatorAt + 1 : operatorAt + 2;
                if (valueAt > text.Length || (text[operatorAt] != '=' && (operatorAt + 1 >= text.Length || text[operatorAt + 1] != '=')))
                {
                    return false;
                }

                result._operator = text[operatorAt..valueAt].ToString();
                ReadOnlySpan<char> value = text[valueAt..].Trim();
                if (value.Length >= 2 && (value[0] is '"' or '\'') && value[^1] == value[0])
                {
                    value = value[1..^1];
                }

                result._value = value.ToString();
            }

            test = result;
            return true;
        }

        public bool Matches(ICssElement element)
        {
            if (element.GetAttribute(_name) is not { } actual)
            {
                return false;
            }

            return _operator switch
            {
                null => true,
                "=" => actual == _value,
                "~=" => ContainsWord(actual, _value),
                "|=" => actual == _value || actual.StartsWith(_value + "-", StringComparison.Ordinal),
                "^=" => _value.Length > 0 && actual.StartsWith(_value, StringComparison.Ordinal),
                "$=" => _value.Length > 0 && actual.EndsWith(_value, StringComparison.Ordinal),
                _ => _value.Length > 0 && actual.Contains(_value, StringComparison.Ordinal),
            };
        }
    }
}
