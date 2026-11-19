namespace Csag.Blueprint.Realtime.AzureSignalR;

using Azure.Core;

/// <summary>
/// Configuration for the Azure SignalR Service provider of the real-time channel.
/// </summary>
/// <remarks>
/// The provider authenticates to the service in one of two ways, and exactly one must be configured:
/// an access key through <see cref="ConnectionString"/>, or Microsoft Entra ID through
/// <see cref="Endpoint"/> together with <see cref="Credential"/>.
/// </remarks>
public sealed class BlueprintAzureSignalROptions
{
    /// <summary>
    /// Gets or sets the Azure SignalR Service connection string. It must carry an <c>Endpoint</c> and an
    /// <c>AccessKey</c>. Leave it unset when <see cref="Endpoint"/> and <see cref="Credential"/> are used.
    /// </summary>
    /// <remarks>
    /// Against the local emulator this needs a <c>ClientEndpoint</c> in addition to <c>Endpoint</c> when
    /// the API and the browser reach the emulator under different hostnames, for example the compose
    /// network and localhost.
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the endpoint of the Azure SignalR Service, for example
    /// <c>https://&lt;name&gt;.service.signalr.net</c>, when the provider authenticates with Microsoft Entra
    /// ID. Requires <see cref="Credential"/>, and excludes <see cref="ConnectionString"/>.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the Microsoft Entra ID credential the provider authenticates to <see cref="Endpoint"/>
    /// with. Its identity needs the roles SignalR REST API Owner and SignalR App Server on the service.
    /// </summary>
    /// <remarks>
    /// Configuration binding does not set this property, so a host sets it in code, typically through
    /// <c>services.AddOptions&lt;BlueprintAzureSignalROptions&gt;().Configure&lt;TDependency&gt;(...)</c>
    /// when the credential comes from the container.
    /// </remarks>
    public TokenCredential? Credential { get; set; }

    /// <summary>
    /// Gets or sets the hub name clients connect to.
    /// </summary>
    public string HubName { get; set; } = "app";

    /// <summary>
    /// Gets or sets how long an issued connection ticket stays valid, in minutes.
    /// </summary>
    public int TicketTokenLifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// Gets or sets how long a granted group membership survives, in hours.
    /// </summary>
    /// <remarks>
    /// Must be greater than zero and outlast the ticket. The service reads a zero time-to-live as "only
    /// the connections this user has open right now", and at ticket-issuing time that is none, so the
    /// channel would come up and deliver nothing. The service holds no membership longer than a year.
    /// </remarks>
    public int GroupMembershipTtlHours { get; set; } = 12;
}
