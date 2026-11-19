namespace Csag.Blueprint.Realtime.AzureSignalR.UnitTests;

using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Csag.Blueprint.Application.Abstractions.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Tests for whom <see cref="AzureSignalRRealtimeService"/> addresses when it publishes, run against a
/// stand-in for the service's data-plane REST API on a loopback port.
/// </summary>
/// <remarks>
/// The transient transport is nothing but HTTP requests to that API, with the recipient in the path,
/// so a local listener sees exactly what Azure would receive. That makes the number of sends and the
/// identities they address observable without an emulator or any network beyond loopback.
/// </remarks>
public sealed class AzureSignalRRealtimeServiceTests
{
    private static readonly Guid TenantId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FirstUserId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SecondUserId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ThirdUserId = new("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task NotifyUserAsync_SendsOnce_ToTheTenantQualifiedUser()
    {
        // Arrange
        await using var api = await FakeRestApi.StartAsync();
        var logger = new RecordingLogger();
        await using var service = CreateService(api, logger);

        // Act
        await service.NotifyUserAsync(TenantId, FirstUserId, SampleEvent(), TestContext.Current.CancellationToken);

        // Assert
        api.SentToUsers.ShouldBe([RealtimeGroupNames.ServiceUserId(TenantId, FirstUserId)]);
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task NotifyUsersAsync_SendsOncePerUser_ToEachTenantQualifiedUser()
    {
        // Arrange
        await using var api = await FakeRestApi.StartAsync();
        var logger = new RecordingLogger();
        await using var service = CreateService(api, logger);

        // Act
        await service.NotifyUsersAsync(
            TenantId,
            [FirstUserId, SecondUserId, ThirdUserId],
            SampleEvent(),
            TestContext.Current.CancellationToken);

        // Assert
        // One request per user is what the XML documentation tells callers a multi-user send costs in
        // round trips, so a change in the SDK's fan-out shows up here first.
        api.SentToUsers.ShouldBe(
            [
                RealtimeGroupNames.ServiceUserId(TenantId, FirstUserId),
                RealtimeGroupNames.ServiceUserId(TenantId, SecondUserId),
                RealtimeGroupNames.ServiceUserId(TenantId, ThirdUserId),
            ],
            ignoreOrder: true);
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task NotifyUsersAsync_RepeatedUser_IsSentToOnce()
    {
        // Arrange
        await using var api = await FakeRestApi.StartAsync();
        await using var service = CreateService(api, new RecordingLogger());

        // Act
        await service.NotifyUsersAsync(
            TenantId,
            [FirstUserId, SecondUserId, FirstUserId],
            SampleEvent(),
            TestContext.Current.CancellationToken);

        // Assert
        // A second request for the same user would reach every one of their connections again: a
        // duplicate event on screen and a second message against the quota for each open tab.
        api.SentToUsers.ShouldBe(
            [
                RealtimeGroupNames.ServiceUserId(TenantId, FirstUserId),
                RealtimeGroupNames.ServiceUserId(TenantId, SecondUserId),
            ],
            ignoreOrder: true);
    }

    [Fact]
    public async Task NotifyUsersAsync_NoUsers_SendsNothing()
    {
        // Arrange
        await using var api = await FakeRestApi.StartAsync();
        var logger = new RecordingLogger();
        await using var service = CreateService(api, logger);

        // Act
        await service.NotifyUsersAsync(TenantId, [], SampleEvent(), TestContext.Current.CancellationToken);

        // Assert
        // A recipient list computed from data can legitimately come out empty, so this is a normal
        // call rather than a caller error.
        api.SentToUsers.ShouldBeEmpty();
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task NotifyUsersAsync_WhenOneSendFails_LogsInsteadOfThrowing_AndStillSendsToTheOthers()
    {
        // Arrange
        var failingUser = RealtimeGroupNames.ServiceUserId(TenantId, FirstUserId);
        await using var api = await FakeRestApi.StartAsync(failingUser);
        var logger = new RecordingLogger();
        await using var service = CreateService(api, logger);

        // Act
        await service.NotifyUsersAsync(
            TenantId,
            [FirstUserId, SecondUserId],
            SampleEvent(),
            TestContext.Current.CancellationToken);

        // Assert
        api.SentToUsers.ShouldBe(
            [failingUser, RealtimeGroupNames.ServiceUserId(TenantId, SecondUserId)],
            ignoreOrder: true);

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldNotBeNull();
    }

    [Fact]
    public async Task AfterDisposal_PublishingSendsNothing_AndLogsInsteadOfThrowing()
    {
        // Arrange
        await using var api = await FakeRestApi.StartAsync();
        var logger = new RecordingLogger();

        var service = CreateService(api, logger);
        await service.DisposeAsync();

        // Act
        await service.NotifyUserAsync(TenantId, FirstUserId, SampleEvent(), TestContext.Current.CancellationToken);

        // Assert
        // A hub context created after disposal would never be disposed.
        api.SentToUsers.ShouldBeEmpty();
        logger.Entries.ShouldHaveSingleItem().Exception.ShouldBeOfType<ObjectDisposedException>();
    }

    [Fact]
    public async Task NotifyUserAsync_WithCredential_SendsTheCredentialsTokenAsBearer()
    {
        // Arrange
        await using var api = await FakeRestApi.StartAsync();
        var credential = new RecordingTokenCredential();
        var logger = new RecordingLogger();
        await using var service = CreateService(api, logger, credential);

        // Act
        await service.NotifyUserAsync(TenantId, FirstUserId, SampleEvent(), TestContext.Current.CancellationToken);

        // Assert
        // The request is authorized by the credential's token as is, not by a token the SDK signs
        // itself, so the identity's role on the service decides what it may do.
        var send = api.Requests.ShouldHaveSingleItem();
        send.Path.ShouldEndWith("/:send");
        send.Authorization.ShouldBe($"Bearer {RecordingTokenCredential.Token}");
        credential.RequestedScopes.ShouldAllBe(scope => scope == "https://signalr.azure.com/.default");
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task IssueAsync_WithCredential_SignsTheTicketWithTheKeyTheServiceIssued()
    {
        // Arrange
        await using var api = await FakeRestApi.StartAsync();
        await using var service = CreateService(api, new RecordingLogger(), new RecordingTokenCredential());

        // Act
        var ticket = await service.IssueAsync(TenantId, FirstUserId, TestContext.Current.CancellationToken);

        // Assert
        // With Microsoft Entra ID there is no access key to sign client tokens with, so the SDK asks
        // the service for a temporary one. That request and the group membership both carry the
        // credential's token, and the ticket is signed with the key the service handed out.
        api.Requests.Select(request => (request.Method, request.Authorization)).ShouldBe(
            [
                ("PUT", $"Bearer {RecordingTokenCredential.Token}"),
                ("GET", $"Bearer {RecordingTokenCredential.Token}"),
            ],
            ignoreOrder: true);
        api.Requests.ShouldContain(request => request.Path == FakeRestApi.AccessKeyPath);

        ticket.ShouldNotBeNull();
        ticket.Url.Authority.ShouldBe(api.Endpoint.Authority);
        ShouldBeSignedWith(ticket.AccessToken, FakeRestApi.IssuedKeyId, FakeRestApi.IssuedAccessKey);
    }

    private static AzureSignalRRealtimeService CreateService(FakeRestApi api, RecordingLogger logger, TokenCredential? credential = null)
    {
        // The validator only admits https for an endpoint, while the stand-in listens on plain http,
        // so these options are handed over unvalidated.
        var options = Options.Create(new BlueprintAzureSignalROptions
        {
            ConnectionString = credential is null ? api.ConnectionString : null,
            Endpoint = credential is null ? null : api.Endpoint,
            Credential = credential,
            HubName = FakeRestApi.HubName,
            TicketTokenLifetimeMinutes = 60,
            GroupMembershipTtlHours = 12,
        });

        return new AzureSignalRRealtimeService(options, TimeProvider.System, logger);
    }

    private static RealtimeEvent SampleEvent()
    {
        return RealtimeEvent.For("test.changed", Guid.NewGuid(), TimeProvider.System);
    }

    /// <summary>
    /// Asserts that a JSON Web Token names the key and carries a valid HMAC-SHA256 signature made with it.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <param name="keyId">The expected key identifier in the header.</param>
    /// <param name="key">The key the signature must verify with.</param>
    private static void ShouldBeSignedWith(string token, string keyId, string key)
    {
        var parts = token.Split('.');
        parts.Length.ShouldBe(3);

        using var header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0]));
        header.RootElement.GetProperty("kid").GetString().ShouldBe(keyId);
        header.RootElement.GetProperty("alg").GetString().ShouldBe("HS256");

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"));
        Base64Url.DecodeFromChars(parts[2]).ShouldBe(expected);
    }

    /// <summary>
    /// A request the stand-in received.
    /// </summary>
    /// <param name="Method">The HTTP method.</param>
    /// <param name="Path">The request path.</param>
    /// <param name="Authorization">The <c>Authorization</c> header, or <c>null</c> without one.</param>
    private sealed record RecordedRequest(string Method, string Path, string? Authorization);

    /// <summary>
    /// Hands out one fixed token and records the scopes it is asked for.
    /// </summary>
    private sealed class RecordingTokenCredential : TokenCredential
    {
        public const string Token = "entra-token";

        private readonly ConcurrentQueue<string> requestedScopes = new();

        public IReadOnlyList<string> RequestedScopes => [.. this.requestedScopes];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            foreach (var scope in requestContext.Scopes)
            {
                this.requestedScopes.Enqueue(scope);
            }

            return new AccessToken(Token, DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(this.GetToken(requestContext, cancellationToken));
        }
    }

    /// <summary>
    /// Answers the data-plane REST API on a loopback port and records every request, and the user each
    /// send-to-user request addresses.
    /// </summary>
    private sealed class FakeRestApi : IAsyncDisposable
    {
        public const string HubName = "app";

        /// <summary>
        /// Where the SDK fetches the temporary key it signs client tokens with under Microsoft Entra ID.
        /// </summary>
        public const string AccessKeyPath = "/api/v1/auth/accessKey";

        /// <summary>
        /// The identifier of the temporary key this stand-in hands out.
        /// </summary>
        public const string IssuedKeyId = "temporary-key";

        /// <summary>
        /// The temporary key this stand-in hands out.
        /// </summary>
        public const string IssuedAccessKey = "ZYXWVUTSRQPONMLKJIHGFEDCBA9876543210ZYXWVUTS";

        private const string SendToUserPrefix = "/api/hubs/" + HubName + "/users/";

        private const string SendToUserSuffix = "/:send";

        /// <summary>
        /// Any 44-character key will do: the SDK signs each request with it, and nothing here checks
        /// the signature.
        /// </summary>
        private const string AccessKey = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ABCDEFGH";

        private readonly WebApplication app;
        private readonly HashSet<string> failingUsers;
        private readonly ConcurrentQueue<string> sentToUsers = new();
        private readonly ConcurrentQueue<RecordedRequest> requests = new();

        private FakeRestApi(WebApplication app, HashSet<string> failingUsers)
        {
            this.app = app;
            this.failingUsers = failingUsers;
        }

        /// <summary>
        /// Gets the service user identifiers addressed so far, in arrival order.
        /// </summary>
        public IReadOnlyList<string> SentToUsers => [.. this.sentToUsers];

        /// <summary>
        /// Gets every request received so far, in arrival order.
        /// </summary>
        public IReadOnlyList<RecordedRequest> Requests => [.. this.requests];

        /// <summary>
        /// Gets a connection string pointing the SDK at this listener.
        /// </summary>
        public string ConnectionString =>
            $"Endpoint=http://127.0.0.1;Port={this.Endpoint.Port};AccessKey={AccessKey};Version=1.0;";

        /// <summary>
        /// Gets the endpoint of this listener, for authenticating with a credential.
        /// </summary>
        public Uri Endpoint => new($"http://127.0.0.1:{new Uri(this.app.Urls.First()).Port}");

        /// <summary>
        /// Starts a listener on a free loopback port.
        /// </summary>
        /// <param name="failingUsers">Service user identifiers whose sends are answered with a 500.</param>
        /// <returns>The running listener.</returns>
        public static async Task<FakeRestApi> StartAsync(params string[] failingUsers)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));

            var app = builder.Build();
            var api = new FakeRestApi(app, [.. failingUsers]);
            app.Run(api.HandleAsync);

            await app.StartAsync(TestContext.Current.CancellationToken);

            return api;
        }

        public async ValueTask DisposeAsync()
        {
            await this.app.StopAsync(CancellationToken.None);
            await this.app.DisposeAsync();
        }

        private async Task HandleAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;
            this.requests.Enqueue(new RecordedRequest(context.Request.Method, path, context.Request.Headers.Authorization.FirstOrDefault()));

            if (HttpMethods.IsGet(context.Request.Method) && path == AccessKeyPath)
            {
                await context.Response.WriteAsJsonAsync(
                    new { AccessKey = IssuedAccessKey, KeyId = IssuedKeyId },
                    new JsonSerializerOptions(),
                    context.RequestAborted);
                return;
            }

            // Adding a user to a group, which a ticket grants before it is handed out.
            if (HttpMethods.IsPut(context.Request.Method) && path.StartsWith(SendToUserPrefix, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                return;
            }

            if (!HttpMethods.IsPost(context.Request.Method)
                || !path.StartsWith(SendToUserPrefix, StringComparison.Ordinal)
                || !path.EndsWith(SendToUserSuffix, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var user = Uri.UnescapeDataString(path[SendToUserPrefix.Length..^SendToUserSuffix.Length]);
            this.sentToUsers.Enqueue(user);

            // 500 rather than 503: the SDK retries message sends only above 500, so this fails at once
            // instead of after three delayed retries.
            context.Response.StatusCode = this.failingUsers.Contains(user)
                ? StatusCodes.Status500InternalServerError
                : StatusCodes.Status202Accepted;
        }
    }

    /// <summary>
    /// Records the level and exception of every entry, so a test can tell a swallowed failure from
    /// a clean send.
    /// </summary>
    private sealed class RecordingLogger : ILogger<AzureSignalRRealtimeService>
    {
        private readonly ConcurrentQueue<(LogLevel Level, Exception? Exception)> entries = new();

        public IReadOnlyList<(LogLevel Level, Exception? Exception)> Entries => [.. this.entries];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            this.entries.Enqueue((logLevel, exception));
        }
    }
}
