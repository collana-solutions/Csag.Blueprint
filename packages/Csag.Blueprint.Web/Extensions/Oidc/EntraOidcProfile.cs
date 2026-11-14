namespace Csag.Blueprint.Web.Extensions.Oidc;

using System;
using System.Security.Claims;
using Csag.Blueprint.Web.Options.Api.Security.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// Profile for Microsoft Entra ID (Azure AD). Derives the authority from the configured
/// <see cref="MicrosoftEntraSignInAudience"/> (or an explicit <see cref="OidcProviderSettings.Authority"/>),
/// applies tenant-aware issuer validation for multi-tenant apps, and normalizes the claims for the external
/// callback. It computes its own <c>email_verified</c> value to prevent the "nOAuth" account takeover. Uses the
/// generic <c>AddOpenIdConnect</c> handler, not an Entra-specific package.
/// </summary>
public sealed class EntraOidcProfile : OidcProviderProfileBase
{
    /// <inheritdoc/>
    public override void Configure(OpenIdConnectOptions options, OidcProviderSettings settings)
    {
        ApplyCommon(options, settings);

        options.Authority = string.IsNullOrWhiteSpace(settings.Authority)
            ? ResolveAuthority(settings)
            : settings.Authority;

        // Entra id_tokens use short claim names; normalize them ourselves rather than via the
        // inbound map, and expose Entra's role claim.
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = JwtRegisteredClaimNames.Name;
        options.TokenValidationParameters.RoleClaimType = "roles";

        // Without optional claims in the app registration, Entra sends the names only through UserInfo. The
        // handler adds the UserInfo values after the TokenValidated normalization, so map them here too.
        options.ClaimActions.Add(new EntraUserInfoClaimAction(ClaimTypes.GivenName, JwtRegisteredClaimNames.GivenName));
        options.ClaimActions.Add(new EntraUserInfoClaimAction(ClaimTypes.Surname, JwtRegisteredClaimNames.FamilyName));
        options.ClaimActions.Add(new EntraUserInfoClaimAction(ClaimTypes.Email, JwtRegisteredClaimNames.Email));

        // Multi-tenant apps accept tokens from any tenant, so the single discovery issuer cannot be used.
        if (settings.SignInAudience != MicrosoftEntraSignInAudience.SingleTenant)
        {
            options.TokenValidationParameters.IssuerValidator = ValidateMultiTenantIssuer;

            if (settings.SignInAudience == MicrosoftEntraSignInAudience.MultiTenantAndPersonal)
            {
                options.TokenValidationParameters.ValidateIssuer = true;
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The claim normalization is what the external callback's email-trust gate relies on: it drops any
    /// <c>email_verified</c> that arrived in the token and stamps a trustworthy one. It is installed here, after
    /// the application's configuration, and composed with any handler already there, so no application handler
    /// can replace it by accident. The normalization runs first, so such a handler sees the trusted claims.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The handler would not call the normalization (see <see cref="OpenIdConnectEventsGuard.EnsureDelegatesAreDispatched"/>).
    /// </exception>
    public override void PostConfigure(string scheme, OpenIdConnectOptions options, OidcProviderSettings settings)
    {
        OpenIdConnectEventsGuard.EnsureDelegatesAreDispatched(
            scheme,
            options,
            "would bypass the claim normalization the Entra email-trust policy depends on",
            nameof(OpenIdConnectEvents.TokenValidated));

        var tokenValidated = options.Events.OnTokenValidated;
        options.Events.OnTokenValidated = context =>
        {
            if (context.Principal?.Identity is ClaimsIdentity identity)
            {
                NormalizeClaimsForCallback(identity, settings.SignInAudience);
            }

            return tokenValidated(context);
        };
    }

    private static string ResolveAuthority(OidcProviderSettings settings)
    {
        return settings.SignInAudience switch
        {
            MicrosoftEntraSignInAudience.SingleTenant => $"https://login.microsoftonline.com/{settings.TenantId}/v2.0",
            MicrosoftEntraSignInAudience.MultiTenant => "https://login.microsoftonline.com/organizations/v2.0",
            MicrosoftEntraSignInAudience.MultiTenantAndPersonal => "https://login.microsoftonline.com/common/v2.0",
            _ => throw new InvalidOperationException($"Unsupported SignInAudience: {settings.SignInAudience}"),
        };
    }

    /// <summary>
    /// Accepts only the issuer of the tenant that issued the token. A multi-tenant app (<c>/organizations</c>,
    /// <c>/common</c>) gets tokens from many tenants, and each tenant has its own issuer. Accepts
    /// <c>https://login.microsoftonline.com/{tid}/v2.0</c> and the legacy <c>https://sts.windows.net/{tid}/</c>.
    /// </summary>
    private static string ValidateMultiTenantIssuer(string issuer, SecurityToken token, TokenValidationParameters parameters)
    {
        if (token is not JsonWebToken jwt)
        {
            throw new SecurityTokenInvalidIssuerException("Unable to validate the token: it is not a JsonWebToken.");
        }

        var tid = jwt.TryGetPayloadValue<string>("tid", out var tenantId) ? tenantId : null;

        if (string.IsNullOrEmpty(tid))
        {
            throw new SecurityTokenInvalidIssuerException("Token does not contain a 'tid' claim.");
        }

        var expectedIssuer = $"https://login.microsoftonline.com/{tid}/v2.0";
        var expectedStsIssuer = $"https://sts.windows.net/{tid}/";

        if (string.Equals(issuer, expectedIssuer, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(issuer, expectedStsIssuer, StringComparison.OrdinalIgnoreCase))
        {
            return issuer;
        }

        throw new SecurityTokenInvalidIssuerException($"The iss claim '{issuer}' does not match the expected tenant-based value for tid '{tid}'.");
    }

    /// <summary>
    /// Maps the short Entra claims to the <see cref="ClaimTypes"/> that the callback reads, and stamps the
    /// <c>email_verified</c> value that this profile computes. Entra sends no standard <c>email_verified</c>.
    /// In a multi-tenant app, another tenant can set <c>email</c> and <c>preferred_username</c> to any address
    /// (the "nOAuth" account takeover). So the email is trusted only for a single tenant, or when Entra sends
    /// the optional <c>xms_edov</c> (email domain owner verified) claim.
    /// </summary>
    private static void NormalizeClaimsForCallback(ClaimsIdentity identity, MicrosoftEntraSignInAudience audience)
    {
        MapClaimIfMissing(identity, JwtRegisteredClaimNames.Sub, ClaimTypes.NameIdentifier);
        MapClaimIfMissing(identity, JwtRegisteredClaimNames.Email, ClaimTypes.Email);
        MapClaimIfMissing(identity, JwtRegisteredClaimNames.GivenName, ClaimTypes.GivenName);
        MapClaimIfMissing(identity, JwtRegisteredClaimNames.FamilyName, ClaimTypes.Surname);

        var emailIsVerified = audience == MicrosoftEntraSignInAudience.SingleTenant
            || IsClaimTrue(identity.FindFirst("xms_edov")?.Value);

        // Fail closed: an email_verified that arrived in the token would let a value that we did not compute
        // open the account-link gate.
        Claim? staleVerified;
        while ((staleVerified = identity.FindFirst(JwtRegisteredClaimNames.EmailVerified)) != null)
        {
            identity.RemoveClaim(staleVerified);
        }

        identity.AddClaim(new Claim(JwtRegisteredClaimNames.EmailVerified, emailIsVerified ? "true" : "false"));

        // Work and school accounts often have no `email`, only `preferred_username`. Use it only when the email
        // is trusted, so the callback never links on an address that another tenant controls.
        if (emailIsVerified && identity.FindFirst(ClaimTypes.Email) == null)
        {
            var preferredUsername = identity.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value;
            if (!string.IsNullOrEmpty(preferredUsername) && preferredUsername.Contains('@'))
            {
                identity.AddClaim(new Claim(ClaimTypes.Email, preferredUsername));
            }
        }
    }

    private static bool IsClaimTrue(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "1", StringComparison.Ordinal);
    }

    private static void MapClaimIfMissing(ClaimsIdentity identity, string source, string target)
    {
        if (identity.FindFirst(target) != null)
        {
            return;
        }

        var sourceClaim = identity.FindFirst(source);
        if (sourceClaim != null)
        {
            identity.AddClaim(new Claim(target, sourceClaim.Value));
        }
    }
}
