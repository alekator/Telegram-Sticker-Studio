using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace StickerForge.Telegram;

public sealed record UploadedSticker(long Id, long AccessHash, byte[] FileReference, string Emoji)
{
    public override string ToString() => "UploadedSticker [redacted]";
}
public sealed record PublicationRecord(long AccountId, string ShortName, UploadedSticker[] Stickers);

/// <summary>Encrypted, account-bound write-once reservation made before a create RPC.</summary>
public sealed class PublicationJournal(string directory, long accountId, string sessionKey)
{
    private readonly byte[] key = DeriveKey(sessionKey);
    private static byte[] DeriveKey(string sessionKey)
    {
        var bytes = Convert.FromHexString(sessionKey);
        try
        {
            if (bytes.Length != 32) throw new ArgumentException("A 256-bit journal key is required.", nameof(sessionKey));
            return HMACSHA256.HashData(bytes, "StickerForge publication journal v1"u8);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private string PathFor(string shortName)
    {
        if (string.IsNullOrEmpty(shortName) || shortName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new ArgumentException("Invalid short name.");
        if (accountId <= 0) throw new InvalidOperationException("An authenticated account is required.");
        return Path.Combine(directory, accountId.ToString(System.Globalization.CultureInfo.InvariantCulture), shortName.ToLowerInvariant() + ".journal");
    }
    public Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = PathFor(name);
        // Earlier revisions stored unscoped JSON. Never ignore an unresolved old reservation.
        var legacy = Path.Combine(directory, name.ToLowerInvariant() + ".json");
        return Task.FromResult(File.Exists(path) || File.Exists(path + ".done") || File.Exists(legacy));
    }
    public FileStream Acquire(string name)
    {
        var path = PathFor(name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Hold across network operations to prevent concurrent recovery from issuing duplicate adds.
        return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public async Task ReserveAsync(string name, IReadOnlyList<UploadedSticker> stickers, CancellationToken cancellationToken = default)
    {
        var path = PathFor(name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new PublicationRecord(accountId, name.ToLowerInvariant(), stickers.ToArray()));
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var cipher = new byte[plaintext.Length];
            using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plaintext, cipher, tag, Encoding.UTF8.GetBytes($"{accountId}:{name.ToLowerInvariant()}"));
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
            await file.WriteAsync("SFJ1"u8.ToArray(), cancellationToken); await file.WriteAsync(nonce, cancellationToken);
            await file.WriteAsync(tag, cancellationToken); await file.WriteAsync(cipher, cancellationToken);
            await file.FlushAsync(cancellationToken); file.Flush(true);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    public async Task<PublicationRecord> ReadAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var file = File.OpenRead(PathFor(name));
        if (file.Length is < 33 or > 1024 * 1024) throw new InvalidDataException("The publication journal is incomplete.");
        var data = new byte[(int)file.Length]; await file.ReadExactlyAsync(data, cancellationToken);
        if (!data.AsSpan(0, 4).SequenceEqual("SFJ1"u8)) throw new InvalidDataException("Unsupported publication journal.");
        var plaintext = new byte[data.Length - 32];
        try
        {
            using (var aes = new AesGcm(key, 16)) aes.Decrypt(data.AsSpan(4, 12), data.AsSpan(32), data.AsSpan(16, 16), plaintext, Encoding.UTF8.GetBytes($"{accountId}:{name.ToLowerInvariant()}"));
            var record = JsonSerializer.Deserialize<PublicationRecord>(plaintext) ?? throw new InvalidDataException("The publication journal is incomplete.");
            if (record.AccountId != accountId || !string.Equals(record.ShortName, name, StringComparison.OrdinalIgnoreCase) || record.Stickers is null || record.Stickers.Length == 0)
                throw new InvalidDataException("The publication journal does not match this account or pack.");
            return record;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    /// <summary>Keep a non-secret tombstone before removing confirmed document references.</summary>
    public void Complete(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path)) throw new InvalidOperationException("The publication journal is unavailable.");
        var done = path + ".done";
        try
        {
            using var marker = new FileStream(done, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough);
            marker.WriteByte(1); marker.Flush(true);
        }
        catch (IOException) when (File.Exists(done)) { }
        File.Delete(path);
    }
    public long? ReadPendingAdd(string name)
    {
        var path = PathFor(name) + ".pending";
        if (!File.Exists(path)) return null;
        var text = File.ReadAllText(path);
        if (!long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new InvalidDataException("Pending addition state is incomplete; manual reconciliation is required.");
        return id;
    }
    public void MarkPendingAdd(string name, long documentId)
    {
        var path = PathFor(name) + ".pending";
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 32, FileOptions.WriteThrough);
        var bytes = Encoding.ASCII.GetBytes(documentId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        file.Write(bytes); file.Flush(true);
    }
    public void ClearPendingAdd(string name) => File.Delete(PathFor(name) + ".pending");
}
