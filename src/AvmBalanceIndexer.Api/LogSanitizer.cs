using System.Globalization;
using System.Text;

namespace AvmBalanceIndexer.Api;

internal static class LogSanitizer
{
    private const char Backslash = (char)0x5C;
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    /// <summary>
    /// Replaces control characters, invisible format characters (such as bidirectional overrides), line and paragraph separators and the backslash
    /// itself by a visible escape (a backslash, 'u', four hex digits), so that client input stays on one log line and cannot imitate an escape.
    /// </summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        StringBuilder? builder = null;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is Backslash or LineSeparator or ParagraphSeparator || char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format)
            {
                builder ??= new StringBuilder(value.Length + 16).Append(value, 0, i);
                builder.Append(Backslash).Append('u').Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
                builder?.Append(c);
            }
        }

        return builder?.ToString() ?? value;
    }
}
