using System.Text;
using System.Text.RegularExpressions;

namespace Core.Utils
{
    /// <summary>
    /// Server rich text (news bodies are HTML written in the admin) → TextMeshPro rich text. Keeps emphasis,
    /// headings, paragraphs and list items; drops every other tag (scripts, styles, links, images). Text
    /// between tags is entity-decoded and wrapped in &lt;noparse&gt;, so nothing written by the server can
    /// inject TMP markup.
    /// </summary>
    public static class HtmlText
    {
        static readonly Regex Tag = new(@"<(/?)([a-zA-Z0-9]+)[^>]*>", RegexOptions.Compiled);
        static readonly Regex Img = new(@"<img[^>]+src\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex Drop = new(@"<(script|style)[^>]*>.*?</\1>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        /// <summary>Pictographs the SDF fonts have no glyph for (emoji, dingbats, variation selectors): shown as □.</summary>
        static readonly Regex Emoji = new(@"[\uD800-\uDFFF]|[\u2190-\u21FF\u2300-\u23FF\u2460-\u27BF\u2900-\u2BFF\uFE0F\u200D]", RegexOptions.Compiled);
        static readonly Regex Blank = new(@"\n{3,}", RegexOptions.Compiled);

        /// <summary>First &lt;img src&gt; of the body (the web shows it as the news picture when none is set).</summary>
        public static string FirstImage(string html)
        {
            if (string.IsNullOrEmpty(html))
                return null;
            var m = Img.Match(html);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>Plain server text (titles) without the pictographs the fonts cannot draw.</summary>
        public static string StripGlyphs(string text) => string.IsNullOrEmpty(text) ? string.Empty : Emoji.Replace(text, string.Empty).Trim();

        public static string ToTmp(string html)
        {
            if (string.IsNullOrEmpty(html))
                return string.Empty;
            html = Drop.Replace(html, string.Empty);
            // Plain text keeps its newlines; inside HTML, source line breaks are only layout (collapse them).
            var markup = Tag.IsMatch(html);
            var sb = new StringBuilder(html.Length);
            var at = 0;
            foreach (Match m in Tag.Matches(html))
            {
                Text(sb, html.Substring(at, m.Index - at), markup);
                at = m.Index + m.Length;
                var closing = m.Groups[1].Value == "/";
                switch (m.Groups[2].Value.ToLowerInvariant())
                {
                    case "b":
                    case "strong":
                        sb.Append(closing ? "</b>" : "<b>");
                        break;
                    case "i":
                    case "em":
                        sb.Append(closing ? "</i>" : "<i>");
                        break;
                    case "u":
                        sb.Append(closing ? "</u>" : "<u>");
                        break;
                    case "h1":
                    case "h2":
                    case "h3":
                    case "h4":
                        sb.Append(closing ? "</b></size>\n" : "\n<size=118%><b>");
                        break;
                    case "br":
                        sb.Append('\n');
                        break;
                    case "p":
                    case "div":
                        if (closing)
                            sb.Append('\n');
                        break;
                    case "li":
                        sb.Append(closing ? "\n" : "  •  ");
                        break;
                    case "ul":
                    case "ol":
                        sb.Append('\n');
                        break;
                }
            }

            Text(sb, html.Substring(at), markup);
            return Blank.Replace(sb.ToString().Trim(), "\n\n");
        }

        static void Text(StringBuilder sb, string raw, bool markup)
        {
            if (string.IsNullOrEmpty(raw))
                return;
            var decoded = System.Net.WebUtility.HtmlDecode(raw).Replace("\r", string.Empty);
            if (markup)
                decoded = decoded.Replace('\n', ' ');
            decoded = Emoji.Replace(decoded, string.Empty);
            if (decoded.Length == 0)
                return;
            sb.Append("<noparse>").Append(decoded.Replace("</noparse>", "</ noparse>")).Append("</noparse>");
        }
    }
}
