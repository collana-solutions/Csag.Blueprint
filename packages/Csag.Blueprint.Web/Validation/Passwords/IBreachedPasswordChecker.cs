namespace Csag.Blueprint.Web.Validation.Passwords;

/// <summary>
/// Looks a password up in breach data, as configured under
/// <c>Blueprint:Security:PasswordSettings:BreachCheck</c>.
/// </summary>
public interface IBreachedPasswordChecker
{
    /// <summary>
    /// Checks whether a password appears in breach data.
    /// </summary>
    /// <param name="password">The password to check.</param>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns>The outcome of the lookup.</returns>
    Task<BreachCheckResult> CheckAsync(string password, CancellationToken cancellationToken = default);
}
