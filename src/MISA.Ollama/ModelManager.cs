using MISA.Core.Services;
using Newtonsoft.Json;

namespace MISA.Ollama
{
    public class ModelManager
    {
        private readonly OllamaClient _ollamaClient;
        private readonly ConfigService _configService;
        private readonly LoggingService _loggingService;
        private readonly Dictionary<string, ModelConfig> _modelConfigs;
        private readonly HashSet<string> _essentialModels;
        private readonly Dictionary<string, ModelUsageStats> _usageStats;
        private readonly Timer _cleanupTimer;
        private bool _isInitialized;

        public event EventHandler<string>? OnStatusChanged;
        public event EventHandler<string>? OnError;
        public event EventHandler<string>? OnModelLoaded;
        public event EventHandler<string>? OnModelUnloaded;

        public ModelManager(OllamaClient ollamaClient, ConfigService configService, LoggingService loggingService)
        {
            _ollamaClient = ollamaClient ?? throw new ArgumentNullException(nameof(ollamaClient));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            _modelConfigs = GetModelConfigurations();
            _essentialModels = new HashSet<string>
            {
                "mixtral:8x7b",
                "codellama:13b",
                "dolphin-mistral:7b",
                "wizardcoder:3b"
            };

            _usageStats = new Dictionary<string, ModelUsageStats>();
            _cleanupTimer = new Timer(PerformCleanup, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        }

        private Dictionary<string, ModelConfig> GetModelConfigurations()
        {
            return new Dictionary<string, ModelConfig>
            {
                // Large Models - Complex Reasoning & Conversation
                {
                    "mixtral:8x7b", new ModelConfig
                    {
                        Name = "mixtral:8x7b",
                        Category = ModelCategory.Conversation,
                        Size = ModelSize.Large,
                        Description = "Advanced conversational AI with complex reasoning capabilities",
                        RequiredMemoryGB = 16,
                        MaxTokens = 32768,
                        SupportsStreaming = true,
                        EstimatedDownloadGB = 4.7,
                        UseCases = new[] { "conversation", "reasoning", "analysis", "creative_writing" }
                    }
                },
                {
                    "mixtral:8x22b", new ModelConfig
                    {
                        Name = "mixtral:8x22b",
                        Category = ModelCategory.Conversation,
                        Size = ModelSize.VeryLarge,
                        Description = "Ultra-large conversational model for complex tasks",
                        RequiredMemoryGB = 24,
                        MaxTokens = 65536,
                        SupportsStreaming = true,
                        EstimatedDownloadGB = 13.0,
                        UseCases = new[] { "complex_reasoning", "research", "advanced_analysis" },
                        Optional = true
                    }
                },

                // Coding Models
                {
                    "codellama:13b", new ModelConfig
                    {
                        Name = "codellama:13b",
                        Category = ModelCategory.Coding,
                        Size = ModelSize.Medium,
                        Description = "Specialized code generation and technical assistance",
                        RequiredMemoryGB = 10,
                        MaxTokens = 16384,
                        SupportsStreaming = true,
                        EstimatedDownloadGB = 7.4,
                        UseCases = new[] { "coding", "debugging", "code_review", "technical_writing" }
                    }
                },
                {
                    "codellama:34b", new ModelConfig
                    {
                        Name = "codellama:34b",
                        Category = ModelCategory.Coding,
                        Size = ModelSize.Large,
                        Description = "Advanced code generation for complex programming tasks",
                        RequiredMemoryGB = 20,
                        MaxTokens = 32768,
                        SupportsStreaming = true,
                        EstimatedDownloadGB = 19.0,
                        UseCases = new[] { "complex_coding", "architecture_design", "advanced_debugging" },
                        Optional = true
                    }
                },

                // Creative Models
                {
                    "dolphin-mistral:7b", new ModelConfig
                    {
                        Name = "dolphin-mistral:7b",
                        Category = ModelCategory.Creative,
                        Size = ModelSize.Medium,
                        Description = "Creative AI for brainstorming and artistic tasks",
                        RequiredMemoryGB = 8,
                        MaxTokens = 8192,
                        SupportsStreaming = true,
                        EstimatedDownloadGB = 3.8,
                        UseCases = new[] { "creative_writing", "brainstorming", "art", "design" }
                    }
                },

                // Quick Response Models
                {
                    "wizardcoder:3b", new ModelConfig
                    {
                        Name = "wizardcoder:3b",
                        Category = ModelCategory.Coding,
                        Size = ModelSize.Small,
                        Description = "Fast code generation for quick assistance",
                        RequiredMemoryGB = 4,
                        MaxTokens = 4096,
                        SupportsStreaming = true,
                        EstimatedDownloadGB = 1.8,
                        UseCases = new[] { "quick_coding", "syntax_help", "small_snippets" }
                    }
                },
                {
                    "phi:2.7b", new ModelConfig
                    {
                        Name = "phi:2.7b",
                        Category = ModelCategory.Conversation,
                        Size = ModelSize.Small,
                        Description = "Lightweight conversational model for quick responses",
                        RequiredMemoryGB = 4,
                        MaxTokens = 2048,
                        SupportsStreaming = true,
                        EstimatedDownloadGB = 1.6,
                        UseCases = new[] { "quick_chat", "simple_questions", "status_updates" },
                        Optional = true
                    }
                },

                // Multilingual Models
                {
                    "llama2:7b-chat", new ModelConfig
                    {
                        Name = "llama2:7b-chat",
                        Category = ModelCategory.Conversation,
                        Size = ModelSize.Medium,
                        Description = "Multilingual conversational AI",
                        RequiredMemoryGB = 8,
                        MaxTokens = 4096,
                        SupportsStreaming = true,
                        EstimatedDownloadGB = 3.8,
                        UseCases = new[] { "multilingual", "translation", "international_chat" },
                        Optional = true
                    }
                },

                // Vision Models (when available)
                {
                    "llava:7b", new ModelConfig
                    {
                        Name = "llava:7b",
                        Category = ModelCategory.Vision,
                        Size = ModelSize.Medium,
                        Description = "Multimodal model for vision-language tasks",
                        RequiredMemoryGB = 8,
                        MaxTokens = 4096,
                        SupportsStreaming = true,
                        EstimatedDownloadGB = 4.5,
                        UseCases = new[] { "image_analysis", "visual_qa", "screen_understanding" },
                        Optional = true,
                        SupportsVision = true
                    }
                }
            };
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            try
            {
                _loggingService.LogInformation("Initializing Model Manager...");
                OnStatusChanged?.Invoke(this, "Initializing Model Manager...");

                // Load usage statistics from storage
                await LoadUsageStatsAsync();

                // Check system resources
                var systemInfo = GetSystemInfo();
                _loggingService.LogInformation($"System Resources: {systemInfo.TotalMemoryGB}GB RAM, GPU: {(systemInfo.HasGPU ? "Yes" : "No")}");

                // Initialize essential models
                await InitializeEssentialModelsAsync();

                _isInitialized = true;
                OnStatusChanged?.Invoke(this, "Model Manager initialized successfully");
                _loggingService.LogInformation("Model Manager initialized successfully");
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Failed to initialize Model Manager: {ex.Message}");
                _loggingService.LogError(ex, "Failed to initialize Model Manager");
                throw;
            }
        }

        public async Task InitializeEssentialModelsAsync()
        {
            var tasks = new List<Task>();

            foreach (var modelName in _essentialModels)
            {
                if (_modelConfigs.ContainsKey(modelName))
                {
                    tasks.Add(EnsureModelAvailableAsync(modelName));
                }
            }

            await Task.WhenAll(tasks);
        }

        private async Task EnsureModelAvailableAsync(string modelName)
        {
            try
            {
                if (await _ollamaClient.IsModelInstalledAsync(modelName))
                {
                    _loggingService.LogInformation($"Model {modelName} is already available");
                    return;
                }

                _loggingService.LogInformation($"Installing essential model: {modelName}");
                await _ollamaClient.DownloadModelAsync(modelName);
                _loggingService.LogInformation($"Successfully installed model: {modelName}");
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, $"Failed to install essential model {modelName}");
                // Continue with other models even if one fails
            }
        }

        public async Task<string> SelectOptimalModelAsync(string prompt, string? personalityType = null)
        {
            try
            {
                var availableModels = await _ollamaClient.GetInstalledModelsAsync();
                var systemInfo = GetSystemInfo();

                // Analyze prompt to determine task type
                var taskType = AnalyzePromptType(prompt);

                // Get candidate models for this task type
                var candidates = _modelConfigs.Values
                    .Where(config => config.UseCases.Contains(taskType))
                    .Where(config => availableModels.Any(m => m.StartsWith(config.Name)))
                    .ToList();

                if (!candidates.Any())
                {
                    // Fallback to any available model
                    candidates = _modelConfigs.Values
                        .Where(config => availableModels.Any(m => m.StartsWith(config.Name)))
                        .ToList();
                }

                if (!candidates.Any())
                {
                    throw new InvalidOperationException("No models are available. Please install at least one model.");
                }

                // Filter by system requirements
                candidates = candidates
                    .Where(config => config.RequiredMemoryGB <= systemInfo.AvailableMemoryGB)
                    .ToList();

                // Sort by performance (prefer smaller, faster models for quick tasks)
                var sortedCandidates = candidates
                    .OrderByDescending(config => CalculateModelScore(config, prompt.Length, systemInfo))
                    .ToList();

                var selectedModel = sortedCandidates.First().Name;

                // Update usage statistics
                UpdateModelUsage(selectedModel);

                _loggingService.LogInformation($"Selected model: {selectedModel} for task: {taskType}");
                return selectedModel;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to select optimal model");
                throw;
            }
        }

        private string AnalyzePromptType(string prompt)
        {
            var lowerPrompt = prompt.ToLower();

            // Check for coding-related keywords
            var codingKeywords = new[] { "code", "function", "programming", "debug", "algorithm", "script", "develop", "implement" };
            if (codingKeywords.Any(keyword => lowerPrompt.Contains(keyword)))
            {
                return "coding";
            }

            // Check for creative tasks
            var creativeKeywords = new[] { "create", "write", "design", "brainstorm", "imagine", "story", "poem", "art" };
            if (creativeKeywords.Any(keyword => lowerPrompt.Contains(keyword)))
            {
                return "creative_writing";
            }

            // Check for analysis tasks
            var analysisKeywords = new[] { "analyze", "explain", "compare", "evaluate", "review", "assess", "examine" };
            if (analysisKeywords.Any(keyword => lowerPrompt.Contains(keyword)))
            {
                return "analysis";
            }

            // Check for reasoning tasks
            var reasoningKeywords = new[] { "why", "how", "what if", "should i", "recommend", "decide", "solve" };
            if (reasoningKeywords.Any(keyword => lowerPrompt.Contains(keyword)))
            {
                return "reasoning";
            }

            // Default to conversation
            return "conversation";
        }

        private double CalculateModelScore(ModelConfig config, int promptLength, SystemInfo systemInfo)
        {
            double score = 0;

            // Base score for category match
            score += config.Category switch
            {
                ModelCategory.Coding => promptLength > 100 ? 10 : 8,
                ModelCategory.Creative => promptLength > 50 ? 9 : 7,
                ModelCategory.Conversation => promptLength < 500 ? 10 : 8,
                ModelCategory.Vision => 15, // Prefer vision models for visual tasks
                _ => 5
            };

            // Memory efficiency bonus
            if (config.RequiredMemoryGB <= systemInfo.AvailableMemoryGB * 0.5)
            {
                score += 5;
            }
            else if (config.RequiredMemoryGB > systemInfo.AvailableMemoryGB)
            {
                score -= 20; // Heavy penalty for insufficient memory
            }

            // Size preference based on prompt complexity
            if (promptLength < 100 && config.Size == ModelSize.Small)
            {
                score += 3;
            }
            else if (promptLength > 500 && config.Size == ModelSize.Large)
            {
                score += 5;
            }

            // Usage history bonus (models used successfully get a boost)
            if (_usageStats.ContainsKey(config.Name))
            {
                var stats = _usageStats[config.Name];
                if (stats.SuccessRate > 0.9)
                {
                    score += 2;
                }
            }

            return Math.Max(0, score);
        }

        public async Task<bool> DownloadModelAsync(string modelName)
        {
            try
            {
                if (!_modelConfigs.ContainsKey(modelName))
                {
                    _loggingService.LogWarning($"Unknown model: {modelName}");
                    return false;
                }

                var config = _modelConfigs[modelName];
                var systemInfo = GetSystemInfo();

                if (config.RequiredMemoryGB > systemInfo.AvailableMemoryGB)
                {
                    _loggingService.LogWarning($"Insufficient memory for {modelName}. Required: {config.RequiredMemoryGB}GB, Available: {systemInfo.AvailableMemoryGB}GB");
                    return false;
                }

                await _ollamaClient.DownloadModelAsync(modelName);
                OnModelLoaded?.Invoke(this, modelName);
                return true;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, $"Failed to download model {modelName}");
                return false;
            }
        }

        public async Task<bool> RemoveModelAsync(string modelName)
        {
            try
            {
                if (_essentialModels.Contains(modelName))
                {
                    _loggingService.LogWarning($"Cannot remove essential model: {modelName}");
                    return false;
                }

                await _ollamaClient.DeleteModelAsync(modelName);
                OnModelUnloaded?.Invoke(this, modelName);
                return true;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, $"Failed to remove model {modelName}");
                return false;
            }
        }

        public async Task<List<ModelConfig>> GetAvailableModelsAsync()
        {
            try
            {
                var installedModels = await _ollamaClient.GetInstalledModelsAsync();
                var availableConfigs = _modelConfigs.Values
                    .Where(config => installedModels.Any(m => m.StartsWith(config.Name)))
                    .ToList();

                return availableConfigs;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to get available models");
                return new List<ModelConfig>();
            }
        }

        public async Task<List<ModelConfig>> GetRecommendedModelsAsync()
        {
            var systemInfo = GetSystemInfo();
            var availableModels = await GetAvailableModelsAsync();

            return _modelConfigs.Values
                .Where(config => config.RequiredMemoryGB <= systemInfo.AvailableMemoryGB)
                .Where(config => !config.Optional || availableModels.Any(m => m.StartsWith(config.Name)))
                .OrderBy(config => config.RequiredMemoryGB)
                .ToList();
        }

        public async Task OptimizeModelsAsync()
        {
            try
            {
                _loggingService.LogInformation("Starting model optimization...");
                OnStatusChanged?.Invoke(this, "Optimizing models...");

                var systemInfo = GetSystemInfo();
                var usageStats = GetModelUsageStats();

                // Remove unused models if memory is limited
                if (systemInfo.AvailableMemoryGB < 8)
                {
                    await RemoveUnusedModelsAsync(usageStats);
                }

                // Download frequently used models
                await DownloadFrequentlyUsedModelsAsync(usageStats);

                _loggingService.LogInformation("Model optimization completed");
                OnStatusChanged?.Invoke(this, "Model optimization completed");
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to optimize models");
                OnError?.Invoke(this, $"Model optimization failed: {ex.Message}");
            }
        }

        private async Task RemoveUnusedModelsAsync(Dictionary<string, DateTime> usageStats)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-30); // 30 days
            var unusedModels = usageStats
                .Where(kvp => kvp.Value < cutoffDate)
                .Select(kvp => kvp.Key)
                .Where(model => !_essentialModels.Contains(model))
                .ToList();

            foreach (var model in unusedModels)
            {
                await RemoveModelAsync(model);
            }
        }

        private async Task DownloadFrequentlyUsedModelsAsync(Dictionary<string, DateTime> usageStats)
        {
            var frequentModels = usageStats
                .Where(kvp => DateTime.UtcNow - kvp.Value < TimeSpan.FromDays(7))
                .Select(kvp => kvp.Key)
                .Where(model => _modelConfigs.ContainsKey(model))
                .ToList();

            foreach (var model in frequentModels)
            {
                await EnsureModelAvailableAsync(model);
            }
        }

        private SystemInfo GetSystemInfo()
        {
            var totalMemory = GC.GetTotalMemory(false) / (1024 * 1024 * 1024); // Rough estimate
            var availableMemory = Math.Max(4, 16 - totalMemory); // Conservative estimate

            return new SystemInfo
            {
                TotalMemoryGB = 16, // Assume 16GB for now
                AvailableMemoryGB = availableMemory,
                HasGPU = true, // Assume GPU is available
                CpuCores = Environment.ProcessorCount
            };
        }

        private void UpdateModelUsage(string modelName)
        {
            if (!_usageStats.ContainsKey(modelName))
            {
                _usageStats[modelName] = new ModelUsageStats();
            }

            _usageStats[modelName].UsageCount++;
            _usageStats[modelName].LastUsed = DateTime.UtcNow;
        }

        private async Task LoadUsageStatsAsync()
        {
            try
            {
                var statsFile = "data/model_usage_stats.json";
                if (File.Exists(statsFile))
                {
                    var json = await File.ReadAllTextAsync(statsFile);
                    var loadedStats = JsonConvert.DeserializeObject<Dictionary<string, ModelUsageStats>>(json);
                    if (loadedStats != null)
                    {
                        foreach (var kvp in loadedStats)
                        {
                            _usageStats[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, "Failed to load model usage statistics");
            }
        }

        private async Task SaveUsageStatsAsync()
        {
            try
            {
                var statsFile = "data/model_usage_stats.json";
                Directory.CreateDirectory("data");
                var json = JsonConvert.SerializeObject(_usageStats, Formatting.Indented);
                await File.WriteAllTextAsync(statsFile, json);
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, "Failed to save model usage statistics");
            }
        }

        private void PerformCleanup(object? state)
        {
            try
            {
                _ = Task.Run(async () =>
                {
                    await OptimizeModelsAsync();
                    await SaveUsageStatsAsync();
                });
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to perform model cleanup");
            }
        }

        public Dictionary<string, DateTime> GetModelUsageStats()
        {
            return _usageStats.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.LastUsed);
        }

        public async Task StopAsync()
        {
            try
            {
                _cleanupTimer?.Dispose();
                await SaveUsageStatsAsync();
                _loggingService.LogInformation("Model Manager stopped");
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Error stopping Model Manager");
            }
        }

        public string GetStatus()
        {
            var installedCount = _ollamaClient.GetInstalledModelsAsync().Result.Count;
            var totalModels = _modelConfigs.Count;
            return $"{installedCount}/{totalModels} models available";
        }
    }

    public class ModelConfig
    {
        public string Name { get; set; } = string.Empty;
        public ModelCategory Category { get; set; }
        public ModelSize Size { get; set; }
        public string Description { get; set; } = string.Empty;
        public int RequiredMemoryGB { get; set; }
        public int MaxTokens { get; set; }
        public bool SupportsStreaming { get; set; }
        public double EstimatedDownloadGB { get; set; }
        public string[] UseCases { get; set; } = Array.Empty<string>();
        public bool Optional { get; set; }
        public bool SupportsVision { get; set; }
    }

    public enum ModelCategory
    {
        Conversation,
        Coding,
        Creative,
        Vision,
        Analysis
    }

    public enum ModelSize
    {
        Small,    // < 4GB
        Medium,   // 4-8GB
        Large,    // 8-16GB
        VeryLarge // > 16GB
    }

    public class ModelUsageStats
    {
        public int UsageCount { get; set; }
        public DateTime LastUsed { get; set; } = DateTime.UtcNow;
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public double SuccessRate => UsageCount > 0 ? (double)SuccessCount / UsageCount : 0;
    }

    public class SystemInfo
    {
        public int TotalMemoryGB { get; set; }
        public int AvailableMemoryGB { get; set; }
        public bool HasGPU { get; set; }
        public int CpuCores { get; set; }
    }
}