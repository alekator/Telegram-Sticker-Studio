using System.Security.Cryptography;
using StickerForge.Telegram;

namespace StickerForge.Tests;

public class PasswordVaultTests
{
    [Fact]
    public async Task VaultRoundTripRejectsWrongPasswordAndTampering()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".vault");
        try
        {
            var credentials = new TelegramCredentials(123, "hash-secret", "session-secret");
            Assert.DoesNotContain("secret", credentials.ToString());
            await PasswordVault.SaveAsync(path, credentials, "correct password");
            Assert.Equal(credentials, await PasswordVault.LoadAsync(path, "correct password"));
            await Assert.ThrowsAsync<IOException>(() => PasswordVault.SaveAsync(path, new TelegramCredentials(999, "other", "other"), "correct password"));
            Assert.Equal(credentials, await PasswordVault.LoadAsync(path, "correct password"));
            Assert.DoesNotContain("session-secret", System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path)));
            await Assert.ThrowsAnyAsync<CryptographicException>(() => PasswordVault.LoadAsync(path, "wrong password"));
            var bytes = await File.ReadAllBytesAsync(path); bytes[^1] ^= 1; await File.WriteAllBytesAsync(path, bytes);
            await Assert.ThrowsAnyAsync<CryptographicException>(() => PasswordVault.LoadAsync(path, "correct password"));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task RejectsOversizedVault()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".vault");
        try
        {
            await File.WriteAllBytesAsync(path, new byte[65537]);
            await Assert.ThrowsAsync<InvalidDataException>(() => PasswordVault.LoadAsync(path, "any password"));
        }
        finally { File.Delete(path); }
    }
}
