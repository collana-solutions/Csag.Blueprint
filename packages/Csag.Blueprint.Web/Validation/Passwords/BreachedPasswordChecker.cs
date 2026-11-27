namespace Csag.Blueprint.Web.Validation.Passwords;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Csag.Blueprint.Web.Options.Api.Security;
using Csag.Blueprint.Web.Options.Api.Security.Password;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Looks a password up in Pwned Passwords ranges with k-anonymity: only the first five characters of the
/// password's SHA-1 hash leave the process, and the remaining 35 are compared against the returned range.
/// <see cref="BreachCheckMode.Online"/> reads the range from the range API with response padding, so the
/// response size does not reveal the range either. <see cref="BreachCheckMode.Offline"/> reads it from a
/// local directory of range files.
/// </summary>
public sealed class BreachedPasswordChecker : IBreachedPasswordChecker
{
    private const int PrefixLength = 5;

    private readonly IHttpClientFactory httpClientFactory;
    private readonly IOptionsMonitor<SecuritySettings> securitySettings;
    private readonly ILogger<BreachedPasswordChecker> logger;

    public BreachedPasswordChecker(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<SecuritySettings> securitySettings,
        ILogger<BreachedPasswordChecker> logger)
    {
        this.httpClientFactory = httpClientFactory;
        this.securitySettings = securitySettings;
        this.logger = logger;
    }

    /// <summary>
    /// Gets the name of the <see cref="HttpClient"/> the online lookup uses.
    /// </summary>
    public static string HttpClientName => "Csag.Blueprint.BreachedPasswords";

    /// <inheritdoc />
    public Task<BreachCheckResult> CheckAsync(string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);

        var settings = this.securitySettings.CurrentValue.PasswordSettings.BreachCheck;
        return settings.Mode == BreachCheckMode.Disabled || password.Length == 0
            ? Task.FromResult(BreachCheckResult.NotChecked)
            : this.LookUpAsync(settings, password, cancellationToken);
    }

    /// <summary>
    /// Returns the uppercase hex SHA-1 hash of the UTF-8 password, the form the range API is keyed on.
    /// </summary>
    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "The Pwned Passwords range API is keyed on SHA-1. The hash only locates the range; it protects nothing.")]
    private static string Sha1Hex(string password) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));

    /// <summary>
    /// Determines whether a range lists the suffix with a count above zero. Padding entries have a count of zero.
    /// </summary>
    private static bool RangeContains(string range, ReadOnlySpan<char> suffix)
    {
        foreach (var line in range.AsSpan().EnumerateLines())
        {
            var separator = line.IndexOf(':');
            if (separator < 0 || !line[..separator].Trim().Equals(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return int.TryParse(line[(separator + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count > 0;
        }

        return false;
    }

    private async Task<BreachCheckResult> LookUpAsync(BreachCheckSettings settings, string password, CancellationToken cancellationToken)
    {
        var hash = Sha1Hex(password);
        var prefix = hash[..PrefixLength];
        var range = settings.Mode == BreachCheckMode.Online
            ? await this.ReadOnlineRangeAsync(settings, prefix, cancellationToken)
            : await this.ReadOfflineRangeAsync(settings, prefix, cancellationToken);

        if (range is null)
        {
            return BreachCheckResult.Unavailable;
        }

        return RangeContains(range, hash.AsSpan(PrefixLength)) ? BreachCheckResult.Breached : BreachCheckResult.NotBreached;
    }

    private async Task<string?> ReadOnlineRangeAsync(BreachCheckSettings settings, string prefix, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.TimeoutMilliseconds);

        try
        {
            var client = this.httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(settings.RangeApiBaseUrl), prefix));
            request.Headers.Add("Add-Padding", "true");

            using var response = await client.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                this.logger.LogWarning("The breached-password range API answered {StatusCode}; the password was not checked", (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            this.logger.LogWarning(exception, "The breached-password range API did not answer within {TimeoutMilliseconds} ms; the password was not checked", settings.TimeoutMilliseconds);
            return null;
        }
        catch (HttpRequestException exception)
        {
            this.logger.LogWarning(exception, "The breached-password range API could not be reached; the password was not checked");
            return null;
        }
    }

    private async Task<string?> ReadOfflineRangeAsync(BreachCheckSettings settings, string prefix, CancellationToken cancellationToken)
    {
        var path = Path.Combine(settings.OfflineRangeDirectory ?? string.Empty, prefix + ".txt");

        try
        {
            return await File.ReadAllTextAsync(path, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            this.logger.LogWarning(exception, "The breached-password range file for prefix {Prefix} could not be read; the password was not checked", prefix);
            return null;
        }
    }
}
