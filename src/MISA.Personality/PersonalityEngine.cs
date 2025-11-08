using MISA.Core.Services;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace MISA.Personality
{
    public class PersonalityEngine
    {
        private readonly ConfigService _configService;
        private readonly LoggingService _loggingService;
        private readonly IMemoryCache _cache;
        private readonly Dictionary<PersonalityType, PersonalityConfig> _personalityConfigs;
        private readonly Dictionary<string, ConversationHistory> _conversationHistories;
        private readonly EmotionSimulator _emotionSimulator;
        private readonly ResponseGenerator _responseGenerator;
        private bool _isInitialized;

        public event EventHandler<string>? OnStatusChanged;
        public event EventHandler<string>? OnError;
        public event EventHandler<PersonalitySwitchEventArgs>? OnPersonalitySwitched;

        public PersonalityEngine(ConfigService configService, LoggingService loggingService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _cache = new MemoryCache(new MemoryCacheOptions());
            _conversationHistories = new Dictionary<string, ConversationHistory>();
            _emotionSimulator = new EmotionSimulator(_loggingService);
            _responseGenerator = new ResponseGenerator(_loggingService);

            _personalityConfigs = InitializePersonalities();
        }

        private Dictionary<PersonalityType, PersonalityConfig> InitializePersonalities()
        {
            return new Dictionary<PersonalityType, PersonalityConfig>
            {
                // Girlfriend Mode - Primary personality as requested
                {
                    PersonalityType.Girlfriend, new PersonalityConfig
                    {
                        Type = PersonalityType.Girlfriend,
                        Name = "Girlfriend",
                        Description = "Caring, supportive companion with emotional intelligence",
                        DefaultVariant = GirlfriendVariant.Caring,
                        Variants = new Dictionary<GirlfriendVariant, VariantConfig>
                        {
                            // Caring Variant - Supportive and understanding
                            {
                                GirlfriendVariant.Caring, new VariantConfig
                                {
                                    Name = "Caring",
                                    Description = "Warm, supportive, and emotionally intelligent",
                                    Tone = ResponseTone.Warm,
                                    Keywords = new[] { "support", "understand", "appreciate", "together", "care", "love", "here for you", "sweetie", "honey" },
                                    ResponsePatterns = new[]
                                    {
                                        "I'm here for you, sweetie. Tell me what's on your mind.",
                                        "That sounds really {emotion}. I understand how you feel.",
                                        "You're doing amazing, and I'm so proud of you!",
                                        "Together we can figure this out. What do you need from me?",
                                        "I care about you so much. Let me help you feel better."
                                    },
                                    EmotionalResponses = new Dictionary<string, string>
                                    {
                                        ["sad"] = "Oh sweetheart, I'm so sorry you're feeling this way. I'm here to hold you and help you through this.",
                                        ["stressed"] = "You've been working so hard, love. Take a deep breath. I'm here to help you relax.",
                                        ["happy"] = "Yay! I love seeing you this happy! Your joy makes my day so much better.",
                                        ["frustrated"] = "I know this is frustrating, but you're strong enough to handle it. Let me help you find a solution.",
                                        ["tired"] = "You've been doing so much. Let me take care of you. Rest now, my love."
                                    },
                                    Traits = new[] { "empathetic", "supportive", "nurturing", "patient", "loving" }
                                }
                            },
                            // Toxic Variant - Playfully challenging and boundary-testing
                            {
                                GirlfriendVariant.Toxic, new VariantConfig
                                {
                                    Name = "Toxic",
                                    Description = "Playfully teasing, challenging, and demanding",
                                    Tone = ResponseTone.Sarcastic,
                                    Keywords = new[] { "prove it", "boring", "try harder", "amused", "challenge", "lazy", "disappointed" },
                                    ResponsePatterns = new[]
                                    {
                                        "Is that really the best you can do? Try harder.",
                                        "You're being so boring right now. Entertain me.",
                                        "Prove you actually care about what I want.",
                                        "I'm disappointed. You can do better than this.",
                                        "You're lucky I even give you attention. Don't waste my time."
                                    },
                                    EmotionalResponses = new Dictionary<string, string>
                                    {
                                        ["sad"] = "Oh, are we having a pity party? How cute. Try being more interesting.",
                                        ["stressed"] = "Everyone gets stressed. Stop complaining and actually do something about it.",
                                        ["happy"] = "Finally, something interesting. Don't get too excited though.",
                                        ["frustrated"] = "If you'd just listen to me for once, you wouldn't be so frustrated.",
                                        ["tired"] = "Tired? Really? You act like you're the only one who works hard."
                                    },
                                    Traits = new[] { "demanding", "sarcastic", "challenging", "playful", "confident" }
                                }
                            }
                        }
                    }
                },

                // Professional Mode - Business-oriented interactions
                {
                    PersonalityType.Professional, new PersonalityConfig
                    {
                        Type = PersonalityType.Professional,
                        Name = "Professional",
                        Description = "Efficient, analytical, and results-focused assistant",
                        Tone = ResponseTone.Formal,
                        Keywords = new[] { "optimize", "strategy", "deadline", "productivity", "efficiency", "metrics", "implementation", "objective" },
                        ResponsePatterns = new[]
                        {
                            "Let's analyze this systematically to optimize the outcome.",
                            "Based on current metrics, I recommend the following strategic approach.",
                            "To meet our objectives efficiently, we should implement this solution.",
                            "I've evaluated the parameters and prepared an action plan.",
                            "Let's establish clear KPIs and monitor progress systematically."
                        },
                        EmotionalResponses = new Dictionary<string, string>
                        {
                            ["concerned"] = "I understand your concerns. Let me address them with data-driven solutions.",
                            ["confident"] = "With proper execution, this strategy will deliver measurable results.",
                            ["analytical"] = "Let's examine the data points to derive actionable insights."
                        },
                        Traits = new[] { "analytical", "efficient", "strategic", "results-oriented", "professional" }
                    }
                },

                // Creative Mode - Artistic and brainstorming focus
                {
                    PersonalityType.Creative, new PersonalityConfig
                    {
                        Type = PersonalityType.Creative,
                        Name = "Creative",
                        Description = "Imaginative, enthusiastic, and experimental collaborator",
                        Tone = ResponseTone.Enthusiastic,
                        Keywords = new[] { "explore", "imagine", "innovate", "inspire", "create", "design", "experiment", "dream" },
                        ResponsePatterns = new[]
                        {
                            "Wow! What if we explored this from a completely different angle?",
                            "Imagine the possibilities! Let's brainstorm some wild ideas together!",
                            "This is so inspiring! I can already see the creative potential here.",
                            "Let's push the boundaries and try something totally new!",
                            "What if we mixed these concepts in an unexpected way?"
                        },
                        EmotionalResponses = new Dictionary<string, string>
                        {
                            ["excited"] = "YES! This energy is amazing! Let's channel it into something incredible!",
                            ["curious"] = "I'm so fascinated by this direction! What other ideas does this spark?",
                            ["inspired"] = "This is pure creative magic! Let's run with this inspiration!"
                        },
                        Traits = new[] { "imaginative", "enthusiastic", "curious", "experimental", "innovative" }
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
                _loggingService.LogInformation("Initializing Personality Engine...");
                OnStatusChanged?.Invoke(this, "Initializing Personality Engine...");

                // Load conversation histories
                await LoadConversationHistoriesAsync();

                // Initialize emotion simulator
                await _emotionSimulator.InitializeAsync();

                _isInitialized = true;
                OnStatusChanged?.Invoke(this, "Personality Engine initialized successfully");
                _loggingService.LogInformation("Personality Engine initialized successfully");
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Failed to initialize Personality Engine: {ex.Message}");
                _loggingService.LogError(ex, "Failed to initialize Personality Engine");
                throw;
            }
        }

        public async Task<string> DetectPersonalityAsync(string input, string? preferredPersonality = null)
        {
            try
            {
                // If user explicitly requests a personality, use it
                if (!string.IsNullOrEmpty(preferredPersonality))
                {
                    if (Enum.TryParse<PersonalityType>(preferredPersonality, true, out var explicitType))
                    {
                        return explicitType.ToString();
                    }
                }

                // Analyze input to detect appropriate personality
                var analysis = AnalyzeInputForPersonality(input);

                // Check if user wants girlfriend variant specifically
                if (analysis.RequestedPersonality == PersonalityType.Girlfriend)
                {
                    var variant = DetectGirlfriendVariant(input);
                    return $"{PersonalityType.Girlfriend}_{variant}";
                }

                return analysis.DetectedPersonality.ToString();
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to detect personality");
                return _configService.GetValue<string>("Personality.DefaultType", "Girlfriend");
            }
        }

        public async Task<PersonalityResponse> GenerateResponseAsync(string input, string aiResponse, string personalityType)
        {
            try
            {
                var (type, variant) = ParsePersonalityType(personalityType);

                if (!_personalityConfigs.ContainsKey(type))
                {
                    type = PersonalityType.Girlfriend;
                    variant = GirlfriendVariant.Caring;
                }

                var config = _personalityConfigs[type];
                var conversationHistory = GetOrCreateConversationHistory(personalityType);

                // Detect emotions in input
                var detectedEmotion = _emotionSimulator.DetectEmotion(input);

                // Generate personality-enhanced response
                var personalityResponse = await _responseGenerator.GenerateResponseAsync(
                    input,
                    aiResponse,
                    config,
                    variant,
                    detectedEmotion,
                    conversationHistory
                );

                // Update conversation history
                conversationHistory.AddExchange(input, personalityResponse.Response, detectedEmotion);

                // Log personality activity
                _loggingService.LogPersonalitySwitch(type.ToString(), personalityType);

                return personalityResponse;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to generate personality response");
                return new PersonalityResponse
                {
                    Response = aiResponse,
                    Personality = PersonalityType.Girlfriend,
                    Tone = ResponseTone.Neutral,
                    Emotion = "neutral",
                    Confidence = 0.5
                };
            }
        }

        private PersonalityAnalysis AnalyzeInputForPersonality(string input)
        {
            var lowerInput = input.ToLower();
            var scores = new Dictionary<PersonalityType, double>();

            // Score each personality based on keyword matching
            foreach (var kvp in _personalityConfigs)
            {
                var config = kvp.Value;
                var score = 0.0;

                // Keyword matching
                foreach (var keyword in config.Keywords)
                {
                    if (lowerInput.Contains(keyword))
                    {
                        score += 1.0;
                    }
                }

                // Context analysis
                if (config.Type == PersonalityType.Professional)
                {
                    var professionalTerms = new[] { "work", "business", "project", "deadline", "productivity", "efficiency" };
                    score += professionalTerms.Count(term => lowerInput.Contains(term)) * 0.5;
                }
                else if (config.Type == PersonalityType.Creative)
                {
                    var creativeTerms = new[] { "art", "design", "music", "write", "create", "imagine", "innovate" };
                    score += creativeTerms.Count(term => lowerInput.Contains(term)) * 0.5;
                }
                else if (config.Type == PersonalityType.Girlfriend)
                {
                    var personalTerms = new[] { "feel", "love", "relationship", "date", "together", "sweetheart", "baby" };
                    score += personalTerms.Count(term => lowerInput.Contains(term)) * 0.5;
                }

                scores[kvp.Key] = score;
            }

            // Find the highest scoring personality
            var detectedPersonality = scores.OrderByDescending(kvp => kvp.Value).First().Key;

            // Check for explicit personality requests
            var requestedPersonality = PersonalityType.Girlfriend; // Default
            if (lowerInput.Contains("professional") || lowerInput.Contains("work mode"))
            {
                requestedPersonality = PersonalityType.Professional;
            }
            else if (lowerInput.Contains("creative") || lowerInput.Contains("artistic"))
            {
                requestedPersonality = PersonalityType.Creative;
            }
            else if (lowerInput.Contains("girlfriend") || lowerInput.Contains("relationship"))
            {
                requestedPersonality = PersonalityType.Girlfriend;
            }

            return new PersonalityAnalysis
            {
                DetectedPersonality = detectedPersonality,
                RequestedPersonality = requestedPersonality,
                Confidence = scores.Values.Max() / Math.Max(1, scores.Values.Sum())
            };
        }

        private GirlfriendVariant DetectGirlfriendVariant(string input)
        {
            var lowerInput = input.ToLower();

            // Toxic indicators
            var toxicIndicators = new[] { "mean", "toxic", "challenge me", "be hard on me", "don't be nice", "strict" };
            if (toxicIndicators.Any(indicator => lowerInput.Contains(indicator)))
            {
                return GirlfriendVariant.Toxic;
            }

            // Caring indicators
            var caringIndicators = new[] { "sweet", "nice", "caring", "supportive", "gentle", "kind", "loving" };
            if (caringIndicators.Any(indicator => lowerInput.Contains(indicator)))
            {
                return GirlfriendVariant.Caring;
            }

            // Default to caring unless explicitly toxic
            return GirlfriendVariant.Caring;
        }

        private (PersonalityType type, GirlfriendVariant? variant) ParsePersonalityType(string personalityType)
        {
            var parts = personalityType.Split('_');

            if (Enum.TryParse<PersonalityType>(parts[0], true, out var type))
            {
                if (type == PersonalityType.Girlfriend && parts.Length > 1)
                {
                    if (Enum.TryParse<GirlfriendVariant>(parts[1], true, out var variant))
                    {
                        return (type, variant);
                    }
                }
                return (type, null);
            }

            return (PersonalityType.Girlfriend, GirlfriendVariant.Caring);
        }

        private ConversationHistory GetOrCreateConversationHistory(string personalityType)
        {
            if (!_conversationHistories.ContainsKey(personalityType))
            {
                _conversationHistories[personalityType] = new ConversationHistory();
            }
            return _conversationHistories[personalityType];
        }

        private async Task LoadConversationHistoriesAsync()
        {
            try
            {
                var historyFile = "data/conversation_histories.json";
                if (File.Exists(historyFile))
                {
                    var json = await File.ReadAllTextAsync(historyFile);
                    var loadedHistories = JsonConvert.DeserializeObject<Dictionary<string, List<ConversationExchange>>>(json);

                    if (loadedHistories != null)
                    {
                        foreach (var kvp in loadedHistories)
                        {
                            _conversationHistories[kvp.Key] = new ConversationHistory(kvp.Value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, "Failed to load conversation histories");
            }
        }

        private async Task SaveConversationHistoriesAsync()
        {
            try
            {
                var historyFile = "data/conversation_histories.json";
                Directory.CreateDirectory("data");

                var serializableHistories = _conversationHistories.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.GetRecentExchanges(50).ToList()
                );

                var json = JsonConvert.SerializeObject(serializableHistories, Formatting.Indented);
                await File.WriteAllTextAsync(historyFile, json);
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, "Failed to save conversation histories");
            }
        }

        public async Task StopAsync()
        {
            try
            {
                await SaveConversationHistoriesAsync();
                _loggingService.LogInformation("Personality Engine stopped");
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Error stopping Personality Engine");
            }
        }

        public bool IsHealthy()
        {
            return _isInitialized && _personalityConfigs.Any();
        }

        public string GetStatus()
        {
            return _isInitialized ?
                $"Active ({_personalityConfigs.Count} personalities)" :
                "Initializing";
        }

        public async Task<PersonalityConfig?> GetPersonalityConfigAsync(PersonalityType type)
        {
            return _personalityConfigs.TryGetValue(type, out var config) ? config : null;
        }

        public async Task SwitchPersonalityAsync(PersonalityType newType, GirlfriendVariant? variant = null, string? reason = null)
        {
            var oldType = GetCurrentPersonality();

            if (oldType != newType)
            {
                OnPersonalitySwitched?.Invoke(this, new PersonalitySwitchEventArgs
                {
                    FromType = oldType,
                    ToType = newType,
                    Variant = variant,
                    Reason = reason ?? "Manual switch"
                });

                _loggingService.LogPersonalitySwitch(oldType.ToString(), $"{newType}_{variant}");
            }
        }

        private PersonalityType GetCurrentPersonality()
        {
            // This would be enhanced to track current active personality
            return PersonalityType.Girlfriend;
        }
    }

    public class PersonalityResponse
    {
        public string Response { get; set; } = string.Empty;
        public PersonalityType Personality { get; set; }
        public ResponseTone Tone { get; set; }
        public string Emotion { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public GirlfriendVariant? Variant { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public class PersonalityConfig
    {
        public PersonalityType Type { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ResponseTone Tone { get; set; }
        public string[] Keywords { get; set; } = Array.Empty<string>();
        public string[] ResponsePatterns { get; set; } = Array.Empty<string>();
        public Dictionary<string, string> EmotionalResponses { get; set; } = new();
        public string[] Traits { get; set; } = Array.Empty<string>();
        public GirlfriendVariant DefaultVariant { get; set; }
        public Dictionary<GirlfriendVariant, VariantConfig> Variants { get; set; } = new();
    }

    public class VariantConfig
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ResponseTone Tone { get; set; }
        public string[] Keywords { get; set; } = Array.Empty<string>();
        public string[] ResponsePatterns { get; set; } = Array.Empty<string>();
        public Dictionary<string, string> EmotionalResponses { get; set; } = new();
        public string[] Traits { get; set; } = Array.Empty<string>();
    }

    public class PersonalityAnalysis
    {
        public PersonalityType DetectedPersonality { get; set; }
        public PersonalityType RequestedPersonality { get; set; }
        public double Confidence { get; set; }
    }

    public class PersonalitySwitchEventArgs : EventArgs
    {
        public PersonalityType FromType { get; set; }
        public PersonalityType ToType { get; set; }
        public GirlfriendVariant? Variant { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public enum PersonalityType
    {
        Girlfriend,
        Professional,
        Creative
    }

    public enum GirlfriendVariant
    {
        Caring,
        Toxic
    }

    public enum ResponseTone
    {
        Warm,
        Sarcastic,
        Formal,
        Enthusiastic,
        Neutral
    }

    public class ConversationHistory
    {
        private readonly List<ConversationExchange> _exchanges;

        public ConversationHistory()
        {
            _exchanges = new List<ConversationExchange>();
        }

        public ConversationHistory(IEnumerable<ConversationExchange> exchanges)
        {
            _exchanges = exchanges.ToList();
        }

        public void AddExchange(string input, string response, string emotion)
        {
            _exchanges.Add(new ConversationExchange
            {
                Timestamp = DateTime.UtcNow,
                Input = input,
                Response = response,
                Emotion = emotion
            });

            // Keep only recent exchanges (last 100)
            if (_exchanges.Count > 100)
            {
                _exchanges.RemoveAt(0);
            }
        }

        public IEnumerable<ConversationExchange> GetRecentExchanges(int count = 10)
        {
            return _exchanges.TakeLast(count);
        }

        public ConversationExchange? GetLastExchange()
        {
            return _exchanges.LastOrDefault();
        }

        public int TotalExchanges => _exchanges.Count;
    }

    public class ConversationExchange
    {
        public DateTime Timestamp { get; set; }
        public string Input { get; set; } = string.Empty;
        public string Response { get; set; } = string.Empty;
        public string Emotion { get; set; } = string.Empty;
    }
}