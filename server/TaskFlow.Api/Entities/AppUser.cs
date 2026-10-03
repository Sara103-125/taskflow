using Microsoft.AspNetCore.Identity;

namespace TaskFlow.Api.Entities;

/// <summary>
/// The application user. Identity already gives us Id, Email, PasswordHash, etc.
/// We only add a friendly display name.
/// </summary>
public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;

    public List<BoardMember> Memberships { get; set; } = new();
}
