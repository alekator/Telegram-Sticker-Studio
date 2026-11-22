using StickerForge.Telegram;

namespace StickerForge.Tests;

public sealed class TelegramPublicationTests
{
    private sealed class FakeApi : ITelegramStickerApi
    {
        public bool Available = true, Owned = true, FailCreate;
        public int Uploads, Creates;
        public Task<bool> IsShortNameAvailableAsync(string name, CancellationToken ct) => Task.FromResult(Available);
        public Task<string> UploadAsync(byte[] png, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult($"doc-{++Uploads}"); }
        public Task CreateSetAsync(string title, string name, IReadOnlyList<(string Document, string Emoji)> stickers, CancellationToken ct)
        { Creates++; if (FailCreate) throw new IOException("connection lost"); return Task.CompletedTask; }
        public Task<bool> IsOwnedSetAsync(string name, CancellationToken ct) => Task.FromResult(Owned);
    }
    private static PreparedSticker[] Stickers() => [new([1, 2, 3], "😀"), new([4, 5], "🎉")];

    [Fact]
    public async Task ReturnsLinkOnlyAfterServerConfirmation()
    {
        var api = new FakeApi(); var result = await new TelegramPublication(api).PublishAsync("Pack", "pack_name", Stickers());
        Assert.Equal("https://t.me/addstickers/pack_name", result.ToString()); Assert.Equal(2, api.Uploads); Assert.Equal(1, api.Creates);
    }
    [Fact]
    public async Task DoesNotRetryUncertainCreateOrClaimSuccess()
    {
        var api = new FakeApi { FailCreate = true };
        await Assert.ThrowsAsync<PublicationUncertainException>(() => new TelegramPublication(api).PublishAsync("Pack", "pack_name", Stickers()));
        Assert.Equal(1, api.Creates);
    }
    [Fact]
    public async Task RejectsTakenNameAndCancellationBeforeUpload()
    {
        var api = new FakeApi { Available = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new TelegramPublication(api).PublishAsync("Pack", "pack_name", Stickers()));
        Assert.Equal(0, api.Uploads);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TelegramPublication(api).PublishAsync("Pack", "pack_name", Stickers(), cancellationToken: cancelled.Token));
        Assert.Equal(0, api.Creates);
    }
}
