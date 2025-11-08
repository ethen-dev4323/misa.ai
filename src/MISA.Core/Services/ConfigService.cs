using Newtonsoft.Json;
using System.IO;

namespace MISA.Core.Services
{
    public class ConfigService
    {
        private const string CONFIG_FILE = "config/misa-config.json";
        private Dictionary<string, object> _config;
        private readonly LoggingService _loggingService;

        public ConfigService()
        {
            _loggingService = new LoggingService();
        }

        public async Task InitializeAsync()
        {
            try
            {
                // Ensure config directory exists
                Directory.CreateDirectory("config");

                if (File.Exists(CONFIG_FILE))
                {
                    var json = await File.ReadAllTextAsync(CONFIG_FILE);
                    _config = JsonConvert.DeserializeObject<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
                }
                else
                {
                    _config = GetDefaultConfig();
                    await SaveConfigAsync();
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to initialize configuration");
                _config = GetDefaultConfig();
            }
        }

        private Dictionary<string, object> GetDefaultConfig()
        {
            return new Dictionary<string, object>
            {
                ["App"] = new Dictionary<string, object>
                {
                    ["Version"] = "1.0.0",
                    ["Name"] = "MISA AI",
                    ["LogLevel"] = "Information"
                },
                ["Ollama"] = new Dictionary<string, object>
                {
                    ["BaseUrl"] = "http://localhost:11434",
                    ["DefaultModel"] = "mixtral:8x7b",
                    ["TimeoutSeconds"] = 120,
                    ["MaxRetries"] = 3
                },
                ["Personality"] = new Dictionary<string, object>
                {
                    ["DefaultType"] = "Girlfriend",
                    ["DefaultVariant"] = "Caring",
                    ["AutoSwitch"] = true,
                    ["LearningEnabled"] = true
                },
                ["Memory"] = new Dictionary<string, object>
                {
                    ["DatabasePath"] = "data/misa.db",
                    ["MaxMemoryDays"] = 365,
                    ["CloudSyncEnabled"] = false,
                    ["EncryptionEnabled"] = true
                },
                ["Communication"] = new Dictionary<string, object>
                {
                    ["WebSocketPort"] = 8080,
                    ["HttpApiPort"] = 8081,
                    ["WebRTCPPort"] = 8082,
                    ["MaxConnections"] = 10
                },
                ["Background"] = new Dictionary<string, object>
                {
                    ["Enabled"] = true,
                    ["ScreenCaptureInterval"] = 1000,
                    ["MaxStorageGB"] = 10,
                    ["ActivityTracking"] = true
                },
                ["Updates"] = new Dictionary<string, object>
                {
                    ["AutoCheck"] = true,
                    ["CheckIntervalHours"] = 24,
                    ["UpdateChannel"] = "Stable",
                    ["BackupBeforeUpdate"] = true
                },
                ["Security"] = new Dictionary<string, object>
                {
                    ["EncryptionKeyLength"] = 256,
                    ["SessionTimeoutMinutes"] = 60,
                    ["RequireDeviceAuth"] = true,
                    ["AuditLogging"] = true
                },
                ["Resources"] = new Dictionary<string, object>
                {
                    ["MaxMemoryUsageGB"] = 8,
                    ["MaxCpuUsagePercent"] = 50,
                    ["EnableGpuAcceleration"] = true,
                    ["ThermalThrottling"] = true
                }
            };
        }

        public T GetValue<T>(string key, T defaultValue = default!)
        {
            try
            {
                var keys = key.Split('.');
                var current = (object?)_config;

                foreach (var k in keys)
                {
                    if (current is Dictionary<string, object> dict && dict.TryGetValue(k, out var value))
                    {
                        current = value;
                    }
                    else
                    {
                        return defaultValue;
                    }
                }

                if (current is T directValue)
                {
                    return directValue;
                }

                return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(current)) ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        public async Task SetValueAsync(string key, object value)
        {
            try
            {
                var keys = key.Split('.');
                var current = _config;

                for (int i = 0; i < keys.Length - 1; i++)
                {
                    if (!current.TryGetValue(keys[i], out var nextObj) || !(nextObj is Dictionary<string, object> next))
                    {
                        next = new Dictionary<string, object>();
                        current[keys[i]] = next;
                    }
                    current = next;
                }

                current[keys.Last()] = value;
                await SaveConfigAsync();
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, $"Failed to set configuration value: {key}");
            }
        }

        private async Task SaveConfigAsync()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                await File.WriteAllTextAsync(CONFIG_FILE, json);
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to save configuration");
            }
        }

        public async Task ResetToDefaultsAsync()
        {
            _config = GetDefaultConfig();
            await SaveConfigAsync();
        }

        public Dictionary<string, object> GetAllSettings()
        {
            return new Dictionary<string, object>(_config);
        }
    }
}