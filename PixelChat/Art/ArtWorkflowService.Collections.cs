using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using PixelChat.Models;

namespace PixelChat.Art;

public sealed partial class ArtWorkflowService
{
    public async Task<AssetCollectionView> CreateCollectionAsync(Guid projectId, string name, CancellationToken cancellationToken = default)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        var normalized = NormalizeCollectionName(name);
        await EnsureCollectionNameAvailableAsync(projectId, normalized, null, cancellationToken);
        var collection = new AssetCollection { ProjectId = projectId, Name = normalized };
        await db.AssetCollections.AddAsync(collection, cancellationToken);
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new AssetCollectionView(collection.Id, collection.Name, 0);
    }

    public async Task<AssetCollectionView> RenameCollectionAsync(Guid projectId, Guid collectionId, string name, CancellationToken cancellationToken = default)
    {
        var collection = await FindCollectionAsync(projectId, collectionId, cancellationToken);
        var normalized = NormalizeCollectionName(name);
        await EnsureCollectionNameAvailableAsync(projectId, normalized, collectionId, cancellationToken);
        collection.Name = normalized;
        collection.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await GetCollectionForExportAsync(projectId, collectionId, cancellationToken);
    }

    public async Task DeleteCollectionAsync(Guid projectId, Guid collectionId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.AssetCollections.AnyAsync(c => c.ProjectId == projectId && c.Id == collectionId, cancellationToken))
            throw new InvalidOperationException("Collection was not found.");
        var now = DateTime.UtcNow;
        await db.ArtAssets.Where(a => a.ProjectId == projectId && a.CollectionId == collectionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.CollectionId, (Guid?)null)
                .SetProperty(a => a.UpdatedAt, now), cancellationToken);
        await db.AssetCollections.Where(c => c.ProjectId == projectId && c.Id == collectionId)
            .ExecuteDeleteAsync(cancellationToken);
        (await GetProjectAsync(projectId, cancellationToken)).UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MoveAssetsToCollectionAsync(Guid projectId, IReadOnlyList<Guid> assetIds, Guid? collectionId, CancellationToken cancellationToken = default)
    {
        var ids = assetIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
            return;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (collectionId is Guid id)
            _ = await FindCollectionAsync(projectId, id, cancellationToken);
        var assets = await db.ArtAssets.AsNoTracking()
            .Where(a => a.ProjectId == projectId && ids.Contains(a.Id))
            .Select(a => new { a.Id, a.ReviewStatus })
            .ToListAsync(cancellationToken);
        if (assets.Count != ids.Count || assets.Any(a => a.ReviewStatus != AssetReviewStatus.Kept))
            throw new InvalidOperationException("Only kept assets in this project can be moved to a collection.");
        var now = DateTime.UtcNow;
        var moved = await db.ArtAssets.Where(a => a.ProjectId == projectId && ids.Contains(a.Id) && a.ReviewStatus == AssetReviewStatus.Kept)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.CollectionId, collectionId)
                .SetProperty(a => a.UpdatedAt, now), cancellationToken);
        if (moved != ids.Count)
            throw new InvalidOperationException("Assets changed during the move. Refresh and try again.");
        (await GetProjectAsync(projectId, cancellationToken)).UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<AssetCollectionView> GetCollectionForExportAsync(Guid projectId, Guid collectionId, CancellationToken cancellationToken = default)
    {
        var collection = await db.AssetCollections.AsNoTracking()
            .Where(c => c.ProjectId == projectId && c.Id == collectionId)
            .Select(c => new AssetCollectionView(c.Id, c.Name, c.Assets.Count(a => a.ReviewStatus == AssetReviewStatus.Kept)))
            .FirstOrDefaultAsync(cancellationToken);
        return collection ?? throw new InvalidOperationException("Collection was not found.");
    }

    public async Task WriteCollectionZipAsync(Guid projectId, Guid collectionId, Stream destination, CancellationToken cancellationToken = default)
    {
        var collection = await GetCollectionForExportAsync(projectId, collectionId, cancellationToken);
        if (collection.AssetCount == 0)
            throw new InvalidOperationException("Collection is empty.");

        var members = await db.ArtAssets.AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.CollectionId == collectionId && a.ReviewStatus == AssetReviewStatus.Kept)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.Label, a.FileName, a.ContentType })
            .ToListAsync(cancellationToken);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tempPath = Path.Combine(Path.GetTempPath(), $"pixelchat-collection-{Guid.NewGuid():N}.zip");
        await using var archiveFile = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            64 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        using (var archive = new ZipArchive(archiveFile, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var member in members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var data = await db.ArtAssets.AsNoTracking()
                    .Where(a => a.ProjectId == projectId && a.Id == member.Id && a.CollectionId == collectionId && a.ReviewStatus == AssetReviewStatus.Kept)
                    .Select(a => a.Data)
                    .FirstOrDefaultAsync(cancellationToken);
                if (data is null)
                    throw new InvalidOperationException("Collection changed during export. Try again.");
                var name = UniqueArchiveName(member.FileName, member.Label, member.ContentType, usedNames);
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(data, cancellationToken);
            }
        }
        archiveFile.Position = 0;
        await archiveFile.CopyToAsync(destination, cancellationToken);
    }

    private static string UniqueArchiveName(string fileName, string label, string contentType, HashSet<string> usedNames)
    {
        var stem = AssetOutputNaming.SafeStem(Path.GetFileNameWithoutExtension(fileName), AssetOutputNaming.SafeStem(label, "asset"));
        var originalExtension = Path.GetExtension(fileName);
        var extension = contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
            ? originalExtension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpeg" : ".jpg"
            : ".png";
        var candidate = stem + extension;
        for (var suffix = 2; !usedNames.Add(candidate); suffix++)
            candidate = $"{stem}-{suffix}{extension}";
        return candidate;
    }

    private async Task<AssetCollection> FindCollectionAsync(Guid projectId, Guid collectionId, CancellationToken cancellationToken) =>
        await db.AssetCollections.FirstOrDefaultAsync(c => c.ProjectId == projectId && c.Id == collectionId, cancellationToken)
        ?? throw new InvalidOperationException("Collection was not found.");

    private async Task EnsureCollectionNameAvailableAsync(Guid projectId, string name, Guid? excludeId, CancellationToken cancellationToken)
    {
        if (await db.AssetCollections.AnyAsync(c => c.ProjectId == projectId && c.Id != excludeId && c.Name == name, cancellationToken))
            throw new InvalidOperationException("A collection with that name already exists in this project.");
    }

    private static string NormalizeCollectionName(string name)
    {
        var normalized = name.Trim();
        if (normalized.Length is < 1 or > 80)
            throw new InvalidOperationException("Collection name must be 1–80 characters.");
        return normalized;
    }
}
