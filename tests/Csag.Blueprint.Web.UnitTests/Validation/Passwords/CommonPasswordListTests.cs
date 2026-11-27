namespace Csag.Blueprint.Web.UnitTests.Validation.Passwords;

using Csag.Blueprint.Web.Validation.Passwords;

/// <summary>
/// Unit tests for <see cref="CommonPasswordList"/>: case-insensitive lookup and the size of every length band,
/// which decides how many listed passwords still meet a stricter minimum length.
/// </summary>
public sealed class CommonPasswordListTests
{
    [Theory]
    [InlineData("password1234")]
    [InlineData("PASSWORD1234")]
    [InlineData("Password1234")]
    [InlineData("qwertyuiop")]
    public void Contains_ListedPassword_IgnoresCase(string password)
    {
        CommonPasswordList.Contains(password).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Tiger-Lamp-Orbit-42")]
    [InlineData("Quartz-Meadow-Lantern-7")]
    public void Contains_UnlistedPassword_ReturnsFalse(string password)
    {
        CommonPasswordList.Contains(password).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Contains_NullOrEmpty_ReturnsFalse(string? password)
    {
        CommonPasswordList.Contains(password).ShouldBeFalse();
    }

    [Fact]
    public void Count_HoldsEveryEmbeddedPassword()
    {
        CommonPasswordList.Count.ShouldBe(40_000);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(15)]
    public void EmbeddedList_UnderAnyMinimumLengthUpTo15_KeepsAtLeast10000Passwords(int minimumLength)
    {
        var passwords = ReadEmbeddedPasswords();

        passwords.Count(password => password.Length >= minimumLength).ShouldBeGreaterThanOrEqualTo(10_000);
    }

    [Fact]
    public void EmbeddedList_HoldsLowercaseEntriesOfEightOrMoreCharacters()
    {
        var passwords = ReadEmbeddedPasswords();

        passwords.ShouldAllBe(password => password.Length >= 8 && !password.Any(char.IsUpper));
    }

    private static List<string> ReadEmbeddedPasswords()
    {
        using var stream = typeof(CommonPasswordList).Assembly
            .GetManifestResourceStream("Csag.Blueprint.Web.Validation.Passwords.CommonPasswords.txt")!;
        using var reader = new StreamReader(stream);

        var passwords = new List<string>();
        while (reader.ReadLine() is { Length: > 0 } line)
        {
            passwords.Add(line);
        }

        return passwords;
    }
}
