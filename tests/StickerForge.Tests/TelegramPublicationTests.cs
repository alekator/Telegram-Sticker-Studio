using StickerForge.Telegram;

namespace StickerForge.Tests;

public sealed class TelegramPublicationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sf-publish-" + Guid.NewGuid().ToString("N"));
    private const string Key = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private TelegramPublication Publisher(ITelegramStickerApi api) => new(api, new PublicationJournal(directory, 123, Key));
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    private sealed class FakeApi : ITelegramStickerApi
    {
        public bool Available = true, Owned = true, FailCreate;
        public int Uploads, Creates, Adds;
        public long[] ServerIds = [];
        public bool FailAdd;
        public bool FailUpload;
        public CancellationTokenSource? CancelAfterUpload;
        public Task<bool> IsShortNameAvailableAsync(string name, CancellationToken ct) => Task.FromResult(Available);
        public Task<UploadedSticker> UploadAsync(byte[] png, string emoji, CancellationToken ct) { ct.ThrowIfCancellationRequested(); if (FailUpload) throw new IOException("connection lost"); var doc = new UploadedSticker(++Uploads, 42, System.Text.Encoding.UTF8.GetBytes("secret-reference-for-test"), emoji); CancelAfterUpload?.Cancel(); return Task.FromResult(doc); }
        public Task CreateSetAsync(string title, string name, IReadOnlyList<UploadedSticker> stickers, CancellationToken ct)
        { Creates++; ServerIds = stickers.Select(s => s.Id).ToArray(); if (FailCreate) throw new IOException("connection lost"); return Task.CompletedTask; }
        public Task<RemoteStickerSet> GetSetAsync(string name, CancellationToken ct) => Task.FromResult(new RemoteStickerSet(Owned, ServerIds));
        public Task AddStickerAsync(string name, UploadedSticker sticker, CancellationToken ct) { Adds++; ServerIds = [.. ServerIds, sticker.Id]; if (FailAdd) throw new IOException("connection lost"); return Task.CompletedTask; }
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
        api.ServerIds = [999];
        await Assert.ThrowsAsync<InvalidOperationException>(() => Publisher(api).ReconcileAsync("pack_name"));
        api.ServerIds = [1, 2]; api.Owned = false;
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
        Assert.False(await new PublicationJournal(directory, 123, Key).ExistsAsync("pack_name"));
    }
    [Fact]
    public async Task ResumeAfterRestartAddsOnlyMissingAndNeverRepeatsUncertainAdd()
    {
        var api = new FakeApi { FailCreate = true };
        await Assert.ThrowsAsync<PublicationUncertainException>(() => Publisher(api).PublishAsync("Pack", "pack_name", Stickers()));
        api.ServerIds = [1]; api.FailAdd = true;
        await Assert.ThrowsAsync<PublicationUncertainException>(() => Publisher(api).ReconcileAsync("pack_name"));
        api.FailAdd = false;
        Assert.Equal("https://t.me/addstickers/pack_name", (await Publisher(api).ReconcileAsync("pack_name")).ToString());
        Assert.Equal(1, api.Adds); Assert.Equal(new long[] { 1, 2 }, api.ServerIds);
        Assert.False(await new PublicationJournal(directory, 456, Key).ExistsAsync("pack_name"));
        var bytes = await File.ReadAllBytesAsync(Directory.GetFiles(directory, "*.journal", SearchOption.AllDirectories).Single());
        Assert.DoesNotContain("secret-reference-for-test", System.Text.Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("AccessHash", System.Text.Encoding.UTF8.GetString(bytes));
    }
    [Fact]
    public async Task LegacyUnscopedReservationBlocksNewCreate()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "pack_name.json"), "[\"old-id\"]");
        var api = new FakeApi();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Publisher(api).PublishAsync("Pack", "pack_name", Stickers()));
        Assert.Equal(0, api.Uploads); Assert.Equal(0, api.Creates);
    }
    [Fact]
    public async Task LockedPackCannotBePublishedConcurrently()
    {
        using var held = new PublicationJournal(directory, 123, Key).Acquire("pack_name");
        var api = new FakeApi();
        await Assert.ThrowsAsync<IOException>(() => Publisher(api).PublishAsync("Pack", "pack_name", Stickers()));
        Assert.Equal(0, api.Uploads); Assert.Equal(0, api.Creates);
    }
    [Fact]
    public async Task DisconnectBeforeCreateDoesNotReserveOrCreate()
    {
        var api = new FakeApi { FailUpload = true };
        await Assert.ThrowsAsync<IOException>(() => Publisher(api).PublishAsync("Pack", "pack_name", Stickers()));
        Assert.Equal(0, api.Creates);
        Assert.False(await new PublicationJournal(directory, 123, Key).ExistsAsync("pack_name"));
    }
    [Fact]
    public async Task EmptyOrUnexpectedServerSetIsNeverModified()
    {
        var api = new FakeApi { FailCreate = true };
        await Assert.ThrowsAsync<PublicationUncertainException>(() => Publisher(api).PublishAsync("Pack", "pack_name", Stickers()));
        api.ServerIds = [];
        await Assert.ThrowsAsync<InvalidOperationException>(() => Publisher(api).ReconcileAsync("pack_name"));
        api.ServerIds = [1, 999];
        await Assert.ThrowsAsync<InvalidOperationException>(() => Publisher(api).ReconcileAsync("pack_name"));
        Assert.Equal(0, api.Adds);
    }
    [Fact]
    public async Task TamperedJournalCannotBeUsedToModifyPack()
    {
        var api = new FakeApi { FailCreate = true };
        await Assert.ThrowsAsync<PublicationUncertainException>(() => Publisher(api).PublishAsync("Pack", "pack_name", Stickers()));
        var path = Directory.GetFiles(directory, "*.journal", SearchOption.AllDirectories).Single();
        var bytes = await File.ReadAllBytesAsync(path); bytes[^1] ^= 1; await File.WriteAllBytesAsync(path, bytes);
        await Assert.ThrowsAnyAsync<System.Security.Cryptography.CryptographicException>(() => Publisher(api).ReconcileAsync("pack_name"));
        Assert.Equal(0, api.Adds);
    }
}
