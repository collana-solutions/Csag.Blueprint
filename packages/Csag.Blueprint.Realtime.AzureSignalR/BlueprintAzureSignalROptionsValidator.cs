namespace Csag.Blueprint.Realtime.AzureSignalR;

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Azure.Core;
using Microsoft.Azure.SignalR;
using Microsoft.Extensions.Options;

/// <summary>
/// Fails startup on <see cref="BlueprintAzureSignalROptions"/> the provider cannot run with.
/// </summary>
/// <remarks>
/// The hub context is built lazily, on the first ticket or publish, so anything left for the SDK to
/// reject would otherwise surface in production traffic instead of at startup.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by dependency injection.")]
internal sealed partial class BlueprintAzureSignalROptionsValidator : IValidateOptions<BlueprintAzureSignalROptions>
{
    /// <summary>
    /// The longest group membership the service holds, one year in hours.
    /// </summary>
    internal const int MaxGroupMembershipTtlHours = 8760;

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, BlueprintAzureSignalROptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        var authenticationProblem = DescribeAuthenticationProblem(options);
        if (authenticationProblem is not null)
        {
            failures.Add(authenticationProblem);
        }

        if (string.IsNullOrEmpty(options.HubName) || !HubNamePattern().IsMatch(options.HubName))
        {
            failures.Add("BlueprintAzureSignalROptions.HubName must be 1-64 characters of letters, digits or underscores.");
        }

        if (options.TicketTokenLifetimeMinutes <= 0)
        {
            failures.Add("BlueprintAzureSignalROptions.TicketTokenLifetimeMinutes must be greater than 0.");
        }

        // IssueAsync grants the membership first and mints the token afterwards, so the membership clock
        // is always the older of the two. With equal durations the token would still open a connection
        // for a while after the membership ended, and that connection would receive nothing.
        if (options.TicketTokenLifetimeMinutes > 0
            && options.TicketTokenLifetimeMinutes >= (long)options.GroupMembershipTtlHours * 60)
        {
            failures.Add(
                "BlueprintAzureSignalROptions.TicketTokenLifetimeMinutes must be shorter than GroupMembershipTtlHours, "
                + "because the membership is granted first and has to outlast every ticket issued with it.");
        }

        if (options.GroupMembershipTtlHours <= 0 || options.GroupMembershipTtlHours > MaxGroupMembershipTtlHours)
        {
            failures.Add(
                $"BlueprintAzureSignalROptions.GroupMembershipTtlHours must be between 1 and {MaxGroupMembershipTtlHours} (one year). "
                + "The service reads 0 as 'only the connections open right now', which at ticket time is none.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Checks that exactly one way of authenticating to the service is configured, and that it is usable.
    /// </summary>
    /// <param name="options">The configured options.</param>
    /// <returns>The failure message, or <c>null</c> when the configuration is usable.</returns>
    private static string? DescribeAuthenticationProblem(BlueprintAzureSignalROptions options)
    {
        var usesConnectionString = !string.IsNullOrWhiteSpace(options.ConnectionString);
        var usesMicrosoftEntraId = options.Endpoint is not null || options.Credential is not null;

        if (usesConnectionString && usesMicrosoftEntraId)
        {
            return "BlueprintAzureSignalROptions must set either ConnectionString, or Endpoint with Credential, not both.";
        }

        if (usesMicrosoftEntraId)
        {
            return DescribeMicrosoftEntraIdProblem(options.Endpoint, options.Credential);
        }

        if (!usesConnectionString)
        {
            return "BlueprintAzureSignalROptions must set either ConnectionString, or Endpoint with Credential.";
        }

        return DescribeConnectionStringProblem(options.ConnectionString!);
    }

    /// <summary>
    /// Checks that the endpoint and the credential are both present and that the endpoint is usable.
    /// </summary>
    /// <param name="endpoint">The configured endpoint.</param>
    /// <param name="credential">The configured credential.</param>
    /// <returns>The failure message, or <c>null</c> when the pair is usable.</returns>
    private static string? DescribeMicrosoftEntraIdProblem(Uri? endpoint, TokenCredential? credential)
    {
        if (endpoint is null)
        {
            return "BlueprintAzureSignalROptions.Endpoint must be set when Credential is set.";
        }

        if (credential is null)
        {
            return "BlueprintAzureSignalROptions.Credential must be set when Endpoint is set. "
                + "Configuration binding does not supply it, so the host sets it in code.";
        }

        // The SDK would also accept http, but every request carries a bearer token of the credential's
        // identity, which must not travel in clear text. A relative URI would make the SDK throw on its
        // first use instead of at startup.
        return endpoint.IsAbsoluteUri && endpoint.Scheme == Uri.UriSchemeHttps
            ? null
            : "BlueprintAzureSignalROptions.Endpoint must be an absolute https URI, for example https://<name>.service.signalr.net.";
    }

    /// <summary>
    /// Checks a connection string the way the SDK will when the hub context is first built.
    /// </summary>
    /// <param name="connectionString">The configured connection string.</param>
    /// <returns>The failure message, or <c>null</c> when the value is usable.</returns>
    private static string? DescribeConnectionStringProblem(string connectionString)
    {
        // Without an access key the SDK falls back to Microsoft Entra authentication with a credential
        // of its own choosing, which does not exist where this runs, and every REST call fails
        // unauthorized. Microsoft Entra ID goes through Endpoint and Credential instead.
        if (!HasAccessKey(connectionString))
        {
            return "BlueprintAzureSignalROptions.ConnectionString must contain an Endpoint and an AccessKey. "
                + "For Microsoft Entra ID, set Endpoint and Credential instead.";
        }

        // ServiceEndpoint runs the SDK's own parser without any network call, which covers the endpoint,
        // port, version and client endpoint.
        try
        {
            _ = new ServiceEndpoint(connectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return $"BlueprintAzureSignalROptions.ConnectionString is not a valid Azure SignalR connection string: {exception.Message}";
        }

        return null;
    }

    /// <summary>
    /// Determines whether the connection string carries a non-empty <c>AccessKey</c> property.
    /// </summary>
    /// <param name="connectionString">The configured connection string.</param>
    /// <returns><c>true</c> when an access key is present.</returns>
    private static bool HasAccessKey(string connectionString)
    {
        // Split exactly as the SDK parser does, so a base64 key ending in '=' is read whole, and a
        // "ClientEndpoint=" is not mistaken for a key.
        return connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(property => property.Split('=', 2))
            .Any(property => property.Length == 2
                && property[0].Equals("AccessKey", StringComparison.OrdinalIgnoreCase)
                && property[1].Length > 0);
    }

    // \z rather than $, which in .NET also matches before a trailing newline.
    [GeneratedRegex(@"^[A-Za-z0-9_]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex HubNamePattern();
}
