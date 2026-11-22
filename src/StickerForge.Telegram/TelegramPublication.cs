namespace StickerForge.Telegram;

public sealed record PreparedSticker(byte[] Png, string Emoji);
public sealed record PublishProgress(int Completed, int Total, string Stage);

/// <summary>Adapter boundary: production implementation must authenticate the current user.</summary>
public interface ITelegramStickerApi
{
    Task<bool> IsShortNameAvailableAsync(string shortName, CancellationToken cancellationToken);
    Task<string> UploadAsync(byte[] png, CancellationToken cancellationToken);
    Task CreateSetAsync(string title, string shortName, IReadOnlyList<(string Document, string Emoji)> stickers, CancellationToken cancellationToken);
    Task<bool> IsOwnedSetAsync(string shortName, CancellationToken cancellationToken);
}

public sealed class PublicationUncertainException(Exception inner) : Exception(
    "Telegram may have created the set. Check the account before retrying; do not create it again automatically.", inner);

public sealed class TelegramPublication(ITelegramStickerApi api)
{
    public async Task<Uri> PublishAsync(string title, string shortName, IReadOnlyList<PreparedSticker> stickers,
        IProgress<PublishProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 64) throw new ArgumentException("Title must be 1–64 characters.", nameof(title));
        if (string.IsNullOrWhiteSpace(shortName) || shortName.Length > 64 || !char.IsAsciiLetter(shortName[0]) ||
            shortName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_') || shortName.Contains("__", StringComparison.Ordinal))
            throw new ArgumentException("Invalid short name.", nameof(shortName));
        if (stickers.Count is < 1 or > 120 || stickers.Any(s => s.Png.Length is 0 or > StaticStickerPreparer.MaximumBytes || string.IsNullOrWhiteSpace(s.Emoji)))
            throw new ArgumentException("Provide 1–120 prepared stickers, each with emoji.", nameof(stickers));
        cancellationToken.ThrowIfCancellationRequested();
        if (!await api.IsShortNameAvailableAsync(shortName, cancellationToken)) throw new InvalidOperationException("The short name is unavailable.");
        var uploaded = new List<(string Document, string Emoji)>();
        foreach (var sticker in stickers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uploaded.Add((await api.UploadAsync(sticker.Png, cancellationToken), sticker.Emoji));
            progress?.Report(new(uploaded.Count, stickers.Count, "upload"));
        }
        cancellationToken.ThrowIfCancellationRequested();
        // Once sent, cancellation or a lost response cannot prove the server did not create the set.
        try { await api.CreateSetAsync(title, shortName, uploaded, cancellationToken); }
        catch (Exception ex) { throw new PublicationUncertainException(ex); }
        try
        {
            if (!await api.IsOwnedSetAsync(shortName, cancellationToken))
                throw new InvalidOperationException("The server did not confirm the owned sticker set.");
        }
        catch (Exception ex) { throw new PublicationUncertainException(ex); }
        progress?.Report(new(stickers.Count, stickers.Count, "confirmed"));
        return new Uri($"https://t.me/addstickers/{shortName}");
    }
}
