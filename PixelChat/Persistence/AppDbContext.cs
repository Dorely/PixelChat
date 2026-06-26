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
    public DbSet<AnimationRecipe> AnimationRecipes => Set<AnimationRecipe>();
    public DbSet<AnimationRecipeVersion> AnimationRecipeVersions => Set<AnimationRecipeVersion>();
    public DbSet<ActivityRun> ActivityRuns => Set<ActivityRun>();
    public DbSet<ActivityStep> ActivitySteps => Set<ActivityStep>();
    public DbSet<ActivityArtifact> ActivityArtifacts => Set<ActivityArtifact>();
    public DbSet<SpriteSheetDefinition> SpriteSheetDefinitions => Set<SpriteSheetDefinition>();
    public DbSet<SpriteSheetFrameRecord> SpriteSheetFrameRecords => Set<SpriteSheetFrameRecord>();
    public DbSet<SpriteRegion> SpriteRegions => Set<SpriteRegion>();
    public DbSet<StandaloneAsset> StandaloneAssets => Set<StandaloneAsset>();
    public DbSet<FrameSet> FrameSets => Set<FrameSet>();
    public DbSet<Frame> Frames => Set<Frame>();
    public DbSet<Anchor> Anchors => Set<Anchor>();
    public DbSet<SheetLayout> SheetLayouts => Set<SheetLayout>();
    public DbSet<BuiltSheet> BuiltSheets => Set<BuiltSheet>();
    public DbSet<HistoryTask> HistoryTasks => Set<HistoryTask>();
    public DbSet<SpriteEditSession> SpriteEditSessions => Set<SpriteEditSession>();
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

        modelBuilder.Entity<AnimationRecipe>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.Name });
            entity.HasIndex(e => e.GuideAssetId);
            entity.HasIndex(e => e.PrimaryExampleSpriteSheetId);
            entity.Property(e => e.FrameOrderJson).HasDefaultValue("[]");
            entity.Property(e => e.ExpectedFrameBoxesJson).HasDefaultValue("[]");
            entity.Property(e => e.AnchorStrategy).HasDefaultValue("recipe-defined");
            entity.Property(e => e.ExportDefaultsJson).HasDefaultValue("{}");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.AnimationRecipes)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.GuideAsset)
                .WithMany()
                .HasForeignKey(e => e.GuideAssetId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.PrimaryExampleSpriteSheet)
                .WithMany()
                .HasForeignKey(e => e.PrimaryExampleSpriteSheetId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AnimationRecipeVersion>(entity =>
        {
            entity.HasIndex(e => new { e.AnimationRecipeId, e.Version }).IsUnique();
            entity.HasIndex(e => new { e.ProjectId, e.AnimationRecipeId });
            entity.Property(e => e.FrameOrderJson).HasDefaultValue("[]");
            entity.Property(e => e.ExpectedFrameBoxesJson).HasDefaultValue("[]");
            entity.Property(e => e.AnchorStrategy).HasDefaultValue("recipe-defined");
            entity.Property(e => e.ExportDefaultsJson).HasDefaultValue("{}");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.AnimationRecipeVersions)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AnimationRecipe)
                .WithMany(r => r.Versions)
                .HasForeignKey(e => e.AnimationRecipeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ActivityRun>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.UpdatedAt });
            entity.HasIndex(e => new { e.ProjectId, e.WorkflowKind });
            entity.Property(e => e.Status).HasDefaultValue("running");
            entity.Property(e => e.Actor).HasDefaultValue("system");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.ActivityRuns)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ActivityStep>(entity =>
        {
            entity.HasIndex(e => new { e.ActivityRunId, e.SortOrder });
            entity.Property(e => e.Status).HasDefaultValue("completed");
            entity.Property(e => e.PayloadJson).HasDefaultValue("{}");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.ActivitySteps)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ActivityRun)
                .WithMany(r => r.Steps)
                .HasForeignKey(e => e.ActivityRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ActivityArtifact>(entity =>
        {
            entity.HasIndex(e => new { e.ActivityRunId, e.SortOrder });
            entity.HasIndex(e => new { e.ProjectId, e.Kind, e.RefId });

            entity.HasOne(e => e.Project)
                .WithMany(p => p.ActivityArtifacts)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ActivityRun)
                .WithMany(r => r.Artifacts)
                .HasForeignKey(e => e.ActivityRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SpriteSheetDefinition>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.UpdatedAt });
            entity.HasIndex(e => e.SourceAssetId);
            entity.HasIndex(e => e.OutputAssetId);
            entity.Property(e => e.HorizontalAnchor).HasDefaultValue("center");
            entity.Property(e => e.VerticalAnchor).HasDefaultValue("bottom");
            entity.Property(e => e.StabilizationJson).HasDefaultValue("{}");

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
        });

        modelBuilder.Entity<SpriteRegion>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.SourceAssetId, e.Order });
            entity.HasIndex(e => e.SourceAssetId);
            entity.Property(e => e.ShapeJson).HasDefaultValue("[]");
            entity.Property(e => e.RegionType).HasDefaultValue("frame");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.SpriteRegions)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SourceAsset)
                .WithMany()
                .HasForeignKey(e => e.SourceAssetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StandaloneAsset>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.CreatedAt });
            entity.HasIndex(e => e.SourceRegionId);
            entity.HasIndex(e => e.OutputAssetId);

            entity.HasOne(e => e.Project)
                .WithMany(p => p.StandaloneAssets)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SourceRegion)
                .WithMany()
                .HasForeignKey(e => e.SourceRegionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.OutputAsset)
                .WithMany()
                .HasForeignKey(e => e.OutputAssetId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.BitmapRevisionAsset)
                .WithMany()
                .HasForeignKey(e => e.BitmapRevisionAssetId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<FrameSet>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.UpdatedAt });
            entity.HasIndex(e => e.SourceAssetId);
            entity.Property(e => e.OrderedFrameIdsJson).HasDefaultValue("[]");
            entity.Property(e => e.PlaybackSettingsJson).HasDefaultValue("{}");
            entity.Property(e => e.AlignmentSettingsJson).HasDefaultValue("{}");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.FrameSets)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SourceAsset)
                .WithMany()
                .HasForeignKey(e => e.SourceAssetId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Frame>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.FrameSetId, e.Index }).IsUnique();
            entity.HasIndex(e => e.FrameSetId);
            entity.HasIndex(e => e.SourceRegionId);
            entity.Property(e => e.HideFromOnionSkin).HasDefaultValue(false);
            entity.Property(e => e.ShapeJson).HasDefaultValue("[]");
            entity.Property(e => e.WorkingState).HasDefaultValue("none");
            entity.Property(e => e.WorkingContentType).HasDefaultValue("image/png");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.Frames)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.FrameSet)
                .WithMany(s => s.Frames)
                .HasForeignKey(e => e.FrameSetId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SourceRegion)
                .WithMany()
                .HasForeignKey(e => e.SourceRegionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.BitmapRevisionAsset)
                .WithMany()
                .HasForeignKey(e => e.BitmapRevisionAssetId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Anchor>(entity =>
        {
            entity.HasIndex(e => new { e.FrameId, e.Name });
            entity.Property(e => e.Source).HasDefaultValue("manual");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.Anchors)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Frame)
                .WithMany(f => f.Anchors)
                .HasForeignKey(e => e.FrameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SheetLayout>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.FrameSetId });
            entity.Property(e => e.Ordering).HasDefaultValue("rowMajor");
            entity.Property(e => e.HorizontalAnchor).HasDefaultValue("center");
            entity.Property(e => e.VerticalAnchor).HasDefaultValue("bottom");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.SheetLayouts)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.FrameSet)
                .WithMany(s => s.SheetLayouts)
                .HasForeignKey(e => e.FrameSetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BuiltSheet>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.UpdatedAt });
            entity.HasIndex(e => e.SheetLayoutId);
            entity.HasIndex(e => e.OutputAssetId);
            entity.Property(e => e.ManifestJson).HasDefaultValue("{}");
            entity.Property(e => e.LinkedFrameIdsJson).HasDefaultValue("[]");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.BuiltSheets)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SheetLayout)
                .WithMany(l => l.BuiltSheets)
                .HasForeignKey(e => e.SheetLayoutId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.OutputAsset)
                .WithMany()
                .HasForeignKey(e => e.OutputAssetId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<HistoryTask>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.StartedAt });
            entity.Property(e => e.Source).HasDefaultValue("user");
            entity.Property(e => e.OperationsJson).HasDefaultValue("[]");
            entity.Property(e => e.Status).HasDefaultValue("running");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.HistoryTasks)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SpriteEditSession>(entity =>
        {
            entity.HasIndex(e => e.ProjectId).IsUnique().HasFilter("Status = 'pending'");
            entity.HasIndex(e => e.BatchId);
            entity.HasIndex(e => e.TargetSourceAssetId);
            entity.HasIndex(e => e.TargetFrameId);
            entity.Property(e => e.Status).HasDefaultValue("pending");
            entity.Property(e => e.TargetKind).HasDefaultValue("source");
            entity.Property(e => e.CropJson).HasDefaultValue("{}");
            entity.Property(e => e.CandidateAssetIdsJson).HasDefaultValue("[]");
            entity.Property(e => e.OutputStatesJson).HasDefaultValue("[]");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.SpriteEditSessions)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<ArtAsset>()
                .WithMany()
                .HasForeignKey(e => e.TargetSourceAssetId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne<FrameSet>()
                .WithMany()
                .HasForeignKey(e => e.TargetFrameSetId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne<Frame>()
                .WithMany()
                .HasForeignKey(e => e.TargetFrameId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne<GenerationBatch>()
                .WithMany()
                .HasForeignKey(e => e.BatchId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne<ImageMask>()
                .WithMany()
                .HasForeignKey(e => e.MaskId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne<ArtAsset>()
                .WithMany()
                .HasForeignKey(e => e.SelectedCandidateAssetId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ImageMask>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.CreatedAt });
            entity.HasIndex(e => e.AssetId);
            entity.HasIndex(e => new { e.OwnerKind, e.OwnerId });
            entity.Property(e => e.OwnerKind).HasDefaultValue("asset");
            entity.Property(e => e.CoordinateSpace).HasDefaultValue("source");

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
