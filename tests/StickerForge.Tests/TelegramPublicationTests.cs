using StickerForge.Telegram;

namespace StickerForge.Tests;

public sealed class TelegramPublicationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sf-publish-" + Guid.NewGuid().ToString("N"));
    private TelegramPublication Publisher(ITelegramStickerApi api) => new(api, new PublicationJournal(directory));
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    private sealed class FakeApi : ITelegramStickerApi
    {
        public bool Available = true, Owned = true, FailCreate;
        public int Uploads, Creates;
        public string[] ServerIds = [];
        public CancellationTokenSource? CancelAfterUpload;
        public Task<bool> IsShortNameAvailableAsync(string name, CancellationToken ct) => Task.FromResult(Available);
        public Task<string> UploadAsync(byte[] png, CancellationToken ct) { ct.ThrowIfCancellationRequested(); var id = $"doc-{++Uploads}"; CancelAfterUpload?.Cancel(); return Task.FromResult(id); }
        public Task CreateSetAsync(string title, string name, IReadOnlyList<(string Document, string Emoji)> stickers, CancellationToken ct)
        { Creates++; ServerIds = stickers.Select(s => s.Document).ToArray(); if (FailCreate) throw new IOException("connection lost"); return Task.CompletedTask; }
        public Task<bool> MatchesOwnedSetAsync(string name, IReadOnlyList<string> ids, CancellationToken ct) => Task.FromResult(Owned && ServerIds.Order().SequenceEqual(ids.Order()));
    }
    private static PreparedSticker[] Stickers() => [new([1, 2, 3], "😀"), new([4, 5], "🎉")];

    [Fact]
    public async Task ReturnsLinkOnlyAfterServerConfirmation()
    {
        var api = new FakeApi(); var result = await Publisher(api).PublishAsync("Pack", "pack_name", Stickers());
        Assert.Equal("https://t.me/addstickers/pack_name", result.ToString()); Assert.Equal(2, api.Uploads); Assert.Equal(1, api.Creates);
    }
    [Fact]
    public async Task DoesNotRetryUncertainCreateOrClaimSuccess()
    {
        var api = new FakeApi { FailCreate = true };
        await Assert.ThrowsAsync<PublicationUncertainException>(() => Publisher(api).PublishAsync("Pack", "pack_name", Stickers()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Publisher(api).PublishAsync("Pack", "pack_name", Stickers()));
        Assert.Equal(1, api.Creates);
        Assert.Equal("https://t.me/addstickers/pack_name", (await Publisher(api).ReconcileAsync("pack_name")).ToString());
        api.ServerIds = ["different"];
        await Assert.ThrowsAsync<InvalidOperationException>(() => Publisher(api).ReconcileAsync("pack_name"));
        api.ServerIds = ["doc-1", "doc-2"]; api.Owned = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Publisher(api).ReconcileAsync("pack_name"));
    }
    [Fact]
    public async Task RejectsTakenNameAndCancellationBeforeUpload()
    {
        var api = new FakeApi { Available = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Publisher(api).PublishAsync("Pack", "pack_name", Stickers()));
        Assert.Equal(0, api.Uploads);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Publisher(api).PublishAsync("Pack", "pack_name", Stickers(), cancellationToken: cancelled.Token));
        Assert.Equal(0, api.Creates);
    }
    [Fact]
    public async Task CancellationDuringUploadsDoesNotCreateOrReserve()
    {
        using var cancelled = new CancellationTokenSource();
        var api = new FakeApi { CancelAfterUpload = cancelled };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Publisher(api).PublishAsync("Pack", "pack_name", Stickers(), cancellationToken: cancelled.Token));
        Assert.Equal(1, api.Uploads); Assert.Equal(0, api.Creates);
        Assert.False(await new PublicationJournal(directory).ExistsAsync("pack_name"));
    }
}
