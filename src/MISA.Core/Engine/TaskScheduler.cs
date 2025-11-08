using MISA.Core.Services;
using System.Collections.Concurrent;

namespace MISA.Core.Engine
{
    public class TaskScheduler : IDisposable
    {
        private readonly ConfigService _configService;
        private readonly LoggingService _loggingService;
        private readonly ResourceManager _resourceManager;
        private readonly ConcurrentDictionary<string, ScheduledTask> _scheduledTasks;
        private readonly ConcurrentQueue<BackgroundTask> _taskQueue;
        private readonly SemaphoreSlim _concurrencySemaphore;
        private readonly Timer _cleanupTimer;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly List<Task> _workerTasks;

        private bool _isRunning;
        private int _maxConcurrentTasks;
        private int _activeTaskCount;

        public event EventHandler<TaskStartedEventArgs>? OnTaskStarted;
        public event EventHandler<TaskCompletedEventArgs>? OnTaskCompleted;
        public event EventHandler<TaskFailedEventArgs>? OnTaskFailed;
        public event EventHandler<string>? OnStatusChanged;

        public TaskScheduler(ConfigService configService, LoggingService loggingService, ResourceManager resourceManager)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));

            _scheduledTasks = new ConcurrentDictionary<string, ScheduledTask>();
            _taskQueue = new ConcurrentQueue<BackgroundTask>();
            _cancellationTokenSource = new CancellationTokenSource();
            _workerTasks = new List<Task>();

            _maxConcurrentTasks = _configService.GetValue<int>("Scheduler.MaxConcurrentTasks", 3);
            _concurrencySemaphore = new SemaphoreSlim(_maxConcurrentTasks, _maxConcurrentTasks);

            // Cleanup timer for completed tasks
            _cleanupTimer = new Timer(CleanupCompletedTasks, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (_isRunning)
            {
                _loggingService.LogWarning("Task Scheduler is already running");
                return;
            }

            try
            {
                _loggingService.LogInformation("Starting Task Scheduler...");
                OnStatusChanged?.Invoke(this, "Starting Task Scheduler...");

                // Start worker tasks
                for (int i = 0; i < _maxConcurrentTasks; i++)
                {
                    var workerTask = Task.Run(WorkerProcessAsync, _cancellationTokenSource.Token);
                    _workerTasks.Add(workerTask);
                }

                // Start recurring tasks
                await StartRecurringTasksAsync();

                _isRunning = true;
                OnStatusChanged?.Invoke(this, "Task Scheduler started successfully");
                _loggingService.LogInformation($"Task Scheduler started with {_maxConcurrentTasks} workers");
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke(this, "Failed to start Task Scheduler");
                _loggingService.LogError(ex, "Failed to start Task Scheduler");
                throw;
            }
        }

        public async Task StopAsync()
        {
            if (!_isRunning)
                return;

            try
            {
                _loggingService.LogInformation("Stopping Task Scheduler...");
                OnStatusChanged?.Invoke(this, "Stopping Task Scheduler...");

                _isRunning = false;
                _cancellationTokenSource.Cancel();

                // Wait for all worker tasks to complete
                await Task.WhenAll(_workerTasks);

                // Cancel all scheduled tasks
                foreach (var task in _scheduledTasks.Values)
                {
                    task.CancellationTokenSource?.Cancel();
                }

                _scheduledTasks.Clear();
                _taskQueue.Clear();

                OnStatusChanged?.Invoke(this, "Task Scheduler stopped");
                _loggingService.LogInformation("Task Scheduler stopped");
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Error stopping Task Scheduler");
            }
        }

        public string ScheduleTask(BackgroundTask task, ScheduleOptions? options = null)
        {
            try
            {
                var taskId = Guid.NewGuid().ToString("N")[..16];
                var scheduledTask = new ScheduledTask
                {
                    Id = taskId,
                    Task = task,
                    Options = options ?? new ScheduleOptions(),
                    CreatedAt = DateTime.UtcNow,
                    Status = TaskStatus.Pending
                };

                // Set default options
                if (scheduledTask.Options.Priority == TaskPriority.Normal)
                {
                    scheduledTask.Options.Priority = DetermineTaskPriority(task);
                }

                // Adjust for resource constraints
                await AdjustTaskForResourcesAsync(scheduledTask);

                if (scheduledTask.Options.Delay > TimeSpan.Zero)
                {
                    // Schedule delayed execution
                    var delayTimer = new Timer(async _ => await QueueTaskAsync(scheduledTask),
                        null, scheduledTask.Options.Delay, Timeout.InfiniteTimeSpan);
                    scheduledTask.Timer = delayTimer;
                }
                else if (scheduledTask.Options.IsRecurring)
                {
                    // Schedule recurring task
                    await ScheduleRecurringTaskAsync(scheduledTask);
                }
                else
                {
                    // Queue immediately
                    await QueueTaskAsync(scheduledTask);
                }

                _scheduledTasks[taskId] = scheduledTask;

                _loggingService.LogModelActivity($"Task scheduled: {task.Name}", taskId,
                    new Dictionary<string, object>
                    {
                        ["Priority"] = scheduledTask.Options.Priority,
                        ["Delayed"] = scheduledTask.Options.Delay > TimeSpan.Zero,
                        ["Recurring"] = scheduledTask.Options.IsRecurring
                    });

                return taskId;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, $"Failed to schedule task: {task.Name}");
                throw;
            }
        }

        public async Task<bool> CancelTaskAsync(string taskId)
        {
            try
            {
                if (_scheduledTasks.TryRemove(taskId, out var scheduledTask))
                {
                    await CancelTaskAsync(scheduledTask);
                    _loggingService.LogInformation($"Task cancelled: {taskId}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, $"Failed to cancel task: {taskId}");
                return false;
            }
        }

        public ScheduledTask? GetTaskStatus(string taskId)
        {
            return _scheduledTasks.TryGetValue(taskId, out var task) ? task : null;
        }

        public List<ScheduledTask> GetActiveTasks()
        {
            return _scheduledTasks.Values
                .Where(t => t.Status == TaskStatus.Running || t.Status == TaskStatus.Pending)
                .OrderByDescending(t => t.CreatedAt)
                .ToList();
        }

        public List<ScheduledTask> GetRecentTasks(int count = 50)
        {
            return _scheduledTasks.Values
                .OrderByDescending(t => t.CreatedAt)
                .Take(count)
                .ToList();
        }

        public async Task<TaskStatistics> GetTaskStatisticsAsync()
        {
            try
            {
                var stats = new TaskStatistics();

                foreach (var task in _scheduledTasks.Values)
                {
                    stats.TotalTasks++;

                    switch (task.Status)
                    {
                        case TaskStatus.Completed:
                            stats.CompletedTasks++;
                            break;
                        case TaskStatus.Failed:
                            stats.FailedTasks++;
                            break;
                        case TaskStatus.Running:
                            stats.RunningTasks++;
                            break;
                        case TaskStatus.Pending:
                            stats.PendingTasks++;
                            break;
                        case TaskStatus.Cancelled:
                            stats.CancelledTasks++;
                            break;
                    }

                    if (task.Duration.HasValue)
                    {
                        stats.TotalExecutionTime += task.Duration.Value;
                    }
                }

                stats.SuccessRate = stats.TotalTasks > 0 ? (double)stats.CompletedTasks / stats.TotalTasks : 0;
                stats.AverageExecutionTime = stats.CompletedTasks > 0 ?
                    TimeSpan.FromTicks(stats.TotalExecutionTime.Ticks / stats.CompletedTasks) : TimeSpan.Zero;

                return stats;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to get task statistics");
                return new TaskStatistics();
            }
        }

        private async Task WorkerProcessAsync()
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    if (_taskQueue.TryDequeue(out var backgroundTask))
                    {
                        await ExecuteTaskAsync(backgroundTask);
                    }
                    else
                    {
                        // No tasks available, wait briefly
                        await Task.Delay(100, _cancellationTokenSource.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _loggingService.LogError(ex, "Error in worker process");
                }
            }
        }

        private async Task ExecuteTaskAsync(BackgroundTask backgroundTask)
        {
            await _concurrencySemaphore.WaitAsync(_cancellationTokenSource.Token);

            try
            {
                var scheduledTask = _scheduledTasks.Values.FirstOrDefault(st => st.Task == backgroundTask);
                if (scheduledTask != null)
                {
                    scheduledTask.Status = TaskStatus.Running;
                    scheduledTask.StartedAt = DateTime.UtcNow;
                    Interlocked.Increment(ref _activeTaskCount);
                }

                OnTaskStarted?.Invoke(this, new TaskStartedEventArgs
                {
                    TaskId = scheduledTask?.Id ?? "unknown",
                    TaskName = backgroundTask.Name,
                    WorkerId = Thread.CurrentThread.ManagedThreadId
                });

                _loggingService.LogModelActivity($"Task started: {backgroundTask.Name}", scheduledTask?.Id ?? "unknown");

                // Check resource constraints before execution
                if (!await CanExecuteTaskAsync(backgroundTask))
                {
                    throw new InvalidOperationException("Insufficient resources to execute task");
                }

                var startTime = DateTime.UtcNow;

                // Execute the task
                var result = await backgroundTask.ExecuteAsync(_cancellationTokenSource.Token);

                var endTime = DateTime.UtcNow;
                var duration = endTime - startTime;

                if (scheduledTask != null)
                {
                    scheduledTask.Status = TaskStatus.Completed;
                    scheduledTask.CompletedAt = endTime;
                    scheduledTask.Duration = duration;
                    scheduledTask.Result = result;
                    Interlocked.Decrement(ref _activeTaskCount);
                }

                OnTaskCompleted?.Invoke(this, new TaskCompletedEventArgs
                {
                    TaskId = scheduledTask?.Id ?? "unknown",
                    TaskName = backgroundTask.Name,
                    Duration = duration,
                    Result = result
                });

                _loggingService.LogModelActivity($"Task completed: {backgroundTask.Name}", scheduledTask?.Id ?? "unknown",
                    new Dictionary<string, object>
                    {
                        ["Duration"] = duration.TotalMilliseconds,
                        ["Result"] = result?.ToString() ?? "null"
                    });
            }
            catch (OperationCanceledException)
            {
                if (scheduledTask != null)
                {
                    scheduledTask.Status = TaskStatus.Cancelled;
                    scheduledTask.CompletedAt = DateTime.UtcNow;
                    Interlocked.Decrement(ref _activeTaskCount);
                }

                _loggingService.LogInformation($"Task cancelled: {backgroundTask.Name}");
            }
            catch (Exception ex)
            {
                if (scheduledTask != null)
                {
                    scheduledTask.Status = TaskStatus.Failed;
                    scheduledTask.CompletedAt = DateTime.UtcNow;
                    scheduledTask.Error = ex.Message;
                    Interlocked.Decrement(ref _activeTaskCount);
                }

                OnTaskFailed?.Invoke(this, new TaskFailedEventArgs
                {
                    TaskId = scheduledTask?.Id ?? "unknown",
                    TaskName = backgroundTask.Name,
                    Error = ex.Message,
                    Exception = ex
                });

                _loggingService.LogError(ex, $"Task failed: {backgroundTask.Name}");
            }
            finally
            {
                _concurrencySemaphore.Release();
            }
        }

        private async Task<bool> CanExecuteTaskAsync(BackgroundTask task)
        {
            try
            {
                // Check if we should throttle due to resource constraints
                if (_resourceManager.ShouldThrottlePerformance())
                {
                    // Only allow high-priority tasks during throttling
                    return task.Priority == TaskPriority.Critical || task.Priority == TaskPriority.High;
                }

                // Check concurrent task limits
                return _activeTaskCount < _maxConcurrentTasks;
            }
            catch
            {
                return true; // Default to allowing execution if checks fail
            }
        }

        private TaskPriority DetermineTaskPriority(BackgroundTask task)
        {
            // Auto-determine priority based on task characteristics
            if (task.Name.Contains("critical", StringComparison.OrdinalIgnoreCase) ||
                task.Name.Contains("security", StringComparison.OrdinalIgnoreCase))
            {
                return TaskPriority.Critical;
            }

            if (task.Name.Contains("user", StringComparison.OrdinalIgnoreCase) ||
                task.Name.Contains("chat", StringComparison.OrdinalIgnoreCase))
            {
                return TaskPriority.High;
            }

            if (task.Name.Contains("background", StringComparison.OrdinalIgnoreCase) ||
                task.Name.Contains("cleanup", StringComparison.OrdinalIgnoreCase))
            {
                return TaskPriority.Low;
            }

            return TaskPriority.Normal;
        }

        private async Task AdjustTaskForResourcesAsync(ScheduledTask scheduledTask)
        {
            try
            {
                var resources = _resourceManager.GetCurrentMetrics();

                // Adjust task based on current resource usage
                if (resources.CpuUsagePercent > 80 && scheduledTask.Options.Priority == TaskPriority.Low)
                {
                    scheduledTask.Options.Delay = TimeSpan.FromSeconds(30);
                }

                if (resources.MemoryUsagePercent > 90)
                {
                    // Prioritize memory-critical tasks
                    if (scheduledTask.Task.Name.Contains("memory", StringComparison.OrdinalIgnoreCase))
                    {
                        scheduledTask.Options.Priority = TaskPriority.Critical;
                    }
                }

                // Adjust for GPU availability
                if (scheduledTask.Task.RequiresGPU && !_resourceManager.CanAccelerateAI())
                {
                    // Fallback to CPU-only mode
                    scheduledTask.Task.FallbackToCPU = true;
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, "Failed to adjust task for resources");
            }
        }

        private async Task QueueTaskAsync(ScheduledTask scheduledTask)
        {
            try
            {
                // Add to priority queue (simplified - in production would use proper priority queue)
                _taskQueue.Enqueue(scheduledTask.Task);
                scheduledTask.Status = TaskStatus.Pending;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, $"Failed to queue task: {scheduledTask.Task.Name}");
            }
        }

        private async Task ScheduleRecurringTaskAsync(ScheduledTask scheduledTask)
        {
            try
            {
                if (!scheduledTask.CancellationTokenSource.IsCancellationRequested)
                {
                    await QueueTaskAsync(scheduledTask);

                    // Schedule next occurrence
                    var nextDelay = scheduledTask.Options.RecurringInterval ?? TimeSpan.FromHours(1);
                    var nextTimer = new Timer(async _ => await ScheduleRecurringTaskAsync(scheduledTask),
                        null, nextDelay, Timeout.InfiniteTimeSpan);
                    scheduledTask.Timer = nextTimer;
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, $"Failed to schedule recurring task: {scheduledTask.Task.Name}");
            }
        }

        private async Task StartRecurringTasksAsync()
        {
            try
            {
                // Start built-in recurring tasks

                // System cleanup task (every hour)
                var cleanupTask = new SystemCleanupTask();
                ScheduleTask(cleanupTask, new ScheduleOptions
                {
                    IsRecurring = true,
                    RecurringInterval = TimeSpan.FromHours(1),
                    Priority = TaskPriority.Low
                });

                // Resource monitoring task (every 5 minutes)
                var monitorTask = new ResourceMonitoringTask(_resourceManager);
                ScheduleTask(monitorTask, new ScheduleOptions
                {
                    IsRecurring = true,
                    RecurringInterval = TimeSpan.FromMinutes(5),
                    Priority = TaskPriority.Low
                });

                // Health check task (every 15 minutes)
                var healthTask = new HealthCheckTask();
                ScheduleTask(healthTask, new ScheduleOptions
                {
                    IsRecurring = true,
                    RecurringInterval = TimeSpan.FromMinutes(15),
                    Priority = TaskPriority.Normal
                });

                _loggingService.LogInformation("Started built-in recurring tasks");
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to start recurring tasks");
            }
        }

        private void CleanupCompletedTasks(object? state)
        {
            try
            {
                var cutoffTime = DateTime.UtcNow.AddHours(-24);
                var tasksToRemove = _scheduledTasks.Values
                    .Where(t => t.Status == TaskStatus.Completed || t.Status == TaskStatus.Failed || t.Status == TaskStatus.Cancelled)
                    .Where(t => t.CompletedAt.HasValue && t.CompletedAt.Value < cutoffTime)
                    .Select(t => t.Id)
                    .ToList();

                foreach (var taskId in tasksToRemove)
                {
                    if (_scheduledTasks.TryRemove(taskId, out var task))
                    {
                        task.Timer?.Dispose();
                        task.CancellationTokenSource?.Dispose();
                    }
                }

                if (tasksToRemove.Count > 0)
                {
                    _loggingService.LogInformation($"Cleaned up {tasksToRemove.Count} old tasks");
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, "Failed to cleanup completed tasks");
            }
        }

        private async Task CancelTaskAsync(ScheduledTask scheduledTask)
        {
            try
            {
                scheduledTask.CancellationTokenSource?.Cancel();
                scheduledTask.Status = TaskStatus.Cancelled;
                scheduledTask.Timer?.Dispose();
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, $"Failed to cancel scheduled task: {scheduledTask.Task.Name}");
            }
        }

        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();

            _concurrencySemaphore?.Dispose();
            _cleanupTimer?.Dispose();
            _cancellationTokenSource?.Dispose();

            foreach (var task in _scheduledTasks.Values)
            {
                task.Timer?.Dispose();
                task.CancellationTokenSource?.Dispose();
            }
        }
    }

    public abstract class BackgroundTask
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public TaskPriority Priority { get; set; } = TaskPriority.Normal;
        public bool RequiresGPU { get; set; } = false;
        public bool FallbackToCPU { get; set; } = true;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);
        public Dictionary<string, object> Metadata { get; set; } = new();

        public abstract Task<object?> ExecuteAsync(CancellationToken cancellationToken);
    }

    public class ScheduleOptions
    {
        public TaskPriority Priority { get; set; } = TaskPriority.Normal;
        public TimeSpan Delay { get; set; } = TimeSpan.Zero;
        public bool IsRecurring { get; set; } = false;
        public TimeSpan? RecurringInterval { get; set; }
        public int? MaxRetries { get; set; }
        public bool RetryOnFailure { get; set; } = true;
    }

    public class ScheduledTask
    {
        public string Id { get; set; } = string.Empty;
        public BackgroundTask Task { get; set; } = null!;
        public ScheduleOptions Options { get; set; } = new();
        public TaskStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public TimeSpan? Duration { get; set; }
        public object? Result { get; set; }
        public string? Error { get; set; }
        public Timer? Timer { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; } = new();
    }

    public enum TaskStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    public enum TaskPriority
    {
        Critical,
        High,
        Normal,
        Low
    }

    public class TaskStatistics
    {
        public int TotalTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int FailedTasks { get; set; }
        public int RunningTasks { get; set; }
        public int PendingTasks { get; set; }
        public int CancelledTasks { get; set; }
        public double SuccessRate { get; set; }
        public TimeSpan TotalExecutionTime { get; set; }
        public TimeSpan AverageExecutionTime { get; set; }
    }

    public class TaskStartedEventArgs : EventArgs
    {
        public string TaskId { get; set; } = string.Empty;
        public string TaskName { get; set; } = string.Empty;
        public int WorkerId { get; set; }
    }

    public class TaskCompletedEventArgs : EventArgs
    {
        public string TaskId { get; set; } = string.Empty;
        public string TaskName { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public object? Result { get; set; }
    }

    public class TaskFailedEventArgs : EventArgs
    {
        public string TaskId { get; set; } = string.Empty;
        public string TaskName { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public Exception Exception { get; set; } = null!;
    }

    // Built-in task implementations
    public class SystemCleanupTask : BackgroundTask
    {
        public SystemCleanupTask()
        {
            Name = "System Cleanup";
            Description = "Cleans up temporary files and optimizes system performance";
            Priority = TaskPriority.Low;
            Timeout = TimeSpan.FromMinutes(10);
        }

        public override async Task<object?> ExecuteAsync(CancellationToken cancellationToken)
        {
            // Implement system cleanup logic
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken); // Placeholder

            // Clear temporary files
            var tempPath = Path.GetTempPath();
            var tempFiles = Directory.GetFiles(tempPath, "*.*", SearchOption.AllDirectories)
                .Where(f => File.GetCreationTime(f) < DateTime.UtcNow.AddDays(7));

            var cleanedCount = 0;
            foreach (var file in tempFiles)
            {
                try
                {
                    File.Delete(file);
                    cleanedCount++;
                }
                catch
                {
                    // Ignore files that can't be deleted
                }
            }

            return new { CleanedFiles = cleanedCount, Timestamp = DateTime.UtcNow };
        }
    }

    public class ResourceMonitoringTask : BackgroundTask
    {
        private readonly ResourceManager _resourceManager;

        public ResourceMonitoringTask(ResourceManager resourceManager)
        {
            _resourceManager = resourceManager;
            Name = "Resource Monitoring";
            Description = "Monitors system resources and adjusts performance settings";
            Priority = TaskPriority.Low;
            Timeout = TimeSpan.FromMinutes(1);
        }

        public override async Task<object?> ExecuteAsync(CancellationToken cancellationToken)
        {
            var metrics = _resourceManager.GetCurrentMetrics();
            var profile = _resourceManager.GetRecommendedProfile();

            _resourceManager.ApplyPowerSettings(profile);

            return new {
                Metrics = metrics,
                RecommendedProfile = profile,
                Timestamp = DateTime.UtcNow
            };
        }
    }

    public class HealthCheckTask : BackgroundTask
    {
        public HealthCheckTask()
        {
            Name = "Health Check";
            Description = "Performs system health checks and diagnostics";
            Priority = TaskPriority.Normal;
            Timeout = TimeSpan.FromMinutes(5);
        }

        public override async Task<object?> ExecuteAsync(CancellationToken cancellationToken)
        {
            // Implement health check logic
            var healthStatus = new Dictionary<string, bool>
            {
                ["Database"] = CheckDatabaseHealth(),
                ["WebServer"] = CheckWebServerHealth(),
                ["AIModels"] = CheckAIModelsHealth(),
                ["Memory"] = CheckMemoryHealth(),
                ["DiskSpace"] = CheckDiskSpaceHealth()
            };

            var isHealthy = healthStatus.Values.All(status => status);

            return new { IsHealthy = isHealthy, Components = healthStatus, Timestamp = DateTime.UtcNow };
        }

        private bool CheckDatabaseHealth()
        {
            try
            {
                // Check database connectivity
                return true; // Placeholder
            }
            catch
            {
                return false;
            }
        }

        private bool CheckWebServerHealth()
        {
            try
            {
                // Check web server responsiveness
                return true; // Placeholder
            }
            catch
            {
                return false;
            }
        }

        private bool CheckAIModelsHealth()
        {
            try
            {
                // Check AI model availability
                return true; // Placeholder
            }
            catch
            {
                return false;
            }
        }

        private bool CheckMemoryHealth()
        {
            try
            {
                var memoryUsage = GC.GetTotalMemory(false) / (1024 * 1024 * 1024.0);
                return memoryUsage < 8; // Less than 8GB
            }
            catch
            {
                return false;
            }
        }

        private bool CheckDiskSpaceHealth()
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:");
                return drive.AvailableFreeSpace > (1024L * 1024 * 1024 * 10); // At least 10GB free
            }
            catch
            {
                return false;
            }
        }
    }
}