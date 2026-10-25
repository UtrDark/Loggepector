using System;

namespace Loggepector.Utils;

public static class CKey
{
    public static string Normalize(string key)
    {
        Span<char> buffer = key.Length <= 256 ? stackalloc char[key.Length] : new char[key.Length];
        var length = 0;
        var unchanged = true;
        foreach (var character in key)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '@')
            {
                unchanged = false;
                continue;
            }
            var lower = character is >= 'A' and <= 'Z' ? (char)(character + ('a' - 'A')) : character;
            unchanged &= lower == character;
            buffer[length++] = lower;
        }
        return unchanged ? key : new string(buffer[..length]);
    }
}
