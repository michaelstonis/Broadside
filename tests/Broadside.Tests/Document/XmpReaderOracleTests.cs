using System.Collections.Concurrent;
using System.Text;
using Broadside.Tests.Document.LegacyXmp;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// The XMP reader without <c>System.Xml</c> (issue #545) reads every packet as the <c>XmlReader</c>-based reader it replaced did:
/// the same packet, or none, with the same diagnostics (<see cref="XmpOracle"/>). The old reader lives in this project only, as the
/// oracle. Checked over hand-written edge cases of the XML rules, every metadata stream of <c>tests/Corpus</c> and of the
/// real-world corpora (skipped when they are not fetched), and seeded mutations of those packets. ISO 16684-1 §7; ISO 32000-2
/// §14.3.2.
/// </summary>
public sealed class XmpReaderOracleTests(ITestOutputHelper output)
{
    private const string Rdf = "xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"";

    /// <summary>Edge cases of well-formedness and of the RDF forms; <c>$R</c> stands for the <c>rdf</c> namespace declaration.</summary>
    public static TheoryData<string> EdgeCases => new()
    {
        "<?xml version=\"1.0\"?><rdf:RDF $R/>",
        "<?xml version=\"1.1\"?><rdf:RDF $R/>",
        "<?xml version=\"1.0a\" ?><rdf:RDF $R/>",
        "<?xml version=\"1.10\" ?><rdf:RDF $R/>",
        "<?xml version=\" 1.0\"?><rdf:RDF $R/>",
        "<?xml version=\"1.0\" encoding=\"bogus enc\"?><rdf:RDF $R/>",
        "<?xml version=\"1.0\" encoding=\"a&lt;\"?><rdf:RDF $R/>",
        "<?xml version=\"1.0\" standalone=\"maybe\"?><rdf:RDF $R/>",
        "<?xml version=\"1.0\" standalone=\"yes\" encoding=\"utf-8\"?><rdf:RDF $R/>",
        "<?xml encoding=\"UTF-8\"?><rdf:RDF $R/>",
        "<?xml version=\"1.0\"encoding=\"utf-8\"?><rdf:RDF $R/>",
        "<?xml version=\"1.0\"? ><rdf:RDF $R/>",
        "<?xml?><rdf:RDF $R/>",
        "<?xml\tversion='1.0'  ?>\r\n<rdf:RDF $R/>",
        "<?xpacket begin=\"\" ?><?xml version=\"1.0\"?><rdf:RDF $R/>",
        "<?XML version=\"1.0\"?><rdf:RDF $R/>",
        "<?xml-stylesheet a?><?xmlx?><?xpacket?><?xpacket\n?><rdf:RDF $R/>",
        "<?a:b c?><rdf:RDF $R/>",
        "<? xpacket?><rdf:RDF $R/>",
        "<?xpacket a?b?><rdf:RDF $R/>",
        "<?xpacket a? ><rdf:RDF $R/>",
        "<?xpacket begin=\"\u0001\"?><rdf:RDF $R/>",
        "text before<rdf:RDF $R/>",
        "]]> before<rdf:RDF $R/>",
        "<![CDATA[x]]><!----><!---a--><!--a-b--><rdf:RDF $R/>",
        "<!-- a -- b --><rdf:RDF $R/>",
        "<!-- a ---><rdf:RDF $R/>",
        "<!-x--><rdf:RDF $R/>",
        "<![CDATA x]]><rdf:RDF $R/>",
        "<!doctype x><rdf:RDF $R/>",
        "<!DOCTYPE x><rdf:RDF $R/>",
        "<!><rdf:RDF $R/>",
        "</x><rdf:RDF $R/>",
        "<a></a><a><rdf:RDF $R/>",
        "<p:a><rdf:RDF $R/></p:a>",
        "<xmlns:a><xml:b><rdf:RDF $R><xmlns:c/><xml:d/></rdf:RDF></xml:b></xmlns:a>",
        "<a xmlns:p=\"\"><rdf:RDF $R/></a>",
        "<a xmlns:xml=\"http://www.w3.org/XML/1998/namespace\"><rdf:RDF $R/></a>",
        "<a xmlns:xml=\"urn:x\"><rdf:RDF $R/></a>",
        "<a xmlns:p=\"http://www.w3.org/XML/1998/namespace\"><rdf:RDF $R/></a>",
        "<a xmlns=\"http://www.w3.org/2000/xmlns/\"><rdf:RDF $R/></a>",
        "<a xmlns:xmlns=\"urn:x\"><rdf:RDF $R/></a>",
        "<a xml:space=\"bad\"><rdf:RDF $R/></a>",
        "<a xml:space=\" preserve\" xml:lang=\"\"><rdf:RDF $R/></a>",
        "<a xml:space=\"Default\"><rdf:RDF $R/></a>",
        "<a b=\"1\"c=\"2\"><rdf:RDF $R/></a>",
        "<a b='1' b='2'><rdf:RDF $R/></a>",
        "<a xmlns:p=\"u\" xmlns:q=\"u\" p:b='1' q:b='2'><rdf:RDF $R/></a>",
        "<a b=\"x<y\"><rdf:RDF $R/></a>",
        "<a b=\"x>y]]>\"><rdf:RDF $R/></a>",
        "<a b><rdf:RDF $R/></a>",
        "<a b=x><rdf:RDF $R/></a>",
        "<a:><rdf:RDF $R/></a:>",
        "<a:b:c xmlns:a=\"u\"><rdf:RDF $R/></a:b:c>",
        "<a></a ><a></ a><rdf:RDF $R/>",
        "<a/ ><rdf:RDF $R/>",
        "\u0001<rdf:RDF $R/>",
        "<a\u0001><rdf:RDF $R/></a>",
        "<rdf:RDF $R><a b=\"x\ny\r\nz\tw&#xA;&#9;q\">t\r\nu\rv&#xD;</a></rdf:RDF>",
        "<rdf:RDF $R><a>&amp;&lt;&gt;&quot;&apos;&#65;&#x41;&#0000000065;&#x10000;</a></rdf:RDF>",
        "<rdf:RDF $R><a>&#X41;</a></rdf:RDF>",
        "<rdf:RDF $R><a>&#0;</a></rdf:RDF>",
        "<rdf:RDF $R><a>&#xD800;</a></rdf:RDF>",
        "<rdf:RDF $R><a>&#xFFFE;</a></rdf:RDF>",
        "<rdf:RDF $R><a>&#x110000;</a></rdf:RDF>",
        "<rdf:RDF $R><a>&#99999999999999999999;</a></rdf:RDF>",
        "<rdf:RDF $R><a>&foo;</a></rdf:RDF>",
        "<rdf:RDF $R><a>& </a></rdf:RDF>",
        "<rdf:RDF $R><a>&amp</a></rdf:RDF>",
        "<rdf:RDF $R><a>&a:b;</a></rdf:RDF>",
        "<rdf:RDF $R><a>&#x;</a></rdf:RDF>",
        "<rdf:RDF $R><a>]]></a></rdf:RDF>",
        "<rdf:RDF $R><a>]]</a></rdf:RDF>",
        "<rdf:RDF $R><a><![CDATA[x\r\n]]>y<!--c-->z<?p q?></a></rdf:RDF>",
        "<rdf:RDF $R><a><![CDATA[]]]]></a></rdf:RDF>",
        "<rdf:RDF $R><a><!DOCTYPE x></a></rdf:RDF>",
        "<rdf:RDF $R><a><!ELEMENT x></a></rdf:RDF>",
        "<rdf:RDF $R><a><?xml version=\"1.0\"?></a></rdf:RDF>",
        "<rdf:RDF $R><a><?XmL?></a></rdf:RDF>",
        "<rdf:RDF $R><a><? ?></a></rdf:RDF>",
        "<rdf:RDF $R><a>\u0001</a></rdf:RDF>",
        "<rdf:RDF $R><a>\uFFFE</a></rdf:RDF>",
        "<rdf:RDF $R><a>\uD800</a></rdf:RDF>",
        "<rdf:RDF $R><a>\uDC00</a></rdf:RDF>",
        "<rdf:RDF $R><a>\uD800\uDC00\u0085\u2028</a></rdf:RDF>",
        "<rdf:RDF $R><a b=\"\u0001\"/></rdf:RDF>",
        "<rdf:RDF $R><!--\u0001--></rdf:RDF>",
        "<rdf:RDF $R><![CDATA[\u0001]]></rdf:RDF>",
        "<rdf:RDF $R><a\u00B7/><\u00E0/></rdf:RDF>",
        "<rdf:RDF $R><\u00B7a/></rdf:RDF>",
        "<rdf:RDF $R><\u0E2F/></rdf:RDF>",
        "<rdf:RDF $R><\u00AA/></rdf:RDF>",
        "<rdf:RDF $R><\uD800\uDC00/></rdf:RDF>",
        "<rdf:RDF $R><a\uD800\uDC00/></rdf:RDF>",
        "<rdf:RDF $R><-a/></rdf:RDF>",
        "<rdf:RDF $R><a.-_1/><_a/></rdf:RDF>",
        "<rdf:RDF $R><a></b></rdf:RDF>",
        "<rdf:RDF $R><a></a/></rdf:RDF>",
        "<rdf:RDF $R><a></a\n></rdf:RDF>",
        "<rdf:RDF $R><a\n\tb\r\n=\n'1'\n/></rdf:RDF>",
        "<rdf:RDF $R><a></rdf:RDF>",
        "<rdf:RDF $R><a/></rdf:RDF",
        "<rdf:RDF $R><a/>",
        "<rdf:RDF $R></rdf:RDF> junk <<<<!DOCTYPE x>",
        "<rdf:RDF $R><rdf:RDF/></rdf:RDF>",
        "<rdf:RDF $R><a xmlns:b=\"u\"/><b:c/></rdf:RDF>",
        "<rdf:RDF $R><a xmlns=\"\"/><a xmlns:a=\"u\" xmlns:b=\"u\" a:c=\"1\" b:d=\"2\"/></rdf:RDF>",
        "<rdf:RDF $R><a xmlns:b=\"u\" b:=\"1\"/></rdf:RDF>",
        "<rdf:RDF $R><a :b=\"1\"/></rdf:RDF>",
        "<rdf:RDF $R><a xmlns:p=\" \" xmlns:q=\"&#x20;\"/></rdf:RDF>",
        "<x xmlns:x=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><x:RDF><x:a/></x:RDF></x>",
        "<rdf:RDF $R><rdf:Description rdf:about=\"a\" xmlns:d=\"urn:d\" d:x=\"1\" xml:lang=\"de\" plain=\"p\"><d:y>  </d:y><d:y>2</d:y>"
            + "<d:z d:f=\"1\">  </d:z><d:w d:f=\"1\">t</d:w><d:v rdf:resource=\"r\">ignored</d:v><d:u rdf:parseType=\"Resource\" d:g=\"2\"><d:h>3</d:h></d:u>"
            + "<d:t><rdf:Description rdf:about=\"b\"><rdf:value xml:lang=\"en\">4</rdf:value><d:q>5</d:q></rdf:Description></d:t>"
            + "<d:s><rdf:Bag><rdf:li>a</rdf:li><d:x>skip</d:x><rdf:li rdf:parseType=\"Resource\"><d:k>6</d:k></rdf:li></rdf:Bag></d:s>"
            + "<d:r><rdf:Alt><rdf:li xml:lang=\"x-default\">t</rdf:li></rdf:Alt>text after</d:r><d:p>a<d:c/>b</d:p><d:o>x<!--c-->y</d:o>"
            + "<d:n><rdf:Other><d:m>7</d:m></rdf:Other></d:n><rdf:value>8</rdf:value><xml:z>9</xml:z></rdf:Description>"
            + "<rdf:Description rdf:about=\"c\" xmlns:d=\"urn:d\"><d:x>dup</d:x></rdf:Description>text<!--c--></rdf:RDF>",
        "\uFEFF  <rdf:RDF $R/>",
        "junk<rdf:RDF $R/>",
        "no markup",
        "",
    };

    [Theory]
    [MemberData(nameof(EdgeCases))]
    public void An_edge_case_of_the_xml_rules_reads_as_the_old_reader_read_it(string text)
    {
        byte[] data = Encoding.UTF8.GetBytes(text.Replace("$R", Rdf, StringComparison.Ordinal));

        Assert.Null(XmpOracle.Difference(data));
    }

    [Fact]
    public void Deep_nesting_reads_as_the_old_reader_read_it()
    {
        string open = string.Concat(Enumerable.Repeat("<d:a><rdf:Bag><rdf:li>", 60));
        string close = string.Concat(Enumerable.Repeat("</rdf:li></rdf:Bag></d:a>", 60));
        string text = $"<rdf:RDF {Rdf}><rdf:Description xmlns:d=\"urn:d\">{open}deep<d:b>x</d:b>{close}</rdf:Description></rdf:RDF>";

        Assert.Null(XmpOracle.Difference(Encoding.UTF8.GetBytes(text)));
    }

    [Theory]
    [InlineData("utf-16BE")]
    [InlineData("utf-16")]
    [InlineData("utf-32BE")]
    [InlineData("utf-32")]
    public void Every_encoding_reads_as_the_old_reader_read_it(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string text = $"<?xpacket begin=\"\uFEFF\"?><rdf:RDF {Rdf}><rdf:Description xmlns:d=\"urn:d\"><d:a>\u00E9\uD83D\uDE00</d:a></rdf:Description></rdf:RDF>";

        Assert.Null(XmpOracle.Difference([.. encoding.GetPreamble(), .. encoding.GetBytes(text)]));
        Assert.Null(XmpOracle.Difference(encoding.GetBytes(text)));
    }

    [Fact]
    public void Every_packet_of_the_test_corpus_reads_as_the_old_reader_read_it()
    {
        var differences = new List<string>();
        int packets = 0;
        foreach (string name in Corpus.AllFileNames)
        {
            string path = Path.Combine(Corpus.Directory, name);
            foreach (byte[] packet in XmpOracle.MetadataPackets(File.ReadAllBytes(path)))
            {
                packets++;
                if (XmpOracle.Difference(packet) is { } difference)
                {
                    differences.Add($"{Path.GetFileName(path)}:\n{difference}");
                }
            }
        }

        output.WriteLine($"{packets} packets compared.");
        Assert.True(packets > 0);
        Assert.Empty(differences);
    }

    /// <summary>Every corpus <c>tools/CorpusFetcher/corpora.json</c> knows; the ones not fetched are skipped.</summary>
    public static TheoryData<string> RealWorldCorpora => new(
        "pdfjs", "pdf20examples", "verapdf-corpus", "pdfbox", "gwg-output-suite", "safedocs", "pdfium-tests", "qpdf", "bfo-pdfa-testsuite", "isartor");

    [Theory]
    [MemberData(nameof(RealWorldCorpora))]
    [Trait("Category", "Corpus")]
    public void Every_packet_of_a_real_world_corpus_reads_as_the_old_reader_read_it(string corpus)
    {
        IReadOnlyList<string> files = RealWorldCorpus.Files(corpus);
        Assert.SkipWhen(files.Count == 0, $"The real-world corpus {corpus} is not fetched (tools/CorpusFetcher).");

        var differences = new ConcurrentBag<string>();
        int packets = 0;
        int readable = 0;
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
        {
            foreach (byte[] packet in XmpOracle.MetadataPackets(File.ReadAllBytes(RealWorldCorpus.PathOf(file))))
            {
                _ = Interlocked.Increment(ref packets);
                if (XmpOracle.Difference(packet) is { } difference)
                {
                    differences.Add($"{file}:\n{difference}");
                }
                else if (!XmpOracle.Current(packet).StartsWith("null", StringComparison.Ordinal))
                {
                    _ = Interlocked.Increment(ref readable);
                }
            }
        });

        output.WriteLine($"{corpus}: {files.Count} files, {packets} metadata streams compared, {readable} readable packets.");
        Assert.Empty(differences);
    }

    /// <summary>
    /// Mutations of every test-corpus packet and of the edge cases (bytes flipped, deleted, duplicated; XML tokens inserted;
    /// truncation), with a fixed seed so a failure replays. For a longer local run, <c>BROADSIDE_XMP_ORACLE_MUTATIONS</c> sets the
    /// count, <c>BROADSIDE_XMP_ORACLE_SEED</c> the seed, and <c>BROADSIDE_XMP_ORACLE_REAL_WORLD=1</c> adds the packets of the fetched
    /// real-world corpora to the inputs.
    /// </summary>
    [Fact]
    public void Mutated_packets_read_as_the_old_reader_read_them()
    {
        int count = int.TryParse(Environment.GetEnvironmentVariable("BROADSIDE_XMP_ORACLE_MUTATIONS"), out int requested) ? requested : 20_000;
        var seeds = new List<byte[]>();
        foreach (string name in Corpus.AllFileNames)
        {
            seeds.AddRange(XmpOracle.MetadataPackets(File.ReadAllBytes(Path.Combine(Corpus.Directory, name))));
        }

        if (Environment.GetEnvironmentVariable("BROADSIDE_XMP_ORACLE_REAL_WORLD") == "1")
        {
            foreach (string corpus in RealWorldCorpora.Select(row => row.Data))
            {
                foreach (string file in RealWorldCorpus.Files(corpus))
                {
                    seeds.AddRange(XmpOracle.MetadataPackets(File.ReadAllBytes(RealWorldCorpus.PathOf(file))));
                }
            }
        }

        foreach (string text in EdgeCases.Select(row => row.Data))
        {
            seeds.Add(Encoding.UTF8.GetBytes(text.Replace("$R", Rdf, StringComparison.Ordinal)));
        }

        // Trimmed to the rdf:RDF element so mutations land in the part that is read, not in the padding.
        seeds = [.. seeds.Select(Trim).Where(seed => seed.Length > 0).DistinctBy(Convert.ToBase64String)];
#pragma warning disable CA5394 // Reproducible test input, not security.
        var random = new Random(int.TryParse(Environment.GetEnvironmentVariable("BROADSIDE_XMP_ORACLE_SEED"), out int seed) ? seed : 545);
        var differences = new List<string>();
        for (int i = 0; i < count && differences.Count < 5; i++)
        {
            byte[] mutated = Mutate(seeds[random.Next(seeds.Count)], random);
            if (XmpOracle.Difference(mutated) is { } difference)
            {
                differences.Add($"mutation {i}: {Convert.ToBase64String(mutated)}\n{Encoding.UTF8.GetString(mutated)}\n{difference}");
            }
        }

#pragma warning restore CA5394
        foreach (string difference in differences)
        {
            output.WriteLine(difference);
        }

        Assert.Empty(differences);
    }

    private static readonly string[] Tokens =
    [
        "<", ">", "/>", "</", "&", ";", "&amp;", "&lt;", "&#x0;", "&#xD;", "&#10;", "&#x1F600;", "&bogus;", "]]>", "]]", "<!--", "-->", "--",
        "<![CDATA[", "<?", "?>", "<?xml version=\"1.0\"?>", "<!DOCTYPE x>", "<!", "=", "\"", "'", " ", "\t", "\r", "\n", "\r\n", ":",
        "xmlns", "xmlns:", "xmlns=\"\"", "xmlns:p=\"\"", "xmlns:q=\"urn:q\"", " q:a=\"1\"", " a=\"1\"", "xml:", "xml:lang=\"de\"",
        "xml:space=\"x\"", " xml:space=\"preserve\"", "rdf:", "rdf:li", "rdf:value", "rdf:Description", "rdf:parseType=\"Resource\"",
        " rdf:resource=\"r\"", " rdf:about=\"x\"", "<rdf:Bag>", "</rdf:Bag>", "<rdf:li>", "</rdf:li>", "<rdf:Alt>", "<rdf:Seq>",
        "\u00B7", "\u00E0", "\u0300", "\u00AA", "\u0001", "\u0085", "\u2028", "\uFFFE", "\uD800", "\uDC00", "\uD83D\uDE00", "\uFEFF",
    ];

    private static readonly byte[][] TokenBytes = [.. Tokens.Select(Encoding.UTF8.GetBytes)];

    private static byte[] Trim(byte[] packet)
    {
        ReadOnlySpan<byte> span = packet;
        int end = span.IndexOf("</rdf:RDF>"u8);
        return end < 0 ? packet : span[..(end + "</rdf:RDF>".Length)].ToArray();
    }

#pragma warning disable CA5394 // Reproducible test input, not security.
    private static byte[] Mutate(byte[] seed, Random random)
    {
        var data = new List<byte>(seed);
        int steps = 1 + random.Next(4);
        for (int step = 0; step < steps; step++)
        {
            int at = data.Count == 0 ? 0 : random.Next(data.Count);
            switch (random.Next(6))
            {
                case 0 when data.Count > 0:
                    data[at] = (byte)random.Next(256);
                    break;
                case 1 when data.Count > 0:
                    data.RemoveRange(at, Math.Min(1 + random.Next(8), data.Count - at));
                    break;
                case 2 when data.Count > 0:
                    int length = Math.Min(1 + random.Next(32), data.Count - at);
                    data.InsertRange(random.Next(data.Count), data.GetRange(at, length));
                    break;
                case 3 when data.Count > 0:
                    data.RemoveRange(at, data.Count - at);
                    break;
                default:
                    data.InsertRange(at, TokenBytes[random.Next(TokenBytes.Length)]);
                    break;
            }
        }

        return [.. data];
    }
#pragma warning restore CA5394
}
