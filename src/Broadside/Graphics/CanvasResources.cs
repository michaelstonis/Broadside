using Broadside.Objects;

namespace Broadside.Graphics;

/// <summary>
/// Names resources in the resource dictionary a canvas draws with: the same object in the same category always gets the same name,
/// a name already in the dictionary is never replaced, and an object the dictionary already holds keeps the name it has.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.8.3, Table 34: the operands of <c>cs</c>, <c>gs</c> and <c>Do</c> name entries of the <c>ColorSpace</c>,
/// <c>ExtGState</c> and <c>XObject</c> subdictionaries of the current resource dictionary. New names are the category's prefix and a
/// number (<c>CS1</c>, <c>GS1</c>, <c>Fm1</c>), the first one the subdictionary does not have. Objects are matched by their indirect
/// reference (a direct object by identity), so drawing one colour space twice adds one entry.
/// </remarks>
internal sealed class CanvasResources(PdfDocument document, CosDictionary resources)
{
    /// <summary>The <c>ExtGState</c> category.</summary>
    public static readonly CosName ExtGState = new("ExtGState");

    /// <summary>The <c>ColorSpace</c> category.</summary>
    public static readonly CosName ColorSpace = new("ColorSpace");

    /// <summary>The <c>XObject</c> category.</summary>
    public static readonly CosName XObject = new("XObject");

    private readonly Dictionary<(CosName Category, CosObject Value), CosName> _names = [];

    /// <summary>Returns the name <paramref name="value"/> has, or is given, in the <paramref name="category"/> subdictionary.</summary>
    /// <param name="category">The subdictionary's key.</param>
    /// <param name="prefix">The prefix of a new name.</param>
    /// <param name="value">The resource: an indirect reference, or a direct object.</param>
    /// <returns>The name.</returns>
    public CosName Name(CosName category, string prefix, CosObject value)
    {
        CosDictionary entries = Category(category);
        if (_names.TryGetValue((category, value), out CosName? known) && entries.TryGetValue(known, out CosObject? current) && Same(current, value))
        {
            return known;
        }

        foreach ((CosName key, CosObject existing) in entries)
        {
            if (Same(existing, value))
            {
                _names[(category, value)] = key;
                return key;
            }
        }

        CosName name;
        for (int number = 1; ; number++)
        {
            name = new CosName(prefix + number.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!entries.ContainsKey(name))
            {
                break;
            }
        }

        entries[name] = value;
        _names[(category, value)] = name;
        return name;
    }

    private static bool Same(CosObject existing, CosObject value) =>
        value is CosReference reference ? reference.Equals(existing) : ReferenceEquals(existing, value);

    /// <summary>Returns the category's subdictionary, created (direct) when it is missing or not a dictionary.</summary>
    private CosDictionary Category(CosName category)
    {
        if (resources.TryGetValue(category, out CosObject? value) && document.Resolve(value) is CosDictionary dictionary)
        {
            return dictionary;
        }

        var created = new CosDictionary();
        resources[category] = created;
        return created;
    }
}
