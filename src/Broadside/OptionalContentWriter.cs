using Broadside.Objects;
using Broadside.Writing;

namespace Broadside;

/// <summary>Writes optional content: groups and membership dictionaries in the catalog's <c>OCProperties</c>, and <c>OC</c> entries.</summary>
/// <remarks>ISO 32000-2 §8.11.2, §8.11.3.3 and §8.11.4.2, Table 98.</remarks>
internal static class OptionalContentWriter
{
    private static readonly CosName[] PolicyNames = [.. Enum.GetNames<PdfVisibilityPolicy>().Select(static name => new CosName(name))];
    private static readonly CosName[] OperatorNames = [new("Group"), new("And"), new("Or"), new("Not")];

    /// <summary>Returns the indirect reference of a group or membership of <paramref name="target"/>, or throws.</summary>
    public static CosReference ReferenceOf(PdfDocument target, PdfDocument owner, CosReference? reference, string parameter)
    {
        if (!ReferenceEquals(owner, target))
        {
            throw new ArgumentException("The optional content belongs to another document.", parameter);
        }

        return reference ?? throw new ArgumentException("The optional content group or membership dictionary is not an indirect object; content refers to it by reference (ISO 32000-2 §8.11.3).", parameter);
    }

    /// <summary>Adds a group named <paramref name="name"/>, listed in <c>OCGs</c> and the default configuration's <c>Order</c>, OFF when not <paramref name="visible"/>.</summary>
    public static CosReference AddGroup(PdfDocument document, string name, bool visible)
    {
        CosDictionary properties = Properties(document, out bool created);
        CosReference reference = document.AddObject(new CosDictionary
        {
            [KnownNames.Type] = FileAndLayerNames.OCG,
            [FileAndLayerNames.Name] = Structure.StructureWriter.Text(name),
        });
        Array(document, properties, FileAndLayerNames.OCGs).Add(reference);
        if (document.Resolve(properties.TryGetValue(FileAndLayerNames.D, out CosObject? d) ? d : null) is not CosDictionary configuration)
        {
            configuration = [];
            properties[FileAndLayerNames.D] = configuration;
            created = true;
        }

        if (created || document.Resolve(configuration.TryGetValue(FileAndLayerNames.Order, out CosObject? order) ? order : null) is CosArray)
        {
            Array(document, configuration, FileAndLayerNames.Order).Add(reference);
        }

        bool baseOff = FileAndLayerNames.OFF.Equals(document.Resolve(configuration.TryGetValue(FileAndLayerNames.BaseState, out CosObject? state) ? state : null));
        if (visible == baseOff)
        {
            Array(document, configuration, visible ? FileAndLayerNames.ON : FileAndLayerNames.OFF).Add(reference);
        }

        document.RequireFeature(VersionedFeatures.OptionalContent);
        return reference;
    }

    /// <summary>Adds a membership dictionary over <paramref name="groups"/> with <paramref name="policy"/>.</summary>
    public static CosReference AddMembership(PdfDocument document, IEnumerable<PdfOptionalContentGroup> groups, PdfVisibilityPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy), policy, "Not a defined visibility policy.");
        }

        var array = new CosArray();
        foreach (PdfOptionalContentGroup group in groups)
        {
            ArgumentNullException.ThrowIfNull(group, nameof(groups));
            array.Add(ReferenceOf(document, group.Document, group.Reference, nameof(groups)));
        }

        document.RequireFeature(VersionedFeatures.OptionalContent);
        return document.AddObject(new CosDictionary
        {
            [KnownNames.Type] = FileAndLayerNames.OCMD,
            [FileAndLayerNames.OCGs] = array,
            [FileAndLayerNames.P] = PolicyNames[(int)policy],
        });
    }

    /// <summary>Adds a membership dictionary whose visibility <paramref name="expression"/> decides; its groups are also listed in <c>OCGs</c>.</summary>
    public static CosReference AddMembership(PdfDocument document, PdfVisibilityExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var groups = new CosArray();
        CosObject ve = Expression(document, expression.Operator == PdfVisibilityOperator.Group ? PdfVisibilityExpression.Or(expression) : expression, groups);
        document.RequireFeature(VersionedFeatures.OptionalContent);
        document.RequireFeature(VersionedFeatures.VisibilityExpression);
        return document.AddObject(new CosDictionary
        {
            [KnownNames.Type] = FileAndLayerNames.OCMD,
            [FileAndLayerNames.OCGs] = groups,
            [FileAndLayerNames.VE] = ve,
        });
    }

    /// <summary>Sets or removes (<paramref name="reference"/> <see langword="null"/>) the <c>OC</c> entry of <paramref name="target"/>.</summary>
    public static void Assign(PdfDocument document, CosDictionary target, CosReference? reference)
    {
        if (reference is null)
        {
            target.Remove(FileAndLayerNames.OC);
            return;
        }

        target[FileAndLayerNames.OC] = reference;
        document.RequireFeature(VersionedFeatures.OptionalContent);
    }

    private static CosArray Expression(PdfDocument document, PdfVisibilityExpression expression, CosArray groups)
    {
        if (expression.Operator == PdfVisibilityOperator.Group)
        {
            throw new InvalidOperationException("A group leaf is written by its operator.");
        }

        var array = new CosArray { OperatorNames[(int)expression.Operator] };
        foreach (PdfVisibilityExpression operand in expression.Operands)
        {
            if (operand.Operator == PdfVisibilityOperator.Group)
            {
                CosReference reference = ReferenceOf(document, operand.Group!.Document, operand.Group.Reference, nameof(expression));
                array.Add(reference);
                if (!groups.Contains(reference))
                {
                    groups.Add(reference);
                }
            }
            else
            {
                array.Add(Expression(document, operand, groups));
            }
        }

        return array;
    }

    private static CosDictionary Properties(PdfDocument document, out bool created)
    {
        CosDictionary catalog = document.Catalog;
        if (document.Resolve(catalog.TryGetValue(FileAndLayerNames.OCProperties, out CosObject? value) ? value : null) is CosDictionary existing)
        {
            created = false;
            return existing;
        }

        created = true;
        var properties = new CosDictionary();
        catalog[FileAndLayerNames.OCProperties] = properties;
        return properties;
    }

    private static CosArray Array(PdfDocument document, CosDictionary owner, CosName key)
    {
        if (document.Resolve(owner.TryGetValue(key, out CosObject? value) ? value : null) is CosArray array)
        {
            return array;
        }

        array = [];
        owner[key] = array;
        return array;
    }
}
