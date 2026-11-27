using Broadside.Tests.Content;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>
/// The PII presets of <see cref="TextPatterns"/> (issue #633): what each finds on a page and what it rejects, its validator
/// (Luhn, ISO 7064 mod 97, the SSN's reserved numbers) included. ISO 32000-2 §9.10.
/// </summary>
public sealed class TextPatternTests
{
    [Theory]
    [InlineData("Write to jane.doe@example.com today", "jane.doe@example.com")]
    [InlineData("Mail: J.Smith+news@Mail.Example.CO.UK.", "J.Smith+news@Mail.Example.CO.UK")]
    public void Email_addresses_are_found(string line, string expected) =>
        Assert.Equal([expected], Find(line, TextPatterns.EmailAddress));

    [Theory]
    [InlineData("user@localhost is not an address")]
    [InlineData("an @ sign alone, or name@ and @example.com")]
    public void Email_lookalikes_are_not_found(string line) => Assert.Empty(Find(line, TextPatterns.EmailAddress));

    [Theory]
    [InlineData("Call (555) 010-0199 now", "(555) 010-0199")]
    [InlineData("Call 555-010-0199 now", "555-010-0199")]
    [InlineData("Call +1 555 010 0199 now", "+1 555 010 0199")]
    [InlineData("London +44 20 7946 0958 office", "+44 20 7946 0958")]
    [InlineData("Berlin 030 12345678 office", "030 12345678")]
    [InlineData("Paris 01 23 45 67 89 office", "01 23 45 67 89")]
    public void Phone_numbers_are_found(string line, string expected) =>
        Assert.Equal([expected], Find(line, TextPatterns.PhoneNumber));

    [Theory]
    [InlineData("Order 5550100199 shipped")]
    [InlineData("Dated 05/12/2024 and 01.02.2024")]
    [InlineData("Call +1 23 for short")]
    public void Phone_lookalikes_are_not_found(string line) => Assert.Empty(Find(line, TextPatterns.PhoneNumber));

    [Theory]
    [InlineData("SSN 123-45-6789 on file", "123-45-6789")]
    [InlineData("SSN 078 05 1120 on file", "078 05 1120")]
    public void Social_security_numbers_are_found(string line, string expected) =>
        Assert.Equal([expected], Find(line, TextPatterns.UsSocialSecurityNumber));

    [Theory]
    [InlineData("SSN 000-12-3456")]
    [InlineData("SSN 666-12-3456")]
    [InlineData("SSN 912-34-5678")]
    [InlineData("SSN 123-00-4567")]
    [InlineData("SSN 123-45-0000")]
    [InlineData("SSN 123-45 6789 mixes separators")]
    [InlineData("Phone 123-456-7890 is not one")]
    public void Reserved_and_malformed_social_security_numbers_are_not_found(string line) =>
        Assert.Empty(Find(line, TextPatterns.UsSocialSecurityNumber));

    [Theory]
    [InlineData("Visa 4111 1111 1111 1111 exp", "4111 1111 1111 1111")]
    [InlineData("Amex 3782-822463-10005 exp", "3782-822463-10005")]
    [InlineData("MC 5555555555554444.", "5555555555554444")]
    public void Card_numbers_that_pass_the_Luhn_check_are_found(string line, string expected) =>
        Assert.Equal([expected], Find(line, TextPatterns.CreditCardNumber));

    [Theory]
    [InlineData("Visa 4111 1111 1111 1112 exp")]
    [InlineData("Short 4111 1111 111")]
    [InlineData("Long 4111 1111 1111 1111 1111 1")]
    public void Card_numbers_that_fail_the_Luhn_check_or_the_length_are_not_found(string line) =>
        Assert.Empty(Find(line, TextPatterns.CreditCardNumber));

    [Theory]
    [InlineData("IBAN GB82 WEST 1234 5698 7654 32 paid", "GB82 WEST 1234 5698 7654 32")]
    [InlineData("IBAN DE89370400440532013000 paid", "DE89370400440532013000")]
    public void Ibans_that_pass_mod_97_are_found(string line, string expected) =>
        Assert.Equal([expected], Find(line, TextPatterns.Iban));

    [Theory]
    [InlineData("IBAN GB83 WEST 1234 5698 7654 32 paid")]
    [InlineData("IBAN DE89370400440532013001 paid")]
    [InlineData("IBAN gb82 west 1234 5698 7654 32 paid")]
    public void Ibans_that_fail_mod_97_are_not_found(string line) => Assert.Empty(Find(line, TextPatterns.Iban));

    [Theory]
    [InlineData("Host 192.0.2.10 down", "192.0.2.10")]
    [InlineData("Range 10.0.0.1, then 255.255.255.255.", "10.0.0.1", "255.255.255.255")]
    public void IPv4_addresses_are_found(string line, params string[] expected) =>
        Assert.Equal(expected, Find(line, TextPatterns.IPv4Address));

    [Theory]
    [InlineData("Host 256.1.1.1 bad")]
    [InlineData("Version 1.2.3.4.5 bad")]
    [InlineData("Three 10.0.0 parts")]
    public void IPv4_lookalikes_are_not_found(string line) => Assert.Empty(Find(line, TextPatterns.IPv4Address));

    [Theory]
    [InlineData("See https://example.com/a?b=1.", "https://example.com/a?b=1")]
    [InlineData("See www.example.org, then", "www.example.org")]
    [InlineData("Get ftp://files.example.net/x.zip now", "ftp://files.example.net/x.zip")]
    public void Urls_are_found_without_trailing_punctuation(string line, string expected) =>
        Assert.Equal([expected], Find(line, TextPatterns.Url));

    [Fact]
    public void A_bare_domain_is_not_a_url() => Assert.Empty(Find("Visit example.com today", TextPatterns.Url));

    [Fact]
    public void Each_preset_is_a_new_query_the_caller_may_change()
    {
        TextQuery first = TextPatterns.EmailAddress;
        TextQuery second = TextPatterns.EmailAddress;
        Assert.NotSame(first, second);
        first.WithMatchFilter(null);
        Assert.Null(first.MatchFilter);
        Assert.True(TextPatterns.CreditCardNumber.IsRegex);
        Assert.NotNull(TextPatterns.CreditCardNumber.MatchFilter);
    }

    [Fact]
    public void A_match_filter_drops_the_matches_it_rejects()
    {
        TextQuery query = TextQuery.FromRegex(@"\d+").WithMatchFilter(static match => match.Text.Length > 2);
        Assert.Equal(["123", "4567"], Find("1 22 123 4567", query));
    }

    [Fact]
    public void A_number_split_across_two_lines_is_found_once()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 10 Tf 72 700 Td (Card 4111 1111) Tj 0 -12 Td (1111 1111 ends) Tj ET");
        IReadOnlyList<TextMatch> matches = document.Pages[0].FindText(TextPatterns.CreditCardNumber);
        TextMatch match = Assert.Single(matches);
        Assert.Equal("4111 1111 1111 1111", match.Text);
        Assert.Equal(2, match.Quads.Count);
    }

    private static List<string> Find(string line, TextQuery query)
    {
        string escaped = line.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);
        using PdfDocument document = RedactionSamples.Open($"BT /F1 10 Tf 72 700 Td ({escaped}) Tj ET");
        return [.. document.Pages[0].FindText(query).Select(static match => match.Text)];
    }
}
