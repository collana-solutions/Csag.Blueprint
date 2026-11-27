namespace Csag.Blueprint.Web.Options.Api.Security.Password
{
    /// <summary>
    /// Settings of the breached-password check that <c>AddBlueprintPasswordScreening</c> adds to Identity.
    /// </summary>
    public sealed class BreachCheckSettings
    {
        /// <summary>
        /// The Pwned Passwords range API, which answers <c>GET {base}{prefix}</c> with every hash suffix of the range.
        /// </summary>
        private const string DefaultRangeApiBaseUrl = "https://api.pwnedpasswords.com/range/";

        /// <summary>
        /// Gets or sets where the check looks up a password. <see cref="BreachCheckMode.Disabled"/> by default.
        /// </summary>
        public BreachCheckMode Mode { get; set; } = BreachCheckMode.Disabled;

        /// <summary>
        /// Gets or sets a value indicating whether a password is accepted when the lookup cannot complete
        /// (timeout, network or HTTP error, missing range file). Defaults to <see langword="true"/>, so an outage
        /// of the range source never blocks sign-up or a password change.
        /// </summary>
        public bool FailOpen { get; set; } = true;

        /// <summary>
        /// Gets or sets how long one online lookup may take before it counts as unavailable.
        /// </summary>
        public int TimeoutMilliseconds { get; set; } = 2000;

        /// <summary>
        /// Gets or sets the base URL of the range API for <see cref="BreachCheckMode.Online"/>. The five-character
        /// prefix is appended to it.
        /// </summary>
        public string RangeApiBaseUrl { get; set; } = DefaultRangeApiBaseUrl;

        /// <summary>
        /// Gets or sets the directory for <see cref="BreachCheckMode.Offline"/>. It holds one file per range, named
        /// after the uppercase five-character prefix (<c>21BD1.txt</c>). Each line is <c>SUFFIX:COUNT</c>, the
        /// format the range API returns.
        /// </summary>
        public string? OfflineRangeDirectory { get; set; }
    }
}
