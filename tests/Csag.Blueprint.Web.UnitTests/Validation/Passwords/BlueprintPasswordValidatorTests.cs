namespace Csag.Blueprint.Web.UnitTests.Validation.Passwords;

using Csag.Blueprint.Tests.Shared.Entities;
using Csag.Blueprint.Web.Options.Api.Security;
using Csag.Blueprint.Web.Options.Api.Security.Password;
using Csag.Blueprint.Web.Validation.Passwords;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;
using Moq;

// Inside this namespace the bare identifier "Options" binds to the Csag.Blueprint.Web.Options
// namespace, so the Microsoft options factory needs an explicit alias.
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

/// <summary>
/// Unit tests for <see cref="BlueprintPasswordValidator{TUser}"/>: each screening rule, the order of the local
/// checks and the breach lookup, fail-open and fail-closed, and the localized descriptions.
/// </summary>
public sealed class BlueprintPasswordValidatorTests
{
    private const string CleanPassword = "Tiger-Lamp-Orbit-42";

    [Fact]
    public async Task ValidateAsync_CleanPassword_SucceedsAfterOneBreachLookupAsync()
    {
        var checker = new StubBreachedPasswordChecker(BreachCheckResult.NotBreached);
        var validator = CreateValidator(checker);

        var result = await validator.ValidateAsync(CreateUserManager("anna.meier@example.com"), new TestUser(), CleanPassword);

        result.Succeeded.ShouldBeTrue();
        checker.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ValidateAsync_NoPassword_LeavesItToIdentitysRulesAsync(string? password)
    {
        var checker = new StubBreachedPasswordChecker(BreachCheckResult.Breached);
        var validator = CreateValidator(checker);

        var result = await validator.ValidateAsync(CreateUserManager("anna.meier@example.com"), new TestUser(), password);

        result.Succeeded.ShouldBeTrue();
        checker.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task ValidateAsync_CommonPassword_FailsWithoutABreachLookupAsync()
    {
        var checker = new StubBreachedPasswordChecker(BreachCheckResult.NotBreached);
        var validator = CreateValidator(checker);

        var result = await validator.ValidateAsync(CreateUserManager("anna.meier@example.com"), new TestUser(), "Password1234");

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PasswordScreeningErrorCodes.TooCommon);
        checker.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task ValidateAsync_PasswordWithAContextWord_FailsNamingTheWordAsync()
    {
        var validator = CreateValidator(new StubBreachedPasswordChecker(BreachCheckResult.NotBreached), contextSpecificWords: ["collana"]);

        var result = await validator.ValidateAsync(CreateUserManager("anna.meier@example.com"), new TestUser(), "C0ll4na-Sunrise-2026");

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(PasswordScreeningErrorCodes.ContainsContextWord);
        error.Description.ShouldBe("Password must not contain \"collana\"");
    }

    [Theory]
    [InlineData("Sunrise-Meier-2026")]
    [InlineData("AnnaMeier-Orbit!")]
    public async Task ValidateAsync_PasswordWithTheEmailLocalPart_FailsAsync(string password)
    {
        var validator = CreateValidator(new StubBreachedPasswordChecker(BreachCheckResult.NotBreached));

        var result = await validator.ValidateAsync(CreateUserManager("anna.meier@example.com"), new TestUser(), password);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PasswordScreeningErrorCodes.ContainsAccountName);
    }

    [Fact]
    public async Task ValidateAsync_PasswordWithAShortPartOfTheEmail_SucceedsAsync()
    {
        var validator = CreateValidator(new StubBreachedPasswordChecker(BreachCheckResult.NotBreached));

        var result = await validator.ValidateAsync(CreateUserManager("max.meier@example.com"), new TestUser(), "Maximum-Sunrise-2026");

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ShortWholeEmailLocalPart_IsNotMatchedInsideOrdinaryWordsAsync()
    {
        var validator = CreateValidator(new StubBreachedPasswordChecker(BreachCheckResult.NotBreached));

        var result = await validator.ValidateAsync(CreateUserManager("tim@example.com"), new TestUser(), "Optimistic-Orbit-42");

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_PasswordIdentitysOwnOptionsRefuse_SkipsTheBreachLookupAsync()
    {
        // Identity's default options require 6 characters, so its own validator reports this password.
        var checker = new StubBreachedPasswordChecker(BreachCheckResult.Unavailable);
        var validator = CreateValidator(checker, failOpen: false);

        var result = await validator.ValidateAsync(CreateUserManager("anna.meier@example.com"), new TestUser(), "Ab1!");

        result.Succeeded.ShouldBeTrue();
        checker.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task ValidateAsync_PasswordWithTheUserName_FailsAsync()
    {
        var validator = CreateValidator(new StubBreachedPasswordChecker(BreachCheckResult.NotBreached));

        var result = await validator.ValidateAsync(CreateUserManager(email: null, userName: "orbitpilot"), new TestUser(), "0rbitPilot-2026!");

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PasswordScreeningErrorCodes.ContainsAccountName);
    }

    [Fact]
    public async Task ValidateAsync_StoreWithoutEmailSupport_ChecksTheUserNameOnlyAsync()
    {
        var store = new Mock<IUserStore<TestUser>>();
        store.Setup(s => s.GetUserNameAsync(It.IsAny<TestUser>(), It.IsAny<CancellationToken>())).ReturnsAsync("orbitpilot");
        var manager = new UserManager<TestUser>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var validator = CreateValidator(new StubBreachedPasswordChecker(BreachCheckResult.NotBreached));

        var result = await validator.ValidateAsync(manager, new TestUser(), CleanPassword);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_BreachedPassword_FailsAsync()
    {
        var validator = CreateValidator(new StubBreachedPasswordChecker(BreachCheckResult.Breached));

        var result = await validator.ValidateAsync(CreateUserManager("anna.meier@example.com"), new TestUser(), CleanPassword);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PasswordScreeningErrorCodes.Breached);
    }

    [Fact]
    public async Task ValidateAsync_BreachCheckUnavailableAndFailOpen_SucceedsAsync()
    {
        var validator = CreateValidator(new StubBreachedPasswordChecker(BreachCheckResult.Unavailable), failOpen: true);

        var result = await validator.ValidateAsync(CreateUserManager("anna.meier@example.com"), new TestUser(), CleanPassword);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_BreachCheckUnavailableAndFailClosed_FailsAsync()
    {
        var validator = CreateValidator(new StubBreachedPasswordChecker(BreachCheckResult.Unavailable), failOpen: false);

        var result = await validator.ValidateAsync(CreateUserManager("anna.meier@example.com"), new TestUser(), CleanPassword);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PasswordScreeningErrorCodes.BreachCheckUnavailable);
    }

    [Fact]
    public async Task ValidateAsync_WithLocalizer_UsesTheTranslationKeyAsync()
    {
        var validator = CreateValidator(new StubBreachedPasswordChecker(BreachCheckResult.NotBreached), localizer: new EchoStringLocalizer());

        var result = await validator.ValidateAsync(CreateUserManager("anna.meier@example.com"), new TestUser(), "Password1234");

        result.Errors.ShouldHaveSingleItem().Description.ShouldBe("Validation.PasswordTooCommon");
    }

    [Fact]
    public async Task ValidateAsync_LocalizerWithoutTheKey_FallsBackToEnglishAsync()
    {
        var validator = CreateValidator(new StubBreachedPasswordChecker(BreachCheckResult.NotBreached), localizer: new MissingKeyStringLocalizer());

        var result = await validator.ValidateAsync(CreateUserManager("anna.meier@example.com"), new TestUser(), "Password1234");

        result.Errors.ShouldHaveSingleItem().Description.ShouldBe("This password is too common. Choose one that is harder to guess");
    }

    private static BlueprintPasswordValidator<TestUser> CreateValidator(
        IBreachedPasswordChecker checker,
        IList<string>? contextSpecificWords = null,
        bool failOpen = true,
        IStringLocalizer? localizer = null)
    {
        var passwordSettings = new PasswordSettings
        {
            ContextSpecificWords = contextSpecificWords ?? [],
            BreachCheck = new BreachCheckSettings { FailOpen = failOpen },
        };
        var securitySettings = MicrosoftOptions.Create(new SecuritySettings { PasswordSettings = passwordSettings });
        return new BlueprintPasswordValidator<TestUser>(securitySettings, checker, localizer);
    }

    private static UserManager<TestUser> CreateUserManager(string? email, string? userName = null)
    {
        var store = new Mock<IUserStore<TestUser>>();
        store.Setup(s => s.GetUserNameAsync(It.IsAny<TestUser>(), It.IsAny<CancellationToken>())).ReturnsAsync(userName ?? email);
        store.As<IUserEmailStore<TestUser>>()
            .Setup(s => s.GetEmailAsync(It.IsAny<TestUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(email);
        return new UserManager<TestUser>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private sealed class StubBreachedPasswordChecker(BreachCheckResult result) : IBreachedPasswordChecker
    {
        public int Calls { get; private set; }

        public Task<BreachCheckResult> CheckAsync(string password, CancellationToken cancellationToken = default)
        {
            this.Calls++;
            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// Localizer stub that echoes the lookup key back as the localized value.
    /// </summary>
    private sealed class EchoStringLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] => new(name, name);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    /// <summary>
    /// Localizer stub for an application that has not defined the screening keys yet.
    /// </summary>
    private sealed class MissingKeyStringLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] => new(name, name, resourceNotFound: true);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
