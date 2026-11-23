namespace StickerForge.Telegram;

public sealed record PreparedSticker(byte[] Webp, string Emoji);
public sealed record PublishProgress(int Completed, int Total, string Stage);
public sealed record RemoteStickerSet(bool Owned, long[] DocumentIds);

/// <summary>Adapter boundary: production implementation must authenticate the current user.</summary>
public interface ITelegramStickerApi
{
    Task<bool> IsShortNameAvailableAsync(string shortName, CancellationToken cancellationToken);
    Task<UploadedSticker> UploadAsync(byte[] webp, string emoji, CancellationToken cancellationToken);
    Task CreateSetAsync(string title, string shortName, IReadOnlyList<UploadedSticker> stickers, CancellationToken cancellationToken);
    Task<RemoteStickerSet> GetSetAsync(string shortName, CancellationToken cancellationToken);
    Task AddStickerAsync(string shortName, UploadedSticker sticker, CancellationToken cancellationToken);
}

public sealed class PublicationUncertainException(Exception inner) : Exception(
    "Telegram may have completed the operation. Check the set before retrying; do not create it again automatically.", inner);
public sealed class TelegramFloodWaitException(int seconds, Exception? inner = null) : Exception("Telegram requested a rate limit wait.", inner)
{
    public int Seconds { get; } = seconds;
}

public sealed class TelegramPublication(ITelegramStickerApi api, PublicationJournal journal)
{
    private static async Task<T> ReadWithFloodWaitAsync<T>(Func<Task<T>> operation, IProgress<PublishProgress>? progress, CancellationToken cancellationToken)
    {
        try { return await operation(); }
        catch (TelegramFloodWaitException ex) when (ex.Seconds is >= 0 and <= 300)
        {
            for (var remaining = ex.Seconds; remaining > 0; remaining--)
            {
                progress?.Report(new(0, 1, $"Telegram rate limit: {remaining}s"));
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return await operation(); // One retry only; read-only calls have no ambiguous mutation.
        }
    }
    public async Task<Uri> PublishAsync(string title, string shortName, IReadOnlyList<PreparedSticker> stickers,
        IProgress<PublishProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 64) throw new ArgumentException("Title must be 1–64 characters.", nameof(title));
        if (string.IsNullOrWhiteSpace(shortName) || shortName.Length > 64 || !char.IsAsciiLetter(shortName[0]) ||
            shortName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_') || shortName.Contains("__", StringComparison.Ordinal))
            throw new ArgumentException("Invalid short name.", nameof(shortName));
        if (stickers.Count is < 1 or > 120 || stickers.Any(s => s.Webp.Length is 0 or > StaticStickerPreparer.MaximumBytes))
            throw new ArgumentException("Provide 1–120 valid prepared stickers.", nameof(stickers));
        if (stickers.Any(s => !EmojiValidator.IsValid(s.Emoji)))
            throw new ArgumentException("Each sticker needs 1–20 valid Unicode emoji sequences.", nameof(stickers));
        cancellationToken.ThrowIfCancellationRequested();
        using var operationLock = journal.Acquire(shortName);
        if (await journal.ExistsAsync(shortName, cancellationToken)) throw new InvalidOperationException("A previous publication needs reconciliation before retrying.");
        if (!await ReadWithFloodWaitAsync(() => api.IsShortNameAvailableAsync(shortName, cancellationToken), progress, cancellationToken)) throw new InvalidOperationException("The short name is unavailable.");
        var uploaded = new List<UploadedSticker>();
        foreach (var sticker in stickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uploaded.Add(await api.UploadAsync(sticker.Webp, sticker.Emoji, cancellationToken));
            progress?.Report(new(uploaded.Count, stickers.Count, "upload"));
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (uploaded.Select(x => x.Id).Distinct().Count() != uploaded.Count) throw new InvalidOperationException("Telegram returned duplicate document identities; cannot publish safely.");
        await journal.ReserveAsync(shortName, uploaded, cancellationToken);
        // Once sent, cancellation or a lost response cannot prove the server did not create the set.
        try { await api.CreateSetAsync(title, shortName, uploaded, cancellationToken); }
        catch (Exception ex) { throw new PublicationUncertainException(ex); }
        try
        {
            var set = await ReadWithFloodWaitAsync(() => api.GetSetAsync(shortName, cancellationToken), progress, cancellationToken);
            if (!set.Owned || !set.DocumentIds.Order().SequenceEqual(uploaded.Select(x => x.Id).Order()))
                throw new InvalidOperationException("The server did not confirm the expected owned sticker set.");
        }
        catch (Exception ex) { throw new PublicationUncertainException(ex); }
        journal.Complete(shortName);
        progress?.Report(new(stickers.Count, stickers.Count, "confirmed"));
        return new Uri($"https://t.me/addstickers/{shortName}");
    }

    public async Task<Uri> ReconcileAsync(string shortName, CancellationToken cancellationToken = default, IProgress<PublishProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var operationLock = journal.Acquire(shortName);
        var record = await journal.ReadAsync(shortName, cancellationToken);
        var expected = record.Stickers.Select(s => s.Id).ToArray();
        if (expected.Distinct().Count() != expected.Length) throw new InvalidDataException("Duplicate document identities in journal.");
        var pending = journal.ReadPendingAdd(shortName);
        if (pending.HasValue)
        {
            if (!expected.Contains(pending.Value)) throw new InvalidDataException("The pending addition is not in the publication journal.");
            var observed = await ReadWithFloodWaitAsync(() => api.GetSetAsync(shortName, cancellationToken), progress, cancellationToken);
            if (!observed.Owned || observed.DocumentIds.Distinct().Count() != observed.DocumentIds.Length || observed.DocumentIds.Except(expected).Any() || !observed.DocumentIds.Contains(pending.Value))
                throw new InvalidOperationException("The previous add is unresolved. Check Telegram manually; it will not be repeated automatically.");
            journal.MarkAddConfirmed(shortName, pending.Value);
            journal.ClearPendingAdd(shortName);
        }
        foreach (var sticker in record.Stickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var set = await ReadWithFloodWaitAsync(() => api.GetSetAsync(shortName, cancellationToken), progress, cancellationToken);
            if (!set.Owned || set.DocumentIds.Length == 0 || set.DocumentIds.Distinct().Count() != set.DocumentIds.Length || set.DocumentIds.Except(expected).Any())
                throw new InvalidOperationException("The server set cannot be matched safely; resolve it manually.");
            if (set.DocumentIds.Contains(sticker.Id)) continue;
            if (journal.WasAddConfirmed(shortName, sticker.Id))
                throw new InvalidOperationException("A previously confirmed addition is missing. Automatic recovery stopped.");
            // Durable intent prevents a late response causing an automatic duplicate after restart.
            journal.MarkPendingAdd(shortName, sticker.Id);
            try { await api.AddStickerAsync(shortName, sticker, cancellationToken); }
            catch (Exception ex) { throw new PublicationUncertainException(ex); }
            var afterAdd = await ReadWithFloodWaitAsync(() => api.GetSetAsync(shortName, cancellationToken), progress, cancellationToken);
            if (!afterAdd.Owned || afterAdd.DocumentIds.Distinct().Count() != afterAdd.DocumentIds.Length || afterAdd.DocumentIds.Except(expected).Any() || !afterAdd.DocumentIds.Contains(sticker.Id))
                throw new PublicationUncertainException(new InvalidOperationException("The addition has not been confirmed."));
            journal.MarkAddConfirmed(shortName, sticker.Id);
            journal.ClearPendingAdd(shortName);
        }
        var confirmed = await ReadWithFloodWaitAsync(() => api.GetSetAsync(shortName, cancellationToken), progress, cancellationToken);
        if (!confirmed.Owned || !confirmed.DocumentIds.Order().SequenceEqual(expected.Order()))
            throw new InvalidOperationException("The server did not confirm all expected stickers.");
        journal.Complete(shortName);
        return new Uri($"https://t.me/addstickers/{shortName}");
    }
}
