namespace TaskFlow.Api.Auth;

/// <summary>
/// Bound from the "Jwt" config section. The Key is a secret: it comes from
/// user-secrets locally and from App Service settings (Jwt__Key) in Azure.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "TaskFlow";
    public string Audience { get; set; } = "TaskFlow";
    public int ExpiryHours { get; set; } = 8;
}
