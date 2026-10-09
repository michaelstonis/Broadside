using Broadside.Caching;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>
/// The document's optional content properties: every optional content group, the default and alternate configurations, and the
/// visibility computation over them.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.11 (PDF 1.5), §8.11.4.2, Table 98. Read from the catalog's <c>OCProperties</c>; when it is absent there is no
/// optional content and every optional content structure is ignored (<see cref="PdfDocument.OptionalContent"/> is
/// <see langword="null"/>; all content is visible).
/// </para>
/// <para>
/// The group list (<c>OCGs</c>), each group's intents and each membership dictionary's compiled visibility are read once, when first
/// needed, and kept as a snapshot: <see cref="PdfDocument.OptionalContent"/> returns a new view after <c>OCProperties</c> is replaced.
/// Everything else (configurations, names, usage) reads the COS objects on every call. Nothing is ever written to the file:
/// a missing <c>D</c> reads as an empty configuration with a diagnostic.
/// </para>
/// <para>
/// Visibility (§8.11.2, §8.11.3): content marked with a group listed in <c>OCGs</c> is visible when the group is ON; with a
/// membership dictionary, when its visibility expression or policy says so. Content marked with anything else (a group not listed,
/// a dictionary that is neither) is not optional content and is visible. Nested optional content is visible only when every
/// level is (<see cref="PdfOptionalContentTracker"/>). <see cref="IsVisible"/> allocates nothing once a membership is compiled.
/// </para>
/// </remarks>
public sealed class PdfOptionalContentProperties
{
    private const int MaxExpressionDepth = 32;
    private const int MaxExpressionNodes = 4096;

    private readonly CosReference? _reference;
    private readonly PdfOptionalContentGroup[] _groups;
    private readonly Dictionary<CosDictionary, PdfOptionalContentGroup> _byDictionary = new(ReferenceEqualityComparer.Instance);
    private readonly OnceCache<CosDictionary, VisibilityProgram> _programs = new();
    private readonly CosDictionary _missingDefault = new();

    internal PdfOptionalContentProperties(PdfDocument document, CosDictionary dictionary, CosReference? reference)
    {
        Document = document;
        Dictionary = dictionary;
        _reference = reference;
        var groups = new List<PdfOptionalContentGroup>();
        if (ViewReading.Get(document, dictionary, FileAndLayerNames.OCGs) is CosArray array)
        {
            foreach (CosObject entry in array)
            {
                switch (document.Resolve(entry))
                {
                    case CosDictionary group when !_byDictionary.ContainsKey(group):
                        if (ViewReading.Name(document, group, KnownNames.Type) is { } type && !type.Equals(FileAndLayerNames.OCG))
                        {
                            Warn(DiagnosticCodes.OptionalContentGroupInvalid, $"An OCGs entry is a /{type.Value} dictionary, not an optional content group; it is ignored.", entry as CosReference);
                            break;
                        }

                        if (!group.ContainsKey(KnownNames.Type))
                        {
                            Warn(DiagnosticCodes.OptionalContentGroupInvalid, "An optional content group has no Type entry; it is read as an optional content group.", entry as CosReference);
                        }

                        var view = new PdfOptionalContentGroup(document, group, entry as CosReference, groups.Count);
                        groups.Add(view);
                        _byDictionary.Add(group, view);
                        break;
                    case CosDictionary or CosNull:
                        break;
                    default:
                        Warn(DiagnosticCodes.OptionalContentGroupInvalid, "An OCGs entry is not a dictionary; it is ignored.", entry as CosReference);
                        break;
                }
            }
        }
        else
        {
            Warn(DiagnosticCodes.OptionalContentGroupsMissing, "The optional content properties have no OCGs array; no group is optional content and all content is visible.", null);
        }

        _groups = [.. groups];
        GroupIntents = [.. _groups.Select(group => PdfOptionalContentGroup.ReadIntents(document, group.Dictionary))];
    }

    /// <summary>Gets the optional content properties dictionary (<c>OCProperties</c>).</summary>
    /// <remarks>ISO 32000-2 §8.11.4.2, Table 98.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets every optional content group, in the order of <c>OCGs</c> (duplicates once).</summary>
    /// <remarks>ISO 32000-2 §8.11.4.2, Table 98: every optional content group shall be listed.</remarks>
    public IReadOnlyList<PdfOptionalContentGroup> Groups => _groups;

    /// <summary>Gets the default configuration (<c>D</c>), which sets the initial states.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.2, Table 98 (required). When missing, an empty configuration (all defaults) with an <c>OptionalContentConfigMissing</c> diagnostic.</remarks>
    public PdfOptionalContentConfiguration DefaultConfiguration
    {
        get
        {
            if (ViewReading.Get(Document, Dictionary, FileAndLayerNames.D) is CosDictionary configuration)
            {
                return new PdfOptionalContentConfiguration(this, configuration, ViewReading.ReferenceOf(Dictionary, FileAndLayerNames.D), isDefault: true);
            }

            Warn(DiagnosticCodes.OptionalContentConfigMissing, "The optional content properties have no default configuration D; an empty one is used (every group ON).", null);
            return new PdfOptionalContentConfiguration(this, _missingDefault, null, isDefault: true);
        }
    }

    /// <summary>Gets the alternate configurations (<c>Configs</c>).</summary>
    /// <remarks>ISO 32000-2 §8.11.4.2, Table 98.</remarks>
    public IReadOnlyList<PdfOptionalContentConfiguration> Configurations => ViewReading.Get(Document, Dictionary, FileAndLayerNames.Configs) is CosArray array
        ? [.. array.Where(entry => Document.Resolve(entry) is CosDictionary).Select(entry => new PdfOptionalContentConfiguration(this, (CosDictionary)Document.Resolve(entry), entry as CosReference, isDefault: false))]
        : [];

    /// <summary>Gets the document.</summary>
    internal PdfDocument Document { get; }

    /// <summary>Gets each group's intents by ordinal, read once.</summary>
    internal CosName[][] GroupIntents { get; }

    /// <summary>Gets <c>D</c>, or an empty dictionary when it is missing (no diagnostic).</summary>
    internal CosDictionary DefaultConfigurationDictionary =>
        ViewReading.Get(Document, Dictionary, FileAndLayerNames.D) as CosDictionary ?? _missingDefault;

    /// <summary>Returns the listed group <paramref name="value"/> is (or refers to), or <see langword="null"/>.</summary>
    /// <param name="value">A group dictionary or a reference to one.</param>
    /// <returns>The group, or <see langword="null"/> when the value is not a group listed in <c>OCGs</c>.</returns>
    /// <remarks>ISO 32000-2 §8.11.4.2, Table 98.</remarks>
    public PdfOptionalContentGroup? FindGroup(CosObject? value) =>
        Document.Resolve(value) is CosDictionary dictionary && _byDictionary.TryGetValue(dictionary, out PdfOptionalContentGroup? group) ? group : null;

    /// <summary>Returns a view over the membership dictionary <paramref name="value"/> is (or refers to), or <see langword="null"/>.</summary>
    /// <param name="value">A membership dictionary or a reference to one.</param>
    /// <returns>The view, or <see langword="null"/> when the value is not a dictionary of type <c>OCMD</c>.</returns>
    /// <remarks>ISO 32000-2 §8.11.2.2, Table 97.</remarks>
    public PdfOptionalContentMembership? FindMembership(CosObject? value) =>
        Document.Resolve(value) is CosDictionary dictionary && ViewReading.HasType(Document, dictionary, FileAndLayerNames.OCMD)
            ? new PdfOptionalContentMembership(this, dictionary, value as CosReference)
            : null;

    /// <summary>Computes the initial states from the default configuration: <c>BaseState</c>, then <c>ON</c>, then <c>OFF</c>.</summary>
    /// <returns>The states every processor starts from, with the default configuration's intents.</returns>
    /// <remarks>
    /// ISO 32000-2 §8.11.4.5 steps a and b. Usage and auto-state entries are not applied (that is for interactive processors and
    /// an explicit step: <see cref="ApplyAutoStates"/>). A <c>BaseState</c> other than ON in <c>D</c> is reported and honoured
    /// (OFF) or read as ON (Unchanged); a group in both <c>ON</c> and <c>OFF</c> is reported and ends OFF; an <c>Intent</c> other
    /// than View in <c>D</c> is reported and honoured.
    /// </remarks>
    public PdfOptionalContentState GetDefaultStates() => ComputeStates(DefaultConfiguration, current: null);

    /// <summary>Computes the states <paramref name="configuration"/> gives when applied over <paramref name="current"/>.</summary>
    /// <param name="configuration">The default configuration or one of <see cref="Configurations"/>.</param>
    /// <param name="current">The states a <c>BaseState</c> of Unchanged keeps; the default states when <see langword="null"/>.</param>
    /// <returns>The new states, with the configuration's intents.</returns>
    /// <remarks>ISO 32000-2 §8.11.4.3, Table 99, and §8.11.4.5.</remarks>
    public PdfOptionalContentState GetStates(PdfOptionalContentConfiguration configuration, PdfOptionalContentState? current = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (current is not null)
        {
            CheckState(current);
        }

        return ComputeStates(configuration, current);
    }

    /// <summary>Returns whether content marked with <paramref name="ocgOrOcmd"/> is visible under <paramref name="state"/>.</summary>
    /// <param name="ocgOrOcmd">
    /// The value of an <c>OC</c> entry (XObject, annotation) or the property list of an <c>/OC</c> marked-content section: a group,
    /// a membership dictionary, or a reference to one. <see langword="null"/> and anything else is not optional content: visible.
    /// </param>
    /// <param name="state">States from this properties view.</param>
    /// <returns><see langword="false"/> only when the content is optional and hidden.</returns>
    /// <exception cref="ArgumentException"><paramref name="state"/> was computed by another properties view.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.11.2, §8.11.3.2 and §8.11.3.3. A group listed in <c>OCGs</c> gives its state, or has no effect when its intent
    /// does not count (§8.11.2.3). A membership dictionary evaluates its visibility expression, else its policy over its groups; a
    /// malformed expression falls back to the policy (<c>VisibilityExpressionInvalid</c>). A group dictionary not listed in
    /// <c>OCGs</c> is not optional content (<c>OptionalContentGroupNotListed</c>). Allocation-free once the membership is compiled.
    /// </remarks>
    public bool IsVisible(CosObject? ocgOrOcmd, PdfOptionalContentState state)
    {
        CheckState(state);
        if (Document.Resolve(ocgOrOcmd) is not CosDictionary dictionary)
        {
            return true;
        }

        if (_byDictionary.TryGetValue(dictionary, out PdfOptionalContentGroup? group))
        {
            return !state.Effective[group.Ordinal] || state.On[group.Ordinal];
        }

        VisibilityProgram program = _programs.GetOrCreate(
            dictionary,
            (Properties: this, Reference: ocgOrOcmd as CosReference),
            static (dictionary, context) => new Created<VisibilityProgram>(context.Properties.Compile(dictionary, context.Reference)),
            static (_, _) => VisibilityProgram.AlwaysVisible);
        return program.Evaluate(state.On, state.Effective);
    }

    /// <summary>
    /// Applies the auto-state (<c>AS</c>) entries of the state's configuration whose <c>Event</c> is <paramref name="usageEvent"/>:
    /// each listed group's usage categories give a recommended state, and a group is ON only when all its recommendations are.
    /// </summary>
    /// <param name="state">The current states.</param>
    /// <param name="usageEvent">View when displaying, Print or Export for the duration of those operations.</param>
    /// <param name="context">The external factors: zoom, language, user. A category whose factor is not supplied recommends nothing.</param>
    /// <returns>The new states; groups without any recommendation keep theirs.</returns>
    /// <remarks>
    /// ISO 32000-2 §8.11.4.4 and §8.11.4.5. Categories: View, Print, Export (the usage's state; Print without PrintState leaves the
    /// group unchanged), Zoom (ON when min ≤ zoom &lt; max), User (ON on an exact name match), Language (ON on an exact match; else,
    /// when no group of the entry matches exactly, ON for partial matches that are Preferred; otherwise OFF).
    /// </remarks>
    public PdfOptionalContentState ApplyAutoStates(PdfOptionalContentState state, PdfOptionalContentEvent usageEvent, PdfOptionalContentUsageContext context)
    {
        CheckState(state);
        ArgumentNullException.ThrowIfNull(context);
        var recommendation = new bool?[_groups.Length];
        foreach (PdfOptionalContentUsageApplication application in state.Configuration.AutoStates)
        {
            if (application.Event != usageEvent)
            {
                continue;
            }

            IReadOnlyList<PdfOptionalContentGroup> groups = application.Groups;
            IReadOnlyList<CosName> categories = application.Categories;
            bool anyExactLanguage = context.Language is { } wanted
                && groups.Any(group => group.Usage?.Language is { } language && string.Equals(language, wanted, StringComparison.OrdinalIgnoreCase));
            foreach (PdfOptionalContentGroup group in groups)
            {
                PdfOptionalContentUsage? usage = group.Usage;
                foreach (CosName category in categories)
                {
                    bool? recommended = Recommend(category.Value, usage, context, anyExactLanguage);
                    if (recommended is { } value)
                    {
                        recommendation[group.Ordinal] = (recommendation[group.Ordinal] ?? true) && value;
                    }
                }
            }
        }

        bool[] states = state.CopyStates();
        for (int ordinal = 0; ordinal < states.Length; ordinal++)
        {
            if (recommendation[ordinal] is { } value)
            {
                states[ordinal] = value;
            }
        }

        return new PdfOptionalContentState(this, state.Configuration, states, [.. state.Intents]);
    }

    /// <summary>
    /// Applies every group's usage state for <paramref name="usageEvent"/> (<c>ViewState</c>, <c>PrintState</c> or <c>ExportState</c>)
    /// whether or not an auto-state entry asks for it, as pdf.js and PDFBox do. Not the specification's behaviour: for engine parity only.
    /// </summary>
    /// <param name="state">The current states.</param>
    /// <param name="usageEvent">Which usage state to apply.</param>
    /// <returns>The new states.</returns>
    /// <remarks>ISO 32000-2 §8.11.4.4 says usage states apply only through <c>AS</c> ("If no AS entry is present, states shall not be automatically adjusted").</remarks>
    public PdfOptionalContentState ApplyUsageStates(PdfOptionalContentState state, PdfOptionalContentEvent usageEvent)
    {
        CheckState(state);
        bool[] states = state.CopyStates();
        foreach (PdfOptionalContentGroup group in _groups)
        {
            PdfOptionalContentUsage? usage = group.Usage;
            bool? value = usageEvent switch
            {
                PdfOptionalContentEvent.View => usage?.ViewState,
                PdfOptionalContentEvent.Print => usage?.PrintState,
                _ => usage?.ExportState,
            };
            if (value is { } on)
            {
                states[group.Ordinal] = on;
            }
        }

        return new PdfOptionalContentState(this, state.Configuration, states, [.. state.Intents]);
    }

    /// <summary>Reads a visibility policy name; <see langword="null"/> when absent or unknown.</summary>
    internal static PdfVisibilityPolicy? ReadPolicy(CosName? name) => name?.Value switch
    {
        "AllOn" => PdfVisibilityPolicy.AllOn,
        "AnyOn" => PdfVisibilityPolicy.AnyOn,
        "AnyOff" => PdfVisibilityPolicy.AnyOff,
        "AllOff" => PdfVisibilityPolicy.AllOff,
        _ => null,
    };

    /// <summary>
    /// Reads a group or an array of groups, keeping listed groups in order. Null and deleted entries are skipped silently; a group
    /// dictionary not listed in <c>OCGs</c> is reported (<c>OptionalContentGroupNotListed</c>) and skipped; anything else is
    /// reported (<c>OptionalContentGroupInvalid</c>) and skipped.
    /// </summary>
    internal IReadOnlyList<PdfOptionalContentGroup> ReadGroupList(CosObject? value) => ReadGroupList(value, null);

    /// <inheritdoc cref="ReadGroupList(CosObject?)"/>
    internal IReadOnlyList<PdfOptionalContentGroup> ReadGroupList(CosObject? value, CosReference? owner)
    {
        var groups = new List<PdfOptionalContentGroup>();
        switch (value)
        {
            case CosDictionary:
                Add(value);
                break;
            case CosArray array:
                foreach (CosObject entry in array)
                {
                    Add(entry);
                }

                break;
        }

        return groups;

        void Add(CosObject entry)
        {
            switch (Document.Resolve(entry))
            {
                case CosNull:
                    break;
                case CosDictionary dictionary when _byDictionary.TryGetValue(dictionary, out PdfOptionalContentGroup? group):
                    groups.Add(group);
                    break;
                case CosDictionary dictionary when !ViewReading.HasType(Document, dictionary, FileAndLayerNames.OCMD):
                    Warn(DiagnosticCodes.OptionalContentGroupNotListed, "An optional content group is not listed in OCProperties OCGs; it is not optional content.", entry as CosReference ?? owner);
                    break;
                default:
                    Warn(DiagnosticCodes.OptionalContentGroupInvalid, "An entry that shall be an optional content group is not one; it is ignored.", entry as CosReference ?? owner);
                    break;
            }
        }
    }

    /// <summary>Parses a visibility expression; <see langword="null"/> (with a diagnostic) when it is unusable.</summary>
    internal PdfVisibilityExpression? ParseExpression(CosArray array, CosReference? owner)
    {
        int nodes = 0;
        var path = new HashSet<CosArray>(ReferenceEqualityComparer.Instance);
        string? problem = null;
        PdfVisibilityExpression? expression = ParseNode(array, 0);
        if (problem is not null)
        {
            Warn(DiagnosticCodes.VisibilityExpressionInvalid, problem, owner);
        }

        return expression;

        PdfVisibilityExpression? ParseNode(CosArray node, int depth)
        {
            if (depth >= MaxExpressionDepth || ++nodes > MaxExpressionNodes)
            {
                problem = "A visibility expression nests too deeply; the OCGs and P entries are used instead.";
                return null;
            }

            if (!path.Add(node))
            {
                problem = "A visibility expression contains itself; the OCGs and P entries are used instead.";
                return null;
            }

            try
            {
                PdfVisibilityOperator? @operator = node.Count > 0 && Document.Resolve(node[0]) is CosName name
                    ? name.Value switch
                    {
                        "And" => PdfVisibilityOperator.And,
                        "Or" => PdfVisibilityOperator.Or,
                        "Not" => PdfVisibilityOperator.Not,
                        _ => null,
                    }
                    : null;
                if (@operator is null)
                {
                    problem = "A visibility expression does not start with And, Or or Not; the OCGs and P entries are used instead.";
                    return null;
                }

                var operands = new List<PdfVisibilityExpression>();
                for (int index = 1; index < node.Count; index++)
                {
                    switch (Document.Resolve(node[index]))
                    {
                        case CosNull:
                            break;
                        case CosArray nested:
                            PdfVisibilityExpression? operand = ParseNode(nested, depth + 1);
                            if (operand is null)
                            {
                                return null;
                            }

                            operands.Add(operand);
                            break;
                        case CosDictionary dictionary when _byDictionary.TryGetValue(dictionary, out PdfOptionalContentGroup? group):
                            operands.Add(new PdfVisibilityExpression(PdfVisibilityOperator.Group, group, []));
                            break;
                        case CosDictionary:
                            Warn(DiagnosticCodes.OptionalContentGroupNotListed, "A visibility expression names a group not listed in OCProperties OCGs; the operand has no effect.", node[index] as CosReference ?? owner);
                            break;
                        default:
                            problem ??= "A visibility expression operand is neither a group nor an expression; it is ignored.";
                            break;
                    }
                }

                if (@operator == PdfVisibilityOperator.Not && node.Count != 2)
                {
                    problem ??= "A Not visibility expression shall have exactly one operand; the first one is used.";
                    if (operands.Count > 1)
                    {
                        operands.RemoveRange(1, operands.Count - 1);
                    }
                }

                return new PdfVisibilityExpression(@operator.Value, null, operands);
            }
            finally
            {
                path.Remove(node);
            }
        }
    }

    /// <summary>Compiles the visibility of a dictionary that is not a listed group.</summary>
    private VisibilityProgram Compile(CosDictionary dictionary, CosReference? reference)
    {
        CosName? type = ViewReading.Name(Document, dictionary, KnownNames.Type);
        if (type is not null && type.Equals(FileAndLayerNames.OCG))
        {
            Warn(DiagnosticCodes.OptionalContentGroupNotListed, "Content refers to an optional content group not listed in OCProperties OCGs; it is not optional content and is visible.", reference);
            return VisibilityProgram.AlwaysVisible;
        }

        if (type is null || !type.Equals(FileAndLayerNames.OCMD))
        {
            return VisibilityProgram.AlwaysVisible;
        }

        if (ViewReading.Get(Document, dictionary, FileAndLayerNames.VE) is CosArray expression && ParseExpression(expression, reference) is { } parsed)
        {
            return VisibilityProgram.Compile(parsed);
        }

        CosName? policyName = ViewReading.Name(Document, dictionary, FileAndLayerNames.P);
        PdfVisibilityPolicy? policy = ReadPolicy(policyName);
        if (policyName is not null && policy is null)
        {
            Warn(DiagnosticCodes.VisibilityPolicyInvalid, $"The visibility policy /{policyName.Value} is not AllOn, AnyOn, AnyOff or AllOff; AnyOn is used.", reference);
        }

        return VisibilityProgram.Compile(ReadGroupList(ViewReading.Get(Document, dictionary, FileAndLayerNames.OCGs), reference), policy ?? PdfVisibilityPolicy.AnyOn);
    }

    private PdfOptionalContentState ComputeStates(PdfOptionalContentConfiguration configuration, PdfOptionalContentState? current)
    {
        CosReference? where = configuration.Reference ?? _reference;
        PdfOptionalContentBaseState baseState = configuration.BaseState;
        CosName? written = ViewReading.Name(Document, configuration.Dictionary, FileAndLayerNames.BaseState);
        if (configuration.IsDefault && written is not null && baseState != PdfOptionalContentBaseState.On)
        {
            Warn(DiagnosticCodes.OptionalContentBaseStateInvalid, $"The default configuration's BaseState shall be ON, not /{written.Value}; {(baseState == PdfOptionalContentBaseState.Off ? "OFF is honoured" : "ON is used")}.", where);
            if (baseState == PdfOptionalContentBaseState.Unchanged)
            {
                baseState = PdfOptionalContentBaseState.On;
            }
        }

        IReadOnlyList<CosName> intents = configuration.Intents;
        if (configuration.IsDefault && ViewReading.Get(Document, configuration.Dictionary, FileAndLayerNames.Intent) is not null
            && !(intents.Count == 1 && intents[0].Equals(FileAndLayerNames.View)))
        {
            Warn(DiagnosticCodes.OptionalContentIntentInvalid, "The default configuration's Intent shall be View; the written intent is honoured.", where);
        }

        bool[] states;
        if (baseState == PdfOptionalContentBaseState.Unchanged)
        {
            states = (current ?? ComputeStates(DefaultConfiguration, null)).CopyStates();
        }
        else
        {
            states = new bool[_groups.Length];
            Array.Fill(states, baseState == PdfOptionalContentBaseState.On);
        }

        var on = new HashSet<int>();
        foreach (PdfOptionalContentGroup group in configuration.On)
        {
            states[group.Ordinal] = true;
            on.Add(group.Ordinal);
        }

        foreach (PdfOptionalContentGroup group in configuration.Off)
        {
            if (on.Contains(group.Ordinal))
            {
                Warn(DiagnosticCodes.OptionalContentGroupOnAndOff, $"The group '{group.Name}' is in both the ON and the OFF array of a configuration; OFF is applied last.", where);
            }

            states[group.Ordinal] = false;
        }

        return new PdfOptionalContentState(this, configuration, states, [.. intents]);
    }

    private void CheckState(PdfOptionalContentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!ReferenceEquals(state.Properties, this))
        {
            throw new ArgumentException("The state was computed by another optional content properties view.", nameof(state));
        }
    }

    private void Warn(string code, string message, CosReference? reference) => ViewReading.Warn(Document, code, message, reference ?? _reference);

    private static bool? Recommend(string category, PdfOptionalContentUsage? usage, PdfOptionalContentUsageContext context, bool anyExactLanguage)
    {
        if (usage is null)
        {
            return null;
        }

        switch (category)
        {
            case "View":
                return usage.ViewState;
            case "Print":
                return usage.PrintState;
            case "Export":
                return usage.ExportState;
            case "Zoom":
                return usage.ZoomMin is { } min && context.Zoom is { } zoom ? zoom >= min && zoom < (usage.ZoomMax ?? double.PositiveInfinity) : null;
            case "User":
                IReadOnlyList<string> names = usage.UserNames;
                return names.Count > 0 && context.UserNames is { } users ? names.Any(users.Contains) : null;
            case "Language":
                if (usage.Language is not { } language || context.Language is not { } wanted)
                {
                    return null;
                }

                if (string.Equals(language, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return !anyExactLanguage && usage.LanguagePreferred
                    && string.Equals(language.Split('-')[0], wanted.Split('-')[0], StringComparison.OrdinalIgnoreCase);
            default:
                return null;
        }
    }
}
