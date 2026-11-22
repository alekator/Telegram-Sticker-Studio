using System.Text.Json;
using StickerForge.Core;
using System.Security.Cryptography;

namespace StickerForge.Infrastructure;

public sealed class ProjectStore
{
    private sealed record FileModel(int SchemaVersion, string Name, List<ItemModel> Stickers);
    private sealed record ItemModel(Guid Id, string SourcePath, string? EditDocument = null);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public async Task SaveAsync(StickerProject project, string path, CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(path)!;
        var items = new List<ItemModel>();
        foreach (var item in project.Stickers)
        {
            string? relative = null;
            if (item.EditableDocument is { } bytes)
            {
                var assetDirectory = Path.Combine(directory, Path.GetFileName(path) + ".assets");
                Directory.CreateDirectory(assetDirectory);
                var filename = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + ".imagespace";
                var destination = Path.Combine(assetDirectory, filename);
                if (!File.Exists(destination))
                {
                    var assetTemp = Path.Combine(assetDirectory, $".{Guid.NewGuid():N}.tmp");
                    try
                    {
                        await File.WriteAllBytesAsync(assetTemp, bytes, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        try { File.Move(assetTemp, destination, false); }
                        catch (IOException) when (File.Exists(destination)) { }
                    }
                    finally { if (File.Exists(assetTemp)) File.Delete(assetTemp); }
                }
                if (!SHA256.HashData(await File.ReadAllBytesAsync(destination, cancellationToken)).AsSpan().SequenceEqual(SHA256.HashData(bytes)))
                    throw new InvalidDataException("Saved editing document failed integrity verification.");
                relative = Path.GetRelativePath(directory, destination);
            }
            items.Add(new ItemModel(item.Id, Path.GetRelativePath(directory, Path.GetFullPath(item.SourcePath)), relative));
        }
        var model = new FileModel(StickerProject.CurrentSchemaVersion, project.Name, items);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
                await JsonSerializer.SerializeAsync(stream, model, Options, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<StickerProject> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path);
        await using var stream = File.OpenRead(path);
        var model = await JsonSerializer.DeserializeAsync<FileModel>(stream, Options, cancellationToken)
            ?? throw new InvalidDataException("Project file is empty.");
        if (model.SchemaVersion is not (1 or StickerProject.CurrentSchemaVersion))
            throw new InvalidDataException($"Unsupported project schema: {model.SchemaVersion}.");
        if (model.Name is null || model.Stickers is null || model.Stickers.Any(s => s is null || string.IsNullOrWhiteSpace(s.SourcePath)))
            throw new InvalidDataException("Project file is incomplete.");
        var directory = Path.GetDirectoryName(path)!;
        var result = new StickerProject
        {
            Name = model.Name,
            Stickers = model.Stickers.Select(s => new StickerItem { Id = s.Id, SourcePath = Path.GetFullPath(s.SourcePath, directory) }).ToList()
        };
        for (var i = 0; i < model.Stickers.Count; i++)
            if (model.Stickers[i].EditDocument is { } edit)
                result.Stickers[i].EditableDocument = await File.ReadAllBytesAsync(Path.GetFullPath(edit, directory), cancellationToken);
        return result;
    }
}
