namespace Csag.Blueprint.Web.UnitTests.Extensions;

using Csag.Blueprint.Tests.Shared.Entities;
using Csag.Blueprint.Web.Extensions;
using Csag.Blueprint.Web.Validation.Passwords;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moq;

/// <summary>
/// Unit tests for <see cref="PasswordScreeningExtensions.AddBlueprintPasswordScreening"/>: the validator joins
/// Identity's password validators, and a password set through <see cref="UserManager{TUser}"/> is screened.
/// </summary>
public sealed class PasswordScreeningExtensionsTests
{
    private readonly Mock<IUserPasswordStore<TestUser>> store = new();

    public PasswordScreeningExtensionsTests()
    {
        this.store.Setup(s => s.GetUserNameAsync(It.IsAny<TestUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TestUser user, CancellationToken _) => user.UserName);
    }

    [Fact]
    public void AddBlueprintPasswordScreening_AddsTheValidatorAndTheChecker()
    {
        using var provider = this.BuildProvider();
        using var scope = provider.CreateScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TestUser>>();
        userManager.PasswordValidators.ShouldContain(validator => validator is BlueprintPasswordValidator<TestUser>);
        scope.ServiceProvider.GetRequiredService<IBreachedPasswordChecker>().ShouldBeOfType<BreachedPasswordChecker>();
    }

    [Fact]
    public async Task CreateAsync_WithACommonPassword_IsRefusedBeforeTheUserIsStoredAsync()
    {
        using var provider = this.BuildProvider();
        using var scope = provider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TestUser>>();

        var result = await userManager.CreateAsync(new TestUser { UserName = "anna.meier@example.com" }, "Password1234");

        result.Errors.ShouldContain(error => error.Code == PasswordScreeningErrorCodes.TooCommon);
        this.store.Verify(s => s.CreateAsync(It.IsAny<TestUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IUserStore<TestUser>>(this.store.Object);
        services.AddIdentityCore<TestUser>().AddBlueprintPasswordScreening();
        return services.BuildServiceProvider();
    }
}
