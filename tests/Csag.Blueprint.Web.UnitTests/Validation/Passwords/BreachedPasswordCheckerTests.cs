namespace Csag.Blueprint.Web.UnitTests.Validation.Passwords;

using System.Net;
using Csag.Blueprint.Web.Options.Api.Security;
using Csag.Blueprint.Web.Options.Api.Security.Password;
using Csag.Blueprint.Web.Validation.Passwords;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

/// <summary>
/// Unit tests for <see cref="BreachedPasswordChecker"/> against a stub range API and a temporary range
/// directory: what leaves the process, how ranges are read, and how failures surface.
/// </summary>
public sealed class BreachedPasswordCheckerTests : IDisposable
{
    // SHA-1 of "Tiger-Lamp-Orbit-42" is 8F728DD2513449EBA0816CD645C782872CCB41AF.
    private const string Password = "Tiger-Lamp-Orbit-42";
    private const string Prefix = "8F728";
    private const string Suffix = "DD2513449EBA0816CD645C782872CCB41AF";

    private readonly string rangeDirectory = Directory.CreateTempSubdirectory("breach-ranges-").FullName;

    public void Dispose()
    {
        Directory.Delete(this.rangeDirectory, recursive: true);
    }

    [Fact]
    public async Task CheckAsync_Disabled_LooksNothingUpAsync()
    {
        var handler = new StubRangeHandler(HttpStatusCode.OK, string.Empty);
        var checker = CreateChecker(new BreachCheckSettings { Mode = BreachCheckMode.Disabled }, handler);

        var result = await checker.CheckAsync(Password, TestContext.Current.CancellationToken);

        result.ShouldBe(BreachCheckResult.NotChecked);
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task CheckAsync_Online_SendsOnlyTheFiveCharacterPrefixWithPaddingAsync()
    {
        var handler = new StubRangeHandler(HttpStatusCode.OK, "0000000000000000000000000000000000A:1");
        var checker = CreateChecker(Online(), handler);

        await checker.CheckAsync(Password, TestContext.Current.CancellationToken);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.RequestUri.ShouldBe(new Uri($"https://ranges.example.test/range/{Prefix}"));
        request.Headers.GetValues("Add-Padding").ShouldBe(["true"]);
        request.RequestUri!.ToString().ShouldNotContain(Suffix);
    }

    [Fact]
    public async Task CheckAsync_SuffixInRangeWithACount_IsBreachedAsync()
    {
        var range = $"0000000000000000000000000000000000A:1\r\n{Suffix}:42\r\nFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF:3";
        var checker = CreateChecker(Online(), new StubRangeHandler(HttpStatusCode.OK, range));

        var result = await checker.CheckAsync(Password, TestContext.Current.CancellationToken);

        result.ShouldBe(BreachCheckResult.Breached);
    }

    [Fact]
    public async Task CheckAsync_SuffixOnlyAsPaddingEntry_IsNotBreachedAsync()
    {
        var checker = CreateChecker(Online(), new StubRangeHandler(HttpStatusCode.OK, $"{Suffix}:0"));

        var result = await checker.CheckAsync(Password, TestContext.Current.CancellationToken);

        result.ShouldBe(BreachCheckResult.NotBreached);
    }

    [Fact]
    public async Task CheckAsync_SuffixNotInRange_IsNotBreachedAsync()
    {
        var checker = CreateChecker(Online(), new StubRangeHandler(HttpStatusCode.OK, "0000000000000000000000000000000000A:7"));

        var result = await checker.CheckAsync(Password, TestContext.Current.CancellationToken);

        result.ShouldBe(BreachCheckResult.NotBreached);
    }

    [Fact]
    public async Task CheckAsync_ApiAnswersWithAnError_IsUnavailableAsync()
    {
        var checker = CreateChecker(Online(), new StubRangeHandler(HttpStatusCode.ServiceUnavailable, string.Empty));

        var result = await checker.CheckAsync(Password, TestContext.Current.CancellationToken);

        result.ShouldBe(BreachCheckResult.Unavailable);
    }

    [Fact]
    public async Task CheckAsync_ApiUnreachable_IsUnavailableAsync()
    {
        var checker = CreateChecker(Online(), new StubRangeHandler(new HttpRequestException("connection refused")));

        var result = await checker.CheckAsync(Password, TestContext.Current.CancellationToken);

        result.ShouldBe(BreachCheckResult.Unavailable);
    }

    [Fact]
    public async Task CheckAsync_ApiSlowerThanTheTimeout_IsUnavailableAsync()
    {
        var settings = Online();
        settings.TimeoutMilliseconds = 100;
        var checker = CreateChecker(settings, StubRangeHandler.NeverAnswering());

        var result = await checker.CheckAsync(Password, TestContext.Current.CancellationToken);

        result.ShouldBe(BreachCheckResult.Unavailable);
    }

    [Fact]
    public async Task CheckAsync_CallerCancels_ThrowsAsync()
    {
        var checker = CreateChecker(Online(), StubRangeHandler.NeverAnswering());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => checker.CheckAsync(Password, cancellation.Token));
    }

    [Fact]
    public async Task CheckAsync_OfflineRangeFileListsTheSuffix_IsBreachedAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(this.rangeDirectory, $"{Prefix}.txt"), $"{Suffix}:3\n", TestContext.Current.CancellationToken);
        var checker = CreateChecker(this.Offline(), StubRangeHandler.NeverAnswering());

        var result = await checker.CheckAsync(Password, TestContext.Current.CancellationToken);

        result.ShouldBe(BreachCheckResult.Breached);
    }

    [Fact]
    public async Task CheckAsync_OfflineRangeFileWithoutTheSuffix_IsNotBreachedAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(this.rangeDirectory, $"{Prefix}.txt"), "0000000000000000000000000000000000A:3\n", TestContext.Current.CancellationToken);
        var checker = CreateChecker(this.Offline(), StubRangeHandler.NeverAnswering());

        var result = await checker.CheckAsync(Password, TestContext.Current.CancellationToken);

        result.ShouldBe(BreachCheckResult.NotBreached);
    }

    [Fact]
    public async Task CheckAsync_OfflineRangeFileMissing_IsUnavailableAsync()
    {
        var checker = CreateChecker(this.Offline(), StubRangeHandler.NeverAnswering());

        var result = await checker.CheckAsync(Password, TestContext.Current.CancellationToken);

        result.ShouldBe(BreachCheckResult.Unavailable);
    }

    private static BreachCheckSettings Online() => new()
    {
        Mode = BreachCheckMode.Online,
        RangeApiBaseUrl = "https://ranges.example.test/range/",
    };

    private static BreachedPasswordChecker CreateChecker(BreachCheckSettings breachCheck, StubRangeHandler handler)
    {
        var settings = new SecuritySettings { PasswordSettings = new PasswordSettings { BreachCheck = breachCheck } };
        return new BreachedPasswordChecker(
            new StubHttpClientFactory(handler),
            new StaticOptionsMonitor(settings),
            NullLogger<BreachedPasswordChecker>.Instance);
    }

    private BreachCheckSettings Offline() => new()
    {
        Mode = BreachCheckMode.Offline,
        OfflineRangeDirectory = this.rangeDirectory,
    };

    private sealed class StubRangeHandler : HttpMessageHandler
    {
        private readonly Func<CancellationToken, Task<HttpResponseMessage>> respond;

        public StubRangeHandler(HttpStatusCode status, string body)
        {
            this.respond = _ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }

        public StubRangeHandler(Exception exception)
        {
            this.respond = _ => Task.FromException<HttpResponseMessage>(exception);
        }

        private StubRangeHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond)
        {
            this.respond = respond;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        public static StubRangeHandler NeverAnswering() => new(async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("A delay without end only ends by cancellation.");
        });

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Requests.Add(request);
            return this.respond(cancellationToken);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StaticOptionsMonitor(SecuritySettings value) : IOptionsMonitor<SecuritySettings>
    {
        public SecuritySettings CurrentValue => value;

        public SecuritySettings Get(string? name) => value;

        public IDisposable? OnChange(Action<SecuritySettings, string?> listener) => null;
    }
}
