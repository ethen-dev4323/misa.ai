using Serilog;
using System.Diagnostics;

namespace MISA.Core.Services
{
    public class LoggingService
    {
        private readonly ILogger _logger;

        public LoggingService()
        {
            _logger = Log.ForContext<LoggingService>();
        }

        public void LogInformation(string message, params object[]? propertyValues)
        {
            _logger.Information(message, propertyValues);
        }

        public void LogWarning(string message, params object[]? propertyValues)
        {
            _logger.Warning(message, propertyValues);
        }

        public void LogError(Exception exception, string message, params object[]? propertyValues)
        {
            _logger.Error(exception, message, propertyValues);
        }

        public void LogError(string message, params object[]? propertyValues)
        {
            _logger.Error(message, propertyValues);
        }

        public void LogDebug(string message, params object[]? propertyValues)
        {
            _logger.Debug(message, propertyValues);
        }

        public void LogVerbose(string message, params object[]? propertyValues)
        {
            _logger.Verbose(message, propertyValues);
        }

        public void LogPerformance(string operation, TimeSpan duration, Dictionary<string, object>? metadata = null)
        {
            var logMessage = $"Performance: {operation} completed in {duration.TotalMilliseconds:F2}ms";

            if (metadata != null && metadata.Count > 0)
            {
                var metadataStr = string.Join(", ", metadata.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                logMessage += $" | {metadataStr}";
            }

            _logger.Information(logMessage);
        }

        public void LogResourceUsage(string component, long memoryUsageMB, double cpuUsagePercent)
        {
            _logger.Information($"Resource Usage - {component}: Memory={memoryUsageMB}MB, CPU={cpuUsagePercent:F1}%");
        }

        public void LogApiCall(string endpoint, string method, int statusCode, TimeSpan duration)
        {
            _logger.Information($"API Call: {method} {endpoint} -> {statusCode} ({duration.TotalMilliseconds:F2}ms)");
        }

        public void LogModelActivity(string model, string operation, TimeSpan duration, Dictionary<string, object>? metadata = null)
        {
            var logMessage = $"Model Activity: {model} - {operation} ({duration.TotalMilliseconds:F2}ms)";

            if (metadata != null && metadata.Count > 0)
            {
                var metadataStr = string.Join(", ", metadata.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                logMessage += $" | {metadataStr}";
            }

            _logger.Information(logMessage);
        }

        public void LogSecurityEvent(string eventType, string? details = null, string? userId = null, string? deviceId = null)
        {
            var logMessage = $"Security Event: {eventType}";

            if (!string.IsNullOrEmpty(details))
                logMessage += $" | Details: {details}";
            if (!string.IsNullOrEmpty(userId))
                logMessage += $" | User: {userId}";
            if (!string.IsNullOrEmpty(deviceId))
                logMessage += $" | Device: {deviceId}";

            _logger.Warning(logMessage);
        }

        public void LogPersonalitySwitch(string fromPersonality, string toPersonality, string? reason = null)
        {
            var logMessage = $"Personality Switch: {fromPersonality} -> {toPersonality}";

            if (!string.IsNullOrEmpty(reason))
                logMessage += $" | Reason: {reason}";

            _logger.Information(logMessage);
        }

        public void LogMemoryOperation(string operation, string memoryType, long sizeBytes = 0)
        {
            var sizeStr = sizeBytes > 0 ? $" | Size: {FormatBytes(sizeBytes)}" : "";
            _logger.Information($"Memory Operation: {operation} - {memoryType}{sizeStr}");
        }

        public void LogDeviceActivity(string deviceId, string activity, Dictionary<string, object>? metadata = null)
        {
            var logMessage = $"Device Activity: {deviceId} - {activity}";

            if (metadata != null && metadata.Count > 0)
            {
                var metadataStr = string.Join(", ", metadata.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                logMessage += $" | {metadataStr}";
            }

            _logger.Information(logMessage);
        }

        public void LogUpdateActivity(string activity, string version, bool success = true)
        {
            var status = success ? "Success" : "Failed";
            _logger.Information($"Update Activity: {activity} - Version {version} - {status}");
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        public IDisposable BeginTimedOperation(string operation, LogLevel level = LogLevel.Information)
        {
            return new TimedOperation(this, operation, level);
        }

        private class TimedOperation : IDisposable
        {
            private readonly LoggingService _loggingService;
            private readonly string _operation;
            private readonly LogLevel _level;
            private readonly Stopwatch _stopwatch;

            public TimedOperation(LoggingService loggingService, string operation, LogLevel level)
            {
                _loggingService = loggingService;
                _operation = operation;
                _level = level;
                _stopwatch = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                _stopwatch.Stop();
                _loggingService.LogPerformance(_operation, _stopwatch.Elapsed);
            }
        }
    }

    public enum LogLevel
    {
        Verbose,
        Debug,
        Information,
        Warning,
        Error,
        Fatal
    }
}