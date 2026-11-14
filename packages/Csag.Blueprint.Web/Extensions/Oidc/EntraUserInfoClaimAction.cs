namespace Csag.Blueprint.Web.Extensions.Oidc;

using System;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

/// <summary>
/// Maps one value of the Entra UserInfo answer to the standard claim type that the external callback reads.
/// The handler runs this after <see cref="EntraClaimPolicy.NormalizeClaimsForCallback"/>. A claim that the ID
/// token gave stays. An email needs the <c>email_verified</c> stamp of that normalization, so UserInfo cannot
/// change which address links accounts.
/// </summary>
internal sealed class EntraUserInfoClaimAction(string claimType, string jsonKey)
    : JsonKeyClaimAction(claimType, ClaimValueTypes.String, jsonKey)
{
    /// <inheritdoc/>
    public override void Run(JsonElement userData, ClaimsIdentity identity, string issuer)
    {
        if (identity.FindFirst(this.ClaimType) != null)
        {
            return;
        }

        if (this.ClaimType == ClaimTypes.Email
            && !string.Equals(identity.FindFirst(JwtRegisteredClaimNames.EmailVerified)?.Value, "true", StringComparison.Ordinal))
        {
            return;
        }

        base.Run(userData, identity, issuer);
    }
}
