using MISA.Core.Services;
using Newtonsoft.Json;
using System.Net.Http;
using System.Text;
using System.Collections.Concurrent;

namespace MISA.Ollama
{
    public class OllamaClient
    {
        private readonly ConfigService _configService;
        private readonly LoggingService _loggingService;
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly TimeSpan _timeout;
        private readonly int _maxRetries;
        private bool _isRunning;
        private readonly ConcurrentDictionary<string, DateTime> _modelLastUsed;

        public event EventHandler<string>? OnStatusChanged;
        public event EventHandler<string>? OnError;
        public event EventHandler<string>? OnModelDownloaded;

        public OllamaClient(ConfigService configService, LoggingService loggingService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _baseUrl = _configService.GetValue<string>("Ollama.BaseUrl", "http://localhost:11434");
            _timeout = TimeSpan.FromSeconds(_configService.GetValue<int>("Ollama.TimeoutSeconds", 120));
            _maxRetries = _configService.GetValue<int>("Ollama.MaxRetries", 3);
            _modelLastUsed = new ConcurrentDictionary<string, DateTime>();

            _httpClient = new HttpClient()
            {
                Timeout = _timeout,
                BaseAddress = new Uri(_baseUrl)
            };

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MISA-AI/1.0.0");
        }

        public async Task StartAsync()
        {
            if (_isRunning)
            {
                _loggingService.LogWarning("Ollama client is already running");
                return;
            }

            try
            {
                _loggingService.LogInformation("Starting Ollama client...");
                OnStatusChanged?.Invoke(this, "Starting Ollama client...");

                // Check if Ollama is available
                if (!await IsRunningAsync())
                {
                    _loggingService.LogWarning("Ollama service not detected, attempting to start...");
                    await StartOllamaServiceAsync();
                }

                // Wait for Ollama to be ready
                await WaitForOllamaReadyAsync();

                _isRunning = true;
                OnStatusChanged?.Invoke(this, "Ollama client started successfully");
                _loggingService.LogInformation("Ollama client started successfully");
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Failed to start Ollama client: {ex.Message}");
                _loggingService.LogError(ex, "Failed to start Ollama client");
                throw;
            }
        }

        public async Task StopAsync()
        {
            if (!_isRunning)
                return;

            try
            {
                _loggingService.LogInformation("Stopping Ollama client...");
                OnStatusChanged?.Invoke(this, "Stopping Ollama client...");

                _isRunning = false;
                _httpClient?.Dispose();

                OnStatusChanged?.Invoke(this, "Ollama client stopped");
                _loggingService.LogInformation("Ollama client stopped");
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Error stopping Ollama client: {ex.Message}");
                _loggingService.LogError(ex, "Error stopping Ollama client");
            }
        }

        public async Task<bool> IsRunningAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/tags");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task StartOllamaServiceAsync()
        {
            try
            {
                _loggingService.LogInformation("Attempting to start Ollama service...");

                // Try to start Ollama service (platform-specific)
                if (OperatingSystem.IsWindows())
                {
                    await StartOllamaWindowsAsync();
                }
                else if (OperatingSystem.IsLinux())
                {
                    await StartOllamaLinuxAsync();
                }
                else if (OperatingSystem.IsMacOS())
                {
                    await StartOllamaMacOSAsync();
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to start Ollama service");
                throw new Exception("Could not start Ollama service. Please install Ollama manually.", ex);
            }
        }

        private async Task StartOllamaWindowsAsync()
        {
            try
            {
                // Check if Ollama is installed
                var ollamaPath = Environment.GetEnvironmentVariable("OLLAMA_PATH") ??
                                @"C:\Program Files\Ollama\ollama.exe";

                if (!File.Exists(ollamaPath))
                {
                    // Try common installation paths
                    var commonPaths = new[]
                    {
                        @"C:\Program Files\Ollama\ollama.exe",
                        @"C:\Program Files (x86)\Ollama\ollama.exe",
                        Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Programs\Ollama\ollama.exe")
                    };

                    foreach (var path in commonPaths)
                    {
                        if (File.Exists(path))
                        {
                            ollamaPath = path;
                            break;
                        }
                    }
                }

                if (!File.Exists(ollamaPath))
                {
                    throw new FileNotFoundException("Ollama executable not found");
                }

                // Start Ollama service
                var startInfo = new ProcessStartInfo
                {
                    FileName = ollamaPath,
                    Arguments = "serve",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };

                var process = Process.Start(startInfo);
                if (process != null)
                {
                    _loggingService.LogInformation($"Started Ollama service (PID: {process.Id})");

                    // Give it time to start
                    await Task.Delay(3000);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to start Ollama on Windows");
                throw;
            }
        }

        private async Task StartOllamaLinuxAsync()
        {
            try
            {
                // Try systemd service first
                var processInfo = new ProcessStartInfo
                {
                    FileName = "systemctl",
                    Arguments = "start ollama",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(processInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    if (process.ExitCode == 0)
                    {
                        _loggingService.LogInformation("Started Ollama systemd service");
                        await Task.Delay(3000);
                        return;
                    }
                }

                // Fallback to running ollama serve directly
                var serveInfo = new ProcessStartInfo
                {
                    FileName = "ollama",
                    Arguments = "serve",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                var serveProcess = Process.Start(serveInfo);
                if (serveProcess != null)
                {
                    _loggingService.LogInformation($"Started Ollama serve process (PID: {serveProcess.Id})");
                    await Task.Delay(3000);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to start Ollama on Linux");
                throw;
            }
        }

        private async Task StartOllamaMacOSAsync()
        {
            try
            {
                // Try to start Ollama app
                var processInfo = new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = "-a Ollama",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(processInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    _loggingService.LogInformation("Started Ollama app on macOS");
                    await Task.Delay(5000); // macOS apps take longer to start
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to start Ollama on macOS");
                throw;
            }
        }

        private async Task WaitForOllamaReadyAsync()
        {
            var maxWaitTime = TimeSpan.FromMinutes(2);
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < maxWaitTime)
            {
                try
                {
                    var response = await _httpClient.GetAsync("/api/tags");
                    if (response.IsSuccessStatusCode)
                    {
                        _loggingService.LogInformation("Ollama service is ready");
                        return;
                    }
                }
                catch
                {
                    // Service not ready yet
                }

                await Task.Delay(2000);
            }

            throw new TimeoutException("Ollama service did not become ready within the expected time");
        }

        public async Task<List<string>> GetInstalledModelsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/tags");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<OllamaModelsResponse>(json);

                return result?.Models?.Select(m => m.Name).ToList() ?? new List<string>();
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to get installed models");
                return new List<string>();
            }
        }

        public async Task<bool> IsModelInstalledAsync(string modelName)
        {
            var installedModels = await GetInstalledModelsAsync();
            return installedModels.Any(m => m.StartsWith(modelName));
        }

        public async Task DownloadModelAsync(string modelName)
        {
            try
            {
                if (await IsModelInstalledAsync(modelName))
                {
                    _loggingService.LogInformation($"Model {modelName} is already installed");
                    return;
                }

                _loggingService.LogInformation($"Downloading model: {modelName}");
                OnStatusChanged?.Invoke(this, $"Downloading model: {modelName}");

                var request = new { name = modelName };
                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("/api/pull", content);
                response.EnsureSuccessStatusCode();

                // Stream the response to track progress
                var responseStream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(responseStream);

                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    try
                    {
                        var progress = JsonConvert.DeserializeObject<OllamaProgressResponse>(line);
                        if (progress?.Status == "success")
                        {
                            _loggingService.LogInformation($"Successfully downloaded model: {modelName}");
                            OnModelDownloaded?.Invoke(this, modelName);
                            OnStatusChanged?.Invoke(this, $"Model downloaded: {modelName}");
                            return;
                        }
                    }
                    catch
                    {
                        // Ignore JSON parsing errors for progress updates
                    }
                }

                throw new Exception("Model download did not complete successfully");
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Failed to download model {modelName}: {ex.Message}");
                _loggingService.LogError(ex, $"Failed to download model {modelName}");
                throw;
            }
        }

        public async Task<string> GenerateAsync(string prompt, string model, Dictionary<string, object>? options = null)
        {
            if (!_isRunning)
            {
                throw new InvalidOperationException("Ollama client is not running");
            }

            try
            {
                var request = new OllamaGenerateRequest
                {
                    Model = model,
                    Prompt = prompt,
                    Stream = false,
                    Options = options ?? new Dictionary<string, object>()
                };

                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await _httpClient.PostAsync("/api/generate", content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<OllamaGenerateResponse>(responseJson);

                // Update model usage
                _modelLastUsed[model] = DateTime.UtcNow;

                return result?.Response?.Trim() ?? string.Empty;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, $"Failed to generate response from {model}");
                throw new Exception($"Failed to generate response: {ex.Message}", ex);
            }
        }

        public async Task<string> GenerateWithRetryAsync(string prompt, string model, Dictionary<string, object>? options = null)
        {
            Exception? lastException = null;

            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    return await GenerateAsync(prompt, model, options);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _loggingService.LogWarning(ex, $"Generate attempt {attempt} failed for model {model}");

                    if (attempt < _maxRetries)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff
                        await Task.Delay(delay);
                    }
                }
            }

            throw lastException ?? new Exception("All retry attempts failed");
        }

        public async Task<OllamaModelInfo?> GetModelInfoAsync(string modelName)
        {
            try
            {
                var response = await _httpClient.GetAsync($"/api/show?name={modelName}");
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<OllamaModelInfo>(json);
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, $"Failed to get model info for {modelName}");
                return null;
            }
        }

        public async Task DeleteModelAsync(string modelName)
        {
            try
            {
                var request = new { name = modelName };
                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.DeleteAsync("/api/delete");
                response.EnsureSuccessStatusCode();

                _modelLastUsed.TryRemove(modelName, out _);
                _loggingService.LogInformation($"Deleted model: {modelName}");
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, $"Failed to delete model {modelName}");
                throw;
            }
        }

        public string GetStatus()
        {
            if (!_isRunning)
                return "Stopped";

            try
            {
                var modelsCount = GetInstalledModelsAsync().Result.Count;
                return $"Running ({modelsCount} models installed)";
            }
            catch
            {
                return "Running (error checking models)";
            }
        }

        public Dictionary<string, DateTime> GetModelUsageStats()
        {
            return new Dictionary<string, DateTime>(_modelLastUsed);
        }

        public async Task<OllamaVersionResponse?> GetVersionAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/version");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<OllamaVersionResponse>(json);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    // DTO Classes for Ollama API
    public class OllamaModelsResponse
    {
        [JsonProperty("models")]
        public List<OllamaModel>? Models { get; set; }
    }

    public class OllamaModel
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("digest")]
        public string Digest { get; set; } = string.Empty;
    }

    public class OllamaGenerateRequest
    {
        [JsonProperty("model")]
        public string Model { get; set; } = string.Empty;

        [JsonProperty("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonProperty("stream")]
        public bool Stream { get; set; }

        [JsonProperty("options")]
        public Dictionary<string, object> Options { get; set; } = new();
    }

    public class OllamaGenerateResponse
    {
        [JsonProperty("response")]
        public string Response { get; set; } = string.Empty;

        [JsonProperty("done")]
        public bool Done { get; set; }

        [JsonProperty("total_duration")]
        public long TotalDuration { get; set; }

        [JsonProperty("eval_count")]
        public int EvalCount { get; set; }
    }

    public class OllamaProgressResponse
    {
        [JsonProperty("status")]
        public string Status { get; set; } = string.Empty;

        [JsonProperty("completed")]
        public bool Completed { get; set; }
    }

    public class OllamaModelInfo
    {
        [JsonProperty("modelfile")]
        public string Modelfile { get; set; } = string.Empty;

        [JsonProperty("parameters")]
        public string Parameters { get; set; } = string.Empty;

        [JsonProperty("template")]
        public string Template { get; set; } = string.Empty;

        [JsonProperty("details")]
        public OllamaModelDetails? Details { get; set; }
    }

    public class OllamaModelDetails
    {
        [JsonProperty("parameter_size")]
        public string ParameterSize { get; set; } = string.Empty;

        [JsonProperty("quantization_level")]
        public string QuantizationLevel { get; set; } = string.Empty;
    }

    public class OllamaVersionResponse
    {
        [JsonProperty("version")]
        public string Version { get; set; } = string.Empty;

        [JsonProperty("build")]
        public string Build { get; set; } = string.Empty;
    }
}