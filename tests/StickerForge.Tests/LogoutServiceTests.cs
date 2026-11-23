using StickerForge.Telegram;

namespace StickerForge.Tests;

public sealed class LogoutServiceTests
{
    [Fact]
    public async Task FailedRevocationPreservesLocalFiles()
    {
        var session = Path.GetTempFileName(); var vault = Path.GetTempFileName(); var released = false;
        try
        {
            await Assert.ThrowsAsync<IOException>(() => LogoutService.RevokeAndRemoveAsync(() => throw new IOException("offline"), () => released = true, session, vault));
            Assert.False(released); Assert.True(File.Exists(session)); Assert.True(File.Exists(vault));
            await LogoutService.RevokeAndRemoveAsync(() => Task.CompletedTask, () => released = true, session, vault);
            Assert.True(released); Assert.False(File.Exists(session)); Assert.False(File.Exists(vault));
        }
        finally { File.Delete(session); File.Delete(vault); }
    }
}
