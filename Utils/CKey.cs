using System.Linq;

namespace Loggepector.Utils;

public static class CKey
{
    public static string Normalize(string key) =>
        new string(key.Where(character => char.IsAsciiLetterOrDigit(character) || character == '@').ToArray()).ToLowerInvariant();
}
