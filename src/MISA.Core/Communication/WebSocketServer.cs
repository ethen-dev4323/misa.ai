using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebSockets;
using System.Net.WebSockets;
using System.Text;
using System.Collections.Concurrent;

namespace MISA.Core.Communication
{
    public class WebSocketServer
    {
        private readonly ConfigService _configService;
        private readonly SecurityService _securityService;
        private readonly LoggingService _loggingService;
        private readonly ConcurrentDictionary<string, WebSocketConnection> _connections;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isRunning;

        public event EventHandler<string>? OnStatusChanged;
        public event EventHandler<string>? OnError;
        public event EventHandler<(string connectionId, string message)>? OnMessageReceived;

        public WebSocketServer(ConfigService configService, SecurityService securityService, LoggingService loggingService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _securityService = securityService ?? throw new ArgumentNullException(nameof(securityService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _connections = new ConcurrentDictionary<string, WebSocketConnection>();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public async Task StartAsync()
        {
            if (_isRunning)
            {
                _loggingService.LogWarning("WebSocket server is already running");
                return;
            }

            try
            {
                _loggingService.LogInformation("Starting WebSocket server...");
                OnStatusChanged?.Invoke(this, "Starting WebSocket server...");

                var port = _configService.GetValue<int>("Communication.WebSocketPort", 8080);
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.ListenAnyIP(port);
                });

                var app = builder.Build();

                app.UseWebSockets();
                app.Map("/ws", HandleWebSocketAsync);

                _ = Task.Run(() => app.RunAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

                _isRunning = true;
                OnStatusChanged?.Invoke(this, $"WebSocket server started on port {port}");
                _loggingService.LogInformation($"WebSocket server started on port {port}");
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Failed to start WebSocket server: {ex.Message}");
                _loggingService.LogError(ex, "Failed to start WebSocket server");
                throw;
            }
        }

        public async Task StopAsync()
        {
            if (!_isRunning)
                return;

            try
            {
                _loggingService.LogInformation("Stopping WebSocket server...");
                OnStatusChanged?.Invoke(this, "Stopping WebSocket server...");

                _cancellationTokenSource.Cancel();

                // Close all active connections
                var closeTasks = _connections.Values.Select(conn => CloseConnectionAsync(conn.ConnectionId));
                await Task.WhenAll(closeTasks);

                _connections.Clear();

                _isRunning = false;
                OnStatusChanged?.Invoke(this, "WebSocket server stopped");
                _loggingService.LogInformation("WebSocket server stopped");
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Error stopping WebSocket server: {ex.Message}");
                _loggingService.LogError(ex, "Error stopping WebSocket server");
            }
        }

        private async Task HandleWebSocketAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }

            try
            {
                var deviceId = context.Request.Query["deviceId"].FirstOrDefault();
                var signature = context.Request.Query["signature"].FirstOrDefault();

                if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(signature))
                {
                    context.Response.StatusCode = 401;
                    return;
                }

                if (!_securityService.ValidateDeviceSignature(deviceId, signature))
                {
                    _loggingService.LogSecurityEventAsync("WebSocketAuthFailed", $"Invalid signature for device: {deviceId}", deviceId: deviceId);
                    context.Response.StatusCode = 401;
                    return;
                }

                using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                var connectionId = Guid.NewGuid().ToString("N");

                var connection = new WebSocketConnection
                {
                    ConnectionId = connectionId,
                    DeviceId = deviceId,
                    WebSocket = webSocket,
                    ConnectedAt = DateTime.UtcNow,
                    LastActivity = DateTime.UtcNow
                };

                _connections[connectionId] = connection;

                _loggingService.LogInformation($"WebSocket connection established: {connectionId} for device {deviceId}");
                _loggingService.LogSecurityEventAsync("WebSocketConnected", deviceId: deviceId);

                await HandleConnectionAsync(connection);
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Error handling WebSocket connection");
            }
        }

        private async Task HandleConnectionAsync(WebSocketConnection connection)
        {
            var buffer = new byte[4096 * 4];

            try
            {
                while (connection.WebSocket.State == WebSocketState.Open &&
                       !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    var result = await connection.WebSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);

                    connection.LastActivity = DateTime.UtcNow;

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        await ProcessMessageAsync(connection, message);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await CloseConnectionAsync(connection.ConnectionId);
                        break;
                    }
                }
            }
            catch (WebSocketException ex)
            {
                _loggingService.LogError(ex, $"WebSocket error for connection {connection.ConnectionId}");
            }
            catch (OperationCanceledException)
            {
                _loggingService.LogInformation($"WebSocket connection {connection.ConnectionId} cancelled");
            }
            finally
            {
                await CloseConnectionAsync(connection.ConnectionId);
            }
        }

        private async Task ProcessMessageAsync(WebSocketConnection connection, string message)
        {
            try
            {
                _loggingService.LogDebug($"Received message from {connection.ConnectionId}: {message.Substring(0, Math.Min(100, message.Length))}...");

                OnMessageReceived?.Invoke(this, (connection.ConnectionId, message));

                // Parse and handle different message types
                var messageObj = JsonConvert.DeserializeObject<dynamic>(message);
                var messageType = messageObj?.type?.ToString();

                switch (messageType)
                {
                    case "ping":
                        await SendMessageAsync(connection.ConnectionId, new { type = "pong", timestamp = DateTime.UtcNow });
                        break;

                    case "chat":
                        await HandleChatMessageAsync(connection, messageObj);
                        break;

                    case "screen_control":
                        await HandleScreenControlMessageAsync(connection, messageObj);
                        break;

                    case "file_transfer":
                        await HandleFileTransferMessageAsync(connection, messageObj);
                        break;

                    default:
                        _loggingService.LogWarning($"Unknown message type: {messageType}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, $"Error processing message from {connection.ConnectionId}");
            }
        }

        private async Task HandleChatMessageAsync(WebSocketConnection connection, dynamic message)
        {
            try
            {
                var input = message.input?.ToString();
                var personality = message.personality?.ToString();

                if (!string.IsNullOrEmpty(input))
                {
                    // This would be handled by the main MISA Engine
                    // For now, send a basic response
                    var response = new
                    {
                        type = "chat_response",
                        message = "Message received and processed",
                        timestamp = DateTime.UtcNow
                    };

                    await SendMessageAsync(connection.ConnectionId, response);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Error handling chat message");
            }
        }

        private async Task HandleScreenControlMessageAsync(WebSocketConnection connection, dynamic message)
        {
            try
            {
                var action = message.action?.ToString();

                var response = new
                {
                    type = "screen_control_response",
                    action = action,
                    success = true,
                    timestamp = DateTime.UtcNow
                };

                await SendMessageAsync(connection.ConnectionId, response);
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Error handling screen control message");
            }
        }

        private async Task HandleFileTransferMessageAsync(WebSocketConnection connection, dynamic message)
        {
            try
            {
                var action = message.action?.ToString();

                var response = new
                {
                    type = "file_transfer_response",
                    action = action,
                    success = true,
                    timestamp = DateTime.UtcNow
                };

                await SendMessageAsync(connection.ConnectionId, response);
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Error handling file transfer message");
            }
        }

        public async Task SendMessageAsync(string connectionId, object message)
        {
            if (_connections.TryGetValue(connectionId, out var connection))
            {
                try
                {
                    var json = JsonConvert.SerializeObject(message);
                    var buffer = Encoding.UTF8.GetBytes(json);

                    await connection.WebSocket.SendAsync(
                        new ArraySegment<byte>(buffer),
                        WebSocketMessageType.Text,
                        true,
                        _cancellationTokenSource.Token);

                    connection.LastActivity = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    _loggingService.LogError(ex, $"Error sending message to connection {connectionId}");
                    await CloseConnectionAsync(connectionId);
                }
            }
        }

        public async Task SendMessageToAllAsync(object message)
        {
            var tasks = _connections.Keys.Select(connId => SendMessageAsync(connId, message));
            await Task.WhenAll(tasks);
        }

        public async Task SendMessageToAllExceptAsync(string excludeConnectionId, object message)
        {
            var tasks = _connections.Keys
                .Where(connId => connId != excludeConnectionId)
                .Select(connId => SendMessageAsync(connId, message));

            await Task.WhenAll(tasks);
        }

        private async Task CloseConnectionAsync(string connectionId)
        {
            if (_connections.TryRemove(connectionId, out var connection))
            {
                try
                {
                    if (connection.WebSocket.State == WebSocketState.Open)
                    {
                        await connection.WebSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Closing",
                            _cancellationTokenSource.Token);
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogError(ex, $"Error closing WebSocket connection {connectionId}");
                }
                finally
                {
                    _loggingService.LogInformation($"WebSocket connection closed: {connectionId}");
                    _loggingService.LogSecurityEventAsync("WebSocketDisconnected", deviceId: connection.DeviceId);
                }
            }
        }

        public WebSocketConnection? GetConnection(string connectionId)
        {
            return _connections.TryGetValue(connectionId, out var connection) ? connection : null;
        }

        public WebSocketConnection? GetConnectionByDeviceId(string deviceId)
        {
            return _connections.Values.FirstOrDefault(conn => conn.DeviceId == deviceId);
        }

        public IEnumerable<WebSocketConnection> GetAllConnections()
        {
            return _connections.Values.ToList();
        }

        public int GetConnectionCount()
        {
            return _connections.Count;
        }

        public async Task CleanupInactiveConnectionsAsync(TimeSpan maxIdleTime)
        {
            var now = DateTime.UtcNow;
            var inactiveConnections = _connections.Values
                .Where(conn => now - conn.LastActivity > maxIdleTime)
                .Select(conn => conn.ConnectionId)
                .ToList();

            foreach (var connectionId in inactiveConnections)
            {
                await CloseConnectionAsync(connectionId);
            }
        }

        public bool IsRunning => _isRunning;
    }

    public class WebSocketConnection
    {
        public string ConnectionId { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public WebSocket WebSocket { get; set; } = null!;
        public DateTime ConnectedAt { get; set; }
        public DateTime LastActivity { get; set; }
    }
}