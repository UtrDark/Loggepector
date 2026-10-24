using System.Text.RegularExpressions;

namespace Loggepector.Utils;

internal static class EmojiMarkup
{
    private static readonly Regex Span = new("""<span\b[^>]*\bclass\s*=\s*["'][^"']*\bemoji-(?<name>[A-Za-z0-9_-]+)[^"']*["'][^>]*>\s*</span\s*>""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    internal static readonly Regex Token = new(@":emoji-(?<name>[A-Za-z0-9_-]+):", RegexOptions.Compiled);
    public static string Replace(string text) => Span.Replace(text, ":emoji-${name}:");
    public static bool Contains(string text) => Span.IsMatch(text);
}
