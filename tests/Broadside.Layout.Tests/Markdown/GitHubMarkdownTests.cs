using Broadside.Layout.Flow;

namespace Broadside.Layout.Tests.Markdown;

/// <summary>
/// The GitHub Flavored Markdown extensions the parser supports (issue #645): tables, strikethrough, extended autolinks and task list
/// items, checked against examples of the GFM 0.29 spec (https://github.github.com/gfm/) rendered as cmark-gfm renders them, and
/// switched off with the extensions.
/// </summary>
public sealed class GitHubMarkdownTests
{
    public static TheoryData<string, string> Examples() => new()
    {
        // Tables (GFM examples 198 to 205).
        { "| foo | bar |\n| --- | --- |\n| baz | bim |\n", "<table>\n<thead>\n<tr>\n<th>foo</th>\n<th>bar</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>baz</td>\n<td>bim</td>\n</tr>\n</tbody>\n</table>\n" },
        { "| abc | defghi |\n:-: | -----------:\nbar | baz\n", "<table>\n<thead>\n<tr>\n<th align=\"center\">abc</th>\n<th align=\"right\">defghi</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td align=\"center\">bar</td>\n<td align=\"right\">baz</td>\n</tr>\n</tbody>\n</table>\n" },
        { "| f\\|oo  |\n| ------ |\n| b `\\|` az |\n| b **\\|** im |\n", "<table>\n<thead>\n<tr>\n<th>f|oo</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>b <code>|</code> az</td>\n</tr>\n<tr>\n<td>b <strong>|</strong> im</td>\n</tr>\n</tbody>\n</table>\n" },
        { "| abc | def |\n| --- | --- |\n| bar | baz |\n> bar\n", "<table>\n<thead>\n<tr>\n<th>abc</th>\n<th>def</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>bar</td>\n<td>baz</td>\n</tr>\n</tbody>\n</table>\n<blockquote>\n<p>bar</p>\n</blockquote>\n" },
        { "| abc | def |\n| --- | --- |\n| bar | baz |\nbar\n\nbar\n", "<table>\n<thead>\n<tr>\n<th>abc</th>\n<th>def</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>bar</td>\n<td>baz</td>\n</tr>\n<tr>\n<td>bar</td>\n<td></td>\n</tr>\n</tbody>\n</table>\n<p>bar</p>\n" },
        { "| abc | def |\n| --- |\n| bar |\n", "<p>| abc | def |\n| --- |\n| bar |</p>\n" },
        { "| abc | def |\n| --- | --- |\n| bar |\n| bar | baz | boo |\n", "<table>\n<thead>\n<tr>\n<th>abc</th>\n<th>def</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>bar</td>\n<td></td>\n</tr>\n<tr>\n<td>bar</td>\n<td>baz</td>\n</tr>\n</tbody>\n</table>\n" },
        { "| abc | def |\n| --- | --- |\n", "<table>\n<thead>\n<tr>\n<th>abc</th>\n<th>def</th>\n</tr>\n</thead>\n</table>\n" },
        { "Intro\n| a |\n| - |\n| b |\n", "<p>Intro</p>\n<table>\n<thead>\n<tr>\n<th>a</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>b</td>\n</tr>\n</tbody>\n</table>\n" },

        // Task list items (GFM examples 279 and 280).
        { "- [ ] foo\n- [x] bar\n", "<ul>\n<li><input disabled=\"\" type=\"checkbox\"> foo</li>\n<li><input checked=\"\" disabled=\"\" type=\"checkbox\"> bar</li>\n</ul>\n" },
        { "- [x] foo\n  - [ ] bar\n  - [x] baz\n- [ ] bim\n", "<ul>\n<li><input checked=\"\" disabled=\"\" type=\"checkbox\"> foo\n<ul>\n<li><input disabled=\"\" type=\"checkbox\"> bar</li>\n<li><input checked=\"\" disabled=\"\" type=\"checkbox\"> baz</li>\n</ul>\n</li>\n<li><input disabled=\"\" type=\"checkbox\"> bim</li>\n</ul>\n" },

        // Strikethrough (GFM examples 491 to 493).
        { "~~Hi~~ Hello, ~there~ world!\n", "<p><del>Hi</del> Hello, <del>there</del> world!</p>\n" },
        { "This ~~has a\n\nnew paragraph~~.\n", "<p>This ~~has a</p>\n<p>new paragraph~~.</p>\n" },
        { "This will ~~~not~~~ strike.\n", "<p>This will ~~~not~~~ strike.</p>\n" },

        // Extended autolinks (GFM examples 621 to 631).
        { "www.commonmark.org\n", "<p><a href=\"http://www.commonmark.org\">www.commonmark.org</a></p>\n" },
        { "Visit www.commonmark.org/help for more information.\n", "<p>Visit <a href=\"http://www.commonmark.org/help\">www.commonmark.org/help</a> for more information.</p>\n" },
        { "Visit www.commonmark.org.\n\nVisit www.commonmark.org/a.b.\n", "<p>Visit <a href=\"http://www.commonmark.org\">www.commonmark.org</a>.</p>\n<p>Visit <a href=\"http://www.commonmark.org/a.b\">www.commonmark.org/a.b</a>.</p>\n" },
        { "www.google.com/search?q=Markup+(business)\n\nwww.google.com/search?q=Markup+(business)))\n\n(www.google.com/search?q=Markup+(business))\n\n(www.google.com/search?q=Markup+(business)\n",
          "<p><a href=\"http://www.google.com/search?q=Markup+(business)\">www.google.com/search?q=Markup+(business)</a></p>\n<p><a href=\"http://www.google.com/search?q=Markup+(business)\">www.google.com/search?q=Markup+(business)</a>))</p>\n<p>(<a href=\"http://www.google.com/search?q=Markup+(business)\">www.google.com/search?q=Markup+(business)</a>)</p>\n<p>(<a href=\"http://www.google.com/search?q=Markup+(business)\">www.google.com/search?q=Markup+(business)</a></p>\n" },
        { "www.google.com/search?q=(business))+ok\n", "<p><a href=\"http://www.google.com/search?q=(business))+ok\">www.google.com/search?q=(business))+ok</a></p>\n" },
        { "www.google.com/search?q=commonmark&hl=en\n\nwww.google.com/search?q=commonmark&hl;\n", "<p><a href=\"http://www.google.com/search?q=commonmark&amp;hl=en\">www.google.com/search?q=commonmark&amp;hl=en</a></p>\n<p><a href=\"http://www.google.com/search?q=commonmark\">www.google.com/search?q=commonmark</a>&amp;hl;</p>\n" },
        { "www.commonmark.org/he<lp\n", "<p><a href=\"http://www.commonmark.org/he\">www.commonmark.org/he</a>&lt;lp</p>\n" },
        { "http://commonmark.org\n\n(Visit https://encrypted.google.com/search?q=Markup+(business))\n", "<p><a href=\"http://commonmark.org\">http://commonmark.org</a></p>\n<p>(Visit <a href=\"https://encrypted.google.com/search?q=Markup+(business)\">https://encrypted.google.com/search?q=Markup+(business)</a>)</p>\n" },
        { "foo@bar.baz\n", "<p><a href=\"mailto:foo@bar.baz\">foo@bar.baz</a></p>\n" },
        { "hello@mail+xyz.example isn't valid, but hello+xyz@mail.example is.\n", "<p>hello@mail+xyz.example isn't valid, but <a href=\"mailto:hello+xyz@mail.example\">hello+xyz@mail.example</a> is.</p>\n" },
        { "a.b-c_d@a.b\n\na.b-c_d@a.b.\n\na.b-c_d@a.b-\n\na.b-c_d@a.b_\n", "<p><a href=\"mailto:a.b-c_d@a.b\">a.b-c_d@a.b</a></p>\n<p><a href=\"mailto:a.b-c_d@a.b\">a.b-c_d@a.b</a>.</p>\n<p>a.b-c_d@a.b-</p>\n<p>a.b-c_d@a.b_</p>\n" },
        { "[www.example.com](https://x.org) `www.example.com`\n", "<p><a href=\"https://x.org\">www.example.com</a> <code>www.example.com</code></p>\n" },
    };

    [Theory]
    [MemberData(nameof(Examples))]
    public void An_extension_example_renders_as_cmark_gfm_does(string markdown, string html) =>
        Assert.Equal(html, MarkdownHtmlRenderer.Render(markdown, gfm: true));

    [Theory]
    [InlineData("| a |\n| - |\n| b |\n", "<p>| a |\n| - |\n| b |</p>\n")]
    [InlineData("~~x~~ www.example.com\n", "<p>~~x~~ www.example.com</p>\n")]
    [InlineData("- [x] done\n", "<ul>\n<li>[x] done</li>\n</ul>\n")]
    public void Without_the_extensions_the_input_is_plain_CommonMark(string markdown, string html) =>
        Assert.Equal(html, MarkdownHtmlRenderer.Render(markdown, gfm: false));

    [Fact]
    public void The_parser_keeps_no_state_between_documents()
    {
        _ = MarkdownParser.Parse("[a]: /first\n", gitHubExtensions: true);

        Assert.Equal("<p>[a]</p>\n", MarkdownHtmlRenderer.Render("[a]\n", gfm: true));
    }
}
