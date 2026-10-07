using System.Net;
using System.Text.RegularExpressions;

namespace SphotoApp.Web.Services;

internal static class DescriptionText
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public static string FromHtml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var text = value.Replace("\r\n", "\n").Replace('\r', '\n');
        // Some exports encode the entire HTML fragment instead of just its text.
        if (!Regex.IsMatch(text, @"<(?:p|span|div|br)\b", RegexOptions.IgnoreCase, Timeout)
            && Regex.IsMatch(text, @"&lt;/?(?:p|span|div|br)\b", RegexOptions.IgnoreCase, Timeout))
            text = WebUtility.HtmlDecode(text);

        text = Regex.Replace(text, @"<!--.*?-->|<(script|style)\b[^>]*>.*?</\1\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline, Timeout);
        text = Regex.Replace(text, @"<br\b[^>]*>|</?(?:p|div|h[1-6]|ul|ol|tr|blockquote|section|article|pre)\b[^>]*>", "\n", RegexOptions.IgnoreCase, Timeout);
        text = Regex.Replace(text, @"<li\b[^>]*>", "\n• ", RegexOptions.IgnoreCase, Timeout);
        text = Regex.Replace(text, @"</li\s*>", "\n", RegexOptions.IgnoreCase, Timeout);
        text = Regex.Replace(text, @"</t[dh]\s*>", " ", RegexOptions.IgnoreCase, Timeout);
        text = Regex.Replace(text, @"</?[a-z][a-z0-9:-]*\b(?:[^>""']|""[^""]*""|'[^']*')*>", "", RegexOptions.IgnoreCase, Timeout);
        // Decode after stripping markup so escaped text such as &lt;5 remains text.
        text = WebUtility.HtmlDecode(text).Replace('\u00a0', ' ').Replace("\u200b", "");
        text = Regex.Replace(text, @"[^\S\n]+", " ", RegexOptions.None, Timeout);
        text = Regex.Replace(text, @" *\n *", "\n", RegexOptions.None, Timeout);
        text = Regex.Replace(text, @"\n{3,}", "\n\n", RegexOptions.None, Timeout);
        text = Regex.Replace(text, @"\n\n(?=• )", "\n", RegexOptions.None, Timeout);
        return text.Trim();
    }
}
