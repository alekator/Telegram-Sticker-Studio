namespace StickerForge.Telegram;

public static class LogoutService
{
    /// <summary>Preserve local credentials if server-side revocation fails.</summary>
    public static async Task RevokeAndRemoveAsync(Func<Task> revoke, Action release, string sessionPath, string vaultPath)
    {
        await revoke();
        release();
        if (File.Exists(sessionPath)) File.Delete(sessionPath);
        if (File.Exists(vaultPath)) File.Delete(vaultPath);
    }
}
