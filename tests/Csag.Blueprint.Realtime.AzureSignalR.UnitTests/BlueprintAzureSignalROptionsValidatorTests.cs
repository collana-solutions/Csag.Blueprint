namespace Csag.Blueprint.Realtime.AzureSignalR.UnitTests;

using Azure.Core;
using Csag.Blueprint.Application.Abstractions.Realtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

/// <summary>
/// Tests for the startup validation of <see cref="BlueprintAzureSignalROptions"/>, through the registration a
/// host calls.
/// </summary>
public sealed class BlueprintAzureSignalROptionsValidatorTests
{
    private const string ValidConnectionString = "Endpoint=https://example.service.signalr.net;AccessKey=abc;Version=1.0;";

    [Fact]
    public void CompleteConfiguration_IsValid()
    {
        Failures(_ => { }).ShouldBeEmpty();
    }

    [Fact]
    public void Defaults_NeedOnlyAConnectionString()
    {
        using var provider = BuildProvider(options => options.ConnectionString = ValidConnectionString);

        var options = provider.GetRequiredService<IOptions<BlueprintAzureSignalROptions>>().Value;

        options.HubName.ShouldBe("app");
        options.TicketTokenLifetimeMinutes.ShouldBe(60);
        options.GroupMembershipTtlHours.ShouldBe(12);
    }

    [Fact]
    public async Task Registration_ResolvesBothInterfacesToOneService()
    {
        // The service is IAsyncDisposable only, so the container has to be disposed asynchronously.
        await using var provider = BuildProvider(options => options.ConnectionString = ValidConnectionString);

        var notifier = provider.GetRequiredService<IRealtimeNotifier>();
        var issuer = provider.GetRequiredService<IRealtimeTicketIssuer>();

        notifier.ShouldBeOfType<AzureSignalRRealtimeService>();
        issuer.ShouldBeSameAs(notifier);
    }

    [Fact]
    public void WithoutConnectionString_IsInvalid()
    {
        Failures(options => options.ConnectionString = null).ShouldHaveSingleItem().ShouldContain("ConnectionString");
    }

    [Fact]
    public void WithUnresolvedSecretPlaceholder_IsInvalid()
    {
        // A host that reads the connection string from a secret store configures a placeholder. If loading
        // the secret fails, the placeholder survives, and this turns it into a startup failure instead of a
        // connection error on the first message.
        Failures(options => options.ConnectionString = "{GoogleSecret:AzureSignalRConnectionString}").ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("Endpoint=not a uri;AccessKey=abc;Version=1.0;")]
    [InlineData("Endpoint=ftp://example.service.signalr.net;AccessKey=abc;Version=1.0;")]
    [InlineData("Endpoint=https://example.service.signalr.net;Port=not-a-number;AccessKey=abc;Version=1.0;")]
    [InlineData("Endpoint=https://example.service.signalr.net;Port=99999;AccessKey=abc;Version=1.0;")]
    [InlineData("Endpoint=https://example.service.signalr.net;AccessKey=abc;Version=2.5;")]
    [InlineData("Endpoint=https://example.service.signalr.net;AccessKey=abc;Version=1.0;ClientEndpoint=not a uri;")]
    public void WithMalformedConnectionString_IsInvalid(string connectionString)
    {
        Failures(options => options.ConnectionString = connectionString).ShouldHaveSingleItem().ShouldContain("not a valid");
    }

    [Theory]
    [InlineData("AccessKey=abc;Version=1.0;")]
    [InlineData("Endpoint=https://example.service.signalr.net;Version=1.0;")]
    [InlineData("Endpoint=https://example.service.signalr.net;AccessKey=;Version=1.0;")]
    [InlineData("ClientEndpoint=https://example.service.signalr.net;AccessKey=abc;Version=1.0;")]
    public void WithIncompleteConnectionString_IsInvalid(string connectionString)
    {
        Failures(options => options.ConnectionString = connectionString).ShouldNotBeEmpty();
    }

    [Fact]
    public void WithEndpointAndCredential_IsValid()
    {
        Failures(UseMicrosoftEntraId).ShouldBeEmpty();
    }

    [Fact]
    public void WithConnectionStringAndEndpoint_IsInvalid()
    {
        // The transient transport refuses a second endpoint, so both together would fail on the first
        // ticket or publish instead of at startup.
        Failures(options =>
            {
                UseMicrosoftEntraId(options);
                options.ConnectionString = ValidConnectionString;
            })
            .ShouldHaveSingleItem().ShouldContain("not both");
    }

    [Fact]
    public void WithEndpointWithoutCredential_IsInvalid()
    {
        Failures(options =>
            {
                UseMicrosoftEntraId(options);
                options.Credential = null;
            })
            .ShouldHaveSingleItem().ShouldContain("Credential must be set");
    }

    [Fact]
    public void WithCredentialWithoutEndpoint_IsInvalid()
    {
        Failures(options =>
            {
                UseMicrosoftEntraId(options);
                options.Endpoint = null;
            })
            .ShouldHaveSingleItem().ShouldContain("Endpoint must be set");
    }

    [Theory]
    [InlineData("http://example.service.signalr.net")]
    [InlineData("ftp://example.service.signalr.net")]
    [InlineData("example.service.signalr.net")]
    public void WithEndpointThatIsNotAbsoluteHttps_IsInvalid(string endpoint)
    {
        Failures(options =>
            {
                UseMicrosoftEntraId(options);
                options.Endpoint = new Uri(endpoint, UriKind.RelativeOrAbsolute);
            })
            .ShouldHaveSingleItem().ShouldContain("absolute https URI");
    }

    [Fact]
    public void WithNeitherConnectionStringNorEndpoint_NamesBothWays()
    {
        Failures(options => options.ConnectionString = " ")
            .ShouldHaveSingleItem().ShouldContain("either ConnectionString, or Endpoint with Credential");
    }

    [Fact]
    public void Binding_ReadsTheEndpoint_AndLeavesTheCredentialToTheHost()
    {
        // Hosts bind the options from a configuration section, which has no way to express a credential.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RealtimeSettings:Endpoint"] = "https://example.service.signalr.net",
            })
            .Build();
        var options = new BlueprintAzureSignalROptions();

        configuration.GetSection("RealtimeSettings").Bind(options);

        options.Endpoint.ShouldBe(new Uri("https://example.service.signalr.net"));
        options.Credential.ShouldBeNull();
    }

    [Fact]
    public void WithEmulatorConnectionString_IsValid()
    {
        // A port and a separate client endpoint, because the API and the browser reach the emulator under
        // different names.
        Failures(options => options.ConnectionString =
                "Endpoint=http://signalr-emulator;Port=8888;AccessKey=ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ABCDEFGH;Version=1.0;ClientEndpoint=http://localhost:20003;")
            .ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(8760, true)]
    [InlineData(8761, false)]
    [InlineData(int.MaxValue, false)]
    public void GroupMembershipTtl_IsAcceptedFromOneHourUpToAYear(int ttlHours, bool expected)
    {
        // A TTL of 0 also fails the ticket rule, which needs the membership to outlast the ticket.
        Failures(options => options.GroupMembershipTtlHours = ttlHours).Count.Equals(0).ShouldBe(expected);
    }

    [Fact]
    public void WithZeroTicketLifetime_IsInvalid()
    {
        Failures(options => options.TicketTokenLifetimeMinutes = 0).ShouldHaveSingleItem().ShouldContain("TicketTokenLifetimeMinutes");
    }

    [Theory]
    [InlineData(59, true)]
    [InlineData(60, false)]
    [InlineData(61, false)]
    public void TicketLifetime_MustExpireBeforeTheMembership(int ticketLifetimeMinutes, bool expected)
    {
        var failures = Failures(options =>
        {
            options.TicketTokenLifetimeMinutes = ticketLifetimeMinutes;
            options.GroupMembershipTtlHours = 1;
        });

        failures.Count.ShouldBe(expected ? 0 : 1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has spaces")]
    [InlineData("has-a-hyphen")]
    [InlineData("app\n")]
    [InlineData(null)]
    public void WithInvalidHubName_IsInvalid(string? hubName)
    {
        Failures(options => options.HubName = hubName!).ShouldHaveSingleItem().ShouldContain("HubName");
    }

    private static void UseMicrosoftEntraId(BlueprintAzureSignalROptions options)
    {
        options.ConnectionString = null;
        options.Endpoint = new Uri("https://example.service.signalr.net");
        options.Credential = new UnusedTokenCredential();
    }

    private static ServiceProvider BuildProvider(Action<BlueprintAzureSignalROptions> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddBlueprintAzureSignalRRealtime(configure);
        return services.BuildServiceProvider();
    }

    private static IReadOnlyList<string> Failures(Action<BlueprintAzureSignalROptions> change)
    {
        using var provider = BuildProvider(options =>
        {
            options.ConnectionString = ValidConnectionString;
            change(options);
        });

        try
        {
            _ = provider.GetRequiredService<IOptions<BlueprintAzureSignalROptions>>().Value;
            return [];
        }
        catch (OptionsValidationException exception)
        {
            return [.. exception.Failures];
        }
    }

    /// <summary>
    /// Stands in for a credential where validation only needs one to be present.
    /// </summary>
    private sealed class UnusedTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Validation never asks for a token.");
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Validation never asks for a token.");
        }
    }
}
