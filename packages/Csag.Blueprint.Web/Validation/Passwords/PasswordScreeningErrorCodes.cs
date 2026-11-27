namespace Csag.Blueprint.Web.Validation.Passwords;

/// <summary>
/// The <see cref="Microsoft.AspNetCore.Identity.IdentityError.Code"/> values that
/// <see cref="BlueprintPasswordValidator{TUser}"/> reports.
/// </summary>
public static class PasswordScreeningErrorCodes
{
    /// <summary>Gets the error code for a password on the common-password list.</summary>
    public static string TooCommon => "PasswordTooCommon";

    /// <summary>Gets the error code for a password that contains a configured context-specific word.</summary>
    public static string ContainsContextWord => "PasswordContainsContextWord";

    /// <summary>Gets the error code for a password that contains the account's email address or user name.</summary>
    public static string ContainsAccountName => "PasswordContainsAccountName";

    /// <summary>Gets the error code for a password found in breach data.</summary>
    public static string Breached => "PasswordBreached";

    /// <summary>Gets the error code for a password the breach check could not look up while it fails closed.</summary>
    public static string BreachCheckUnavailable => "PasswordBreachCheckUnavailable";
}
