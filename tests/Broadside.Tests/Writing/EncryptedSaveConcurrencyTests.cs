using System.Collections.Concurrent;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>
/// Re-saving an encrypted document with its own key, over and over and from many threads at once, while other threads read the same
/// document (issue #297). ISO 32000-2 §7.6.6 (crypt filters), §7.4.10 (the <c>Crypt</c> filter); the concurrency contract on
/// <see cref="PdfDocument"/> (concurrent reads and saves of a document nobody mutates are safe).
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class EncryptedSaveConcurrencyTests
{
    private const string FileName = "encrypted-crypt-filters.pdf";

    [Fact]
    public void Re_saving_a_crypt_filtered_file_with_its_key_from_many_threads_while_others_read_it_always_reads_back_the_same_content()
    {
        string[] expected;
        using (PdfDocument single = Open(Corpus.Bytes(FileName)))
        {
            expected = DecodedContents(single);
        }

        // Each save draws fresh AES IVs, so every round writes different ciphertext for the same plaintext; before #297 about one
        // re-save in 200 read its crypt-filtered stream back with the wrong key (a reader that tried the filter's key as is first).
        const int rounds = 3000;
        using PdfDocument shared = Open(Corpus.Bytes(FileName));
        var failures = new ConcurrentBag<string>();
        Parallel.For(0, rounds, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, round =>
        {
            switch (round % 3)
            {
                case 0:
                    // Read the shared document while other threads save it.
                    Check(round, "read", DecodedContents(shared));
                    break;
                case 1:
                    // Save the shared document (a save only reads it) and read the copy back.
                    Check(round, "shared save", ReadBack(shared));
                    break;
                default:
                    using (PdfDocument own = Open(Corpus.Bytes(FileName)))
                    {
                        Check(round, "own save", ReadBack(own));
                    }

                    break;
            }
        });

        Assert.Empty(failures);

        void Check(int round, string what, string[] actual)
        {
            if (!expected.SequenceEqual(actual))
            {
                failures.Add($"round {round} ({what}): [{string.Join(" | ", actual)}]");
            }
        }
    }

    private static PdfDocument Open(ReadOnlyMemory<byte> bytes) => PdfDocument.Open(bytes, new PdfOptions().WithPassword("owner"));

    private static string[] ReadBack(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output, new PdfSaveOptions());
        using PdfDocument reopened = Open(output.ToArray());
        Assert.Empty(reopened.Diagnostics);
        return DecodedContents(reopened);
    }

    /// <summary>The decoded data of every part of every page's content.</summary>
    private static string[] DecodedContents(PdfDocument document) =>
        [.. document.Pages.SelectMany(page => document.Resolve(page.Dictionary[new CosName("Contents")]) switch
        {
            CosArray parts => parts.Select(part => System.Text.Encoding.Latin1.GetString(document.DecodeStream((CosStream)document.Resolve(part)).Span)),
            CosStream stream => [System.Text.Encoding.Latin1.GetString(document.DecodeStream(stream).Span)],
            _ => [],
        })];
}
