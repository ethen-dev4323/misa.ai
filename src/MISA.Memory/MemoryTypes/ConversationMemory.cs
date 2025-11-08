using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MISA.Memory.MemoryTypes
{
    [Table("Conversations")]
    public class ConversationMemory : IMemory, ISoftDeletable, IValidatable
    {
        [Key]
        [StringLength(50)]
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..16];

        [Required]
        public string Input { get; set; } = string.Empty;

        [StringLength(5000)]
        public string? EncryptedInput { get; set; }

        [Required]
        public string Response { get; set; } = string.Empty;

        [StringLength(5000)]
        public string? EncryptedResponse { get; set; }

        [Required]
        [StringLength(50)]
        public string Personality { get; set; } = "Girlfriend_Caring";

        [StringLength(50)]
        public string? DeviceId { get; set; }

        [StringLength(50)]
        public string? Emotion { get; set; }

        [StringLength(100)]
        public string? EmotionScore { get; set; }

        [StringLength(1000)]
        public string? Context { get; set; }

        public string? Metadata { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public DateTime? ExpiresAt { get; set; }

        public bool IsEncrypted { get; set; }

        public bool IsPinned { get; set; }

        public bool IsArchived { get; set; }

        public double RelevanceScore { get; set; }

        public int MessageCount { get; set; } = 1;

        public long ResponseTimeMs { get; set; }

        public string? ModelUsed { get; set; }

        [StringLength(50)]
        public string SessionId { get; set; }

        [Column(TypeName = "BLOB")]
        public float[]? Embedding { get; set; }

        public MemoryType Type => MemoryType.Conversation;

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
        public virtual ICollection<MemoryTag> Tags { get; set; } = new List<MemoryTag>();

        public ValidationResult Validate()
        {
            var result = new ValidationResult { IsValid = true };

            if (string.IsNullOrWhiteSpace(Input))
            {
                result.IsValid = false;
                result.Errors.Add("Input is required");
            }

            if (string.IsNullOrWhiteSpace(Response))
            {
                result.IsValid = false;
                result.Errors.Add("Response is required");
            }

            if (string.IsNullOrWhiteSpace(Personality))
            {
                result.IsValid = false;
                result.Errors.Add("Personality is required");
            }

            if (ResponseTimeMs < 0)
            {
                result.IsValid = false;
                result.Errors.Add("Response time cannot be negative");
            }

            if (MessageCount <= 0)
            {
                result.IsValid = false;
                result.Errors.Add("Message count must be positive");
            }

            return result;
        }

        // Helper methods
        public void AddTag(MemoryTag tag)
        {
            if (!Tags.Contains(tag))
            {
                Tags.Add(tag);
            }
        }

        public void RemoveTag(MemoryTag tag)
        {
            Tags.Remove(tag);
        }

        public bool HasTag(string tagName)
        {
            return Tags.Any(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
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

        public void SetExpiration(TimeSpan timeSpan)
        {
            ExpiresAt = DateTime.UtcNow.Add(timeSpan);
        }

        public bool IsExpired()
        {
            return ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
        }

        public ConversationMemory Clone()
        {
            return new ConversationMemory
            {
                Input = Input,
                Response = Response,
                Personality = Personality,
                DeviceId = DeviceId,
                Emotion = Emotion,
                Context = Context,
                Metadata = Metadata,
                Timestamp = Timestamp,
                ExpiresAt = ExpiresAt,
                IsEncrypted = IsEncrypted,
                IsPinned = IsPinned,
                IsArchived = IsArchived,
                RelevanceScore = RelevanceScore,
                MessageCount = MessageCount,
                ResponseTimeMs = ResponseTimeMs,
                ModelUsed = ModelUsed,
                SessionId = SessionId,
                Embedding = Embedding?.ToArray(),
                Tags = new List<MemoryTag>(Tags)
            };
        }

        // Static factory methods
        public static ConversationMemory Create(
            string input,
            string response,
            string personality = "Girlfriend_Caring",
            string? deviceId = null)
        {
            return new ConversationMemory
            {
                Input = input,
                Response = response,
                Personality = personality,
                DeviceId = deviceId,
                Timestamp = DateTime.UtcNow,
                RelevanceScore = 1.0
            };
        }

        public static ConversationMemory CreateFromInteraction(
            ChatInteraction interaction,
            string personality = "Girlfriend_Caring")
        {
            return new ConversationMemory
            {
                Input = interaction.UserInput,
                Response = interaction.AiResponse,
                Personality = personality,
                DeviceId = interaction.DeviceId,
                Emotion = interaction.DetectedEmotion,
                Context = interaction.Context,
                Metadata = interaction.Metadata?.ToString(),
                ResponseTimeMs = interaction.ResponseTimeMs,
                ModelUsed = interaction.ModelUsed,
                SessionId = interaction.SessionId,
                Timestamp = interaction.Timestamp
            };
        }

        // Conversion methods
        public ChatInteraction ToChatInteraction()
        {
            return new ChatInteraction
            {
                UserInput = Input,
                AiResponse = Response,
                DeviceId = DeviceId,
                DetectedEmotion = Emotion,
                Context = Context,
                ResponseTimeMs = ResponseTimeMs,
                ModelUsed = ModelUsed,
                SessionId = SessionId,
                Timestamp = Timestamp
            };
        }

        public ConversationSummary ToSummary()
        {
            return new ConversationSummary
            {
                MemoryId = Id,
                Preview = Response.Length > 100 ? Response.Substring(0, 97) + "..." : Response,
                Personality = Personality,
                Timestamp = Timestamp,
                MessageCount = MessageCount,
                HasAttachments = Metadata?.Contains("\"attachments\"") == true,
                IsPinned = IsPinned,
                Tags = Tags.Select(t => t.Name).ToList(),
                Emotion = Emotion,
                ResponseTimeMs = ResponseTimeMs
            };
        }
    }

    // Supporting classes for ConversationMemory
    public class ChatInteraction
    {
        public string UserInput { get; set; } = string.Empty;
        public string AiResponse { get; set; } = string.Empty;
        public string? DeviceId { get; set; }
        public string? DetectedEmotion { get; set; }
        public string? Context { get; set; }
        public object? Metadata { get; set; }
        public long ResponseTimeMs { get; set; }
        public string? ModelUsed { get; set; }
        public string? SessionId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class ConversationSummary
    {
        public string MemoryId { get; set; } = string.Empty;
        public string Preview { get; set; } = string.Empty;
        public string Personality { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public int MessageCount { get; set; }
        public bool HasAttachments { get; set; }
        public bool IsPinned { get; set; }
        public List<string> Tags { get; set; } = new();
        public string? Emotion { get; set; }
        public long ResponseTimeMs { get; set; }
    }
}