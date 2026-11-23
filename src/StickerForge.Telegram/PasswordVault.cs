using System.Security.Cryptography;
using System.Text.Json;

namespace StickerForge.Telegram;

public sealed record TelegramCredentials(int ApiId, string ApiHash, string SessionKey)
{
    public override string ToString() => "TelegramCredentials [redacted]";
}

/// <summary>Password-protected local credentials, independent of OS key-store availability.</summary>
public static class PasswordVault
{
    private static readonly byte[] Magic = "SFV1"u8.ToArray();
    private const int Iterations = 600_000;

    public static async Task SaveAsync(string path, TelegramCredentials credentials, string password, CancellationToken cancellationToken = default)
    {
        if (password.Length < 12) throw new ArgumentException("The local vault password must have at least 12 characters.");
        var salt = RandomNumberGenerator.GetBytes(16); var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(credentials);
        try
        {
            var encrypted = new byte[plaintext.Length]; var tag = new byte[16];
            using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plaintext, encrypted, tag, Magic);
            var data = new byte[48 + encrypted.Length];
            Magic.CopyTo(data, 0); salt.CopyTo(data, 4); nonce.CopyTo(data, 20); tag.CopyTo(data, 32); encrypted.CopyTo(data, 48);
            var fullPath = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var temp = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllBytesAsync(temp, data, cancellationToken); cancellationToken.ThrowIfCancellationRequested(); File.Move(temp, fullPath, false); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plaintext); }
    }

    public static async Task<TelegramCredentials> LoadAsync(string path, string password, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (stream.Length is < 49 or > 64 * 1024) throw new InvalidDataException("Invalid vault file.");
        var data = new byte[(int)stream.Length]; await stream.ReadExactlyAsync(data, cancellationToken);
        if (!data.AsSpan(0, 4).SequenceEqual(Magic)) throw new InvalidDataException("Invalid vault file.");
        var key = Rfc2898DeriveBytes.Pbkdf2(password, data.AsSpan(4, 16), Iterations, HashAlgorithmName.SHA256, 32);
        var plaintext = new byte[data.Length - 48];
        try
        {
            using (var aes = new AesGcm(key, 16)) aes.Decrypt(data.AsSpan(20, 12), data.AsSpan(48), data.AsSpan(32, 16), plaintext, Magic);
            return JsonSerializer.Deserialize<TelegramCredentials>(plaintext) ?? throw new InvalidDataException("Invalid vault contents.");
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plaintext); }
    }
}
