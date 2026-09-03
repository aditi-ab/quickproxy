using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuickProxy.Configuration;
using QuickProxy.Proxy.Config.Models;

namespace QuickProxy.Proxy.Config.Storage;

public interface IConfigEncryptionService
{
    string EncryptString(string plaintext);
    string DecryptString(string ciphertext);
    string EncryptBinaryBase64(string binaryBase64);
    string DecryptBinaryBase64(string ciphertext);
    string EncryptLabels(IReadOnlyList<ConfigLabel> labels);
    List<ConfigLabel> DecryptLabels(string? ciphertext);
}

internal sealed class ConfigEncryptionService(AppModulesConfiguration options) : IConfigEncryptionService
{
    private const string Prefix = "enc:v1:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Lazy<byte[]> _key = new(() => ResolveKey(options.Config.Secrets.EncryptionKey));

    public string EncryptString(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext ?? string.Empty);
        return EncryptBytes(bytes);
    }

    public string DecryptString(string ciphertext)
    {
        var bytes = DecryptBytes(ciphertext);
        return Encoding.UTF8.GetString(bytes);
    }

    public string EncryptBinaryBase64(string binaryBase64)
    {
        var bytes = string.IsNullOrWhiteSpace(binaryBase64) ? [] : Convert.FromBase64String(binaryBase64);
        return EncryptBytes(bytes);
    }

    public string DecryptBinaryBase64(string ciphertext)
    {
        var bytes = DecryptBytes(ciphertext);
        return bytes.Length == 0 ? string.Empty : Convert.ToBase64String(bytes);
    }

    public string EncryptLabels(IReadOnlyList<ConfigLabel> labels)
    {
        var json = JsonSerializer.Serialize(labels ?? [], JsonOptions);
        return EncryptString(json);
    }

    public List<ConfigLabel> DecryptLabels(string? ciphertext)
    {
        if (string.IsNullOrWhiteSpace(ciphertext)) return [];

        var json = DecryptString(ciphertext);
        return JsonSerializer.Deserialize<List<ConfigLabel>>(json, JsonOptions) ?? [];
    }

    private string EncryptBytes(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ciphertext = new byte[plaintext.Length];

        using var aes = new AesGcm(_key.Value, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);
        return Prefix + Convert.ToBase64String(payload);
    }

    private byte[] DecryptBytes(string ciphertext)
    {
        if (string.IsNullOrWhiteSpace(ciphertext) || !ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Secret payload is not in the expected encrypted format.");

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(ciphertext[Prefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Secret payload is not valid encrypted data.", ex);
        }

        if (payload.Length < 28) throw new InvalidOperationException("Secret payload is truncated.");

        var nonce = payload[..12];
        var tag = payload[12..28];
        var encrypted = payload[28..];
        var plaintext = new byte[encrypted.Length];

        using var aes = new AesGcm(_key.Value, tag.Length);
        try
        {
            aes.Decrypt(nonce, encrypted, tag, plaintext);
            return plaintext;
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "Secret payload could not be decrypted with the configured Config:Secrets:EncryptionKey.", ex);
        }
    }

    private static byte[] ResolveKey(string configuredKey)
    {
        if (string.IsNullOrWhiteSpace(configuredKey))
            throw new InvalidOperationException("Config:Secrets:EncryptionKey is required for secret config entries.");

        var trimmed = configuredKey.Trim();
        try
        {
            var key = Convert.FromBase64String(trimmed);
            if (key.Length == 32) return key;
        }
        catch (FormatException)
        {
            // Fall back to deriving a stable 32-byte key from an arbitrary string.
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(trimmed));
    }
}