using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MISA.Memory.MemoryTypes
{
    [Table("PersonalMemories")]
    public class PersonalMemory : IMemory, ISoftDeletable, IValidatable
    {
        [Key]
        [StringLength(50)]
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..16];

        [Required]
        [StringLength(10000)]
        public string Content { get; set; } = string.Empty;

        [StringLength(10000)]
        public string? EncryptedContent { get; set; }

        [Required]
        [StringLength(50)]
        public string Type { get; set; } = "Personal";

        [StringLength(100)]
        public string? Category { get; set; }

        public string? Tags { get; set; }

        public string? Metadata { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public DateTime? ExpiresAt { get; set; }

        [StringLength(100)]
        public string? Source { get; set; }

        [StringLength(500)]
        public string? Title { get; set; }

        [StringLength(1000)]
        public string? Description { get; set; }

        public double Importance { get; set; } = 0.5;

        public bool IsEncrypted { get; set; }

        public bool IsPinned { get; set; }

        public bool IsArchived { get; set; }

        public bool IsPublic { get; set; }

        public bool IsFavorite { get; set; }

        public int AccessCount { get; set; }

        public DateTime? LastAccessed { get; set; }

        public double RelevanceScore { get; set; }

        [StringLength(50)]
        public string? AssociatedProject { get; set; }

        [StringLength(50)]
        public string? AssociatedTask { get; set; }

        public List<string> Keywords { get; set; } = new();

        public List<string> RelatedIds { get; set; } = new();

        public MemoryType MemoryType => Type switch
        {
            "ShortTerm" => MemoryType.ShortTerm,
            "MediumTerm" => MemoryType.MediumTerm,
            "LongTerm" => MemoryType.LongTerm,
            _ => MemoryType.Personal
        };

        [Column(TypeName = "BLOB")]
        public float[]? Embedding { get; set; }

        MemoryType IMemory.Type => MemoryType;

        public bool IsDeleted { get; set; }

        public DateTime? DeletedAt { get; set; }

        [StringLength(100)]
        public string? CloudId { get; set; }

        public DateTime? LastSyncAt { get; set; }

        [StringLength(20)]
        public string SyncStatus { get; set; } = "Pending";

        // Navigation properties
        public virtual MemoryEmbedding? EmbeddingEntity { get; set; }
        public virtual SyncStatus? SyncStatusEntity { get; set; }
        public virtual ICollection<MemoryTag> TagEntities { get; set; } = new List<MemoryTag>();

        public ValidationResult Validate()
        {
            var result = new ValidationResult { IsValid = true };

            if (string.IsNullOrWhiteSpace(Content))
            {
                result.IsValid = false;
                result.Errors.Add("Content is required");
            }

            if (string.IsNullOrWhiteSpace(Type))
            {
                result.IsValid = false;
                result.Errors.Add("Type is required");
            }

            if (Importance < 0 || Importance > 1)
            {
                result.IsValid = false;
                result.Errors.Add("Importance must be between 0 and 1");
            }

            if (AccessCount < 0)
            {
                result.IsValid = false;
                result.Errors.Add("Access count cannot be negative");
            }

            return result;
        }

        // Helper methods
        public void AddTag(string tagName)
        {
            var tags = ParseTags();
            if (!tags.Contains(tagName))
            {
                tags.Add(tagName);
                Tags = string.Join(",", tags);
            }
        }

        public void RemoveTag(string tagName)
        {
            var tags = ParseTags();
            tags.Remove(tagName);
            Tags = string.Join(",", tags);
        }

        public List<string> GetTags()
        {
            return ParseTags();
        }

        private List<string> ParseTags()
        {
            if (string.IsNullOrWhiteSpace(Tags))
                return new List<string>();

            return Tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                     .Select(t => t.Trim())
                     .Where(t => !string.IsNullOrWhiteSpace(t))
                     .ToList();
        }

        public void AddKeyword(string keyword)
        {
            if (!Keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase))
            {
                Keywords.Add(keyword);
            }
        }

        public void RemoveKeyword(string keyword)
        {
            Keywords.RemoveAll(k => k.Equals(keyword, StringComparison.OrdinalIgnoreCase));
        }

        public bool HasKeyword(string keyword)
        {
            return Keywords.Any(k => k.Equals(keyword, StringComparison.OrdinalIgnoreCase));
        }

        public void MarkAsEncrypted()
        {
            IsEncrypted = true;
        }

        public void MarkAsPinned()
        {
            IsPinned = true;
        }

        public void MarkAsArchived()
        {
            IsArchived = true;
        }

        public void MarkAsFavorite()
        {
            IsFavorite = true;
        }

        public void SetExpiration(TimeSpan timeSpan)
        {
            ExpiresAt = DateTime.UtcNow.Add(timeSpan);
        }

        public bool IsExpired()
        {
            return ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
        }

        public void IncrementAccess()
        {
            AccessCount++;
            LastAccessed = DateTime.UtcNow;
        }

        public void SetImportance(double importance)
        {
            Importance = Math.Max(0, Math.Min(1, importance));
        }

        public void AddRelatedMemory(string memoryId)
        {
            if (!RelatedIds.Contains(memoryId))
            {
                RelatedIds.Add(memoryId);
            }
        }

        public void RemoveRelatedMemory(string memoryId)
        {
            RelatedIds.Remove(memoryId);
        }

        // Conversion methods
        public PersonalMemorySummary ToSummary()
        {
            return new PersonalMemorySummary
            {
                MemoryId = Id,
                Title = Title ?? Content.Substring(0, Math.Min(50, Content.Length)),
                Content = Content.Length > 200 ? Content.Substring(0, 197) + "..." : Content,
                Type = Type,
                Category = Category ?? "General",
                Importance = Importance,
                IsPinned = IsPinned,
                IsFavorite = IsFavorite,
                Tags = GetTags(),
                Timestamp = Timestamp,
                AccessCount = AccessCount,
                LastAccessed = LastAccessed,
                HasAttachments = !string.IsNullOrWhiteSpace(Source) && Source.Contains("file://")
            };
        }

        public PersonalMemory Clone()
        {
            return new PersonalMemory
            {
                Content = Content,
                Type = Type,
                Category = Category,
                Tags = Tags,
                Metadata = Metadata,
                Timestamp = Timestamp,
                ExpiresAt = ExpiresAt,
                Source = Source,
                Title = Title,
                Description = Description,
                Importance = Importance,
                IsEncrypted = IsEncrypted,
                IsPinned = IsPinned,
                IsArchived = IsArchived,
                IsPublic = IsPublic,
                IsFavorite = IsFavorite,
                AccessCount = AccessCount,
                LastAccessed = LastAccessed,
                RelevanceScore = RelevanceScore,
                AssociatedProject = AssociatedProject,
                AssociatedTask = AssociatedTask,
                Keywords = new List<string>(Keywords),
                RelatedIds = new List<string>(RelatedIds),
                Embedding = Embedding?.ToArray()
            };
        }

        // Static factory methods
        public static PersonalMemory Create(
            string content,
            string type = "Personal",
            string? category = null,
            double importance = 0.5)
        {
            return new PersonalMemory
            {
                Content = content,
                Type = type,
                Category = category,
                Importance = importance,
                Timestamp = DateTime.UtcNow,
                AccessCount = 1,
                LastAccessed = DateTime.UtcNow
            };
        }

        public static PersonalMemory CreateNote(
            string content,
            string? title = null,
            string? category = null)
        {
            return new PersonalMemory
            {
                Content = content,
                Type = "Note",
                Title = title,
                Category = category ?? "Notes",
                Importance = 0.3,
                Timestamp = DateTime.UtcNow,
                AccessCount = 1,
                LastAccessed = DateTime.UtcNow
            };
        }

        public static PersonalMemory CreateTask(
            string content,
            string? title = null,
            double importance = 0.7)
        {
            return new PersonalMemory
            {
                Content = content,
                Type = "Task",
                Title = title,
                Category = "Tasks",
                Importance = importance,
                Timestamp = DateTime.UtcNow,
                AccessCount = 1,
                LastAccessed = DateTime.UtcNow
            };
        }

        public static PersonalMemory CreateReminder(
            string content,
            DateTime reminderDate,
            string? title = null)
        {
            return new PersonalMemory
            {
                Content = content,
                Type = "Reminder",
                Title = title,
                Category = "Reminders",
                Importance = 0.8,
                ExpiresAt = reminderDate,
                Timestamp = DateTime.UtcNow,
                AccessCount = 1,
                LastAccessed = DateTime.UtcNow
            };
        }

        public static PersonalMemory CreateGoal(
            string content,
            string? title = null,
            double importance = 0.9)
        {
            return new PersonalMemory
            {
                Content = content,
                Type = "Goal",
                Title = title,
                Category = "Goals",
                Importance = importance,
                Timestamp = DateTime.UtcNow,
                AccessCount = 1,
                LastAccessed = DateTime.UtcNow
            };
        }

        public static PersonalMemory CreateQuote(
            string content,
            string? author = null,
            string? source = null)
        {
            return new PersonalMemory
            {
                Content = content,
                Type = "Quote",
                Category = "Quotes",
                Description = author,
                Source = source,
                Importance = 0.4,
                Timestamp = DateTime.UtcNow,
                AccessCount = 1,
                LastAccessed = DateTime.UtcNow
            };
        }
    }

    // Supporting classes for PersonalMemory
    public class PersonalMemorySummary
    {
        public string MemoryId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public double Importance { get; set; }
        public bool IsPinned { get; set; }
        public bool IsFavorite { get; set; }
        public List<string> Tags { get; set; } = new();
        public DateTime Timestamp { get; set; }
        public int AccessCount { get; set; }
        public DateTime? LastAccessed { get; set; }
        public bool HasAttachments { get; set; }
    }
}