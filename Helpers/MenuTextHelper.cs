using System.Text;

namespace RCMenuManager.Helpers;

/// <summary>
/// The shell stores access keys as <c>&amp;X</c> (<c>&amp;&amp;</c> is a literal
/// ampersand). Explorer underlines the letter and hides the marker; a plain
/// <see cref="System.Windows.Controls.TextBlock"/> prints it verbatim, which is why the
/// list showed "显示设置(&amp;D)" while the real menu shows "显示设置(D)".
/// </summary>
public static class MenuTextHelper
{
    /// <summary>
    /// Compares menu text the way Windows does: access-key markers removed and the
    /// trailing accelerator suffix dropped. The list and the preview must use the same
    /// rule, or the same row matches in one place and not in the other.
    /// </summary>
    public static string NormalizeForMatch(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var stripped = StripAccessKeys(text).Trim();
        var open = stripped.LastIndexOf('(');
        if (open > 0 && stripped.EndsWith(")", System.StringComparison.Ordinal))
        {
            var suffix = stripped.Substring(open + 1, stripped.Length - open - 2).Trim();
            // "(O)" / "(z)" -> accelerator suffix; keep real parentheses such as
            // "压缩(zipped)文件夹".
            if (suffix.Length is 1 or 2 && suffix.All(char.IsLetterOrDigit))
                return stripped.Substring(0, open).Trim();
        }
        return stripped;
    }

    /// <summary>Removes the access-key marker but keeps the letter.</summary>
    public static string StripAccessKeys(string? text)
    {
        if (string.IsNullOrEmpty(text) || text!.IndexOf('&') < 0) return text ?? string.Empty;

        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '&') { sb.Append(text[i]); continue; }
            if (i + 1 < text.Length && text[i + 1] == '&') { sb.Append('&'); i++; continue; }
            // A lone '&' marks the next character as the accelerator; drop the marker.
        }
        return sb.ToString();
    }
}
