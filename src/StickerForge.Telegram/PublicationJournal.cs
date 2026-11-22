using System.Text.Json;

namespace StickerForge.Telegram;

/// <summary>Durable reservation made before a create RPC; contains document IDs but no credentials.</summary>
public sealed class PublicationJournal(string directory)
{
    private string PathFor(string shortName)
    {
        if (string.IsNullOrEmpty(shortName) || shortName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new ArgumentException("Invalid short name.");
        return Path.Combine(directory, shortName.ToLowerInvariant() + ".json");
    }
    public Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(File.Exists(PathFor(name))); }
    public async Task ReserveAsync(string name, IReadOnlyList<string> ids, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        // CreateNew also rejects concurrent attempts; never replace an unresolved record.
        await using var file = new FileStream(PathFor(name), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(file, ids, cancellationToken: cancellationToken);
        await file.FlushAsync(cancellationToken);
        file.Flush(true);
    }
    public async Task<string[]> ReadAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var file = File.OpenRead(PathFor(name));
        return await JsonSerializer.DeserializeAsync<string[]>(file, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("The publication journal is incomplete.");
    }
}
