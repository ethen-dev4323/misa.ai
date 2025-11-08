using MISA.Core.Services;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using Azure.Identity;

namespace MISA.Memory
{
    public class MemoryService
    {
        private readonly ConfigService _configService;
        private readonly SecurityService _securityService;
        private readonly LoggingService _loggingService;
        private readonly MemoryDbContext _dbContext;
        private readonly EmbeddingEngine _embeddingEngine;
        private readonly MemoryCache _memoryCache;
        private readonly CloudSyncService _cloudSyncService;
        private readonly ConflictResolver _conflictResolver;
        private readonly EncryptionService _encryptionService;
        private readonly BackgroundMemoryProcessor _backgroundProcessor;
        private bool _isInitialized;

        public event EventHandler<string>? OnStatusChanged;
        public event EventHandler<string>? OnError;
        public event EventHandler<MemoryStoredEventArgs>? OnMemoryStored;
        public event EventHandler<MemoryRetrievedEventArgs>? OnMemoryRetrieved;

        public MemoryService(ConfigService configService, SecurityService securityService, LoggingService loggingService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _securityService = securityService ?? throw new ArgumentNullException(nameof(securityService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            var dbPath = _configService.GetValue<string>("Memory.DatabasePath", "data/misa.db");
            var optionsBuilder = new DbContextOptionsBuilder<MemoryDbContext>();
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
            _dbContext = new MemoryDbContext(optionsBuilder.Options);

            _embeddingEngine = new EmbeddingEngine(_loggingService);
            _memoryCache = new MemoryCache(new MemoryCacheOptions());
            _encryptionService = new EncryptionService(_securityService);
            _conflictResolver = new ConflictResolver(_loggingService);
            _cloudSyncService = new CloudSyncService(_configService, _loggingService, _encryptionService);
            _backgroundProcessor = new BackgroundMemoryProcessor(_loggingService, _embeddingEngine);
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            try
            {
                _loggingService.LogInformation("Initializing Memory Service...");
                OnStatusChanged?.Invoke(this, "Initializing Memory Service...");

                // Initialize database
                await _dbContext.Database.MigrateAsync();

                // Initialize embedding engine
                await _embeddingEngine.InitializeAsync();

                // Initialize encryption service
                await _encryptionService.InitializeAsync();

                // Initialize cloud sync if enabled
                if (_configService.GetValue<bool>("Memory.CloudSyncEnabled", false))
                {
                    await _cloudSyncService.InitializeAsync();
                }

                // Start background processing
                await _backgroundProcessor.StartAsync();

                _isInitialized = true;
                OnStatusChanged?.Invoke(this, "Memory Service initialized successfully");
                _loggingService.LogInformation("Memory Service initialized successfully");
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Failed to initialize Memory Service: {ex.Message}");
                _loggingService.LogError(ex, "Failed to initialize Memory Service");
                throw;
            }
        }

        public async Task StoreInteractionAsync(string input, string response, string personality, string? deviceId = null)
        {
            try
            {
                var memory = new ConversationMemory
                {
                    Id = Guid.NewGuid().ToString(),
                    Input = input,
                    Response = response,
                    Personality = personality,
                    DeviceId = deviceId,
                    Timestamp = DateTime.UtcNow,
                    Type = MemoryType.Conversation
                };

                // Generate embeddings for semantic search
                memory.Embedding = await _embeddingEngine.GenerateEmbeddingAsync($"{input} {response}");

                // Encrypt sensitive data
                if (_configService.GetValue<bool>("Memory.EncryptionEnabled", true))
                {
                    memory.EncryptedInput = await _encryptionService.EncryptAsync(input);
                    memory.EncryptedResponse = await _encryptionService.EncryptAsync(response);
                }

                // Store in database
                _dbContext.Conversations.Add(memory);
                await _dbContext.SaveChangesAsync();

                // Cache for quick retrieval
                _memoryCache.Set(memory.Id, memory, TimeSpan.FromHours(24));

                // Sync to cloud if enabled
                if (_configService.GetValue<bool>("Memory.CloudSyncEnabled", false))
                {
                    _ = Task.Run(async () => await _cloudSyncService.SyncToCloudAsync(memory));
                }

                // Store in short-term memory
                await StoreInShortTermMemoryAsync(memory);

                OnMemoryStored?.Invoke(this, new MemoryStoredEventArgs { MemoryId = memory.Id, Type = memory.Type });
                _loggingService.LogMemoryOperation("Store", "Conversation", input.Length + response.Length);
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to store interaction");
                OnError?.Invoke(this, $"Failed to store interaction: {ex.Message}");
            }
        }

        public async Task StorePersonalMemoryAsync(string content, MemoryType type, List<string>? tags = null, string? category = null)
        {
            try
            {
                var memory = new PersonalMemory
                {
                    Id = Guid.NewGuid().ToString(),
                    Content = content,
                    Type = type,
                    Tags = tags?.ToList() ?? new List<string>(),
                    Category = category,
                    Timestamp = DateTime.UtcNow,
                    Importance = CalculateImportance(content, type)
                };

                // Generate embeddings
                memory.Embedding = await _embeddingEngine.GenerateEmbeddingAsync(content);

                // Encrypt if sensitive
                if (_configService.GetValue<bool>("Memory.EncryptionEnabled", true) && IsSensitiveContent(content))
                {
                    memory.EncryptedContent = await _encryptionService.EncryptAsync(content);
                    memory.IsEncrypted = true;
                }

                // Store in database
                _dbContext.PersonalMemories.Add(memory);
                await _dbContext.SaveChangesAsync();

                // Cache
                _memoryCache.Set(memory.Id, memory, TimeSpan.FromDays(7));

                // Sync to cloud
                if (_configService.GetValue<bool>("Memory.CloudSyncEnabled", false))
                {
                    _ = Task.Run(async () => await _cloudSyncService.SyncToCloudAsync(memory));
                }

                // Store in appropriate memory tier
                await StoreInAppropriateMemoryTierAsync(memory);

                OnMemoryStored?.Invoke(this, new MemoryStoredEventArgs { MemoryId = memory.Id, Type = memory.Type });
                _loggingService.LogMemoryOperation("Store", type.ToString(), content.Length);
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to store personal memory");
                OnError?.Invoke(this, $"Failed to store personal memory: {ex.Message}");
            }
        }

        public async Task StoreContextualMemoryAsync(string context, Dictionary<string, object> metadata, TimeSpan? expiry = null)
        {
            try
            {
                var memory = new ContextualMemory
                {
                    Id = Guid.NewGuid().ToString(),
                    Context = context,
                    Metadata = metadata,
                    Timestamp = DateTime.UtcNow,
                    ExpiresAt = expiry.HasValue ? DateTime.UtcNow.Add(expiry.Value) : (DateTime?)null,
                    Type = MemoryType.Contextual
                };

                // Generate embeddings
                memory.Embedding = await _embeddingEngine.GenerateEmbeddingAsync(context);

                // Store in database
                _dbContext.ContextualMemories.Add(memory);
                await _dbContext.SaveChangesAsync();

                // Cache with expiry
                var cacheExpiry = expiry ?? TimeSpan.FromHours(1);
                _memoryCache.Set(memory.Id, memory, cacheExpiry);

                OnMemoryStored?.Invoke(this, new MemoryStoredEventArgs { MemoryId = memory.Id, Type = memory.Type });
                _loggingService.LogMemoryOperation("Store", "Contextual", context.Length);
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to store contextual memory");
                OnError?.Invoke(this, $"Failed to store contextual memory: {ex.Message}");
            }
        }

        public async Task<List<MemorySearchResult>> SearchMemoriesAsync(string query, int maxResults = 10, MemoryType? typeFilter = null)
        {
            try
            {
                var queryEmbedding = await _embeddingEngine.GenerateEmbeddingAsync(query);
                var results = new List<MemorySearchResult>();

                // Search conversations
                if (!typeFilter.HasValue || typeFilter.Value == MemoryType.Conversation)
                {
                    var conversationResults = await SearchConversationsAsync(queryEmbedding, query, maxResults);
                    results.AddRange(conversationResults);
                }

                // Search personal memories
                if (!typeFilter.HasValue || typeFilter.Value == MemoryType.Personal)
                {
                    var personalResults = await SearchPersonalMemoriesAsync(queryEmbedding, query, maxResults);
                    results.AddRange(personalResults);
                }

                // Search contextual memories
                if (!typeFilter.HasValue || typeFilter.Value == MemoryType.Contextual)
                {
                    var contextualResults = await SearchContextualMemoriesAsync(queryEmbedding, query, maxResults);
                    results.AddRange(contextualResults);
                }

                // Sort by relevance and limit results
                return results
                    .OrderByDescending(r => r.RelevanceScore)
                    .Take(maxResults)
                    .ToList();
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to search memories");
                return new List<MemorySearchResult>();
            }
        }

        private async Task<List<MemorySearchResult>> SearchConversationsAsync(float[] queryEmbedding, string query, int maxResults)
        {
            var conversations = await _dbContext.Conversations
                .Where(c => c.Type == MemoryType.Conversation)
                .ToListAsync();

            var results = new List<MemorySearchResult>();

            foreach (var conv in conversations)
            {
                if (conv.Embedding != null)
                {
                    var similarity = CalculateCosineSimilarity(queryEmbedding, conv.Embedding);
                    if (similarity > 0.3) // Threshold for relevance
                    {
                        var content = $"{conv.Input} {conv.Response}";
                        var decryptedInput = conv.EncryptedInput != null ?
                            await _encryptionService.DecryptAsync(conv.EncryptedInput) : conv.Input;
                        var decryptedResponse = conv.EncryptedResponse != null ?
                            await _encryptionService.DecryptAsync(conv.EncryptedResponse) : conv.Response;

                        results.Add(new MemorySearchResult
                        {
                            MemoryId = conv.Id,
                            Type = MemoryType.Conversation,
                            Content = $"{decryptedInput} {decryptedResponse}",
                            RelevanceScore = similarity,
                            Timestamp = conv.Timestamp,
                            Metadata = new Dictionary<string, object>
                            {
                                ["Personality"] = conv.Personality,
                                ["DeviceId"] = conv.DeviceId ?? "Unknown"
                            }
                        });
                    }
                }
            }

            return results;
        }

        private async Task<List<MemorySearchResult>> SearchPersonalMemoriesAsync(float[] queryEmbedding, string query, int maxResults)
        {
            var memories = await _dbContext.PersonalMemories
                .Where(m => m.Type == MemoryType.Personal)
                .ToListAsync();

            var results = new List<MemorySearchResult>();

            foreach (var memory in memories)
            {
                if (memory.Embedding != null)
                {
                    var similarity = CalculateCosineSimilarity(queryEmbedding, memory.Embedding);
                    if (similarity > 0.3)
                    {
                        var content = memory.IsEncrypted && memory.EncryptedContent != null ?
                            await _encryptionService.DecryptAsync(memory.EncryptedContent) : memory.Content;

                        results.Add(new MemorySearchResult
                        {
                            MemoryId = memory.Id,
                            Type = MemoryType.Personal,
                            Content = content,
                            RelevanceScore = similarity,
                            Timestamp = memory.Timestamp,
                            Metadata = new Dictionary<string, object>
                            {
                                ["Category"] = memory.Category ?? "General",
                                ["Importance"] = memory.Importance,
                                ["Tags"] = memory.Tags
                            }
                        });
                    }
                }
            }

            return results;
        }

        private async Task<List<MemorySearchResult>> SearchContextualMemoriesAsync(float[] queryEmbedding, string query, int maxResults)
        {
            var memories = await _dbContext.ContextualMemories
                .Where(m => m.Type == MemoryType.Contextual)
                .Where(m => !m.ExpiresAt.HasValue || m.ExpiresAt.Value > DateTime.UtcNow)
                .ToListAsync();

            var results = new List<MemorySearchResult>();

            foreach (var memory in memories)
            {
                if (memory.Embedding != null)
                {
                    var similarity = CalculateCosineSimilarity(queryEmbedding, memory.Embedding);
                    if (similarity > 0.3)
                    {
                        results.Add(new MemorySearchResult
                        {
                            MemoryId = memory.Id,
                            Type = MemoryType.Contextual,
                            Content = memory.Context,
                            RelevanceScore = similarity,
                            Timestamp = memory.Timestamp,
                            Metadata = new Dictionary<string, object>(memory.Metadata)
                        });
                    }
                }
            }

            return results;
        }

        public async Task<List<MemorySearchResult>> GetRecentMemoriesAsync(int count = 10, MemoryType? typeFilter = null)
        {
            try
            {
                var results = new List<MemorySearchResult>();

                if (!typeFilter.HasValue || typeFilter.Value == MemoryType.Conversation)
                {
                    var conversations = await _dbContext.Conversations
                        .OrderByDescending(c => c.Timestamp)
                        .Take(count)
                        .ToListAsync();

                    foreach (var conv in conversations)
                    {
                        var decryptedInput = conv.EncryptedInput != null ?
                            await _encryptionService.DecryptAsync(conv.EncryptedInput) : conv.Input;
                        var decryptedResponse = conv.EncryptedResponse != null ?
                            await _encryptionService.DecryptAsync(conv.EncryptedResponse) : conv.Response;

                        results.Add(new MemorySearchResult
                        {
                            MemoryId = conv.Id,
                            Type = MemoryType.Conversation,
                            Content = $"{decryptedInput} {decryptedResponse}",
                            RelevanceScore = 1.0,
                            Timestamp = conv.Timestamp,
                            Metadata = new Dictionary<string, object>
                            {
                                ["Personality"] = conv.Personality
                            }
                        });
                    }
                }

                if (!typeFilter.HasValue || typeFilter.Value == MemoryType.Personal)
                {
                    var personalMemories = await _dbContext.PersonalMemories
                        .Where(m => m.Type == MemoryType.Personal)
                        .OrderByDescending(m => m.Timestamp)
                        .Take(count)
                        .ToListAsync();

                    foreach (var memory in personalMemories)
                    {
                        var content = memory.IsEncrypted && memory.EncryptedContent != null ?
                            await _encryptionService.DecryptAsync(memory.EncryptedContent) : memory.Content;

                        results.Add(new MemorySearchResult
                        {
                            MemoryId = memory.Id,
                            Type = MemoryType.Personal,
                            Content = content,
                            RelevanceScore = 1.0,
                            Timestamp = memory.Timestamp,
                            Metadata = new Dictionary<string, object>
                            {
                                ["Category"] = memory.Category ?? "General",
                                ["Importance"] = memory.Importance,
                                ["Tags"] = memory.Tags
                            }
                        });
                    }
                }

                return results
                    .OrderByDescending(r => r.Timestamp)
                    .Take(count)
                    .ToList();
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to get recent memories");
                return new List<MemorySearchResult>();
            }
        }

        public async Task<bool> DeleteMemoryAsync(string memoryId)
        {
            try
            {
                // Try to find and delete from all memory types
                var conversation = await _dbContext.Conversations.FindAsync(memoryId);
                if (conversation != null)
                {
                    _dbContext.Conversations.Remove(conversation);
                    await _dbContext.SaveChangesAsync();
                    _memoryCache.Remove(memoryId);
                    return true;
                }

                var personalMemory = await _dbContext.PersonalMemories.FindAsync(memoryId);
                if (personalMemory != null)
                {
                    _dbContext.PersonalMemories.Remove(personalMemory);
                    await _dbContext.SaveChangesAsync();
                    _memoryCache.Remove(memoryId);
                    return true;
                }

                var contextualMemory = await _dbContext.ContextualMemories.FindAsync(memoryId);
                if (contextualMemory != null)
                {
                    _dbContext.ContextualMemories.Remove(contextualMemory);
                    await _dbContext.SaveChangesAsync();
                    _memoryCache.Remove(memoryId);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, $"Failed to delete memory {memoryId}");
                return false;
            }
        }

        public async Task<bool> SyncToCloudAsync()
        {
            try
            {
                if (!_configService.GetValue<bool>("Memory.CloudSyncEnabled", false))
                {
                    return false;
                }

                OnStatusChanged?.Invoke(this, "Syncing memories to cloud...");
                await _cloudSyncService.FullSyncAsync();
                OnStatusChanged?.Invoke(this, "Cloud sync completed");
                return true;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to sync to cloud");
                OnError?.Invoke(this, $"Cloud sync failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SyncFromCloudAsync()
        {
            try
            {
                if (!_configService.GetValue<bool>("Memory.CloudSyncEnabled", false))
                {
                    return false;
                }

                OnStatusChanged?.Invoke(this, "Syncing memories from cloud...");
                await _cloudSyncService.DownloadFromCloudAsync();
                OnStatusChanged?.Invoke(this, "Cloud download completed");
                return true;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to sync from cloud");
                OnError?.Invoke(this, $"Cloud download failed: {ex.Message}");
                return false;
            }
        }

        private async Task StoreInShortTermMemoryAsync(IMemory memory)
        {
            // Store in short-term memory for quick access
            _memoryCache.Set($"short_term_{memory.Id}", memory, TimeSpan.FromHours(1));
        }

        private async Task StoreInAppropriateMemoryTierAsync(PersonalMemory memory)
        {
            // Determine storage tier based on importance and type
            var expiry = memory.Type switch
            {
                MemoryType.ShortTerm => TimeSpan.FromDays(1),
                MemoryType.MediumTerm => TimeSpan.FromDays(30),
                MemoryType.LongTerm => TimeSpan.MaxValue,
                _ => TimeSpan.FromDays(7)
            };

            // Adjust expiry based on importance
            if (memory.Importance > 0.8)
            {
                expiry = TimeSpan.MaxValue; // Keep forever for high importance
            }

            _memoryCache.Set($"tiered_{memory.Id}", memory, expiry);
        }

        private double CalculateImportance(string content, MemoryType type)
        {
            var importance = 0.5; // Base importance

            // Boost importance for certain keywords
            var importantKeywords = new[] { "important", "remember", "never forget", "critical", "urgent", "favorite" };
            var contentLower = content.ToLower();

            importance += importantKeywords.Count(keyword => contentLower.Contains(keyword)) * 0.1;

            // Adjust based on content length (longer content might be more important)
            importance += Math.Min(0.2, content.Length / 10000.0);

            // Type-based importance
            importance += type switch
            {
                MemoryType.LongTerm => 0.3,
                MemoryType.Personal => 0.2,
                MemoryType.Contextual => 0.1,
                _ => 0
            };

            return Math.Min(1.0, importance);
        }

        private bool IsSensitiveContent(string content)
        {
            var sensitiveKeywords = new[] { "password", "secret", "private", "confidential", "ssn", "credit card", "bank" };
            var contentLower = content.ToLower();
            return sensitiveKeywords.Any(keyword => contentLower.Contains(keyword));
        }

        private float CalculateCosineSimilarity(float[] vectorA, float[] vectorB)
        {
            if (vectorA.Length != vectorB.Length)
                return 0;

            var dotProduct = 0.0f;
            var magnitudeA = 0.0f;
            var magnitudeB = 0.0f;

            for (int i = 0; i < vectorA.Length; i++)
            {
                dotProduct += vectorA[i] * vectorB[i];
                magnitudeA += vectorA[i] * vectorA[i];
                magnitudeB += vectorB[i] * vectorB[i];
            }

            magnitudeA = (float)Math.Sqrt(magnitudeA);
            magnitudeB = (float)Math.Sqrt(magnitudeB);

            if (magnitudeA == 0 || magnitudeB == 0)
                return 0;

            return dotProduct / (magnitudeA * magnitudeB);
        }

        public async Task<MemoryStats> GetMemoryStatsAsync()
        {
            try
            {
                var conversationCount = await _dbContext.Conversations.CountAsync();
                var personalMemoryCount = await _dbContext.PersonalMemories.CountAsync();
                var contextualMemoryCount = await _dbContext.ContextualMemories.CountAsync();

                var totalMemories = conversationCount + personalMemoryCount + contextualMemoryCount;

                return new MemoryStats
                {
                    TotalMemories = totalMemories,
                    Conversations = conversationCount,
                    PersonalMemories = personalMemoryCount,
                    ContextualMemories = contextualMemoryCount,
                    CacheSize = _memoryCache.Count,
                    CloudSyncEnabled = _configService.GetValue<bool>("Memory.CloudSyncEnabled", false),
                    EncryptionEnabled = _configService.GetValue<bool>("Memory.EncryptionEnabled", true)
                };
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to get memory stats");
                return new MemoryStats();
            }
        }

        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                return _isInitialized && await _dbContext.Database.CanConnectAsync();
            }
            catch
            {
                return false;
            }
        }

        public string GetStatus()
        {
            return _isInitialized ? "Active" : "Initializing";
        }

        public async Task StopAsync()
        {
            try
            {
                await _backgroundProcessor.StopAsync();
                await _cloudSyncService.StopAsync();
                _memoryCache.Dispose();
                await _dbContext.Dispose();

                _loggingService.LogInformation("Memory Service stopped");
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Error stopping Memory Service");
            }
        }
    }

    public class MemorySearchResult
    {
        public string MemoryId { get; set; } = string.Empty;
        public MemoryType Type { get; set; }
        public string Content { get; set; } = string.Empty;
        public float RelevanceScore { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public class MemoryStoredEventArgs : EventArgs
    {
        public string MemoryId { get; set; } = string.Empty;
        public MemoryType Type { get; set; }
    }

    public class MemoryRetrievedEventArgs : EventArgs
    {
        public string MemoryId { get; set; } = string.Empty;
        public MemoryType Type { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    public class MemoryStats
    {
        public int TotalMemories { get; set; }
        public int Conversations { get; set; }
        public int PersonalMemories { get; set; }
        public int ContextualMemories { get; set; }
        public int CacheSize { get; set; }
        public bool CloudSyncEnabled { get; set; }
        public bool EncryptionEnabled { get; set; }
    }

    public enum MemoryType
    {
        Conversation,
        Personal,
        Contextual,
        ShortTerm,
        MediumTerm,
        LongTerm
    }

    public interface IMemory
    {
        string Id { get; set; }
        MemoryType Type { get; set; }
        DateTime Timestamp { get; set; }
        float[]? Embedding { get; set; }
    }
}