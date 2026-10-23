using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using KDesktop.Core.Platform;

namespace KDesktop.Views.Assistant;

/// <summary>
/// The Markdown of a long answer (stage 14.2), shown: headings, paragraphs, lists (nested by their indent), quotes, code,
/// tables and rules, with bold, italics, code, struck text and links inside. The same reading gives a
/// <see cref="FlowDocument"/> (the reading sheet: selectable, scrolls) and a panel of text blocks (the chat's rows). Links
/// open in the user's browser, never elevated. What it does not know is shown as plain text.
/// </summary>
internal static partial class MarkdownView
{
    private abstract record Block;

    private sealed record Heading(int Level, string Text) : Block;

    private sealed record Paragraph(string Text) : Block;

    private sealed record Item(bool Ordered, string Mark, int Depth, string Text) : Block;

    private sealed record Quote(string Text) : Block;

    private sealed record Code(string Text) : Block;

    private sealed record Table(List<string[]> Rows) : Block;

    private sealed record Rule : Block;

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    /// <summary>The text as a document for <see cref="FlowDocumentScrollViewer"/>.</summary>
    public static FlowDocument Document(string markdown, double fontSize = 14)
    {
        var document = new FlowDocument
        {
            FontFamily = SystemFonts.MessageFontFamily,
            FontSize = fontSize,
            PagePadding = new Thickness(0),
            Foreground = Res("TextFillColorPrimaryBrush"),
            TextAlignment = TextAlignment.Left,
        };
        System.Windows.Documents.List? list = null;
        var depth = 0;
        foreach (var block in Parse(markdown))
        {
            if (block is not Item) list = null;
            switch (block)
            {
                case Heading h:
                    document.Blocks.Add(Para(h.Text, fontSize * HeadingScale(h.Level), FontWeights.SemiBold, new Thickness(0, h.Level <= 2 ? 14 : 10, 0, 4)));
                    break;
                case Paragraph p:
                    document.Blocks.Add(Para(p.Text, fontSize, FontWeights.Normal, new Thickness(0, 0, 0, 8)));
                    break;
                case Quote q:
                    var quote = Para(q.Text, fontSize, FontWeights.Normal, new Thickness(0, 0, 0, 8));
                    quote.BorderBrush = Res("Kd.AccentText");
                    quote.BorderThickness = new Thickness(3, 0, 0, 0);
                    quote.Padding = new Thickness(10, 2, 0, 2);
                    quote.Foreground = Res("TextFillColorSecondaryBrush");
                    document.Blocks.Add(quote);
                    break;
                case Code c:
                    document.Blocks.Add(new System.Windows.Documents.Paragraph(new Run(c.Text))
                    {
                        FontFamily = Mono,
                        FontSize = fontSize - 1.5,
                        Background = Res("Kd.ChipBackground"),
                        Padding = new Thickness(10, 8, 10, 8),
                        Margin = new Thickness(0, 0, 0, 8),
                    });
                    break;
                case Rule:
                    document.Blocks.Add(new BlockUIContainer(new Border { Height = 1, Background = Res("Kd.MenuSeparator"), Margin = new Thickness(0, 6, 0, 10) }));
                    break;
                case Table t:
                    document.Blocks.Add(TableOf(t, fontSize));
                    break;
                case Item i:
                    if (list is null || i.Depth != depth)
                    {
                        // A new list, or one level in or out: nested in the last item when it goes in.
                        var fresh = new System.Windows.Documents.List
                        {
                            MarkerStyle = i.Ordered ? TextMarkerStyle.Decimal : i.Depth % 2 == 0 ? TextMarkerStyle.Disc : TextMarkerStyle.Circle,
                            Margin = new Thickness(0, 0, 0, i.Depth == 0 ? 8 : 2),
                            Padding = new Thickness(22, 0, 0, 0),
                        };
                        if (i.Ordered && int.TryParse(i.Mark, out var start) && start > 1) fresh.StartIndex = start;
                        if (list is not null && i.Depth > depth && list.ListItems.LastListItem is { } parent) parent.Blocks.Add(fresh);
                        else document.Blocks.Add(fresh);
                        list = fresh;
                        depth = i.Depth;
                    }
                    list.ListItems.Add(new ListItem(Para(i.Text, fontSize, FontWeights.Normal, new Thickness(0, 0, 0, 2))));
                    break;
            }
        }
        return document;
    }

    /// <summary>The text as blocks of text, for a row of the chat (not selectable; the sheet is).</summary>
    public static StackPanel Panel(string markdown, double fontSize = 14)
    {
        var panel = new StackPanel();
        foreach (var block in Parse(markdown))
        {
            switch (block)
            {
                case Heading h:
                    panel.Children.Add(Text(h.Text, fontSize * HeadingScale(h.Level), FontWeights.SemiBold, new Thickness(0, 6, 0, 3)));
                    break;
                case Paragraph p:
                    panel.Children.Add(Text(p.Text, fontSize, FontWeights.Normal, new Thickness(0, 0, 0, 6)));
                    break;
                case Quote q:
                    panel.Children.Add(new Border
                    {
                        BorderBrush = Res("Kd.AccentText"),
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        Padding = new Thickness(8, 0, 0, 0),
                        Margin = new Thickness(0, 0, 0, 6),
                        Child = Text(q.Text, fontSize, FontWeights.Normal, new Thickness(0)),
                    });
                    break;
                case Code c:
                    panel.Children.Add(new Border
                    {
                        Background = Res("Kd.ChipBackground"),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(8, 6, 8, 6),
                        Margin = new Thickness(0, 0, 0, 6),
                        Child = new TextBlock { Text = c.Text, FontFamily = Mono, FontSize = fontSize - 1.5, TextWrapping = TextWrapping.Wrap, Foreground = Res("TextFillColorPrimaryBrush") },
                    });
                    break;
                case Rule:
                    panel.Children.Add(new Border { Height = 1, Background = Res("Kd.MenuSeparator"), Margin = new Thickness(0, 4, 0, 8) });
                    break;
                case Table t:
                    foreach (var row in t.Rows)
                        panel.Children.Add(Text(string.Join("  ·  ", row), fontSize, ReferenceEquals(row, t.Rows[0]) ? FontWeights.SemiBold : FontWeights.Normal, new Thickness(0, 0, 0, 2)));
                    break;
                case Item i:
                    var line = new Grid { Margin = new Thickness(i.Depth * 18, 0, 0, 2) };
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    line.Children.Add(new TextBlock { Text = i.Ordered ? i.Mark + "." : "•", FontSize = fontSize, Foreground = Res("TextFillColorSecondaryBrush") });
                    var body = Text(i.Text, fontSize, FontWeights.Normal, new Thickness(0));
                    Grid.SetColumn(body, 1);
                    line.Children.Add(body);
                    panel.Children.Add(line);
                    break;
            }
        }
        return panel;
    }

    /// <summary>The text without its marks, for the clipboard as plain text and for UI Automation.</summary>
    public static string Plain(string markdown)
    {
        var text = new StringBuilder();
        foreach (var block in Parse(markdown))
        {
            switch (block)
            {
                case Heading h: text.AppendLine(Strip(h.Text)).AppendLine(); break;
                case Paragraph p: text.AppendLine(Strip(p.Text)).AppendLine(); break;
                case Quote q: text.AppendLine(Strip(q.Text)).AppendLine(); break;
                case Code c: text.AppendLine(c.Text).AppendLine(); break;
                case Table t: foreach (var row in t.Rows) text.AppendLine(string.Join("\t", row.Select(Strip))); text.AppendLine(); break;
                case Item i: text.Append(new string(' ', i.Depth * 2)).Append(i.Ordered ? i.Mark + ". " : "• ").AppendLine(Strip(i.Text)); break;
            }
        }
        return text.ToString().Trim();
    }

    private static double HeadingScale(int level) => level switch { 1 => 1.45, 2 => 1.28, 3 => 1.14, _ => 1.04 };

    private static System.Windows.Documents.Paragraph Para(string text, double size, FontWeight weight, Thickness margin)
    {
        var paragraph = new System.Windows.Documents.Paragraph { FontSize = size, FontWeight = weight, Margin = margin, LineHeight = size * 1.4 };
        paragraph.Inlines.AddRange(Inlines(text));
        return paragraph;
    }

    private static TextBlock Text(string text, double size, FontWeight weight, Thickness margin)
    {
        var block = new TextBlock { FontSize = size, FontWeight = weight, Margin = margin, TextWrapping = TextWrapping.Wrap, Foreground = Res("TextFillColorPrimaryBrush") };
        block.Inlines.AddRange(Inlines(text));
        return block;
    }

    private static System.Windows.Documents.Table TableOf(Table t, double fontSize)
    {
        var table = new System.Windows.Documents.Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 10), BorderBrush = Res("Kd.MenuSeparator"), BorderThickness = new Thickness(1) };
        var columns = t.Rows.Max(r => r.Length);
        for (var c = 0; c < columns; c++) table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        for (var r = 0; r < t.Rows.Count; r++)
        {
            var row = new TableRow();
            if (r == 0) row.Background = Res("Kd.ChipBackground");
            for (var c = 0; c < columns; c++)
            {
                var cell = Para(c < t.Rows[r].Length ? t.Rows[r][c] : string.Empty, fontSize - 0.5, r == 0 ? FontWeights.SemiBold : FontWeights.Normal, new Thickness(0));
                row.Cells.Add(new TableCell(cell) { Padding = new Thickness(8, 4, 8, 4), BorderBrush = Res("Kd.MenuSeparator"), BorderThickness = new Thickness(0, 0, 1, 1) });
            }
            group.Rows.Add(row);
        }
        table.RowGroups.Add(group);
        return table;
    }

    private static Brush Res(string key) => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;

    // ── Reading ──────────────────────────────────────────────────────────

    private static List<Block> Parse(string markdown)
    {
        var blocks = new List<Block>();
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var paragraph = new List<string>();
        void EndParagraph()
        {
            if (paragraph.Count > 0) blocks.Add(new Paragraph(string.Join(' ', paragraph)));
            paragraph.Clear();
        }
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                EndParagraph();
                var code = new List<string>();
                // Up to its closing fence, or the end (one still being written).
                while (++i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal)) code.Add(lines[i]);
                blocks.Add(new Code(string.Join('\n', code).TrimEnd()));
                continue;
            }
            if (trimmed.Length == 0)
            {
                EndParagraph();
                continue;
            }
            if (HeadingLine().Match(trimmed) is { Success: true } heading)
            {
                EndParagraph();
                blocks.Add(new Heading(heading.Groups[1].Length, heading.Groups[2].Value.TrimEnd('#', ' ')));
                continue;
            }
            if (trimmed.Length >= 3 && (trimmed.All(c => c is '-' or ' ') || trimmed.All(c => c is '*' or ' ') || trimmed.All(c => c is '_' or ' ')) && trimmed.Count(c => c != ' ') >= 3)
            {
                EndParagraph();
                blocks.Add(new Rule());
                continue;
            }
            if (ItemLine().Match(line) is { Success: true } item)
            {
                EndParagraph();
                var indent = item.Groups[1].Value.Replace("\t", "    ", StringComparison.Ordinal).Length;
                var mark = item.Groups[2].Value;
                var ordered = char.IsDigit(mark[0]);
                blocks.Add(new Item(ordered, ordered ? mark.TrimEnd('.', ')') : mark, Math.Min(indent / 2, 4), item.Groups[3].Value.Trim()));
                continue;
            }
            if (trimmed.StartsWith('>'))
            {
                EndParagraph();
                var quote = new List<string> { trimmed.TrimStart('>').Trim() };
                while (i + 1 < lines.Length && lines[i + 1].Trim().StartsWith('>')) quote.Add(lines[++i].Trim().TrimStart('>').Trim());
                blocks.Add(new Quote(string.Join(' ', quote)));
                continue;
            }
            if (trimmed.StartsWith('|'))
            {
                EndParagraph();
                var rows = new List<string[]>();
                for (; i < lines.Length && lines[i].Trim().StartsWith('|'); i++)
                {
                    var row = lines[i].Trim().Trim('|');
                    // The line under the header.
                    if (row.Replace("|", "", StringComparison.Ordinal).Trim(' ', '-', ':').Length == 0) continue;
                    rows.Add([.. row.Split('|').Select(c => c.Trim())]);
                }
                i--;
                if (rows.Count > 0) blocks.Add(new Table(rows));
                continue;
            }
            // More of the item above, indented under it.
            if (paragraph.Count == 0 && line.StartsWith("  ", StringComparison.Ordinal) && blocks is [.., Item last])
            {
                blocks[^1] = last with { Text = last.Text + " " + trimmed };
                continue;
            }
            paragraph.Add(trimmed);
        }
        EndParagraph();
        return blocks;
    }

    private static List<Inline> Inlines(string text)
    {
        var inlines = new List<Inline>();
        var plain = new StringBuilder();
        void Flush()
        {
            if (plain.Length > 0) inlines.Add(new Run(plain.ToString()));
            plain.Clear();
        }
        var at = 0;
        while (at < text.Length)
        {
            var match = InlineMark().Match(text, at);
            if (!match.Success)
            {
                plain.Append(text, at, text.Length - at);
                break;
            }
            plain.Append(text, at, match.Index - at);
            Flush();
            if (match.Groups["code"].Success)
                inlines.Add(new Run(match.Groups["code"].Value) { FontFamily = Mono, Background = Res("Kd.ChipBackground") });
            else if (match.Groups["bold"].Success)
                inlines.Add(new Bold(Spanned(match.Groups["bold"].Value)));
            else if (match.Groups["bold2"].Success)
                inlines.Add(new Bold(Spanned(match.Groups["bold2"].Value)));
            else if (match.Groups["strike"].Success)
                inlines.Add(new Span(Spanned(match.Groups["strike"].Value)) { TextDecorations = TextDecorations.Strikethrough });
            else if (match.Groups["italic"].Success)
                inlines.Add(new Italic(Spanned(match.Groups["italic"].Value)));
            else if (match.Groups["italic2"].Success)
                inlines.Add(new Italic(Spanned(match.Groups["italic2"].Value)));
            else if (match.Groups["label"].Success)
                inlines.Add(Link(match.Groups["label"].Value, match.Groups["url"].Value));
            else if (match.Groups["bare"].Success)
                inlines.Add(Link(match.Groups["bare"].Value, match.Groups["bare"].Value));
            at = match.Index + match.Length;
        }
        Flush();
        return inlines;
    }

    private static Span Spanned(string text)
    {
        var span = new Span();
        span.Inlines.AddRange(Inlines(text));
        return span;
    }

    private static Inline Link(string label, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return new Run(label);
        var link = new Hyperlink(new Run(label)) { NavigateUri = uri, Foreground = Res("Kd.AccentText"), ToolTip = uri.ToString() };
        link.RequestNavigate += (_, e) =>
        {
            ProcessLauncher.Launch(new LaunchSpec(e.Uri.ToString()), asAdmin: false);
            e.Handled = true;
        };
        return link;
    }

    /// <summary>Inline marks taken out, for plain text.</summary>
    private static string Strip(string text) => string.Concat(Inlines(text).Select(TextOf));

    private static string TextOf(Inline inline) => inline switch
    {
        Run run => run.Text,
        System.Windows.Documents.Span span => string.Concat(span.Inlines.Select(TextOf)),
        _ => string.Empty,
    };

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^(\s*)([-*+•]|\d{1,3}[.)])\s+(.*)$")]
    private static partial Regex ItemLine();

    [GeneratedRegex(@"`(?<code>[^`]+)`|\*\*(?<bold>.+?)\*\*|__(?<bold2>.+?)__|~~(?<strike>.+?)~~|(?<![\w*])\*(?<italic>[^*\s][^*]*?)\*(?![\w*])|(?<![\w_])_(?<italic2>[^_\s][^_]*?)_(?![\w_])|\[(?<label>[^\]]+)\]\((?<url>[^)\s]+)\)|(?<bare>https?://[^\s<>()]+[^\s<>().,;:!?»”])")]
    private static partial Regex InlineMark();
}
