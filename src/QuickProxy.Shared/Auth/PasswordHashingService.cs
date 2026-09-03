using System.Security.Cryptography;
using System.Text;

namespace QuickProxy.Shared.Auth;

public interface IPasswordHashingService
{
    string HashPassword(string password);
    bool Verify(string passwordHash, string password);
}

public sealed class PasswordHashingService : IPasswordHashingService
{
    private const string Prefix = "pbkdf2-sha256";
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;

    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize);

        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string passwordHash, string password)
    {
        if (string.IsNullOrWhiteSpace(passwordHash)) return false;

        var parts = passwordHash.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal)) return false;

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0) return false;

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expected.Length);

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}