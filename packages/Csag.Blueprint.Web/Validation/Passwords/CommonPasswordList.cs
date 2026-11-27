namespace Csag.Blueprint.Web.Validation.Passwords;

using System.Collections.Frozen;

/// <summary>
/// The common passwords that password screening rejects: 40,000 passwords of 8 or more characters, the
/// 10,000 most frequent of each length band (8-9, 10-11, 12-14 and 15 or more). Under any minimum length
/// from 8 to 15, at least 10,000 of them meet the policy. The list comes from the xato.net list of the one
/// million most common passwords; <c>scripts/build-common-passwords.mjs</c> builds it.
/// </summary>
public static class CommonPasswordList
{
    private const string ResourceName = "Csag.Blueprint.Web.Validation.Passwords.CommonPasswords.txt";

    private static readonly Lazy<FrozenSet<string>> Passwords = new(Load);

    /// <summary>
    /// Gets the number of passwords in the list.
    /// </summary>
    public static int Count => Passwords.Value.Count;

    /// <summary>
    /// Determines whether a password is in the list, ignoring case.
    /// </summary>
    /// <param name="password">The password to look up.</param>
    /// <returns><see langword="true"/> when the password is a listed common password.</returns>
    public static bool Contains(string? password) =>
        !string.IsNullOrEmpty(password) && Passwords.Value.Contains(password);

    private static FrozenSet<string> Load()
    {
        var assembly = typeof(CommonPasswordList).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded resource '{ResourceName}' is missing from {assembly.GetName().Name}.");
        using var reader = new StreamReader(stream);

        var passwords = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                passwords.Add(line);
            }
        }

        return passwords.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }
}
