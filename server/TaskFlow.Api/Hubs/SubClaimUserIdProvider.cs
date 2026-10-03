using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.SignalR;

namespace TaskFlow.Api.Hubs;

/// <summary>
/// Tells SignalR which user a connection belongs to, so Clients.User(id) reaches every tab
/// that user has open. Our JWT keeps the user id in the "sub" claim.
/// </summary>
public class SubClaimUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
}
