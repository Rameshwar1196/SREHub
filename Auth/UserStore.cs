using System.Collections.Concurrent;
using System.Text.Json;

namespace Aras.SreHub.Auth;

public sealed class UserStore
{
    private readonly AuthOptions _options;
    private readonly ILogger<UserStore> _logger;
    private readonly string _storePath;
    private readonly Dictionary<string, AuthUser> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FailedAttempts> _failures = new(StringComparer.OrdinalIgnoreCase);

    public UserStore(AuthOptions options, IWebHostEnvironment environment, ILogger<UserStore> logger)
    {
        _options = options;
        _logger = logger;
        _storePath = Path.Combine(environment.ContentRootPath, "App_Data", "users.json");

        Load();
    }

    public string GetDisplayName(string email) =>
        _users.TryGetValue(email, out var user) && !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName : email;

    public bool TryAuthenticate(string email, string password, string client, out AuthUser? user, out string error)
    {
        user = null;
        error = string.Empty;

        var normalized = (email ?? string.Empty).Trim();
        var key = $"{normalized}|{client}";

        if (_failures.TryGetValue(key, out var attempts) && attempts.LockedUntil > DateTimeOffset.UtcNow)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((attempts.LockedUntil - DateTimeOffset.UtcNow).TotalMinutes));
            error = $"Too many failed attempts. Try again in {minutes} minute(s).";
            return false;
        }

        _users.TryGetValue(normalized, out var candidate);

        // Always run the KDF so a missing account costs the same as a wrong password.
        var valid = PasswordHasher.Verify(password ?? string.Empty, candidate?.PasswordHash ?? PasswordHasher.DummyHash);

        if (!valid || candidate is null)
        {
            RegisterFailure(key);
            error = "Incorrect email or password.";
            return false;
        }

        _failures.TryRemove(key, out _);
        user = candidate;
        return true;
    }

    private void RegisterFailure(string key)
    {
        _failures.AddOrUpdate(
            key,
            _ => new FailedAttempts(1, DateTimeOffset.MinValue),
            (_, current) =>
            {
                var count = current.LockedUntil > DateTimeOffset.UtcNow ? current.Count : current.Count + 1;
                var lockedUntil = count >= _options.MaxFailedAttempts
                    ? DateTimeOffset.UtcNow.AddMinutes(_options.LockoutMinutes)
                    : current.LockedUntil;

                return new FailedAttempts(count, lockedUntil);
            });
    }

    private void Load()
    {
        if (File.Exists(_storePath))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<List<AuthUser>>(File.ReadAllText(_storePath), SerializerOptions) ?? [];
                foreach (var user in stored.Where(user => !string.IsNullOrWhiteSpace(user.Email)))
                {
                    Add(user);
                }
            }
            catch (JsonException exception)
            {
                _logger.LogError(exception, "Could not read {Path}; it will be recreated if no users are configured.", _storePath);
            }
        }

        // Configured accounts are loaded last so they override the seeded file.
        foreach (var user in _options.Users.Where(user => !string.IsNullOrWhiteSpace(user.Email)))
        {
            Add(user);
        }

        if (_users.Count == 0)
        {
            Seed();
        }
    }

    private void Add(AuthUser user)
    {
        if (!PasswordHasher.IsValidHash(user.PasswordHash))
        {
            _logger.LogWarning("User '{Email}' was skipped: PasswordHash is missing or not a pbkdf2 hash.", user.Email);
            return;
        }

        var email = user.Email.Trim();

        _users[email] = new AuthUser
        {
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? email : user.DisplayName.Trim(),
            PasswordHash = user.PasswordHash
        };
    }

    private void Seed()
    {
        var password = PasswordHasher.GeneratePassword();
        var user = new AuthUser
        {
            Email = _options.SeedEmail,
            DisplayName = "Administrator",
            PasswordHash = PasswordHasher.Hash(password)
        };

        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
        File.WriteAllText(_storePath, JsonSerializer.Serialize(new[] { user }, SerializerOptions));

        _users[user.Email] = user;

        Console.WriteLine();
        Console.WriteLine("  ===========================================================");
        Console.WriteLine("   SRE Hub - first run: an administrator account was created");
        Console.WriteLine("  ===========================================================");
        Console.WriteLine($"   Email    : {user.Email}");
        Console.WriteLine($"   Password : {password}");
        Console.WriteLine();
        Console.WriteLine("   This password is shown once. Store it in a password manager.");
        Console.WriteLine($"   Hash saved to: {_storePath}");
        Console.WriteLine("  ===========================================================");
        Console.WriteLine();
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly record struct FailedAttempts(int Count, DateTimeOffset LockedUntil);
}
