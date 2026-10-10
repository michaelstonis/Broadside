using Broadside.Objects;

namespace Broadside.Tests.Objects;

/// <summary>ISO 32000-2 §7.9.2.2 and Annex D: decoding string objects as text strings.</summary>
public sealed class TextStringTests
{
    [Fact]
    public void Decodes_utf16be_after_its_byte_order_marker()
    {
        // §7.9.2.2.1 Example 2: the Russian word for "test".
        var text = new CosString([0xFE, 0xFF, 0x04, 0x42, 0x04, 0x35, 0x04, 0x41, 0x04, 0x42]);

        Assert.Equal("тест", text.DecodeText());
    }

    [Fact]
    public void Decodes_utf16be_supplementary_characters()
    {
        var text = new CosString([0xFE, 0xFF, 0xD8, 0x3D, 0xDE, 0x00]);

        Assert.Equal("\U0001F600", text.DecodeText());
    }

    [Fact]
    public void Decodes_utf8_after_its_byte_order_marker()
    {
        var text = new CosString([0xEF, 0xBB, 0xBF, .. "Café"u8]);

        Assert.Equal("Café", text.DecodeText());
    }

    [Fact]
    public void Decodes_pdfdocencoding_when_there_is_no_byte_order_marker()
    {
        // §7.9.2.2.1 Example 1: 0x8B is the per-mille sign; the others are rows of Table D.3.
        var text = new CosString([.. "text"u8, 0x8B, 0x18, 0x80, 0x93, 0xA0, 0xE9, 0x9F]);

        Assert.Equal("text‰˘•ﬁ€é\u009F", text.DecodeText());
    }

    [Fact]
    public void Decodes_a_hexadecimal_string_the_same_way()
    {
        CosString text = Assert.IsType<CosString>(CosObject.Parse("<FEFF0041>"u8));

        Assert.Equal("A", text.DecodeText());
    }

    [Fact]
    public void Removes_language_escape_sequences_from_unicode_text()
    {
        byte[] utf16 = [0xFE, 0xFF, 0x00, 0x1B, 0x00, (byte)'e', 0x00, (byte)'n', 0x00, 0x1B, 0x00, (byte)'H', 0x00, 0x1B, 0x00, (byte)'j', 0x00, (byte)'a', 0x00, (byte)'J', 0x00, (byte)'P', 0x00, 0x1B, 0x00, (byte)'i'];
        byte[] utf8 = [0xEF, 0xBB, 0xBF, 0x1B, .. "en"u8, 0x1B, .. "Hi"u8];

        Assert.Equal("Hi", new CosString(utf16).DecodeText());
        Assert.Equal("Hi", new CosString(utf8).DecodeText());
    }

    [Fact]
    public void Tolerates_malformed_unicode()
    {
        Assert.Equal("A", new CosString([0xFE, 0xFF, 0x00, 0x41, 0x42]).DecodeText());
        Assert.Equal("�", new CosString([0xEF, 0xBB, 0xBF, 0xC3]).DecodeText());
    }
}
