using TL;

namespace StickerForge.Telegram;

/// <summary>MTProto adapter. Uploaded document handles live only for this client session.</summary>
public sealed class WTelegramStickerApi(TelegramLogin login) : ITelegramStickerApi
{
    private readonly Dictionary<string, InputDocument> documents = [];

    public async Task<bool> IsShortNameAvailableAsync(string shortName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var available = await login.Client.Stickers_CheckShortName(shortName);
        cancellationToken.ThrowIfCancellationRequested();
        return available;
    }

    public async Task<string> UploadAsync(byte[] png, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(png, writable: false);
        var file = await login.Client.UploadFileAsync(stream, "sticker.png");
        cancellationToken.ThrowIfCancellationRequested();
        var media = await login.Client.Messages_UploadMedia(new InputPeerSelf(), new InputMediaUploadedDocument
        {
            file = file, mime_type = "image/png", attributes = [new DocumentAttributeFilename { file_name = "sticker.png" }]
        });
        cancellationToken.ThrowIfCancellationRequested();
        if (media is not MessageMediaDocument { document: Document document }) throw new InvalidDataException("Telegram did not return an uploaded document.");
        var key = Guid.NewGuid().ToString("N");
        documents.Add(key, new InputDocument { id = document.id, access_hash = document.access_hash, file_reference = document.file_reference });
        return key;
    }

    public async Task CreateSetAsync(string title, string shortName, IReadOnlyList<(string Document, string Emoji)> stickers, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = stickers.Select(s => new InputStickerSetItem { document = documents[s.Document], emoji = s.Emoji }).ToArray();
        await login.Client.Stickers_CreateStickerSet(new InputUserSelf(), title, shortName, items);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task<bool> IsOwnedSetAsync(string shortName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await login.Client.Messages_GetStickerSet(new InputStickerSetShortName { short_name = shortName }, 0);
        cancellationToken.ThrowIfCancellationRequested();
        return result is Messages_StickerSet set && set.set.flags.HasFlag(StickerSet.Flags.creator);
    }
}
