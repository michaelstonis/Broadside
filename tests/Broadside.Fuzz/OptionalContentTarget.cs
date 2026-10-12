using System.Globalization;
using System.Text;
using Broadside.Objects;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>optional-content</c> target (issue #76): random optional content properties, membership dictionaries and visibility
/// expression graphs (cyclic, deep, mistyped), then every optional content computation. A corpus file (input starting with
/// <c>%PDF-</c>) is opened as is; any other input drives a generator. Invariants: nothing throws in lenient mode, every walk
/// terminates, visibility is deterministic and the tracker's nesting returns to zero.
/// </summary>
/// <remarks>ISO 32000-2 §8.11.</remarks>
internal static class OptionalContentTarget
{
    private static readonly string[] Operators = ["/And", "/Or", "/Not", "/Xor", "42", "(Or)"];
    private static readonly string[] Policies = ["/AllOn", "/AnyOn", "/AnyOff", "/AllOff", "/Sometimes", "7"];
    private static readonly string[] BaseStates = ["/ON", "/OFF", "/Unchanged", "/Maybe"];
    private static readonly string[] Intents = ["/View", "/Design", "/All", "[]", "[/View /Design]", "/Other"];

    public static void Target(ReadOnlySpan<byte> data)
    {
        byte[] file = data.StartsWith("%PDF-"u8) ? data.ToArray() : Generate(data);
        using PdfDocument? document = FuzzTargets.OpenOrNull(file);
        if (document?.OptionalContent is not { } properties)
        {
            return;
        }

        PdfOptionalContentState state = properties.GetDefaultStates();
        _ = (properties.DefaultConfiguration.Order.Count, properties.DefaultConfiguration.RadioButtonGroups.Count, properties.DefaultConfiguration.Locked.Count);
        foreach (PdfOptionalContentConfiguration configuration in properties.Configurations)
        {
            PdfOptionalContentState alternate = properties.GetStates(configuration, state);
            _ = (configuration.Order.Count, configuration.AutoStates.Count);
            properties.ApplyAutoStates(alternate, PdfOptionalContentEvent.View, new PdfOptionalContentUsageContext { Zoom = 1, Language = "en-US", UserNames = ["user"] });
        }

        state = properties.ApplyUsageStates(state, PdfOptionalContentEvent.Print);
        var tracker = new PdfOptionalContentTracker(properties, state);
        var oc = new CosName("OC");
        int count = Math.Min(document.Trailer.TryGetValue(new CosName("Size"), out CosObject? size) && size is CosInteger { Value: > 0 and < 4096 } integer ? (int)integer.Value : 64, 4096);
        for (int number = 1; number < count; number++)
        {
            var reference = new CosReference(number, 0);
            bool visible = properties.IsVisible(reference, state);
            if (properties.IsVisible(reference, state) != visible)
            {
                throw new InvalidOperationException($"IsVisible gave two answers for object {number}.");
            }

            _ = properties.FindMembership(reference)?.VisibilityExpression?.ToString();
            tracker.BeginMarkedContent(oc, reference);
            if (document.Resolve(reference) is CosArray array)
            {
                state = state.Apply(array);
            }
        }

        for (int number = 1; number < count; number++)
        {
            tracker.EndMarkedContent();
        }

        if (tracker.Depth != 0 || !tracker.IsVisible)
        {
            throw new InvalidOperationException("The tracker did not return to the top level after balanced EMCs.");
        }
    }

    /// <summary>Builds a file whose optional content is drawn from <paramref name="data"/>.</summary>
    private static byte[] Generate(ReadOnlySpan<byte> data)
    {
        var input = new Input(data.ToArray());
        int objects = 2 + (input.Next() % 14);
        int first = 4;
        string Ref() => string.Create(CultureInfo.InvariantCulture, $"{first + (input.Next() % (objects + 1))} 0 R");
        string RefList(int max)
        {
            var builder = new StringBuilder("[");
            int length = input.Next() % (max + 1);
            for (int index = 0; index < length; index++)
            {
                builder.Append(Ref()).Append(' ');
            }

            return builder.Append(']').ToString();
        }

        string Expression(int depth)
        {
            var builder = new StringBuilder("[").Append(Operators[input.Next() % Operators.Length]);
            int operands = input.Next() % 4;
            for (int index = 0; index < operands; index++)
            {
                builder.Append(' ').Append(input.Next() % 3 == 0 && depth < 64 ? Expression(depth + 1) : Ref());
            }

            return builder.Append(']').ToString();
        }

        string Order(int depth)
        {
            var builder = new StringBuilder("[");
            int items = input.Next() % 4;
            for (int index = 0; index < items; index++)
            {
                builder.Append(' ').Append((input.Next() % 4) switch
                {
                    0 when depth < 40 => Order(depth + 1),
                    1 => "(Label)",
                    _ => Ref(),
                });
            }

            return builder.Append(']').ToString();
        }

        var bodies = new List<string>
        {
            $"<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs {RefList(objects)} /D << /BaseState {BaseStates[input.Next() % BaseStates.Length]} /ON {RefList(3)} /OFF {RefList(3)} /Intent {Intents[input.Next() % Intents.Length]} /Order {Order(0)} /RBGroups [{RefList(3)}] /Locked {RefList(2)} /AS [<< /Event /View /Category [/View /Zoom /Language /User] /OCGs {RefList(3)} >>] >> /Configs [<< /BaseState {BaseStates[input.Next() % BaseStates.Length]} /ON {RefList(3)} /Intent {Intents[input.Next() % Intents.Length]} >>] >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>",
        };
        for (int index = 0; index <= objects; index++)
        {
            bodies.Add((input.Next() % 6) switch
            {
                0 => $"<< /Type /OCG /Name (G{index}) /Intent {Intents[input.Next() % Intents.Length]} /Usage << /View << /ViewState /OFF >> /Zoom << /min 0.5 /max 2 >> /Language << /Lang (en) /Preferred /ON >> /User << /Type /Ind /Name [(user)] >> >> >>",
                1 => $"<< /Type /OCMD /VE {(input.Next() % 2 == 0 ? Expression(0) : Ref())} /OCGs {RefList(3)} /P {Policies[input.Next() % Policies.Length]} >>",
                2 => Expression(0),
                3 => $"<< /Type /OCMD /OCGs {(input.Next() % 2 == 0 ? Ref() : RefList(4))} /P {Policies[input.Next() % Policies.Length]} >>",
                4 => "[/ON " + Ref() + " /Toggle " + Ref() + " /OFF " + Ref() + "]",
                _ => input.Next() % 2 == 0 ? "null" : "(text)",
            });
        }

        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (int index = 0; index < bodies.Count; index++)
        {
            offsets.Add(text.Length);
            text.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{bodies[index]}\nendobj\n");
        }

        int xref = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 {bodies.Count + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            text.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {bodies.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(text.ToString());
    }

    /// <summary>The input as a stream of choices; past its end, zeros.</summary>
    private sealed class Input(byte[] data)
    {
        private int _position;

        public int Next() => _position < data.Length ? data[_position++] : 0;
    }
}
