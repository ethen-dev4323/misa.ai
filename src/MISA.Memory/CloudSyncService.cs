using MISA.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MISA.Memory
{
    public class CloudSyncService
    {
        private readonly ConfigService _configService;
        private readonly LoggingService _loggingService;
        private readonly EncryptionService _encryptionService;
        private readonly Dictionary<string, ICloudProvider> _providers;
        private readonly SemaphoreSlim _syncSemaphore;
        private readonly Timer _autoSyncTimer;
        private bool _isInitialized;
        private bool _isAutoSyncEnabled;
        private TimeSpan _autoSyncInterval;

        public event EventHandler<CloudSyncEventArgs>? OnSyncStarted;
        public event EventHandler<CloudSyncEventArgs>? OnSyncCompleted;
        public event EventHandler<CloudSyncEventArgs>? OnSyncFailed;
        public event EventHandler<CloudConflictEventArgs>? OnConflictDetected;

        public CloudSyncService(ConfigService configService, LoggingService loggingService, EncryptionService encryptionService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));

            _providers = new Dictionary<string, ICloudProvider>();
            _syncSemaphore = new SemaphoreSlim(1, 1);

            _isAutoSyncEnabled = _configService.GetValue<bool>("Memory.CloudSyncEnabled", false);
            _autoSyncInterval = TimeSpan.FromHours(_configService.GetValue<int>("Memory.CloudSyncIntervalHours", 1));

            if (_isAutoSyncEnabled)
            {
                _autoSyncTimer = new Timer(AutoSyncCallback, null, _autoSyncInterval, _autoSyncInterval);
            }
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            try
            {
                _loggingService.LogInformation("Initializing Cloud Sync Service...");
                OnStatusChanged?.Invoke(this, "Initializing Cloud Sync Service...");

                // Initialize cloud providers
                await InitializeProvidersAsync();

                // Test connectivity
                await TestProviderConnectivityAsync();

                _isInitialized = true;
                OnStatusChanged?.Invoke(this, "Cloud Sync Service initialized successfully");
                _loggingService.LogInformation("Cloud Sync Service initialized successfully");
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Failed to initialize Cloud Sync Service: {ex.Message}");
                _loggingService.LogError(ex, "Failed to initialize Cloud Sync Service");
                throw;
            }
        }

        private async Task InitializeProvidersAsync()
        {
            try
            {
                // Azure Blob Storage Provider
                var azureConnectionString = _configService.GetValue<string>("Cloud.AzureConnectionString");
                if (!string.IsNullOrEmpty(azureConnectionString))
                {
                    var azureProvider = new AzureBlobProvider(azureConnectionString, _loggingService);
                    await azureProvider.InitializeAsync();
                    _providers["Azure"] = azureProvider;
                    _loggingService.LogInformation("Azure Blob Storage provider initialized");
                }

                // AWS S3 Provider (placeholder for future implementation)
                var awsAccessKey = _configService.GetValue<string>("Cloud.AWSAccessKey");
                var awsSecretKey = _configService.GetValue<string>("Cloud.AWSSecretKey");
                var awsRegion = _configService.GetValue<string>("Cloud.AWSRegion", "us-east-1");
                var awsBucket = _configService.GetValue<string>("Cloud.AWSBucket");

                if (!string.IsNullOrEmpty(awsAccessKey) && !string.IsNullOrEmpty(awsSecretKey))
                {
                    // var s3Provider = new S3Provider(awsAccessKey, awsSecretKey, awsRegion, awsBucket, _loggingService);
                    // await s3Provider.InitializeAsync();
                    // _providers["AWS"] = s3Provider;
                    _loggingService.LogInformation("AWS S3 provider configuration found (implementation pending)");
                }

                // Set primary provider
                var primaryProviderName = _configService.GetValue<string>("Cloud.PrimaryProvider", "Azure");
                if (_providers.ContainsKey(primaryProviderName))
                {
                    _primaryProvider = _providers[primaryProviderName];
                    _loggingService.LogInformation($"Primary cloud provider set to: {primaryProviderName}");
                }
                else if (_providers.Any())
                {
                    _primaryProvider = _providers.Values.First();
                    _loggingService.LogInformation($"Primary cloud provider defaulted to: {_providers.Keys.First()}");
                }

                if (_primaryProvider == null)
                {
                    _loggingService.LogWarning("No cloud providers configured - cloud sync will be disabled");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to initialize cloud providers");
            }
        }

        private async Task TestProviderConnectivityAsync()
        {
            if (_primaryProvider == null)
                return;

            try
            {
                await _primaryProvider.TestConnectivityAsync();
                _loggingService.LogInformation("Cloud provider connectivity test passed");
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, "Cloud provider connectivity test failed");
                throw new InvalidOperationException("Cloud provider connectivity test failed", ex);
            }
        }

        public async Task<bool> SyncToCloudAsync<T>(T memory) where T : IMemory
        {
            if (_primaryProvider == null)
            {
                _loggingService.LogWarning("Cannot sync to cloud - no provider configured");
                return false;
            }

            await _syncSemaphore.WaitAsync();
            try
            {
                OnSyncStarted?.Invoke(this, new CloudSyncEventArgs
                {
                    MemoryId = GetMemoryId(memory),
                    MemoryType = memory.Type.ToString(),
                    Direction = "Upload",
                    Timestamp = DateTime.UtcNow
                });

                // Serialize and encrypt memory
                var serializedMemory = await SerializeMemoryAsync(memory);
                var cloudId = await _primaryProvider.UploadAsync(GetMemoryTypeName(memory), GetMemoryId(memory), serializedMemory);

                // Update sync status
                await UpdateSyncStatusAsync(GetMemoryId(memory), memory.Type.ToString(), cloudId, "Success");

                OnSyncCompleted?.Invoke(this, new CloudSyncEventArgs
                {
                    MemoryId = GetMemoryId(memory),
                    MemoryType = memory.Type.ToString(),
                    CloudId = cloudId,
                    Direction = "Upload",
                    Timestamp = DateTime.UtcNow
                });

                _loggingService.LogMemoryOperation("Cloud Upload", memory.Type.ToString(), serializedMemory.Length);
                return true;
            }
            catch (Exception ex)
            {
                await UpdateSyncStatusAsync(GetMemoryId(memory), memory.Type.ToString(), null, "Failed", ex.Message);

                OnSyncFailed?.Invoke(this, new CloudSyncEventArgs
                {
                    MemoryId = GetMemoryId(memory),
                    MemoryType = memory.Type.ToString(),
                    Direction = "Upload",
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.UtcNow
                });

                _loggingService.LogError(ex, $"Failed to sync memory to cloud: {GetMemoryId(memory)}");
                return false;
            }
            finally
            {
                _syncSemaphore.Release();
            }
        }

        public async Task<T?> SyncFromCloudAsync<T>(string memoryId) where T : class, IMemory
        {
            if (_primaryProvider == null)
            {
                _loggingService.LogWarning("Cannot sync from cloud - no provider configured");
                return null;
            }

            await _syncSemaphore.WaitAsync();
            try
            {
                OnSyncStarted?.Invoke(this, new CloudSyncEventArgs
                {
                    MemoryId = memoryId,
                    MemoryType = typeof(T).Name,
                    Direction = "Download",
                    Timestamp = DateTime.UtcNow
                });

                var memoryTypeName = GetMemoryTypeName<T>();
                var cloudData = await _primaryProvider.DownloadAsync(memoryTypeName, memoryId);

                if (cloudData == null)
                {
                    _loggingService.LogWarning($"Memory not found in cloud: {memoryId}");
                    return null;
                }

                // Deserialize and decrypt memory
                var memory = await DeserializeMemoryAsync<T>(cloudData);

                OnSyncCompleted?.Invoke(this, new CloudSyncEventArgs
                {
                    MemoryId = memoryId,
                    MemoryType = memory.Type.ToString(),
                    Direction = "Download",
                    Timestamp = DateTime.UtcNow
                });

                _loggingService.LogMemoryOperation("Cloud Download", memory.Type.ToString(), cloudData.Length);
                return memory;
            }
            catch (Exception ex)
            {
                OnSyncFailed?.Invoke(this, new CloudSyncEventArgs
                {
                    MemoryId = memoryId,
                    MemoryType = typeof(T).Name,
                    Direction = "Download",
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.UtcNow
                });

                _loggingService.LogError(ex, $"Failed to sync memory from cloud: {memoryId}");
                return null;
            }
            finally
            {
                _syncSemaphore.Release();
            }
        }

        public async Task<List<CloudMemoryInfo>> ListCloudMemoriesAsync(string? memoryType = null, int limit = 100)
        {
            if (_primaryProvider == null)
            {
                _loggingService.LogWarning("Cannot list cloud memories - no provider configured");
                return new List<CloudMemoryInfo>();
            }

            try
            {
                var memoryTypeName = memoryType ?? null;
                var cloudItems = await _primaryProvider.ListAsync(memoryTypeName, limit);

                var memoryInfos = cloudItems.Select(item => new CloudMemoryInfo
                {
                    Id = item.Id,
                    Name = item.Name,
                    Size = item.Size,
                    LastModified = item.LastModified,
                    ContentType = item.ContentType,
                    MemoryType = ExtractMemoryTypeFromName(item.Name)
                }).ToList();

                _loggingService.LogInformation($"Listed {memoryInfos.Count} memories from cloud");
                return memoryInfos;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to list cloud memories");
                return new List<CloudMemoryInfo>();
            }
        }

        public async Task<bool> DeleteFromCloudAsync(string memoryId, string memoryType)
        {
            if (_primaryProvider == null)
                return false;

            try
            {
                await _primaryProvider.DeleteAsync(memoryType, memoryId);
                _loggingService.LogInformation($"Deleted memory from cloud: {memoryId}");
                return true;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, $"Failed to delete memory from cloud: {memoryId}");
                return false;
            }
        }

        public async Task<bool> FullSyncAsync()
        {
            if (_primaryProvider == null)
                return false;

            await _syncSemaphore.WaitAsync();
            try
            {
                _loggingService.LogInformation("Starting full cloud synchronization...");
                OnStatusChanged?.Invoke(this, "Starting full cloud sync...");

                // Get all memories that need syncing
                var memoriesToSync = await GetMemoriesToSyncAsync();
                var successCount = 0;
                var failureCount = 0;

                foreach (var memoryInfo in memoriesToSync)
                {
                    try
                    {
                        var memory = await LoadMemoryAsync(memoryInfo.Id, memoryInfo.Type);
                        if (memory != null)
                        {
                            var success = await SyncToCloudAsync(memory);
                            if (success)
                                successCount++;
                            else
                                failureCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogError(ex, $"Failed to sync memory {memoryInfo.Id} during full sync");
                        failureCount++;
                    }
                }

                // Check for memories in cloud that aren't locally
                await SyncFromCloudToLocalAsync();

                _loggingService.LogInformation($"Full sync completed: {successCount} successful, {failureCount} failed");
                OnStatusChanged?.Invoke(this, $"Full sync completed: {successCount} successful, {failureCount} failed");

                return failureCount == 0;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Full cloud sync failed");
                OnError?.Invoke(this, $"Full cloud sync failed: {ex.Message}");
                return false;
            }
            finally
            {
                _syncSemaphore.Release();
            }
        }

        public async Task<bool> ResolveConflictAsync(CloudConflict conflict)
        {
            try
            {
                OnConflictDetected?.Invoke(this, conflict);

                // Default resolution strategy: keep the most recent version
                if (conflict.LocalMemory.Timestamp >= conflict.CloudMemory.Timestamp)
                {
                    // Local version is newer, sync to cloud
                    var success = await SyncToCloudAsync(conflict.LocalMemory);
                    if (success)
                    {
                        await UpdateSyncStatusAsync(
                            GetMemoryId(conflict.LocalMemory),
                            conflict.LocalMemory.Type.ToString(),
                            conflict.CloudMemory.CloudId,
                            "ConflictResolved"
                        );
                    }
                    return success;
                }
                else
                {
                    // Cloud version is newer, sync from cloud
                    var downloadedMemory = await SyncFromCloudAsync(conflict.CloudMemory.Id, conflict.CloudMemory.Type);
                    return downloadedMemory != null;
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, $"Failed to resolve conflict for memory: {conflict.CloudMemory.Id}");
                return false;
            }
        }

        private async Task<byte[]> SerializeMemoryAsync<T>(T memory) where T : IMemory
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                var json = JsonSerializer.Serialize(memory, options);
                var data = Encoding.UTF8.GetBytes(json);

                // Encrypt if encryption service is available
                if (_encryptionService != null)
                {
                    data = _encryptionService.Encrypt(json);
                }

                return data;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to serialize memory");
                throw;
            }
        }

        private async Task<T> DeserializeMemoryAsync<T>(byte[] data) where T : class, IMemory
        {
            try
            {
                // Decrypt if encryption service is available
                if (_encryptionService != null)
                {
                    var decryptedData = _encryptionService.Decrypt(Convert.ToBase64String(data));
                    data = Encoding.UTF8.GetBytes(decryptedData);
                }

                var json = Encoding.UTF8.GetString(data);
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true
                };

                return JsonSerializer.Deserialize<T>(json, options) ?? throw new InvalidOperationException("Failed to deserialize memory");
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to deserialize memory");
                throw;
            }
        }

        private string GetMemoryId(IMemory memory)
        {
            return memory.Id;
        }

        private string GetMemoryTypeName(IMemory memory)
        {
            return memory.Type switch
            {
                MemoryType.Conversation => "conversations",
                MemoryType.Personal => "personal_memories",
                MemoryType.Contextual => "contextual_memories",
                _ => "unknown"
            };
        }

        private string GetMemoryTypeName<T>() where T : IMemory
        {
            if (typeof(T) == typeof(ConversationMemory))
                return "conversations";
            if (typeof(T) == typeof(PersonalMemory))
                return "personal_memories";
            if (typeof(T) == typeof(ContextualMemory))
                return "contextual_memories";

            return "unknown";
        }

        private string ExtractMemoryTypeFromName(string name)
        {
            if (name.StartsWith("conversation"))
                return "Conversation";
            if (name.StartsWith("personal"))
                return "Personal";
            if (name.StartsWith("contextual"))
                return "Contextual";

            return "Unknown";
        }

        private async Task<List<MemorySyncInfo>> GetMemoriesToSyncAsync()
        {
            // This would query the database for memories that need syncing
            // For now, return empty list as placeholder
            return await Task.FromResult(new List<MemorySyncInfo>());
        }

        private async Task<IMemory?> LoadMemoryAsync(string memoryId, string memoryType)
        {
            // This would load the memory from the database
            // For now, return null as placeholder
            return await Task.FromResult<IMemory?>(null);
        }

        private async Task SyncFromCloudToLocalAsync()
        {
            // This would compare cloud and local memories and sync missing ones
            // Implementation placeholder
        }

        private async Task UpdateSyncStatusAsync(string memoryId, string memoryType, string? cloudId, string status, string? errorMessage = null)
        {
            // This would update the sync status in the database
            // Implementation placeholder
        }

        private async void AutoSyncCallback(object? state)
        {
            if (_isAutoSyncEnabled)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await FullSyncAsync();
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogError(ex, "Auto-sync failed");
                    }
                });
            }
        }

        public async Task StopAsync()
        {
            try
            {
                _loggingService.LogInformation("Stopping Cloud Sync Service...");
                _isAutoSyncEnabled = false;
                _autoSyncTimer?.Dispose();

                // Cleanup providers
                foreach (var provider in _providers.Values)
                {
                    await provider.DisposeAsync();
                }
                _providers.Clear();

                _loggingService.LogInformation("Cloud Sync Service stopped");
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Error stopping Cloud Sync Service");
            }
        }

        public bool IsConnected => _primaryProvider?.IsConnected ?? false;

        public string GetStatus()
        {
            if (!_isInitialized)
                return "Not initialized";
            if (_primaryProvider == null)
                return "No cloud provider configured";
            if (IsConnected)
                return "Connected";
            return "Disconnected";
        }

        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                return _isInitialized && _primaryProvider != null && await _primaryProvider.TestConnectivityAsync();
            }
            catch
            {
                return false;
            }
        }

        // Events
        public event EventHandler<string>? OnStatusChanged;
        public event EventHandler<string>? OnError;

        // Private fields
        private ICloudProvider? _primaryProvider;
    }

    // Cloud provider interface
    public interface ICloudProvider : IDisposable
    {
        Task InitializeAsync();
        Task<bool> TestConnectivityAsync();
        Task<string> UploadAsync(string containerName, string blobName, byte[] data);
        Task<byte[]?> DownloadAsync(string containerName, string blobName);
        Task<bool> DeleteAsync(string containerName, string blobName);
        Task<List<CloudItem>> ListAsync(string? containerName = null, int? limit = null);
        bool IsConnected { get; }
    }

    // Azure Blob Storage provider
    public class AzureBlobProvider : ICloudProvider
    {
        private readonly string _connectionString;
        private readonly LoggingService _loggingService;
        private BlobServiceClient? _blobServiceClient;
        private bool _isInitialized;
        private bool _isConnected;

        public AzureBlobProvider(string connectionString, LoggingService loggingService)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        public bool IsConnected => _isConnected;

        public async Task InitializeAsync()
        {
            try
            {
                _blobServiceClient = new BlobServiceClient(_connectionString);

                // Test connectivity
                await TestConnectivityAsync();

                _isInitialized = true;
                _loggingService.LogInformation("Azure Blob Storage provider initialized");
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to initialize Azure Blob Storage provider");
                throw;
            }
        }

        public async Task<bool> TestConnectivityAsync()
        {
            if (_blobServiceClient == null)
                return false;

            try
            {
                var properties = await _blobServiceClient.GetPropertiesAsync();
                _isConnected = true;
                return true;
            }
            catch (Exception ex)
            {
                _isConnected = false;
                _loggingService.LogWarning(ex, "Azure Blob Storage connectivity test failed");
                return false;
            }
        }

        public async Task<string> UploadAsync(string containerName, string blobName, byte[] data)
        {
            if (_blobServiceClient == null)
                throw new InvalidOperationException("Provider not initialized");

            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                await containerClient.CreateIfNotExistsAsync();

                var blobClient = containerClient.GetBlobClient(blobName);
                using var stream = new MemoryStream(data);
                await blobClient.UploadAsync(stream, true);

                return blobClient.Uri.ToString();
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, $"Failed to upload blob {blobName} to container {containerName}");
                throw;
            }
        }

        public async Task<byte[]?> DownloadAsync(string containerName, string blobName)
        {
            if (_blobServiceClient == null)
                throw new InvalidOperationException("Provider not initialized");

            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobName);

                if (!await blobClient.ExistsAsync())
                    return null;

                using var stream = new MemoryStream();
                await blobClient.DownloadToAsync(stream);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, $"Failed to download blob {blobName} from container {containerName}");
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string containerName, string blobName)
        {
            if (_blobServiceClient == null)
                return false;

            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobName);
                var response = await blobClient.DeleteIfExistsAsync();
                return response.Value;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, $"Failed to delete blob {blobName} from container {containerName}");
                return false;
            }
        }

        public async Task<List<CloudItem>> ListAsync(string? containerName = null, int? limit = null)
        {
            if (_blobServiceClient == null)
                return new List<CloudItem>();

            try
            {
                var items = new List<CloudItem>();

                if (string.IsNullOrEmpty(containerName))
                {
                    // List all containers
                    await foreach (var containerItem in _blobServiceClient.GetBlobsAsync())
                    {
                        var containerClient = _blobServiceClient.GetBlobContainerClient(containerItem.Name);
                        await foreach (var blobItem in containerClient.GetBlobsAsync())
                        {
                            items.Add(new CloudItem
                            {
                                Id = blobItem.Name,
                                Name = blobItem.Name,
                                Size = blobItem.Properties.ContentLength ?? 0,
                                LastModified = blobItem.Properties.LastModified ?? DateTime.MinValue,
                                ContentType = blobItem.Properties.ContentType
                            });

                            if (limit.HasValue && items.Count >= limit.Value)
                                break;
                        }
                    }
                }
                else
                {
                    // List blobs in specific container
                    var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                    await foreach (var blobItem in containerClient.GetBlobsAsync())
                    {
                        items.Add(new CloudItem
                        {
                            Id = blobItem.Name,
                            Name = blobItem.Name,
                            Size = blobItem.Properties.ContentLength ?? 0,
                            LastModified = blobItem.Properties.LastModified ?? DateTime.MinValue,
                            ContentType = blobItem.Properties.ContentType
                        });

                        if (limit.HasValue && items.Count >= limit.Value)
                            break;
                    }
                }

                return items.OrderByDescending(i => i.LastModified).ToList();
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to list cloud items");
                return new List<CloudItem>();
            }
        }

        public async Task DisposeAsync()
        {
            _isConnected = false;
            _isInitialized = false;
            // No explicit dispose needed for BlobServiceClient
            await Task.CompletedTask;
        }

        void IDisposable.Dispose()
        {
            _isConnected = false;
            _isInitialized = false;
        }
    }

    // Supporting classes
    public class CloudItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
        public string? ContentType { get; set; }
    }

    public class CloudMemoryInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
        public string? ContentType { get; set; }
        public string MemoryType { get; set; } = string.Empty;
        public string? CloudId { get; set; }
    }

    public class CloudSyncEventArgs : EventArgs
    {
        public string MemoryId { get; set; } = string.Empty;
        public string MemoryType { get; set; } = string.Empty;
        public string? CloudId { get; set; }
        public string Direction { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class CloudConflict
    {
        public IMemory LocalMemory { get; set; } = null!;
        public CloudMemoryInfo CloudMemory { get; set; } = null!;
        public ConflictType ConflictType { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public class MemorySyncInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public DateTime LastSync { get; set; }
        public string SyncStatus { get; set; } = string.Empty;
    }

    public enum ConflictType
    {
        ModifiedInBoth,
        DeletedInCloud,
        DeletedInLocal,
        DifferentContent
    }
}