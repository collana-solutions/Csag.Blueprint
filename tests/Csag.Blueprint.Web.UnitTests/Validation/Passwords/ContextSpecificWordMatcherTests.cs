namespace Csag.Blueprint.Web.UnitTests.Validation.Passwords;

using Csag.Blueprint.Web.Validation.Passwords;

/// <summary>
/// Unit tests for <see cref="ContextSpecificWordMatcher"/>: substitutions, separators, case and the minimum
/// word length.
/// </summary>
public sealed class ContextSpecificWordMatcherTests
{
    [Theory]
    [InlineData("MyCollanaPassword")]
    [InlineData("C0LL4NA!2026")]
    [InlineData("co11ana-rocks")]
    [InlineData("c.o.l.l.a.n.a")]
    [InlineData("Co!!@n@ forever")]
    public void FindWord_PasswordWithTheWordInAnyDisguise_ReturnsTheWordAsConfigured(string password)
    {
        ContextSpecificWordMatcher.FindWord(password, ["csag", "Collana"]).ShouldBe("Collana");
    }

    [Fact]
    public void FindWord_WordWithSeparators_MatchesThePasswordWithout()
    {
        ContextSpecificWordMatcher.FindWord("BlueprintWeb2026", ["blueprint-web"]).ShouldBe("blueprint-web");
    }

    [Fact]
    public void FindWord_NoWordInPassword_ReturnsNull()
    {
        ContextSpecificWordMatcher.FindWord("Tiger-Lamp-Orbit-42", ["collana", "csag"]).ShouldBeNull();
    }

    [Fact]
    public void FindWord_WordShorterThanMinimum_IsSkipped()
    {
        ContextSpecificWordMatcher.FindWord("abcdefgh", ["ab", "--"]).ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FindWord_NullOrEmptyPassword_ReturnsNull(string? password)
    {
        ContextSpecificWordMatcher.FindWord(password, ["collana"]).ShouldBeNull();
    }

    [Fact]
    public void FindWord_NullOrBlankWords_AreSkipped()
    {
        ContextSpecificWordMatcher.FindWord("Collana2026", [null, " ", "collana"]).ShouldBe("collana");
    }

    [Theory]
    [InlineData("P@$$w0rd", "password")]
    [InlineData("Blue-Print_2026", "biueprint2o26")]
    [InlineData("4837+9", "abettg")]
    [InlineData("ILLINOIS", "iiiinois")]
    public void Normalize_ReplacesSubstitutionsAndDropsSeparators(string value, string expected)
    {
        ContextSpecificWordMatcher.Normalize(value).ShouldBe(expected);
    }
}
