using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MISA.Memory
{
    public class MemoryDbContext : DbContext
    {
        public MemoryDbContext(DbContextOptions<MemoryDbContext> options)
            : base(options)
        {
            // Enable auto-change tracking for better performance
            ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTrackingWithIdentityResolution;
        }

        // Database Sets for each memory type
        public DbSet<ConversationMemory> Conversations { get; set; }
        public DbSet<PersonalMemory> PersonalMemories { get; set; }
        public DbSet<ContextualMemory> ContextualMemories { get; set; }
        public DbSet<MemoryTag> MemoryTags { get; set; }
        public DbSet<MemoryEmbedding> MemoryEmbeddings { get; set; }
        public DbSet<DeviceSession> DeviceSessions { get; set; }
        public DbSet<SyncStatus> SyncStatuses { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure ConversationMemory
            modelBuilder.Entity<ConversationMemory>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.Timestamp).HasDefaultValueSql("datetime('now')");
                entity.Property(e => e.Input).IsRequired();
                entity.Property(e => e.Response).IsRequired();
                entity.Property(e => e.Personality).IsRequired().HasMaxLength(50);
                entity.Property(e => e.DeviceId).HasMaxLength(100);
                entity.Property(e => e.Emotion).HasMaxLength(50);
                entity.Property(e => e.Context).HasColumnType("text");
                entity.Property(e => e.Metadata).HasColumnType("json");
                entity.Property(e => e.Embedding).HasColumnType("blob");

                // Indexes for better query performance
                entity.HasIndex(e => e.Timestamp);
                entity.HasIndex(e => e.Personality);
                entity.HasIndex(e => e.DeviceId);
                entity.HasIndex(e => new { e.Timestamp, e.Personality });
            });

            // Configure PersonalMemory
            modelBuilder.Entity<PersonalMemory>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.Timestamp).HasDefaultValueSql("datetime('now')");
                entity.Property(e => e.Content).IsRequired().HasColumnType("text");
                entity.Property(e => e.Category).HasMaxLength(100);
                entity.Property(e => e.Type).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Tags).HasColumnType("json");
                entity.Property(e => e.Metadata).HasColumnType("json");
                entity.Property(e => e.Embedding).HasColumnType("blob");
                entity.Property(e => e.EncryptedContent).HasColumnType("blob");
                entity.Property(e => e.Importance).HasDefaultValue(0.5);

                // Indexes
                entity.HasIndex(e => e.Timestamp);
                entity.HasIndex(e => e.Category);
                entity.HasIndex(e => e.Type);
                entity.HasIndex(e => e.Importance);
                entity.HasIndex(e => e.ExpiresAt);
            });

            // Configure ContextualMemory
            modelBuilder.Entity<ContextualMemory>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.Timestamp).HasDefaultValueSql("datetime('now')");
                entity.Property(e => e.Context).IsRequired().HasColumnType("text");
                entity.Property(e => e.Metadata).HasColumnType("json");
                entity.Property(e => e.Embedding).HasColumnType("blob");
                entity.Property(e => e.Type).IsRequired().HasMaxLength(50);

                // Indexes
                entity.HasIndex(e => e.Timestamp);
                entity.HasIndex(e => e.ExpiresAt);
                entity.HasIndex(e => new { e.Timestamp, e.ExpiresAt });
            });

            // Configure MemoryTag
            modelBuilder.Entity<MemoryTag>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Color).HasMaxLength(7); // Hex color
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now')");

                // Unique constraint on tag name
                entity.HasIndex(e => e.Name).IsUnique();
            });

            // Configure MemoryEmbedding
            modelBuilder.Entity<MemoryEmbedding>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.MemoryId).IsRequired().HasMaxLength(100);
                entity.Property(e => e.MemoryType).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Embedding).IsRequired().HasColumnType("blob");
                entity.Property(e => e.ModelName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now')");

                // Indexes for similarity search
                entity.HasIndex(e => e.MemoryId);
                entity.HasIndex(e => e.MemoryType);
                entity.HasIndex(e => new { e.MemoryId, e.MemoryType });
            });

            // Configure DeviceSession
            modelBuilder.Entity<DeviceSession>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.DeviceId).IsRequired().HasMaxLength(100);
                entity.Property(e => e.DeviceName).IsRequired().HasMaxLength(200);
                entity.Property(e => e.DeviceType).IsRequired().HasMaxLength(50);
                entity.Property(e => e.StartedAt).HasDefaultValueSql("datetime('now')");
                entity.Property(e => e.EndedAt);
                entity.Property(e => e.Metadata).HasColumnType("json");
                entity.Property(e => e.IsActive).HasDefaultValue(true);

                // Indexes
                entity.HasIndex(e => e.DeviceId);
                entity.HasIndex(e => e.StartedAt);
                entity.HasIndex(e => e.IsActive);
            });

            // Configure SyncStatus
            modelBuilder.Entity<SyncStatus>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.MemoryId).IsRequired().HasMaxLength(100);
                entity.Property(e => e.MemoryType).IsRequired().HasMaxLength(50);
                entity.Property(e => e.CloudProvider).IsRequired().HasMaxLength(50);
                entity.Property(e => e.CloudId).HasMaxLength(200);
                entity.Property(e => e.LastSyncAt).HasDefaultValueSql("datetime('now')");
                entity.Property(e => e.SyncStatus).IsRequired().HasMaxLength(20);
                entity.Property(e => e.ErrorMessage).HasColumnType("text");
                entity.Property(e => e.Metadata).HasColumnType("json");

                // Indexes
                entity.HasIndex(e => e.MemoryId);
                entity.HasIndex(e => e.MemoryType);
                entity.HasIndex(e => e.SyncStatus);
                entity.HasIndex(e => e.LastSyncAt);
            });

            // Enable foreign key constraints and cascade deletes where appropriate
            ConfigureRelationships(modelBuilder);

            // Create full-text search indexes
            EnableFullTextSearch(modelBuilder);

            // Seed initial data
            SeedInitialData(modelBuilder);
        }

        private void ConfigureRelationships(ModelBuilder modelBuilder)
        {
            // ConversationMemory <-> MemoryEmbedding (one-to-one)
            modelBuilder.Entity<ConversationMemory>()
                .HasOne<MemoryEmbedding>()
                .WithOne()
                .HasForeignKey<MemoryEmbedding>(e => new { e.MemoryId, e.MemoryType })
                .OnDelete(DeleteBehavior.Cascade);

            // PersonalMemory <-> MemoryEmbedding (one-to-one)
            modelBuilder.Entity<PersonalMemory>()
                .HasOne<MemoryEmbedding>()
                .WithOne()
                .HasForeignKey<MemoryEmbedding>(e => new { e.MemoryId, e.MemoryType })
                .OnDelete(DeleteBehavior.Cascade);

            // ContextualMemory <-> MemoryEmbedding (one-to-one)
            modelBuilder.Entity<ContextualMemory>()
                .HasOne<MemoryEmbedding>()
                .WithOne()
                .HasForeignKey<MemoryEmbedding>(e => new { e.MemoryId, e.MemoryType })
                .OnDelete(DeleteBehavior.Cascade);

            // Memory <-> SyncStatus (one-to-one)
            modelBuilder.Entity<ConversationMemory>()
                .HasOne<SyncStatus>()
                .WithOne()
                .HasForeignKey<SyncStatus>(e => new { e.MemoryId, e.MemoryType })
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PersonalMemory>()
                .HasOne<SyncStatus>()
                .WithOne()
                .HasForeignKey<SyncStatus>(e => new { e.MemoryId, e.MemoryType })
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ContextualMemory>()
                .HasOne<SyncStatus>()
                .WithOne()
                .HasForeignKey<SyncStatus>(e => new { e.MemoryId, e.MemoryType })
                .OnDelete(DeleteBehavior.Cascade);
        }

        private void EnableFullTextSearch(ModelBuilder modelBuilder)
        {
            // Enable FTS5 for SQLite for efficient text search
            modelBuilder.Entity<ConversationMemory>()
                .ToTable(t =>
                {
                    t.HasTrigger("FTS_ConversationMemory_Delete");
                });

            modelBuilder.Entity<PersonalMemory>()
                .ToTable(t =>
                {
                    t.HasTrigger("FTS_PersonalMemory_Delete");
                });

            modelBuilder.Entity<ContextualMemory>()
                .ToTable(t =>
                {
                    t.HasTrigger("FTS_ContextualMemory_Delete");
                });
        }

        private void SeedInitialData(ModelBuilder modelBuilder)
        {
            // Seed default tags
            modelBuilder.Entity<MemoryTag>().HasData(
                new MemoryTag { Id = "personal", Name = "Personal", Color = "#FF6B6B" },
                new MemoryTag { Id = "work", Name = "Work", Color = "#4ECDC4" },
                new MemoryTag { Id = "important", Name = "Important", Color = "#FFD93D" },
                new MemoryTag { Id = "idea", Name = "Idea", Color = "#6C5CE7" },
                new MemoryTag { Id = "reminder", Name = "Reminder", Color = "#A29BFE" },
                new MemoryTag { Id = "note", Name = "Note", Color = "#FD79A8" },
                new MemoryTag { Id = "quote", Name = "Quote", Color = "#FDCB6E" },
                new MemoryTag { Id = "task", Name = "Task", Color = "#6C5CE7" },
                new MemoryTag { Id = "project", Name = "Project", Color = "#00B894" },
                new MemoryTag { Id = "goal", Name = "Goal", Color = "#E17055" }
            );
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // Update timestamps automatically
            UpdateTimestamps();

            // Soft delete support
            HandleSoftDeletes();

            // Validate entity constraints
            ValidateEntities();

            return await base.SaveChangesAsync(cancellationToken);
        }

        private void UpdateTimestamps()
        {
            var entries = ChangeTracker.Entries()
                .Where(e => e.Entity is IMemory && (e.State == EntityState.Added || e.State == EntityState.Modified));

            foreach (var entry in entries)
            {
                var memory = (IMemory)entry.Entity;
                if (entry.State == EntityState.Added)
                {
                    memory.Timestamp = DateTime.UtcNow;
                }
            }
        }

        private void HandleSoftDeletes()
        {
            var deletedEntries = ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Deleted && e.Entity is ISoftDeletable);

            foreach (var entry in deletedEntries)
            {
                entry.State = EntityState.Modified;
                entry.Property("IsDeleted").CurrentValue = true;
                entry.Property("DeletedAt").CurrentValue = DateTime.UtcNow;
            }
        }

        private void ValidateEntities()
        {
            var entities = ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
                .Select(e => e.Entity);

            foreach (var entity in entities)
            {
                if (entity is IValidatable validatable)
                {
                    var validationResult = validatable.Validate();
                    if (!validationResult.IsValid)
                    {
                        throw new ValidationException(validationResult.Errors);
                    }
                }
            }
        }
    }

    // Base interfaces and classes for common functionality
    public interface IMemory
    {
        string Id { get; set; }
        DateTime Timestamp { get; set; }
        MemoryType Type { get; set; }
        float[]? Embedding { get; set; }
    }

    public interface ISoftDeletable
    {
        bool IsDeleted { get; set; }
        DateTime? DeletedAt { get; set; }
    }

    public interface IValidatable
    {
        ValidationResult Validate();
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public class ValidationException : Exception
    {
        public List<string> ValidationErrors { get; }

        public ValidationException(ValidationResult validationResult)
            : base($"Validation failed: {string.Join(", ", validationResult.Errors)}")
        {
            ValidationErrors = validationResult.Errors;
        }
    }

    // Extension methods for common operations
    public static class MemoryDbContextExtensions
    {
        public static IQueryable<T> ActiveOnly<T>(this IQueryable<T> query) where T : class, ISoftDeletable
        {
            return query.Where(e => !e.IsDeleted);
        }

        public static async Task<List<T>> GetRecentAsync<T>(
            this MemoryDbContext context,
            int count = 10,
            CancellationToken cancellationToken = default) where T : class, IMemory
        {
            return await context.Set<T>()
                .OrderByDescending(m => m.Timestamp)
                .Take(count)
                .ToListAsync(cancellationToken);
        }

        public static async Task<List<T>> SearchByTextAsync<T>(
            this MemoryDbContext context,
            string searchText,
            int limit = 50,
            CancellationToken cancellationToken = default) where T : class, IMemory
        {
            var lowerSearchText = searchText.ToLower();

            return await context.Set<T>()
                .Where(m => EF.Functions.Like(EF.Property<string>(m, "Content").ToLower(), $"%{lowerSearchText}%"))
                .OrderByDescending(m => m.Timestamp)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        public static async Task<MemoryStats> GetStatisticsAsync(
            this MemoryDbContext context,
            CancellationToken cancellationToken = default)
        {
            var stats = new MemoryStats();

            stats.Conversations = await context.Conversations.CountAsync(cancellationToken);
            stats.PersonalMemories = await context.PersonalMemories.CountAsync(cancellationToken);
            stats.ContextualMemories = await context.ContextualMemories.CountAsync(cancellationToken);
            stats.TotalMemories = stats.Conversations + stats.PersonalMemories + stats.ContextualMemories;

            // Calculate memory usage estimates
            var totalSize = await context.Database.SqlQueryRaw<long>(
                "SELECT SUM(pgsize) FROM dbstat WHERE name IN ('Conversations', 'PersonalMemories', 'ContextualMemories')")
                .FirstOrDefaultAsync(cancellationToken);

            stats.EstimatedDatabaseSizeMB = totalSize / (1024.0 * 1024.0);

            return stats;
        }

        public static async Task CleanupExpiredMemoriesAsync(
            this MemoryDbContext context,
            CancellationToken cancellationToken = default)
        {
            var expiredMemories = context.ContextualMemories
                .Where(m => m.ExpiresAt.HasValue && m.ExpiresAt.Value < DateTime.UtcNow);

            context.ContextualMemories.RemoveRange(expiredMemories);
            await context.SaveChangesAsync(cancellationToken);
        }

        public static async Task<List<MemorySearchResult>> SemanticSearchAsync(
            this MemoryDbContext context,
            float[] queryEmbedding,
            int maxResults = 10,
            MemoryType? typeFilter = null,
            CancellationToken cancellationToken = default)
        {
            // This would implement vector similarity search
            // For now, return empty list as placeholder
            return await Task.FromResult(new List<MemorySearchResult>());
        }
    }

    // Statistics class for memory usage
    public class MemoryStats
    {
        public int TotalMemories { get; set; }
        public int Conversations { get; set; }
        public int PersonalMemories { get; set; }
        public int ContextualMemories { get; set; }
        public double EstimatedDatabaseSizeMB { get; set; }
        public DateTime LastSync { get; set; }
        public int SyncPendingCount { get; set; }
        public int SyncFailedCount { get; set; }
    }
}