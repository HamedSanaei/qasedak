using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Identity.Application.Authentication;

namespace Qasedak.Modules.Identity.Infrastructure.Authentication;

/// <summary>
/// Minimal bearer-token authentication scheme backed by <see cref="ISecurityTokenIssuer"/>.
/// Valid tokens yield a ClaimsPrincipal with the user id and email; anything else yields
/// no result so the boundary answers 401 without leaking why.
/// </summary>
public sealed class SecurityTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISecurityTokenIssuer tokenIssuer)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "QasedakBearer";

    private const string BearerPrefix = "Bearer ";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = header[BearerPrefix.Length..].Trim();
        if (token.Length == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var validation = ValidateToken(token);
        if (!validation.IsValid)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, validation.UserId.ToString()),
            new Claim(ClaimTypes.Email, validation.Email),
        ], authenticationType: SchemeName);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>
    /// A host without a signing key answers 401 rather than 500: misconfiguration is
    /// indistinguishable from an invalid credential at the boundary.
    /// </summary>
    private TokenValidationResult ValidateToken(string token)
    {
        try
        {
            return tokenIssuer.Validate(token);
        }
        catch (InvalidOperationException)
        {
            return default;
        }
    }
}
