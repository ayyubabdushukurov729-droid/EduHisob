using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace EduHisob.Data.Assistant;

// Yordamchi javobidagi oddiy markdownni xavfsiz HTMLga aylantiradi: sarlavha, qalin matn, ro'yxat, jadval, havola.
// Avval hamma narsa HTML-escape qilinadi, havolalar faqat sayt ichidagi manzillarga ("/...") ruxsat etiladi.
public static partial class ChatMarkdown
{
    public static string ToHtml(string markdown)
    {
        var html = new StringBuilder();
        var lines = markdown.Replace("\r", "").Split('\n');
        string? openList = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith('|') && i + 1 < lines.Length && SeparatorRow().IsMatch(lines[i + 1].Trim()))
            {
                CloseList(html, ref openList);
                html.Append("<div class=\"chat-table\"><table><thead><tr>");
                foreach (var cell in Cells(trimmed)) html.Append("<th>").Append(Inline(cell)).Append("</th>");
                html.Append("</tr></thead><tbody>");
                i += 2;
                for (; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++)
                {
                    html.Append("<tr>");
                    foreach (var cell in Cells(lines[i].Trim())) html.Append("<td>").Append(Inline(cell)).Append("</td>");
                    html.Append("</tr>");
                }
                i--;
                html.Append("</tbody></table></div>");
                continue;
            }

            var bullet = BulletItem().Match(trimmed);
            var numbered = NumberedItem().Match(trimmed);
            if (bullet.Success || numbered.Success)
            {
                var kind = bullet.Success ? "ul" : "ol";
                if (openList != kind)
                {
                    CloseList(html, ref openList);
                    html.Append('<').Append(kind).Append('>');
                    openList = kind;
                }
                html.Append("<li>").Append(Inline((bullet.Success ? bullet : numbered).Groups[1].Value)).Append("</li>");
                continue;
            }

            CloseList(html, ref openList);
            if (trimmed.Length == 0) continue;
            var heading = Heading().Match(trimmed);
            if (heading.Success)
            {
                html.Append("<h4>").Append(Inline(heading.Groups[1].Value)).Append("</h4>");
            }
            else
            {
                html.Append("<p>").Append(Inline(trimmed)).Append("</p>");
            }
        }
        CloseList(html, ref openList);
        return html.ToString();
    }

    private static void CloseList(StringBuilder html, ref string? openList)
    {
        if (openList is null) return;
        html.Append("</").Append(openList).Append('>');
        openList = null;
    }

    private static IEnumerable<string> Cells(string row) => row.Trim().Trim('|').Split('|').Select(cell => cell.Trim());

    private static string Inline(string text)
    {
        var escaped = WebUtility.HtmlEncode(text);
        escaped = Link().Replace(escaped, match =>
        {
            var url = WebUtility.HtmlDecode(match.Groups[2].Value);
            return url.StartsWith('/') && !url.StartsWith("//")
                ? $"<a href=\"{WebUtility.HtmlEncode(url)}\">{match.Groups[1].Value}</a>"
                : match.Groups[1].Value;
        });
        escaped = Bold().Replace(escaped, "<strong>$1</strong>");
        escaped = Code().Replace(escaped, "<code>$1</code>");
        escaped = Italic().Replace(escaped, "<em>$1</em>");
        return escaped;
    }

    [GeneratedRegex(@"^\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?$")] private static partial Regex SeparatorRow();
    [GeneratedRegex(@"^[-*•]\s+(.*)$")] private static partial Regex BulletItem();
    [GeneratedRegex(@"^\d+[.)]\s+(.*)$")] private static partial Regex NumberedItem();
    [GeneratedRegex(@"^#{1,4}\s+(.*)$")] private static partial Regex Heading();
    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")] private static partial Regex Link();
    [GeneratedRegex(@"\*\*(.+?)\*\*")] private static partial Regex Bold();
    [GeneratedRegex(@"`([^`]+)`")] private static partial Regex Code();
    [GeneratedRegex(@"(?<![\w*])_(.+?)_(?![\w*])")] private static partial Regex Italic();
}
