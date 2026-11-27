namespace Csag.Blueprint.Web.UnitTests.Options.Api.Security.Password;

using Csag.Blueprint.Web.Options.Api.Security.Password;
using FluentValidation.TestHelper;

/// <summary>
/// Unit tests for <see cref="PasswordSettingsValidator"/>, covering the length/unique-chars
/// bounds and the relational rule between them.
/// </summary>
public sealed class PasswordSettingsValidatorTests
{
    private readonly PasswordSettingsValidator validator = new();

    [Theory]
    [InlineData(1, 1)]
    [InlineData(8, 4)]
    [InlineData(256, 256)]
    public void Validate_ValidLengthCombinations_Pass(int requiredLength, int requiredUniqueChars)
    {
        var settings = new PasswordSettings
        {
            RequiredLength = requiredLength,
            RequiredUniqueChars = requiredUniqueChars,
        };

        var result = this.validator.TestValidate(settings);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_RequiredLengthBelowOne_Fails(int requiredLength)
    {
        var settings = new PasswordSettings { RequiredLength = requiredLength, RequiredUniqueChars = 1 };

        var result = this.validator.TestValidate(settings);

        result.ShouldHaveValidationErrorFor(x => x.RequiredLength)
            .WithErrorMessage("RequiredLength must be at least 1");
    }

    [Fact]
    public void Validate_RequiredLengthAbove256_Fails()
    {
        var settings = new PasswordSettings { RequiredLength = 257, RequiredUniqueChars = 1 };

        var result = this.validator.TestValidate(settings);

        result.ShouldHaveValidationErrorFor(x => x.RequiredLength)
            .WithErrorMessage("RequiredLength must not exceed 256");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_RequiredUniqueCharsBelowOne_Fails(int requiredUniqueChars)
    {
        var settings = new PasswordSettings { RequiredLength = 8, RequiredUniqueChars = requiredUniqueChars };

        var result = this.validator.TestValidate(settings);

        result.ShouldHaveValidationErrorFor(x => x.RequiredUniqueChars)
            .WithErrorMessage("RequiredUniqueChars must be at least 1");
    }

    [Fact]
    public void Validate_RequiredUniqueCharsAbove256_Fails()
    {
        var settings = new PasswordSettings { RequiredLength = 256, RequiredUniqueChars = 257 };

        var result = this.validator.TestValidate(settings);

        result.ShouldHaveValidationErrorFor(x => x.RequiredUniqueChars)
            .WithErrorMessage("RequiredUniqueChars must not exceed 256");
    }

    [Fact]
    public void Validate_RequiredUniqueCharsGreaterThanRequiredLength_Fails()
    {
        var settings = new PasswordSettings { RequiredLength = 8, RequiredUniqueChars = 9 };

        var result = this.validator.TestValidate(settings);

        result.ShouldHaveValidationErrorFor(x => x.RequiredUniqueChars)
            .WithErrorMessage("RequiredUniqueChars cannot be greater than RequiredLength");
    }

    [Fact]
    public void Validate_ContextSpecificWordsOfThreeOrMoreLettersOrDigits_Pass()
    {
        var settings = new PasswordSettings { RequiredLength = 8, RequiredUniqueChars = 1, ContextSpecificWords = ["csag", "blueprint-web", "app"] };

        var result = this.validator.TestValidate(settings);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("--")]
    [InlineData("  ")]
    public void Validate_ContextSpecificWordTooShort_Fails(string word)
    {
        var settings = new PasswordSettings { RequiredLength = 8, RequiredUniqueChars = 1, ContextSpecificWords = ["csag", word] };

        var result = this.validator.TestValidate(settings);

        result.ShouldHaveValidationErrorFor("ContextSpecificWords[1]")
            .WithErrorMessage($"ContextSpecificWords entry '{word}' must contain at least 3 letters or digits");
    }

    [Fact]
    public void Validate_BreachCheckOnlineWithTheDefaultApi_Passes()
    {
        var settings = new PasswordSettings { RequiredLength = 8, RequiredUniqueChars = 1, BreachCheck = { Mode = BreachCheckMode.Online } };

        var result = this.validator.TestValidate(settings);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("http://ranges.example.test/range/")]
    [InlineData("https://ranges.example.test/range")]
    [InlineData("ranges.example.test/range/")]
    [InlineData("")]
    public void Validate_BreachCheckOnlineWithAnInvalidApiUrl_Fails(string url)
    {
        var settings = new PasswordSettings
        {
            RequiredLength = 8,
            RequiredUniqueChars = 1,
            BreachCheck = { Mode = BreachCheckMode.Online, RangeApiBaseUrl = url },
        };

        var result = this.validator.TestValidate(settings);

        result.ShouldHaveValidationErrorFor("BreachCheck.RangeApiBaseUrl");
    }

    [Theory]
    [InlineData(99)]
    [InlineData(30001)]
    public void Validate_BreachCheckTimeoutOutOfRange_Fails(int timeoutMilliseconds)
    {
        var settings = new PasswordSettings { RequiredLength = 8, RequiredUniqueChars = 1, BreachCheck = { TimeoutMilliseconds = timeoutMilliseconds } };

        var result = this.validator.TestValidate(settings);

        result.ShouldHaveValidationErrorFor("BreachCheck.TimeoutMilliseconds");
    }

    [Fact]
    public void Validate_BreachCheckOfflineWithoutADirectory_Fails()
    {
        var settings = new PasswordSettings { RequiredLength = 8, RequiredUniqueChars = 1, BreachCheck = { Mode = BreachCheckMode.Offline } };

        var result = this.validator.TestValidate(settings);

        result.ShouldHaveValidationErrorFor("BreachCheck.OfflineRangeDirectory")
            .WithErrorMessage("BreachCheck:OfflineRangeDirectory is required when Mode is Offline");
    }

    [Fact]
    public void Validate_BreachCheckOfflineWithAMissingDirectory_Fails()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-ranges-{Guid.NewGuid():N}");
        var settings = new PasswordSettings
        {
            RequiredLength = 8,
            RequiredUniqueChars = 1,
            BreachCheck = { Mode = BreachCheckMode.Offline, OfflineRangeDirectory = missing },
        };

        var result = this.validator.TestValidate(settings);

        result.ShouldHaveValidationErrorFor("BreachCheck.OfflineRangeDirectory")
            .WithErrorMessage($"BreachCheck:OfflineRangeDirectory '{missing}' does not exist");
    }

    [Fact]
    public void Validate_BreachCheckOfflineWithAnExistingDirectory_Passes()
    {
        var directory = Directory.CreateTempSubdirectory("breach-ranges-");
        try
        {
            var settings = new PasswordSettings
            {
                RequiredLength = 8,
                RequiredUniqueChars = 1,
                BreachCheck = { Mode = BreachCheckMode.Offline, OfflineRangeDirectory = directory.FullName },
            };

            var result = this.validator.TestValidate(settings);

            result.ShouldNotHaveAnyValidationErrors();
        }
        finally
        {
            directory.Delete();
        }
    }
}
