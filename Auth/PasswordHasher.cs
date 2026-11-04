using System.Security.Cryptography;

namespace Aras.SreHub.Auth;

public static class PasswordHasher
{
    private const string Prefix = "pbkdf2.sha256";
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    // A syntactically valid hash used to keep verification cost constant for unknown accounts.
    public static readonly string DummyHash = Hash(Guid.NewGuid().ToString("N"));

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string? encoded)
    {
        if (!TryParse(encoded, out var iterations, out var salt, out var expected))
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static bool IsValidHash(string? encoded) => TryParse(encoded, out _, out _, out _);

    private static bool TryParse(string? encoded, out int iterations, out byte[] salt, out byte[] key)
    {
        iterations = 0;
        salt = [];
        key = [];

        if (string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        var parts = encoded.Split('$');

        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out iterations) || iterations < 1000)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            key = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length >= 8 && key.Length is >= 16 and <= 64;
    }

    public static string GeneratePassword(int length = 20)
    {
        const string alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789-_";
        return RandomNumberGenerator.GetString(alphabet, length);
    }
}
