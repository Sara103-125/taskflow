namespace TaskFlow.Api;

public static class RateLimitPolicies
{
    /// <summary>Applied to login / register / demo: a few attempts per minute per IP address.</summary>
    public const string Auth = "auth";
}
