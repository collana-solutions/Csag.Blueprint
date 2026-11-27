namespace Csag.Blueprint.Web.Validation.Passwords;

/// <summary>
/// The outcome of an <see cref="IBreachedPasswordChecker"/> lookup.
/// </summary>
public enum BreachCheckResult
{
    /// <summary>
    /// The check is disabled, so nothing was looked up.
    /// </summary>
    NotChecked = 0,

    /// <summary>
    /// The password's hash is not in its range.
    /// </summary>
    NotBreached = 1,

    /// <summary>
    /// The password's hash is in its range with a count above zero.
    /// </summary>
    Breached = 2,

    /// <summary>
    /// The range could not be read: timeout, network or HTTP error, or a missing or unreadable range file.
    /// </summary>
    Unavailable = 3,
}
