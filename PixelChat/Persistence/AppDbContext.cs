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
    public DbSet<AssetProfile> AssetProfiles => Set<AssetProfile>();
    public DbSet<AssetAnimationJob> AssetAnimationJobs => Set<AssetAnimationJob>();
    public DbSet<AssetAnimationCandidate> AssetAnimationCandidates => Set<AssetAnimationCandidate>();
    public DbSet<AssetAnimationFrameAttempt> AssetAnimationFrameAttempts => Set<AssetAnimationFrameAttempt>();
    public DbSet<BackgroundRemovalExportCache> BackgroundRemovalExportCaches => Set<BackgroundRemovalExportCache>();
    public DbSet<ExportStepCache> ExportStepCaches => Set<ExportStepCache>();
    public DbSet<GenerationBatch> GenerationBatches => Set<GenerationBatch>();
    public DbSet<PromptRecipe> PromptRecipes => Set<PromptRecipe>();
    public DbSet<PromptRecipeVersion> PromptRecipeVersions => Set<PromptRecipeVersion>();
    public DbSet<SpriteSheetDefinition> SpriteSheetDefinitions => Set<SpriteSheetDefinition>();
    public DbSet<SpriteSheetFrameRecord> SpriteSheetFrameRecords => Set<SpriteSheetFrameRecord>();
    public DbSet<ImageMask> ImageMasks => Set<ImageMask>();
    public DbSet<ChatContextAttachment> ChatContextAttachments => Set<ChatContextAttachment>();
    public DbSet<CompareReviewSet> CompareReviewSets => Set<CompareReviewSet>();
    public DbSet<CompareReviewSetItem> CompareReviewSetItems => Set<CompareReviewSetItem>();
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

        modelBuilder.Entity<AssetProfile>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.CreatedAt });
            entity.HasIndex(e => e.CanonicalAssetId);
            entity.HasIndex(e => e.StyleAssetId);
            entity.Property(e => e.AssetType).HasDefaultValue("unit");
            entity.Property(e => e.StructureType).HasDefaultValue("biped");
            entity.Property(e => e.ChromaColor).HasDefaultValue("#ff00ff");
            entity.Property(e => e.PaletteJson).HasDefaultValue("[]");
            entity.Property(e => e.RequiredFeaturesJson).HasDefaultValue("[]");
            entity.Property(e => e.ForbiddenChangesJson).HasDefaultValue("[]");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.AssetProfiles)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CanonicalAsset)
                .WithMany()
                .HasForeignKey(e => e.CanonicalAssetId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.StyleAsset)
                .WithMany()
                .HasForeignKey(e => e.StyleAssetId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AssetAnimationJob>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.CreatedAt });
            entity.HasIndex(e => e.AssetProfileId);
            entity.HasIndex(e => e.OutputSpriteSheetId);
            entity.HasIndex(e => e.SelectedCandidateId);
            entity.Property(e => e.Status).HasDefaultValue("planned");
            entity.Property(e => e.Strategy).HasDefaultValue("hybrid");
            entity.Property(e => e.AnimationSpecJson).HasDefaultValue("{}");
            entity.Property(e => e.LayoutSpecJson).HasDefaultValue("{}");
            entity.Property(e => e.RawQaSummaryJson).HasDefaultValue("{}");
            entity.Property(e => e.FrameQaSummaryJson).HasDefaultValue("{}");
            entity.Property(e => e.MotionQaSummaryJson).HasDefaultValue("{}");
            entity.Property(e => e.FrameStatusesJson).HasDefaultValue("[]");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.AssetAnimationJobs)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AssetProfile)
                .WithMany(p => p.AnimationJobs)
                .HasForeignKey(e => e.AssetProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.GuideAsset)
                .WithMany()
                .HasForeignKey(e => e.GuideAssetId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.DiagnosticGuideAsset)
                .WithMany()
                .HasForeignKey(e => e.DiagnosticGuideAssetId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.OutputSpriteSheet)
                .WithMany()
                .HasForeignKey(e => e.OutputSpriteSheetId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.SelectedCandidate)
                .WithMany()
                .HasForeignKey(e => e.SelectedCandidateId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AssetAnimationCandidate>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.AssetAnimationJobId, e.CandidateIndex }).IsUnique();
            entity.HasIndex(e => e.GenerationBatchId);
            entity.HasIndex(e => e.OutputAssetId);
            entity.Property(e => e.State).HasDefaultValue("generated");
            entity.Property(e => e.RawQaStatus).HasDefaultValue("pending");
            entity.Property(e => e.RawQaSummaryJson).HasDefaultValue("{}");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.AssetAnimationCandidates)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AssetAnimationJob)
                .WithMany(j => j.Candidates)
                .HasForeignKey(e => e.AssetAnimationJobId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.GenerationBatch)
                .WithMany()
                .HasForeignKey(e => e.GenerationBatchId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.OutputAsset)
                .WithMany()
                .HasForeignKey(e => e.OutputAssetId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AssetAnimationFrameAttempt>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.AssetAnimationJobId, e.FrameIndex, e.AttemptNumber });
            entity.HasIndex(e => e.AssetAnimationCandidateId);
            entity.HasIndex(e => e.SourceAssetId);
            entity.Property(e => e.AttemptKind).HasDefaultValue("mark");
            entity.Property(e => e.Status).HasDefaultValue("pending");
            entity.Property(e => e.RepairHistoryJson).HasDefaultValue("[]");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.AssetAnimationFrameAttempts)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AssetAnimationJob)
                .WithMany(j => j.FrameAttempts)
                .HasForeignKey(e => e.AssetAnimationJobId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AssetAnimationCandidate)
                .WithMany(c => c.FrameAttempts)
                .HasForeignKey(e => e.AssetAnimationCandidateId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.SourceAsset)
                .WithMany()
                .HasForeignKey(e => e.SourceAssetId)
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
            entity.HasIndex(e => e.SourceImageAssetId);
            entity.Property(e => e.ShapeJson).HasDefaultValue("[]");
            entity.Property(e => e.WorkingState).HasDefaultValue("none");
            entity.Property(e => e.WorkingContentType).HasDefaultValue("image/png");
            entity.Property(e => e.FootContactsJson).HasDefaultValue("[]");
            entity.Property(e => e.AppliedScale).HasDefaultValue(1d);
            entity.Property(e => e.RepairHistoryJson).HasDefaultValue("[]");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.SpriteSheetFrameRecords)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SpriteSheetDefinition)
                .WithMany(s => s.FrameRecords)
                .HasForeignKey(e => e.SpriteSheetDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SourceImageAsset)
                .WithMany()
                .HasForeignKey(e => e.SourceImageAssetId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.SourceAnimationJob)
                .WithMany()
                .HasForeignKey(e => e.SourceAnimationJobId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.SourceAnimationCandidate)
                .WithMany()
                .HasForeignKey(e => e.SourceAnimationCandidateId)
                .OnDelete(DeleteBehavior.SetNull);
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

        modelBuilder.Entity<CompareReviewSet>(entity =>
        {
            entity.HasIndex(e => e.ProjectId).IsUnique();

            entity.HasOne(e => e.Project)
                .WithMany(p => p.CompareReviewSets)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CompareReviewSetItem>(entity =>
        {
            entity.HasIndex(e => new { e.CompareReviewSetId, e.SortOrder });
            entity.HasIndex(e => new { e.CompareReviewSetId, e.Kind, e.RefId }).IsUnique();
            entity.Property(e => e.Kind).HasConversion<string>();

            entity.HasOne(e => e.CompareReviewSet)
                .WithMany(s => s.Items)
                .HasForeignKey(e => e.CompareReviewSetId)
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
