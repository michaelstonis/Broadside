using System.Reflection;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// One overload style for the open and save entry points: every asynchronous method mirrors a synchronous overload (the same
/// parameters) plus a trailing defaulted cancellation token, options are overloads and never optional parameters, and a save to a
/// path is all or nothing. ISO 32000-2 §7.5 (file structure); the spec's memory and I/O decision (<c>Async</c> only at the open and
/// save boundaries).
/// </summary>
public sealed class AsyncEntryPointTests
{
    private static readonly Type[] EntryPointTypes = [typeof(PdfDocument), typeof(PdfEngine)];

    [Fact]
    public void Every_synchronous_open_and_save_overload_has_an_asynchronous_counterpart_with_a_trailing_cancellation_token()
    {
        List<string> missing = [];
        foreach (Type type in EntryPointTypes)
        {
            foreach (MethodInfo method in EntryPoints(type, "Open", "Save"))
            {
                Type[] expected = [.. method.GetParameters().Select(p => p.ParameterType), typeof(CancellationToken)];
                MethodInfo? counterpart = Find(type, method.Name + "Async", method.IsStatic, expected);
                if (counterpart is null)
                {
                    missing.Add($"{Describe(method)} has no {method.Name}Async({string.Join(", ", expected.Select(t => t.Name))})");
                }
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_asynchronous_open_and_save_overload_mirrors_a_synchronous_one_except_the_network_opens()
    {
        List<string> unmatched = [];
        foreach (Type type in EntryPointTypes)
        {
            foreach (MethodInfo method in EntryPoints(type, "OpenAsync", "SaveAsync"))
            {
                ParameterInfo[] parameters = method.GetParameters();
                Assert.True(parameters.Length > 0 && parameters[^1].ParameterType == typeof(CancellationToken), $"{Describe(method)} does not end in a CancellationToken.");
                Assert.True(parameters[^1].IsOptional && parameters[^1].Name == "cancellationToken", $"{Describe(method)}: the token is not an optional parameter named cancellationToken.");

                // An open through an HttpClient has no synchronous form: a blocking read of the network is what the rule exists to avoid.
                if (parameters.Any(p => p.ParameterType == typeof(HttpClient)))
                {
                    continue;
                }

                Type[] expected = [.. parameters.Take(parameters.Length - 1).Select(p => p.ParameterType)];
                if (Find(type, method.Name[..^"Async".Length], method.IsStatic, expected) is null)
                {
                    unmatched.Add(Describe(method));
                }
            }
        }

        Assert.Empty(unmatched);
    }

    [Fact]
    public void Options_are_overloads_never_optional_parameters()
    {
        List<string> mixed = [];
        foreach (Type type in EntryPointTypes)
        {
            foreach (MethodInfo method in EntryPoints(type, "Open", "Save", "OpenAsync", "SaveAsync"))
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    bool trailingToken = parameter.ParameterType == typeof(CancellationToken) && parameter.Position == method.GetParameters().Length - 1;
                    if (parameter.IsOptional && !trailingToken)
                    {
                        mixed.Add($"{Describe(method)}: {parameter.Name}");
                    }
                }
            }
        }

        Assert.Empty(mixed);
    }

    [Fact]
    public async Task Each_asynchronous_open_gives_the_same_document_as_its_synchronous_form()
    {
        string path = Corpus.Path("flate-stream.pdf");
        byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        CancellationToken token = TestContext.Current.CancellationToken;

        using PdfDocument fromPath = await PdfDocument.OpenAsync(path, token);
        using PdfDocument fromPathWithOptions = await PdfDocument.OpenAsync(path, new PdfOptions(), token);
        using PdfDocument fromBytes = await PdfDocument.OpenAsync(bytes, token);
        using PdfDocument fromBytesWithOptions = await PdfDocument.OpenAsync(bytes, new PdfOptions(), token);
        using PdfDocument fromEngine = await new PdfEngine().OpenAsync(path, new PdfPassword("unused"), token);
        using PdfDocument fromEngineBytes = await new PdfEngine().OpenAsync(bytes, new PdfPassword("unused"), token);
        using var stream = new MemoryStream(bytes);
        using PdfDocument fromStream = await new PdfEngine().OpenAsync(stream, new PdfPassword("unused"), token);
        using PdfDocument expected = await PdfDocument.OpenAsync(path, token);

        foreach (PdfDocument document in new[] { fromPath, fromPathWithOptions, fromBytes, fromBytesWithOptions, fromEngine, fromEngineBytes, fromStream })
        {
            Assert.Equal(expected.Pages.Count, document.Pages.Count);
        }
    }

    [Fact]
    public async Task A_canceled_token_stops_an_open_before_it_starts()
    {
        string path = Corpus.Path("flate-stream.pdf");
        var canceled = new CancellationToken(canceled: true);
        byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PdfDocument.OpenAsync(path, canceled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PdfDocument.OpenAsync(bytes, canceled));
    }

    [Fact]
    public async Task SaveAsync_to_a_path_writes_the_same_bytes_as_Save()
    {
        using PdfDocument source = await PdfDocument.OpenAsync(Corpus.Path("flate-stream.pdf"), TestContext.Current.CancellationToken);
        string directory = NewDirectory();
        try
        {
            string synchronous = Path.Combine(directory, "sync.pdf");
            string asynchronous = Path.Combine(directory, "async.pdf");
            string defaults = Path.Combine(directory, "defaults.pdf");
            var options = new PdfSaveOptions().WithUpdateModificationDate(false);

#pragma warning disable CA1849 // The synchronous path is what the asynchronous one is compared with.
            source.Save(synchronous, options);
#pragma warning restore CA1849
            await source.SaveAsync(asynchronous, options, TestContext.Current.CancellationToken);
            await source.SaveAsync(defaults, TestContext.Current.CancellationToken);

            byte[] expected = await File.ReadAllBytesAsync(synchronous, TestContext.Current.CancellationToken);
            byte[] actual = await File.ReadAllBytesAsync(asynchronous, TestContext.Current.CancellationToken);
            Assert.True(expected.AsSpan().SequenceEqual(actual));
            using PdfDocument reopened = await PdfDocument.OpenAsync(defaults, TestContext.Current.CancellationToken);
            Assert.Single(reopened.Pages);
            Assert.Equal(["async.pdf", "defaults.pdf", "sync.pdf"], Directory.GetFiles(directory).Select(Path.GetFileName).Order());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_save_canceled_between_chunks_stops_writing_and_leaves_nothing_at_the_destination()
    {
        using PdfDocument source = await PdfDocument.OpenAsync(Corpus.Path("flate-stream.pdf"), TestContext.Current.CancellationToken);
        string directory = NewDirectory();
        try
        {
            string path = Path.Combine(directory, "out.pdf");
            using var cancellation = new CancellationTokenSource();
            using var tripwire = new CancelingStream(cancellation);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.SaveAsync(tripwire, cancellation.Token));

            // Writing stopped at the first chunk after the cancellation: no further chunk reached the stream.
            Assert.Equal(1, tripwire.Writes);

            // A path save that is canceled creates no file and leaves no temporary one.
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.SaveAsync(path, canceled.Token));
            Assert.Empty(Directory.GetFileSystemEntries(directory));

            // And it leaves an existing file as it was.
            await File.WriteAllTextAsync(path, "previous", TestContext.Current.CancellationToken);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.SaveAsync(path, canceled.Token));
            Assert.Equal("previous", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Single(Directory.GetFileSystemEntries(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_save_that_cannot_be_planned_leaves_an_existing_file_and_no_temporary_one()
    {
        using PdfDocument source = await PdfDocument.OpenAsync(Corpus.Path("flate-stream.pdf"), TestContext.Current.CancellationToken);
        string directory = NewDirectory();
        try
        {
            string path = Path.Combine(directory, "out.pdf");
            await File.WriteAllTextAsync(path, "previous", TestContext.Current.CancellationToken);

            // A save refused while it is planned, before any file is created.
            await Assert.ThrowsAsync<ArgumentNullException>(() => source.SaveAsync(path, null!, TestContext.Current.CancellationToken));

            Assert.Equal("previous", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Single(Directory.GetFileSystemEntries(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string NewDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"broadside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static IEnumerable<MethodInfo> EntryPoints(Type type, params string[] names) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => names.Contains(m.Name, StringComparer.Ordinal));

    private static MethodInfo? Find(Type type, string name, bool isStatic, Type[] parameters) =>
        type.GetMethod(
            name,
            BindingFlags.Public | BindingFlags.DeclaredOnly | (isStatic ? BindingFlags.Static : BindingFlags.Instance),
            binder: null,
            parameters,
            modifiers: null);

    private static string Describe(MethodInfo method) =>
        $"{method.DeclaringType!.Name}.{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})";

    /// <summary>A stream that cancels its token when the first chunk is written and counts the writes it is given.</summary>
    private sealed class CancelingStream(CancellationTokenSource cancellation) : Stream
    {
        public int Writes { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Writes++;
            await cancellation.CancelAsync();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
