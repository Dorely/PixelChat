using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PixelChat.Models;

namespace PixelChat.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ILogger<AppDbContext> logger) : DbContext(options)
{
    private const int MaxLockedSaveAttempts = 6;

    public DbSet<LlmProvider> LlmProviders => Set<LlmProvider>();
    public DbSet<OAuthToken> OAuthTokens => Set<OAuthToken>();
    public DbSet<StoredSecret> StoredSecrets => Set<StoredSecret>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ArtAsset> ArtAssets => Set<ArtAsset>();
    public DbSet<BackgroundRemovalExportCache> BackgroundRemovalExportCaches => Set<BackgroundRemovalExportCache>();
    public DbSet<ExportStepCache> ExportStepCaches => Set<ExportStepCache>();
    public DbSet<GenerationBatch> GenerationBatches => Set<GenerationBatch>();
    public DbSet<PromptRecipe> PromptRecipes => Set<PromptRecipe>();
    public DbSet<PromptRecipeVersion> PromptRecipeVersions => Set<PromptRecipeVersion>();
    public DbSet<SpriteSheetDefinition> SpriteSheetDefinitions => Set<SpriteSheetDefinition>();
    public DbSet<SpriteSheetFrameRecord> SpriteSheetFrameRecords => Set<SpriteSheetFrameRecord>();
    public DbSet<ImageMask> ImageMasks => Set<ImageMask>();
    public DbSet<ChatContextAttachment> ChatContextAttachments => Set<ChatContextAttachment>();
    public DbSet<AssistantConversation> AssistantConversations => Set<AssistantConversation>();
    public DbSet<AssistantMessage> AssistantMessages => Set<AssistantMessage>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesWithLockRetryAsync(acceptAllChangesOnSuccess: true, cancellationToken);

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        SaveChangesWithLockRetryAsync(acceptAllChangesOnSuccess, cancellationToken);

    private async Task<int> SaveChangesWithLockRetryAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(100);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            }
            catch (DbUpdateException ex) when (IsSqliteLocked(ex) && attempt < MaxLockedSaveAttempts && !cancellationToken.IsCancellationRequested)
            {
                var retryDelay = delay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 75));
                logger.LogWarning(
                    ex,
                    "SQLite database was locked during SaveChanges; retrying attempt {Attempt}/{MaxAttempts} after {DelayMs} ms.",
                    attempt,
                    MaxLockedSaveAttempts,
                    retryDelay.TotalMilliseconds);
                await Task.Delay(retryDelay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 2_000));
            }
        }
    }

    private static bool IsSqliteLocked(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqliteException { SqliteErrorCode: 5 or 6 })
                return true;
        }

        return false;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LlmProvider>(entity =>
        {
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.AuthType).HasConversion<string>();
            entity.Property(e => e.LastChatTestAuthType).HasConversion<string>();

            entity.HasOne(e => e.CredentialSource)
                .WithMany(e => e.ChildModels)
                .HasForeignKey(e => e.CredentialSourceId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OAuthToken>(entity =>
        {
            entity.HasIndex(e => e.ProviderId);

            entity.HasOne(e => e.Provider)
                .WithMany(p => p.OAuthTokens)
                .HasForeignKey(e => e.ProviderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StoredSecret>(entity =>
        {
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.ActiveSpriteSheetId);
            entity.Property(e => e.ActiveWorkspaceMode).HasConversion<string>();
        });

        modelBuilder.Entity<ArtAsset>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.CreatedAt });
            entity.HasIndex(e => e.ParentAssetId);
            entity.HasIndex(e => e.SourceBatchId);
            entity.Property(e => e.Kind).HasConversion<string>();

            entity.HasOne(e => e.Project)
                .WithMany(p => p.Assets)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ParentAsset)
                .WithMany(e => e.ChildAssets)
                .HasForeignKey(e => e.ParentAssetId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.SourceBatch)
                .WithMany(b => b.OutputAssets)
                .HasForeignKey(e => e.SourceBatchId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.SourcePromptRecipe)
                .WithMany()
                .HasForeignKey(e => e.SourcePromptRecipeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<BackgroundRemovalExportCache>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.AssetId });
            entity.HasIndex(e => new
            {
                e.AssetId,
                e.SourceImageSha256,
                e.RemovalMethod,
                e.ModelName,
                e.RembgPackageVersion,
                e.AlphaMatting,
                e.OptionsHash,
            }).IsUnique();

            entity.HasOne(e => e.Asset)
                .WithMany()
                .HasForeignKey(e => e.AssetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExportStepCache>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.AssetId, e.SourceImageSha256, e.StepIndex }).IsUnique();
            entity.HasIndex(e => new { e.ProjectId, e.AssetId, e.SourceImageSha256 });
            entity.HasIndex(e => e.OutputImageSha256);

            entity.HasOne(e => e.Asset)
                .WithMany()
                .HasForeignKey(e => e.AssetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GenerationBatch>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.CreatedAt });
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.Background).HasDefaultValue("auto");
            entity.Property(e => e.OutputStatesJson).HasDefaultValue("[]");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.GenerationBatches)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ParentBatch)
                .WithMany(e => e.ChildBatches)
                .HasForeignKey(e => e.ParentBatchId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.PromptRecipe)
                .WithMany()
                .HasForeignKey(e => e.PromptRecipeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PromptRecipe>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.Name });

            entity.HasOne(e => e.Project)
                .WithMany(p => p.PromptRecipes)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PromptRecipeVersion>(entity =>
        {
            entity.HasIndex(e => new { e.RecipeId, e.Version }).IsUnique();
            entity.HasIndex(e => new { e.ProjectId, e.RecipeId });

            entity.HasOne(e => e.Project)
                .WithMany(p => p.PromptRecipeVersions)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Recipe)
                .WithMany(r => r.Versions)
                .HasForeignKey(e => e.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SpriteSheetDefinition>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.UpdatedAt });
            entity.HasIndex(e => e.SourceAssetId);
            entity.HasIndex(e => e.OutputAssetId);
            entity.Property(e => e.HorizontalAnchor).HasDefaultValue("center");
            entity.Property(e => e.VerticalAnchor).HasDefaultValue("bottom");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.SpriteSheets)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SourceAsset)
                .WithMany()
                .HasForeignKey(e => e.SourceAssetId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.OutputAsset)
                .WithMany()
                .HasForeignKey(e => e.OutputAssetId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SpriteSheetFrameRecord>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.SpriteSheetDefinitionId, e.Index }).IsUnique();
            entity.HasIndex(e => e.SpriteSheetDefinitionId);
            entity.Property(e => e.ShapeJson).HasDefaultValue("[]");
            entity.Property(e => e.WorkingState).HasDefaultValue("none");
            entity.Property(e => e.WorkingContentType).HasDefaultValue("image/png");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.SpriteSheetFrameRecords)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SpriteSheetDefinition)
                .WithMany(s => s.FrameRecords)
                .HasForeignKey(e => e.SpriteSheetDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ImageMask>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.CreatedAt });
            entity.HasIndex(e => e.AssetId);

            entity.HasOne(e => e.Project)
                .WithMany(p => p.Masks)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Asset)
                .WithMany()
                .HasForeignKey(e => e.AssetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatContextAttachment>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.SortOrder });
            entity.Property(e => e.Type).HasConversion<string>();

            entity.HasOne(e => e.Project)
                .WithMany(p => p.ChatContextAttachments)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AssistantConversation>(entity =>
        {
            entity.HasIndex(e => e.ProjectId);

            entity.HasOne(e => e.Project)
                .WithMany(p => p.AssistantConversations)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Messages)
                .WithOne(e => e.Conversation)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AssistantMessage>(entity =>
        {
            entity.HasIndex(e => new { e.ConversationId, e.Order });
            entity.Property(e => e.Role).HasConversion<string>();
            entity.Property(e => e.Status).HasConversion<string>();
        });
    }
}
