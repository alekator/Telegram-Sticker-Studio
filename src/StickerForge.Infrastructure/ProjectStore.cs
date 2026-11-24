using System.Text.Json;
using StickerForge.Core;

namespace StickerForge.Infrastructure;

public sealed class ProjectStore
{
    private sealed record FileModel(int SchemaVersion, string Name, List<ItemModel> Stickers);
    private sealed record ItemModel(Guid Id, string SourcePath, TransformSettings? Transform = null, List<TextLayer>? TextLayers = null);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public async Task SaveAsync(StickerProject project, string path, CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(path)!;
        var model = new FileModel(StickerProject.CurrentSchemaVersion, project.Name,
            project.Stickers.Select(s => new ItemModel(s.Id, Path.GetRelativePath(directory, Path.GetFullPath(s.SourcePath)), s.Transform, s.TextLayers)).ToList());
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
        if (model.SchemaVersion != StickerProject.CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported project schema: {model.SchemaVersion}.");
        if (model.Name is null || model.Stickers is null || model.Stickers.Any(s => s is null || string.IsNullOrWhiteSpace(s.SourcePath)))
            throw new InvalidDataException("Project file is incomplete.");
        var directory = Path.GetDirectoryName(path)!;
        return new StickerProject
        {
            Name = model.Name,
            Stickers = model.Stickers.Select(s => new StickerItem { Id = s.Id, SourcePath = Path.GetFullPath(s.SourcePath, directory), Transform = s.Transform ?? new(), TextLayers = s.TextLayers ?? [] }).ToList()
        };
    }
}
