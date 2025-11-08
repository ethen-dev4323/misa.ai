using MISA.Core.Communication;
using MISA.Core.Services;
using MISA.Ollama;
using MISA.Personality;
using MISA.Memory;
using MISA.Remote;
using MISA.Background;
using MISA.Builder;
using MISA.Updater;

namespace MISA.Core.Engine
{
    public class MISAEngine
    {
        private readonly ConfigService _configService;
        private readonly SecurityService _securityService;
        private readonly OllamaClient _ollamaClient;
        private readonly ModelManager _modelManager;
        private readonly PersonalityEngine _personalityEngine;
        private readonly WebSocketServer _webSocketServer;
        private readonly HTTPAPIServer _httpApiServer;
        private readonly MemoryService _memoryService;
        private readonly WebRTCServer _webRTCServer;
        private readonly BackgroundService _backgroundService;
        private readonly UpdateManager _updateManager;
        private readonly ProjectBuilder _projectBuilder;
        private readonly DeviceDiscovery _deviceDiscovery;
        private readonly ResourceManager _resourceManager;
        private readonly TaskScheduler _taskScheduler;
        private readonly LoggingService _loggingService;

        private CancellationTokenSource _cancellationTokenSource;
        private bool _isRunning;

        public event EventHandler<string>? OnStatusChanged;
        public event EventHandler<string>? OnError;
        public event EventHandler<string>? OnMessage;

        public MISAEngine(ConfigService configService, SecurityService securityService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _securityService = securityService ?? throw new ArgumentNullException(nameof(securityService));

            _cancellationTokenSource = new CancellationTokenSource();
            _loggingService = new LoggingService();

            InitializeServices();
        }

        private void InitializeServices()
        {
            _ollamaClient = new OllamaClient(_configService, _loggingService);
            _modelManager = new ModelManager(_ollamaClient, _configService, _loggingService);
            _personalityEngine = new PersonalityEngine(_configService, _loggingService);
            _memoryService = new MemoryService(_configService, _securityService, _loggingService);
            _webSocketServer = new WebSocketServer(_configService, _securityService, _loggingService);
            _httpApiServer = new HTTPAPIServer(_configService, _securityService, _loggingService);
            _webRTCServer = new WebRTCServer(_configService, _securityService, _loggingService);
            _backgroundService = new BackgroundService(_configService, _loggingService);
            _updateManager = new UpdateManager(_configService, _loggingService);
            _projectBuilder = new ProjectBuilder(_configService, _loggingService);
            _deviceDiscovery = new DeviceDiscovery(_configService, _loggingService);
            _resourceManager = new ResourceManager(_configService, _loggingService);
            _taskScheduler = new TaskScheduler(_configService, _loggingService);

            // Wire up event handlers
            WireEventHandlers();
        }

        private void WireEventHandlers()
        {
            _ollamaClient.OnStatusChanged += (sender, status) => OnStatusChanged?.Invoke(this, $"Ollama: {status}");
            _modelManager.OnStatusChanged += (sender, status) => OnStatusChanged?.Invoke(this, $"Model Manager: {status}");
            _personalityEngine.OnStatusChanged += (sender, status) => OnStatusChanged?.Invoke(this, $"Personality: {status}");
            _memoryService.OnStatusChanged += (sender, status) => OnStatusChanged?.Invoke(this, $"Memory: {status}");
            _webRTCServer.OnStatusChanged += (sender, status) => OnStatusChanged?.Invoke(this, $"WebRTC: {status}");

            _ollamaClient.OnError += (sender, error) => OnError?.Invoke(this, $"Ollama Error: {error}");
            _modelManager.OnError += (sender, error) => OnError?.Invoke(this, $"Model Manager Error: {error}");
            _personalityEngine.OnError += (sender, error) => OnError?.Invoke(this, $"Personality Error: {error}");
        }

        public async Task StartAsync()
        {
            if (_isRunning)
            {
                _loggingService.LogWarning("MISA Engine is already running");
                return;
            }

            try
            {
                OnStatusChanged?.Invoke(this, "Starting MISA Engine...");

                // Start core services in dependency order
                await _securityService.InitializeAsync();
                await _memoryService.InitializeAsync();
                await _resourceManager.InitializeAsync();

                // Start Ollama and download essential models
                await _ollamaClient.StartAsync();
                await _modelManager.InitializeEssentialModelsAsync();

                // Initialize AI services
                await _personalityEngine.InitializeAsync();

                // Start communication services
                await _webSocketServer.StartAsync();
                await _httpApiServer.StartAsync();
                await _webRTCServer.StartAsync();
                await _deviceDiscovery.StartAsync();

                // Start background services
                await _backgroundService.StartAsync(_cancellationTokenSource.Token);
                await _taskScheduler.StartAsync(_cancellationTokenSource.Token);

                // Start update manager (in background)
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromMinutes(5)); // Delay startup
                    await _updateManager.StartAsync(_cancellationTokenSource.Token);
                }, _cancellationTokenSource.Token);

                _isRunning = true;
                OnStatusChanged?.Invoke(this, "MISA Engine started successfully");
                _loggingService.LogInformation("MISA Engine started successfully");
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Failed to start MISA Engine: {ex.Message}");
                _loggingService.LogError(ex, "Failed to start MISA Engine");
                throw;
            }
        }

        public async Task StopAsync()
        {
            if (!_isRunning)
            {
                return;
            }

            try
            {
                OnStatusChanged?.Invoke(this, "Stopping MISA Engine...");
                _loggingService.LogInformation("Stopping MISA Engine...");

                // Cancel all background operations
                _cancellationTokenSource.Cancel();

                // Stop services in reverse order
                await _taskScheduler.StopAsync();
                await _backgroundService.StopAsync();
                await _deviceDiscovery.StopAsync();
                await _webRTCServer.StopAsync();
                await _httpApiServer.StopAsync();
                await _webSocketServer.StopAsync();
                await _personalityEngine.StopAsync();
                await _modelManager.StopAsync();
                await _ollamaClient.StopAsync();
                await _memoryService.StopAsync();

                _isRunning = false;
                OnStatusChanged?.Invoke(this, "MISA Engine stopped successfully");
                _loggingService.LogInformation("MISA Engine stopped successfully");
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Error stopping MISA Engine: {ex.Message}");
                _loggingService.LogError(ex, "Error stopping MISA Engine");
            }
            finally
            {
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();
            }
        }

        public async Task<string> ProcessInputAsync(string input, string? personalityType = null, string? deviceId = null)
        {
            try
            {
                _loggingService.LogInformation($"Processing input: {input.Substring(0, Math.Min(100, input.Length))}...");

                // Detect or use specified personality type
                var detectedPersonality = await _personalityEngine.DetectPersonalityAsync(input, personalityType);

                // Store input in memory
                await _memoryService.StoreInteractionAsync(input, null, detectedPersonality, deviceId);

                // Select optimal AI model
                var selectedModel = await _modelManager.SelectOptimalModelAsync(input, detectedPersonality);

                // Generate response using Ollama
                var rawResponse = await _ollamaClient.GenerateAsync(input, selectedModel);

                // Apply personality transformation
                var personalityResponse = await _personalityEngine.GenerateResponseAsync(input, rawResponse, detectedPersonality);

                // Store response in memory
                await _memoryService.StoreInteractionAsync(input, personalityResponse.Response, detectedPersonality, deviceId);

                OnMessage?.Invoke(this, personalityResponse.Response);
                return personalityResponse.Response;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Error processing input");
                OnError?.Invoke(this, $"Error processing input: {ex.Message}");
                return "I'm sorry, I encountered an error while processing your request. Please try again.";
            }
        }

        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                var healthChecks = new[]
                {
                    _ollamaClient.IsRunningAsync(),
                    _memoryService.IsHealthyAsync(),
                    _personalityEngine.IsHealthyAsync(),
                    _resourceManager.IsHealthyAsync()
                };

                var results = await Task.WhenAll(healthChecks);
                return results.All(r => r);
            }
            catch
            {
                return false;
            }
        }

        public EngineStatus GetStatus()
        {
            return new EngineStatus
            {
                IsRunning = _isRunning,
                OllamaStatus = _ollamaClient.GetStatus(),
                ModelStatus = _modelManager.GetStatus(),
                PersonalityStatus = _personalityEngine.GetStatus(),
                MemoryStatus = _memoryService.GetStatus(),
                ResourceStatus = _resourceManager.GetStatus()
            };
        }
    }

    public class EngineStatus
    {
        public bool IsRunning { get; set; }
        public string OllamaStatus { get; set; } = string.Empty;
        public string ModelStatus { get; set; } = string.Empty;
        public string PersonalityStatus { get; set; } = string.Empty;
        public string MemoryStatus { get; set; } = string.Empty;
        public string ResourceStatus { get; set; } = string.Empty;
    }
}