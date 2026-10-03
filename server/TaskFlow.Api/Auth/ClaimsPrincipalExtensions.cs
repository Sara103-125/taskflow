using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace TaskFlow.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The user id stored in the JWT "sub" claim.</summary>
    public static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException("Authenticated user has no 'sub' claim.");

    public static string GetDisplayName(this ClaimsPrincipal user) =>
        user.FindFirstValue(JwtRegisteredClaimNames.Name) ?? "Someone";
}
