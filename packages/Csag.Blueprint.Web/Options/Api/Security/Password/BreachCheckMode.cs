namespace Csag.Blueprint.Web.Options.Api.Security.Password
{
    /// <summary>
    /// Where the breached-password check looks up a password's SHA-1 range.
    /// </summary>
    public enum BreachCheckMode
    {
        /// <summary>
        /// No breached-password check runs.
        /// </summary>
        Disabled = 0,

        /// <summary>
        /// The check queries the Pwned Passwords range API with the first five characters of the password's
        /// SHA-1 hash (k-anonymity), so neither the password nor its full hash leaves the process.
        /// </summary>
        Online = 1,

        /// <summary>
        /// The check reads the range from a local directory that holds one file per five-character hash
        /// prefix, for deployments without outbound internet access.
        /// </summary>
        Offline = 2,
    }
}
