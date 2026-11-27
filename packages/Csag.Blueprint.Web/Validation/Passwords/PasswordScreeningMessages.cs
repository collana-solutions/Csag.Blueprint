namespace Csag.Blueprint.Web.Validation.Passwords;

using Microsoft.Extensions.Localization;

/// <summary>
/// Translation keys and English defaults of the password screening messages. An application translates them
/// by defining the keys in its translation defaults; until it does, the English default is shown instead of
/// the key.
/// </summary>
internal static class PasswordScreeningMessages
{
    public const string TooCommonKey = "Validation.PasswordTooCommon";
    public const string ContainsContextWordKey = "Validation.PasswordContainsContextWord";
    public const string ContainsAccountNameKey = "Validation.PasswordContainsAccountName";
    public const string BreachedKey = "Validation.PasswordBreached";
    public const string BreachCheckUnavailableKey = "Validation.PasswordBreachCheckUnavailable";

    public const string TooCommonDefault = "This password is too common. Choose one that is harder to guess";
    public const string ContainsContextWordDefault = "Password must not contain \"{Word}\"";
    public const string ContainsAccountNameDefault = "Password must not contain your email address or user name";
    public const string BreachedDefault = "This password has appeared in a data breach. Choose a different one";
    public const string BreachCheckUnavailableDefault = "The password could not be checked right now. Try again in a few minutes";

    public static string TooCommon(IStringLocalizer? localizer) =>
        Localize(localizer, TooCommonKey, TooCommonDefault);

    public static string ContainsContextWord(IStringLocalizer? localizer, string word)
    {
        if (localizer is not null)
        {
            var localized = localizer[ContainsContextWordKey, new { Word = word }];
            if (!localized.ResourceNotFound)
            {
                return localized.Value;
            }
        }

        return ContainsContextWordDefault.Replace("{Word}", word, StringComparison.Ordinal);
    }

    public static string ContainsAccountName(IStringLocalizer? localizer) =>
        Localize(localizer, ContainsAccountNameKey, ContainsAccountNameDefault);

    public static string Breached(IStringLocalizer? localizer) =>
        Localize(localizer, BreachedKey, BreachedDefault);

    public static string BreachCheckUnavailable(IStringLocalizer? localizer) =>
        Localize(localizer, BreachCheckUnavailableKey, BreachCheckUnavailableDefault);

    private static string Localize(IStringLocalizer? localizer, string key, string fallback)
    {
        if (localizer is null)
        {
            return fallback;
        }

        var localized = localizer[key];
        return localized.ResourceNotFound ? fallback : localized.Value;
    }
}
