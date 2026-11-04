namespace Aras.SreHub.Auth;

public sealed class AuthUser
{
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string SeedEmail { get; set; } = "admin@local";

    public int SessionHours { get; set; } = 12;

    public int MaxFailedAttempts { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;

    public List<AuthUser> Users { get; set; } = [];
}

public sealed class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
