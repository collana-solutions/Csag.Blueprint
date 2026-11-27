namespace Csag.Blueprint.Web.Validation.Passwords;

using System.Text;

/// <summary>
/// Finds context-specific words (organisation, product or account names) inside a password. Both sides are
/// normalised first: lowercased, common character substitutions replaced by the letter they stand for, and
/// everything except letters and digits removed. <c>C0ll4na-2026!</c> therefore contains <c>collana</c>.
/// </summary>
public static class ContextSpecificWordMatcher
{
    /// <summary>
    /// Gets the fewest letters or digits a normalised word needs to be matched. Shorter words would match inside
    /// too many unrelated passwords.
    /// </summary>
    public static int MinimumWordLength => 3;

    /// <summary>
    /// Returns the first word, as given, that the normalised password contains.
    /// </summary>
    /// <param name="password">The password to search.</param>
    /// <param name="words">The words to look for. Words shorter than <see cref="MinimumWordLength"/> after normalisation are skipped.</param>
    /// <returns>The matching word, or <see langword="null"/> when the password contains none.</returns>
    public static string? FindWord(string? password, IEnumerable<string?> words)
    {
        ArgumentNullException.ThrowIfNull(words);

        if (string.IsNullOrEmpty(password))
        {
            return null;
        }

        var normalizedPassword = Normalize(password);
        foreach (var word in words)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                continue;
            }

            var normalizedWord = Normalize(word);
            if (normalizedWord.Length >= MinimumWordLength && normalizedPassword.Contains(normalizedWord, StringComparison.Ordinal))
            {
                return word;
            }
        }

        return null;
    }

    /// <summary>
    /// Normalises a password or word for matching.
    /// </summary>
    /// <param name="value">The text to normalise.</param>
    /// <returns>The lowercased letters and digits of <paramref name="value"/>, with substitutions replaced.</returns>
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            var letter = character switch
            {
                '4' or '@' => 'a',
                '8' => 'b',
                '3' => 'e',
                '9' => 'g',

                // 1, ! and | stand in for both i and l, so the two letters share one form.
                '1' or '!' or '|' or 'l' => 'i',
                '0' => 'o',
                '5' or '$' => 's',
                '7' or '+' => 't',
                _ => character,
            };

            if (char.IsLetterOrDigit(letter))
            {
                builder.Append(letter);
            }
        }

        return builder.ToString();
    }
}
