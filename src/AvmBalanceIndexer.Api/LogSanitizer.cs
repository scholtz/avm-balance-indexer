using System.Globalization;
using System.Text;

namespace AvmBalanceIndexer.Api;

internal static class LogSanitizer
{
    /// <summary>Replaces control characters of client input by a visible escape (a backslash, 'u', four hex digits) so that a value stays on one log line.</summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        StringBuilder? builder = null;
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsControl(value[i]))
            {
                builder ??= new StringBuilder(value.Length + 16).Append(value, 0, i);
                builder.Append("\\u").Append(((int)value[i]).ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
                builder?.Append(value[i]);
            }
        }

        return builder?.ToString() ?? value;
    }
}
