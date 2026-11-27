using System.Text;
using Broadside.Layout.Flow;

namespace Broadside.Layout.Tests.Markdown;

/// <summary>
/// Renders the parser's syntax tree as HTML the way the CommonMark reference implementation (commonmark.js) does, so the spec's
/// examples compare byte for byte; GFM tables, strikethrough and task list items render as cmark-gfm does. Test code only: the
/// library never writes HTML.
/// </summary>
internal static class MarkdownHtmlRenderer
{
    public static string Render(string markdown, bool gfm = false) => Render(MarkdownParser.Parse(markdown, gfm));

    public static string Render(MarkdownNode document)
    {
        var html = new StringBuilder();
        int disableTags = 0;
        char last = '\n';

        void Literal(string text)
        {
            if (text.Length > 0)
            {
                html.Append(text);
                last = text[^1];
            }
        }

        void Cr()
        {
            if (last != '\n')
            {
                Literal("\n");
            }
        }

        void Tag(string tag)
        {
            if (disableTags == 0)
            {
                Literal(tag);
            }
        }

        foreach ((MarkdownNode node, bool entering) in document.Walk())
        {
            switch (node.Kind)
            {
                case MarkdownKind.Text:
                    Literal(Escape(node.Literal));
                    break;
                case MarkdownKind.SoftBreak:
                    Literal("\n");
                    break;
                case MarkdownKind.LineBreak:
                    Tag("<br />");
                    Cr();
                    break;
                case MarkdownKind.Code:
                    Tag("<code>");
                    Literal(Escape(node.Literal));
                    Tag("</code>");
                    break;
                case MarkdownKind.HtmlInline:
                    Literal(node.Literal);
                    break;
                case MarkdownKind.Emphasis:
                    Tag(entering ? "<em>" : "</em>");
                    break;
                case MarkdownKind.Strong:
                    Tag(entering ? "<strong>" : "</strong>");
                    break;
                case MarkdownKind.Strikethrough:
                    Tag(entering ? "<del>" : "</del>");
                    break;
                case MarkdownKind.Link:
                    if (entering)
                    {
                        Tag($"<a href=\"{Escape(node.Destination)}\"" + (node.Title.Length > 0 ? $" title=\"{Escape(node.Title)}\"" : string.Empty) + ">");
                    }
                    else
                    {
                        Tag("</a>");
                    }

                    break;
                case MarkdownKind.Image:
                    if (entering)
                    {
                        if (disableTags == 0)
                        {
                            Literal($"<img src=\"{Escape(node.Destination)}\" alt=\"");
                        }

                        disableTags++;
                    }
                    else
                    {
                        disableTags--;
                        if (disableTags == 0)
                        {
                            if (node.Title.Length > 0)
                            {
                                Literal($"\" title=\"{Escape(node.Title)}");
                            }

                            Literal("\" />");
                        }
                    }

                    break;
                case MarkdownKind.Paragraph:
                    if (node.Parent?.Parent is { Kind: MarkdownKind.List, ListData.Tight: true })
                    {
                        break;
                    }

                    if (entering)
                    {
                        Cr();
                        Tag("<p>");
                    }
                    else
                    {
                        Tag("</p>");
                        Cr();
                    }

                    break;
                case MarkdownKind.Heading:
                    if (entering)
                    {
                        Cr();
                        Tag($"<h{node.Level}>");
                    }
                    else
                    {
                        Tag($"</h{node.Level}>");
                        Cr();
                    }

                    break;
                case MarkdownKind.CodeBlock:
                    string[] info = node.Info.Split([' ', '\t', '\n', '\r', '\f', '\v'], StringSplitOptions.None);
                    Cr();
                    Tag(info.Length > 0 && info[0].Length > 0 ? $"<pre><code class=\"language-{Escape(info[0])}\">" : "<pre><code>");
                    Literal(Escape(node.Literal));
                    Tag("</code></pre>");
                    Cr();
                    break;
                case MarkdownKind.HtmlBlock:
                    Cr();
                    Literal(node.Literal);
                    Cr();
                    break;
                case MarkdownKind.ThematicBreak:
                    Cr();
                    Tag("<hr />");
                    Cr();
                    break;
                case MarkdownKind.BlockQuote:
                    Cr();
                    Tag(entering ? "<blockquote>" : "</blockquote>");
                    Cr();
                    break;
                case MarkdownKind.List:
                    string name = node.ListData!.Ordered ? "ol" : "ul";
                    Cr();
                    Tag(entering ? node.ListData.Ordered && node.ListData.Start != 1 ? $"<ol start=\"{node.ListData.Start}\">" : $"<{name}>" : $"</{name}>");
                    Cr();
                    break;
                case MarkdownKind.Item:
                    if (entering)
                    {
                        Tag("<li>");
                        if (node.TaskChecked is { } done)
                        {
                            Tag(done ? "<input checked=\"\" disabled=\"\" type=\"checkbox\"> " : "<input disabled=\"\" type=\"checkbox\"> ");
                        }
                    }
                    else
                    {
                        Tag("</li>");
                        Cr();
                    }

                    break;
                case MarkdownKind.Table:
                    Cr();
                    Tag(entering ? "<table>" : (node.LastChild is { IsHeader: false } ? "</tbody>\n" : string.Empty) + "</table>");
                    Cr();
                    break;
                case MarkdownKind.TableRow:
                    if (entering)
                    {
                        if (node.IsHeader)
                        {
                            Literal("<thead>\n");
                        }
                        else if (node.Previous is { IsHeader: true })
                        {
                            Literal("<tbody>\n");
                        }

                        Literal("<tr>\n");
                    }
                    else
                    {
                        Literal("</tr>\n");
                        if (node.IsHeader)
                        {
                            Literal("</thead>\n");
                        }
                    }

                    break;
                case MarkdownKind.TableCell:
                    string cell = node.Parent!.IsHeader ? "th" : "td";
                    if (entering)
                    {
                        string align = node.Alignments![0] switch
                        {
                            MarkdownAlignment.Left => " align=\"left\"",
                            MarkdownAlignment.Center => " align=\"center\"",
                            MarkdownAlignment.Right => " align=\"right\"",
                            _ => string.Empty,
                        };
                        Literal($"<{cell}{align}>");
                    }
                    else
                    {
                        Literal($"</{cell}>\n");
                    }

                    break;
            }
        }

        return html.ToString();
    }

    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
}
