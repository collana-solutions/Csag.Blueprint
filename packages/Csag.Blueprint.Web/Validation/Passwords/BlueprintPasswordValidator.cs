namespace Csag.Blueprint.Web.Validation.Passwords;

using Csag.Blueprint.Web.Options.Api.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

/// <summary>
/// Identity password validator that screens every password <see cref="UserManager{TUser}"/> sets (create,
/// add, change and reset) against the common-password list, the configured context-specific words, the
/// account's own email address and user name, and the breached-password check. It runs after Identity's own
/// length and composition rules. Register it with <c>AddBlueprintPasswordScreening()</c> on the Identity
/// builder.
/// </summary>
/// <typeparam name="TUser">The application's user type.</typeparam>
public sealed class BlueprintPasswordValidator<TUser> : IPasswordValidator<TUser>
    where TUser : class
{
    // A part of a name shorter than this, such as "max" or "tim", occurs in too many ordinary words to refuse
    // every password containing it. The whole email local part and user name still use the matcher's minimum.
    private const int MinimumAccountNamePartLength = 4;

    private static readonly char[] AccountNameSeparators = ['.', '_', '-', '+'];

    private readonly IOptions<SecuritySettings> securitySettings;
    private readonly IBreachedPasswordChecker breachedPasswordChecker;
    private readonly IStringLocalizer? localizer;

    public BlueprintPasswordValidator(
        IOptions<SecuritySettings> securitySettings,
        IBreachedPasswordChecker breachedPasswordChecker,
        IStringLocalizer? localizer = null)
    {
        this.securitySettings = securitySettings;
        this.breachedPasswordChecker = breachedPasswordChecker;
        this.localizer = localizer;
    }

    /// <inheritdoc />
    public Task<IdentityResult> ValidateAsync(UserManager<TUser> manager, TUser user, string? password)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        return string.IsNullOrEmpty(password)
            ? Task.FromResult(IdentityResult.Success)
            : this.ScreenAsync(manager, user, password);
    }

    /// <summary>
    /// Returns the email local part and the user name, each whole and split at common separators, so
    /// <c>anna.meier@example.com</c> keeps <c>Meier2026!</c> out as well as <c>AnnaMeier!</c>. Parts
    /// shorter than <see cref="MinimumAccountNamePartLength"/> are left out.
    /// </summary>
    private static async Task<List<string>> GetAccountNamesAsync(UserManager<TUser> manager, TUser user)
    {
        // GetEmailAsync throws for a store without email support.
        var email = manager.SupportsUserEmail ? await manager.GetEmailAsync(user) : null;

        var names = new List<string>();
        foreach (var value in new[] { email, await manager.GetUserNameAsync(user) })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var at = value.IndexOf('@', StringComparison.Ordinal);
            var localPart = at >= 0 ? value[..at] : value;
            names.Add(localPart);
            names.AddRange(localPart
                .Split(AccountNameSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Where(part => ContextSpecificWordMatcher.Normalize(part).Length >= MinimumAccountNamePartLength));
        }

        return names;
    }

    private async Task<IdentityResult> ScreenAsync(UserManager<TUser> manager, TUser user, string password)
    {
        var passwordSettings = this.securitySettings.Value.PasswordSettings;
        var errors = new List<IdentityError>();

        if (CommonPasswordList.Contains(password))
        {
            errors.Add(new IdentityError { Code = PasswordScreeningErrorCodes.TooCommon, Description = PasswordScreeningMessages.TooCommon(this.localizer) });
        }

        var contextWord = ContextSpecificWordMatcher.FindWord(password, passwordSettings.ContextSpecificWords);
        if (contextWord is not null)
        {
            errors.Add(new IdentityError { Code = PasswordScreeningErrorCodes.ContainsContextWord, Description = PasswordScreeningMessages.ContainsContextWord(this.localizer, contextWord) });
        }

        var accountNames = await GetAccountNamesAsync(manager, user);
        if (ContextSpecificWordMatcher.FindWord(password, accountNames) is not null)
        {
            errors.Add(new IdentityError { Code = PasswordScreeningErrorCodes.ContainsAccountName, Description = PasswordScreeningMessages.ContainsAccountName(this.localizer) });
        }

        // A password already refused locally is not sent to the breach lookup.
        if (errors.Count > 0)
        {
            return IdentityResult.Failed([.. errors]);
        }

        // IPasswordValidator has no cancellation token; the configured timeout bounds the lookup.
        var breach = await this.breachedPasswordChecker.CheckAsync(password, CancellationToken.None);
        return breach switch
        {
            BreachCheckResult.Breached => IdentityResult.Failed(new IdentityError { Code = PasswordScreeningErrorCodes.Breached, Description = PasswordScreeningMessages.Breached(this.localizer) }),
            BreachCheckResult.Unavailable when !passwordSettings.BreachCheck.FailOpen =>
                IdentityResult.Failed(new IdentityError { Code = PasswordScreeningErrorCodes.BreachCheckUnavailable, Description = PasswordScreeningMessages.BreachCheckUnavailable(this.localizer) }),
            _ => IdentityResult.Success,
        };
    }
}
