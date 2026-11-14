namespace Csag.Blueprint.Web.UnitTests.Extensions.Oidc;

using System.Security.Claims;
using Csag.Blueprint.Web.Extensions.Oidc;
using Csag.Blueprint.Web.Options.Api.Security.OAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// Unit tests for <see cref="EntraOidcProfile"/>: the authority, the short claim names, the tenant-aware issuer
/// validation, and the claim normalization and email trust ("nOAuth" prevention) of the TokenValidated event.
/// </summary>
public sealed class EntraOidcProfileTests
{
    private const string Scheme = "entra";
    private const string TenantId = "11111111-1111-1111-1111-111111111111";

    private readonly EntraOidcProfile profile = new();

    [Fact]
    public void Configure_SingleTenant_DerivesTenantAuthority()
    {
        var options = new OpenIdConnectOptions();

        this.profile.Configure(options, CreateSettings(MicrosoftEntraSignInAudience.SingleTenant));

        options.Authority.ShouldBe($"https://login.microsoftonline.com/{TenantId}/v2.0");
    }

    [Fact]
    public void Configure_MultiTenant_UsesOrganizationsAuthority()
    {
        var options = new OpenIdConnectOptions();

        this.profile.Configure(options, CreateSettings(MicrosoftEntraSignInAudience.MultiTenant));

        options.Authority.ShouldBe("https://login.microsoftonline.com/organizations/v2.0");
    }

    [Fact]
    public void Configure_MultiTenantAndPersonal_UsesCommonAuthority()
    {
        var options = new OpenIdConnectOptions();

        this.profile.Configure(options, CreateSettings(MicrosoftEntraSignInAudience.MultiTenantAndPersonal));

        options.Authority.ShouldBe("https://login.microsoftonline.com/common/v2.0");
    }

    [Fact]
    public void Configure_ExplicitAuthority_WinsOverAudienceDerivation()
    {
        var options = new OpenIdConnectOptions();
        var settings = CreateSettings(MicrosoftEntraSignInAudience.MultiTenant);
        settings.Authority = "https://login.microsoftonline.us/organizations/v2.0";

        this.profile.Configure(options, settings);

        options.Authority.ShouldBe("https://login.microsoftonline.us/organizations/v2.0");
    }

    [Fact]
    public void Configure_UnknownSignInAudienceWithoutAuthority_Throws()
    {
        var options = new OpenIdConnectOptions();
        var settings = CreateSettings((MicrosoftEntraSignInAudience)999);

        Should.Throw<InvalidOperationException>(() => this.profile.Configure(options, settings));
    }

    [Fact]
    public void Configure_NormalizesShortClaimHandling()
    {
        // Entra id_tokens use short claim names; the profile turns the inbound map off and points the
        // handler at Entra's "name"/"roles" claims instead.
        var options = new OpenIdConnectOptions();

        this.profile.Configure(options, CreateSettings(MicrosoftEntraSignInAudience.SingleTenant));

        options.MapInboundClaims.ShouldBeFalse();
        options.TokenValidationParameters.NameClaimType.ShouldBe("name");
        options.TokenValidationParameters.RoleClaimType.ShouldBe("roles");
    }

    [Fact]
    public void Configure_SingleTenant_KeepsDiscoveryIssuerValidation()
    {
        // A single-tenant app has exactly one issuer, so the discovery-derived issuer suffices and no
        // custom validator is installed.
        var options = new OpenIdConnectOptions();

        this.profile.Configure(options, CreateSettings(MicrosoftEntraSignInAudience.SingleTenant));

        options.TokenValidationParameters.IssuerValidator.ShouldBeNull();
        options.TokenValidationParameters.ValidateIssuer.ShouldBeTrue();
    }

    [Fact]
    public void Configure_MultiTenant_InstallsTenantAwareIssuerValidator()
    {
        var options = new OpenIdConnectOptions();

        this.profile.Configure(options, CreateSettings(MicrosoftEntraSignInAudience.MultiTenant));

        options.TokenValidationParameters.IssuerValidator.ShouldNotBeNull();
    }

    [Fact]
    public void Configure_MultiTenantAndPersonal_InstallsValidatorAndForcesValidationOn()
    {
        var options = new OpenIdConnectOptions();

        this.profile.Configure(options, CreateSettings(MicrosoftEntraSignInAudience.MultiTenantAndPersonal));

        options.TokenValidationParameters.IssuerValidator.ShouldNotBeNull();
        options.TokenValidationParameters.ValidateIssuer.ShouldBeTrue();
    }

    [Theory]
    [InlineData(MicrosoftEntraSignInAudience.MultiTenant, "https://login.microsoftonline.com/" + TenantId + "/v2.0")]
    [InlineData(MicrosoftEntraSignInAudience.MultiTenant, "https://sts.windows.net/" + TenantId + "/")]
    [InlineData(MicrosoftEntraSignInAudience.MultiTenantAndPersonal, "https://login.microsoftonline.com/" + TenantId + "/v2.0")]
    [InlineData(MicrosoftEntraSignInAudience.MultiTenantAndPersonal, "https://sts.windows.net/" + TenantId + "/")]
    public void IssuerValidator_IssuerOfTheTokensTenant_ReturnsIssuer(MicrosoftEntraSignInAudience audience, string issuer)
    {
        var validator = this.ConfigureIssuerValidator(audience);

        validator(issuer, CreateToken(TenantId), new TokenValidationParameters()).ShouldBe(issuer);
    }

    [Theory]
    [InlineData(MicrosoftEntraSignInAudience.MultiTenant)]
    [InlineData(MicrosoftEntraSignInAudience.MultiTenantAndPersonal)]
    public void IssuerValidator_IssuerOfAnotherTenant_Throws(MicrosoftEntraSignInAudience audience)
    {
        var validator = this.ConfigureIssuerValidator(audience);

        Should.Throw<SecurityTokenInvalidIssuerException>(() => validator(
            "https://login.microsoftonline.com/22222222-2222-2222-2222-222222222222/v2.0",
            CreateToken(TenantId),
            new TokenValidationParameters()));
    }

    [Theory]
    [InlineData(MicrosoftEntraSignInAudience.MultiTenant)]
    [InlineData(MicrosoftEntraSignInAudience.MultiTenantAndPersonal)]
    public void IssuerValidator_TokenWithoutTid_Throws(MicrosoftEntraSignInAudience audience)
    {
        var validator = this.ConfigureIssuerValidator(audience);

        Should.Throw<SecurityTokenInvalidIssuerException>(() => validator(
            $"https://login.microsoftonline.com/{TenantId}/v2.0",
            CreateToken(tid: null),
            new TokenValidationParameters()));
    }

    [Theory]
    [InlineData(MicrosoftEntraSignInAudience.MultiTenant)]
    [InlineData(MicrosoftEntraSignInAudience.MultiTenantAndPersonal)]
    public void IssuerValidator_NotAJsonWebToken_Throws(MicrosoftEntraSignInAudience audience)
    {
        var validator = this.ConfigureIssuerValidator(audience);

        Should.Throw<SecurityTokenInvalidIssuerException>(() => validator(
            $"https://login.microsoftonline.com/{TenantId}/v2.0",
            new FakeSecurityToken(),
            new TokenValidationParameters()));
    }

    [Fact]
    public async Task PostConfigure_OnTokenValidated_MapsShortClaimsToStandardTypesAsync()
    {
        var identity = await this.RaiseTokenValidatedAsync(
            MicrosoftEntraSignInAudience.SingleTenant,
            new Claim("sub", "user-1"),
            new Claim("email", "user@example.com"),
            new Claim("given_name", "Erika"),
            new Claim("family_name", "Mustermann"));

        identity.FindFirst(ClaimTypes.NameIdentifier)!.Value.ShouldBe("user-1");
        identity.FindFirst(ClaimTypes.Email)!.Value.ShouldBe("user@example.com");
        identity.FindFirst(ClaimTypes.GivenName)!.Value.ShouldBe("Erika");
        identity.FindFirst(ClaimTypes.Surname)!.Value.ShouldBe("Mustermann");
    }

    [Theory]
    [InlineData(MicrosoftEntraSignInAudience.SingleTenant, null, "true")]
    [InlineData(MicrosoftEntraSignInAudience.MultiTenant, "true", "true")]
    [InlineData(MicrosoftEntraSignInAudience.MultiTenant, "1", "true")]
    [InlineData(MicrosoftEntraSignInAudience.MultiTenant, null, "false")]
    [InlineData(MicrosoftEntraSignInAudience.MultiTenant, "false", "false")]
    [InlineData(MicrosoftEntraSignInAudience.MultiTenantAndPersonal, null, "false")]
    public async Task PostConfigure_OnTokenValidated_StampsTheEmailTrustAsync(
        MicrosoftEntraSignInAudience audience,
        string? xmsEdov,
        string expected)
    {
        Claim[] claims = xmsEdov is null
            ? [new Claim("sub", "u")]
            : [new Claim("sub", "u"), new Claim("xms_edov", xmsEdov)];

        var identity = await this.RaiseTokenValidatedAsync(audience, claims);

        identity.FindAll("email_verified").Select(claim => claim.Value).ShouldBe([expected]);
    }

    [Fact]
    public async Task PostConfigure_OnTokenValidated_ReplacesAnIncomingEmailVerifiedAsync()
    {
        // Fail closed: a forged "true" in the token must not open the account-link gate.
        var identity = await this.RaiseTokenValidatedAsync(
            MicrosoftEntraSignInAudience.MultiTenant,
            new Claim("sub", "u"),
            new Claim("email", "a@example.com"),
            new Claim("email_verified", "true"));

        identity.FindAll("email_verified").Select(claim => claim.Value).ShouldBe(["false"]);
    }

    [Fact]
    public async Task PostConfigure_OnTokenValidated_TrustedWithoutEmail_TakesThePreferredUsernameAsync()
    {
        var identity = await this.RaiseTokenValidatedAsync(
            MicrosoftEntraSignInAudience.MultiTenant,
            new Claim("sub", "u"),
            new Claim("preferred_username", "worker@contoso.com"),
            new Claim("xms_edov", "true"));

        identity.FindFirst(ClaimTypes.Email)!.Value.ShouldBe("worker@contoso.com");
    }

    [Fact]
    public async Task PostConfigure_OnTokenValidated_UntrustedWithoutEmail_IgnoresThePreferredUsernameAsync()
    {
        var identity = await this.RaiseTokenValidatedAsync(
            MicrosoftEntraSignInAudience.MultiTenant,
            new Claim("sub", "u"),
            new Claim("preferred_username", "attacker@contoso.com"));

        identity.FindFirst(ClaimTypes.Email).ShouldBeNull();
    }

    [Fact]
    public async Task PostConfigure_ExistingEvents_AreKeptAndSeeTheNormalizedClaimsAsync()
    {
        // The application's own handlers are already on the options when the profile post-configures them,
        // and the profile composes with them instead of replacing them.
        var options = new OpenIdConnectOptions();
        string? nameIdentifierSeenByExistingHandler = null;
        Func<RedirectContext, Task> redirectHandler = _ => Task.CompletedTask;
        options.Events.OnRedirectToIdentityProvider = redirectHandler;
        options.Events.OnTokenValidated = context =>
        {
            nameIdentifierSeenByExistingHandler = context.Principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Task.CompletedTask;
        };

        var settings = CreateSettings(MicrosoftEntraSignInAudience.SingleTenant);
        this.profile.Configure(options, settings);
        this.profile.PostConfigure(Scheme, options, settings);

        var identity = new ClaimsIdentity(new[] { new Claim("sub", "user-1") }, authenticationType: "Test");
        await options.Events.OnTokenValidated(new TokenValidatedContext(
            new DefaultHttpContext(),
            new AuthenticationScheme("TestScheme", displayName: null, typeof(OpenIdConnectHandler)),
            options,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties()));

        nameIdentifierSeenByExistingHandler.ShouldBe("user-1");
        options.Events.OnRedirectToIdentityProvider.ShouldBeSameAs(redirectHandler);
    }

    [Fact]
    public void PostConfigure_EventsType_Throws()
    {
        // The handler would resolve its events from DI, so the normalization could not be installed.
        var options = new OpenIdConnectOptions { EventsType = typeof(OpenIdConnectEvents) };
        var settings = CreateSettings(MicrosoftEntraSignInAudience.MultiTenant);
        this.profile.Configure(options, settings);

        var exception = Should.Throw<InvalidOperationException>(() => this.profile.PostConfigure(Scheme, options, settings));

        exception.Message.ShouldContain($"'{Scheme}' sets EventsType");
    }

    [Fact]
    public void PostConfigure_EventsSubclassOverridingTokenValidated_Throws()
    {
        // The handler calls the virtual method, which would never reach the normalization delegate.
        var settings = CreateSettings(MicrosoftEntraSignInAudience.MultiTenant);
        var options = new OpenIdConnectOptions();
        this.profile.Configure(options, settings);
        options.Events = new TokenValidatedOverridingEvents();

        var exception = Should.Throw<InvalidOperationException>(() => this.profile.PostConfigure(Scheme, options, settings));

        exception.Message.ShouldContain($"'{Scheme}' uses {nameof(TokenValidatedOverridingEvents)}, which overrides TokenValidated");
    }

    [Fact]
    public void PostConfigure_EventsSubclassOverridingOtherMethods_IsAccepted()
    {
        var settings = CreateSettings(MicrosoftEntraSignInAudience.MultiTenant);
        var options = new OpenIdConnectOptions();
        this.profile.Configure(options, settings);
        options.Events = new RemoteFailureOverridingEvents();

        Should.NotThrow(() => this.profile.PostConfigure(Scheme, options, settings));
    }

    [Fact]
    public void PostConfigure_InterfaceDefault_LeavesTheOptionsAlone()
    {
        // A profile implementing the interface directly keeps compiling and gets a no-op post-configure step.
        IOidcProviderProfile directImplementation = new ConfigureOnlyProfile();
        var options = new OpenIdConnectOptions();
        Func<TokenValidatedContext, Task> tokenValidated = _ => Task.CompletedTask;
        options.Events.OnTokenValidated = tokenValidated;

        directImplementation.PostConfigure(Scheme, options, CreateSettings(MicrosoftEntraSignInAudience.MultiTenant));

        options.Events.OnTokenValidated.ShouldBeSameAs(tokenValidated);
    }

    private static JsonWebToken CreateToken(string? tid)
    {
        var payload = tid is null ? "{}" : $"{{\"tid\":\"{tid}\"}}";
        return new JsonWebToken("{\"alg\":\"none\",\"typ\":\"JWT\"}", payload);
    }

    private static OidcProviderSettings CreateSettings(MicrosoftEntraSignInAudience audience) => new()
    {
        Enabled = true,
        Profile = OidcProviderProfile.Entra,
        ClientId = "client-id",
        ClientSecret = "client-secret",
        TenantId = TenantId,
        SignInAudience = audience,
    };

    private IssuerValidator ConfigureIssuerValidator(MicrosoftEntraSignInAudience audience)
    {
        var options = new OpenIdConnectOptions();
        this.profile.Configure(options, CreateSettings(audience));
        return options.TokenValidationParameters.IssuerValidator!;
    }

    private async Task<ClaimsIdentity> RaiseTokenValidatedAsync(MicrosoftEntraSignInAudience audience, params Claim[] claims)
    {
        var options = new OpenIdConnectOptions();
        var settings = CreateSettings(audience);
        this.profile.Configure(options, settings);
        this.profile.PostConfigure(Scheme, options, settings);
        var identity = new ClaimsIdentity(claims, authenticationType: "Test");

        await options.Events.OnTokenValidated(new TokenValidatedContext(
            new DefaultHttpContext(),
            new AuthenticationScheme("TestScheme", displayName: null, typeof(OpenIdConnectHandler)),
            options,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties()));

        return identity;
    }

    private sealed class ConfigureOnlyProfile : IOidcProviderProfile
    {
        public void Configure(OpenIdConnectOptions options, OidcProviderSettings settings)
        {
            options.ClientId = settings.ClientId;
        }
    }

    private sealed class TokenValidatedOverridingEvents : OpenIdConnectEvents
    {
        public override Task TokenValidated(TokenValidatedContext context) => Task.CompletedTask;
    }

    private sealed class RemoteFailureOverridingEvents : OpenIdConnectEvents
    {
        public override Task RemoteFailure(RemoteFailureContext context) => Task.CompletedTask;
    }

    private sealed class FakeSecurityToken : SecurityToken
    {
        public override string Id => "fake";

        public override string Issuer => "fake";

        public override SecurityKey SecurityKey => null!;

        public override SecurityKey SigningKey { get; set; } = null!;

        public override DateTime ValidFrom => DateTime.UtcNow;

        public override DateTime ValidTo => DateTime.UtcNow;
    }
}
