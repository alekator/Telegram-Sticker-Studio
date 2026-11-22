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

public sealed class TelegramPublication(ITelegramStickerApi api, PublicationJournal journal)
{
    public async Task<Uri> PublishAsync(string title, string shortName, IReadOnlyList<PreparedSticker> stickers,
        IProgress<PublishProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 64) throw new ArgumentException("Title must be 1–64 characters.", nameof(title));
        if (string.IsNullOrWhiteSpace(shortName) || shortName.Length > 64 || !char.IsAsciiLetter(shortName[0]) ||
            shortName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_') || shortName.Contains("__", StringComparison.Ordinal))
            throw new ArgumentException("Invalid short name.", nameof(shortName));
        if (stickers.Count is < 1 or > 120 || stickers.Any(s => s.Webp.Length is 0 or > StaticStickerPreparer.MaximumBytes || string.IsNullOrWhiteSpace(s.Emoji)))
            throw new ArgumentException("Provide 1–120 prepared stickers, each with emoji.", nameof(stickers));
        cancellationToken.ThrowIfCancellationRequested();
        using var operationLock = journal.Acquire(shortName);
        if (await journal.ExistsAsync(shortName, cancellationToken)) throw new InvalidOperationException("A previous publication needs reconciliation before retrying.");
        if (!await api.IsShortNameAvailableAsync(shortName, cancellationToken)) throw new InvalidOperationException("The short name is unavailable.");
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
            var set = await api.GetSetAsync(shortName, cancellationToken);
            if (!set.Owned || !set.DocumentIds.Order().SequenceEqual(uploaded.Select(x => x.Id).Order()))
                throw new InvalidOperationException("The server did not confirm the expected owned sticker set.");
        }
        catch (Exception ex) { throw new PublicationUncertainException(ex); }
        progress?.Report(new(stickers.Count, stickers.Count, "confirmed"));
        return new Uri($"https://t.me/addstickers/{shortName}");
    }

    public async Task<Uri> ReconcileAsync(string shortName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var operationLock = journal.Acquire(shortName);
        var record = await journal.ReadAsync(shortName, cancellationToken);
        var expected = record.Stickers.Select(s => s.Id).ToArray();
        if (expected.Distinct().Count() != expected.Length) throw new InvalidDataException("Duplicate document identities in journal.");
        foreach (var sticker in record.Stickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var set = await api.GetSetAsync(shortName, cancellationToken);
            if (!set.Owned || set.DocumentIds.Length == 0 || set.DocumentIds.Distinct().Count() != set.DocumentIds.Length || set.DocumentIds.Except(expected).Any())
                throw new InvalidOperationException("The server set cannot be matched safely; resolve it manually.");
            if (set.DocumentIds.Contains(sticker.Id)) continue;
            // If this RPC fails, stop. A later attempt must fetch the set again before adding.
            try { await api.AddStickerAsync(shortName, sticker, cancellationToken); }
            catch (Exception ex) { throw new PublicationUncertainException(ex); }
        }
        var confirmed = await api.GetSetAsync(shortName, cancellationToken);
        if (!confirmed.Owned || !confirmed.DocumentIds.Order().SequenceEqual(expected.Order()))
            throw new InvalidOperationException("The server did not confirm all expected stickers.");
        return new Uri($"https://t.me/addstickers/{shortName}");
    }
}
