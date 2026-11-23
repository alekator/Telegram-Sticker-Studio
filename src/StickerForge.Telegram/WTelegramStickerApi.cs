using TL;
using SkiaSharp;

namespace StickerForge.Telegram;

/// <summary>MTProto adapter. Uploaded document handles live only for this client session.</summary>
public sealed class WTelegramStickerApi(TelegramLogin login) : ITelegramStickerApi
{
    private static InputDocument ToDocument(UploadedSticker sticker) => new() { id = sticker.Id, access_hash = sticker.AccessHash, file_reference = sticker.FileReference };
    public static DocumentAttribute[] CreateStickerAttributes(byte[] webp, string emoji)
    {
        if (!EmojiValidator.IsValid(emoji)) throw new ArgumentException("Each sticker needs 1–20 valid emoji.", nameof(emoji));
        using var stream = new SKMemoryStream(webp);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Invalid sticker image.");
        var info = codec.Info;
        if (codec.EncodedFormat != SKEncodedImageFormat.Webp || codec.FrameCount > 1 || webp.Length > StaticStickerPreparer.MaximumBytes ||
            info.Width < 1 || info.Height < 1 || Math.Max(info.Width, info.Height) != 512 || Math.Min(info.Width, info.Height) > 512)
            throw new InvalidDataException("Sticker must be a static WebP up to 512 KiB with one side equal to 512 pixels.");
        // WTelegramClient represents inputStickerSetEmpty as null; see TL.InputStickerSet documentation.
        return [new DocumentAttributeSticker { alt = emoji, stickerset = null! }, new DocumentAttributeImageSize { w = info.Width, h = info.Height }, new DocumentAttributeFilename { file_name = "sticker.webp" }];
    }

    public async Task<bool> IsShortNameAvailableAsync(string shortName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool available;
        try { available = await login.Client.Stickers_CheckShortName(shortName); }
        catch (RpcException ex) when (ex.Message.Contains("FLOOD_WAIT", StringComparison.Ordinal) && ex.X >= 0) { throw new TelegramFloodWaitException(ex.X, ex); }
        cancellationToken.ThrowIfCancellationRequested();
        return available;
    }

    public async Task<UploadedSticker> UploadAsync(byte[] png, string emoji, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var attributes = CreateStickerAttributes(png, emoji);
        using var stream = new MemoryStream(png, writable: false);
        var file = await login.Client.UploadFileAsync(stream, "sticker.webp");
        cancellationToken.ThrowIfCancellationRequested();
        var media = await login.Client.Messages_UploadMedia(new InputPeerSelf(), new InputMediaUploadedDocument
        {
            file = file, mime_type = "image/webp", attributes = attributes
        });
        cancellationToken.ThrowIfCancellationRequested();
        if (media is not MessageMediaDocument { document: Document document }) throw new InvalidDataException("Telegram did not return an uploaded document.");
        return new UploadedSticker(document.id, document.access_hash, document.file_reference, emoji);
    }

    public async Task CreateSetAsync(string title, string shortName, IReadOnlyList<UploadedSticker> stickers, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = stickers.Select(s => new InputStickerSetItem { document = ToDocument(s), emoji = s.Emoji }).ToArray();
        await login.Client.Stickers_CreateStickerSet(new InputUserSelf(), title, shortName, items);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task<RemoteStickerSet> GetSetAsync(string shortName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await login.Client.Messages_GetStickerSet(new InputStickerSetShortName { short_name = shortName }, 0);
            cancellationToken.ThrowIfCancellationRequested();
            if (result is not Messages_StickerSet set) throw new InvalidDataException("Telegram did not return the sticker set.");
            return new RemoteStickerSet(set.set.flags.HasFlag(StickerSet.Flags.creator), set.documents.OfType<Document>().Select(d => d.id).ToArray());
        }
        catch (RpcException ex) when (ex.Message.Contains("FLOOD_WAIT", StringComparison.Ordinal) && ex.X >= 0) { throw new TelegramFloodWaitException(ex.X, ex); }
    }
    public async Task AddStickerAsync(string shortName, UploadedSticker sticker, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await login.Client.Stickers_AddStickerToSet(new InputStickerSetShortName { short_name = shortName }, new InputStickerSetItem { document = ToDocument(sticker), emoji = sticker.Emoji });
        cancellationToken.ThrowIfCancellationRequested();
    }
}
