using System.Text;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Globalization.Tests;

/// <summary>
/// The revision 6 password is prepared with SASLprep, whose normalization step is NFKC (ISO 32000-2 §7.6.4.1, RFC 4013 §2.2), in a
/// host with globalization data. <c>encrypted-user-password.pdf</c>'s user password is <c>pässwort</c> with a precomposed ä.
/// </summary>
public sealed class SaslPrepTests
{
    [Fact]
    public void The_host_normalizes_non_ascii_text()
    {
        Assert.Equal("ä", "ä".Normalize(NormalizationForm.FormKC));
    }

    [Theory]
    [InlineData("pässwort")] // a and a combining diaeresis: NFKC composes them
    [InlineData("ｐässwort")] // a fullwidth p: NFKC maps it to p
    public void A_password_equal_to_the_user_password_after_nfkc_opens_the_document(string password)
    {
        using PdfDocument document = new PdfEngine().Open(Corpus.Path("encrypted-user-password.pdf"), new PdfPassword(password));

        Assert.Equal(PdfAccessLevel.User, document.Security!.Access);
        Assert.Empty(document.Diagnostics);
    }
}
