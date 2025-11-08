using MISA.Core.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Text.Json.Serialization;

namespace MISA.Core.Communication
{
    public class HTTPAPIServer
    {
        private readonly ConfigService _configService;
        private readonly SecurityService _securityService;
        private readonly LoggingService _loggingService;
        private IHost? _host;
        private bool _isRunning;
        private readonly int _port;
        private readonly string _baseUrl;

        public event EventHandler<string>? OnStatusChanged;
        public event EventHandler<string>? OnError;
        public event EventHandler<ApiRequestEventArgs>? OnApiRequest;
        public event EventHandler<ApiResponseEventArgs>? OnApiResponse;

        public HTTPAPIServer(ConfigService configService, SecurityService securityService, LoggingService loggingService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _securityService = securityService ?? throw new ArgumentNullException(nameof(securityService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            _port = _configService.GetValue<int>("Communication.HttpApiPort", 8081);
            _baseUrl = $"http://localhost:{_port}";
        }

        public async Task StartAsync()
        {
            if (_isRunning)
            {
                _loggingService.LogWarning("HTTP API Server is already running");
                return;
            }

            try
            {
                _loggingService.LogInformation($"Starting HTTP API Server on port {_port}...");
                OnStatusChanged?.Invoke(this, "Starting HTTP API Server...");

                var builder = WebApplication.CreateBuilder();
                ConfigureServices(builder.Services);

                var app = builder.Build();
                ConfigureMiddleware(app);
                ConfigureRoutes(app);

                _host = app;

                // Start the server
                await _host.StartAsync();

                _isRunning = true;
                OnStatusChanged?.Invoke(this, $"HTTP API Server started on {_baseUrl}");
                _loggingService.LogInformation($"HTTP API Server started successfully on {_baseUrl}");
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Failed to start HTTP API Server: {ex.Message}");
                _loggingService.LogError(ex, "Failed to start HTTP API Server");
                throw;
            }
        }

        public async Task StopAsync()
        {
            if (!_isRunning || _host == null)
                return;

            try
            {
                _loggingService.LogInformation("Stopping HTTP API Server...");
                OnStatusChanged?.Invoke(this, "Stopping HTTP API Server...");

                await _host.StopAsync(TimeSpan.FromSeconds(10));
                await _host.WaitForShutdownAsync();

                _isRunning = false;
                OnStatusChanged?.Invoke(this, "HTTP API Server stopped");
                _loggingService.LogInformation("HTTP API Server stopped");
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Error stopping HTTP API Server: {ex.Message}");
                _loggingService.LogError(ex, "Error stopping HTTP API Server");
            }
            finally
            {
                _host?.Dispose();
            }
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.PropertyNamingPolicy = null;
                    options.JsonSerializerOptions.WriteIndented = true;
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                });

            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = "MISA AI API",
                    Version = "v1",
                    Description = "REST API for MISA AI Assistant"
                });
            });

            services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", builder =>
                {
                    builder.AllowAnyOrigin()
                           .AllowAnyMethod()
                           .AllowAnyHeader();
                });
            });

            // Register services
            services.AddSingleton(_configService);
            services.AddSingleton(_securityService);
            services.AddSingleton(_loggingService);
        }

        private void ConfigureMiddleware(IApplicationBuilder app)
        {
            if (app is WebApplication webApp)
            {
                // Development middleware
                if (webApp.Environment.IsDevelopment())
                {
                    webApp.UseSwagger();
                    webApp.UseSwaggerUI(c =>
                    {
                        c.SwaggerEndpoint("/swagger/v1/swagger.json", "MISA AI API v1");
                        c.RoutePrefix = "swagger";
                    });
                }

                webApp.UseHttpsRedirection();
                webApp.UseCors("AllowAll");

                // Custom middleware
                webApp.UseMiddleware<RequestLoggingMiddleware>(_loggingService);
                webApp.UseMiddleware<AuthenticationMiddleware>(_securityService);
                webApp.UseMiddleware<RateLimitingMiddleware>();

                webApp.UseRouting();
                webApp.MapControllers();
            }
        }

        private void ConfigureRoutes(IEndpointRouteBuilder routes)
        {
            // Health check endpoint
            routes.MapGet("/health", async context =>
            {
                await context.Response.WriteAsJsonAsync(new { status = "healthy", timestamp = DateTime.UtcNow });
            });

            // Status endpoint
            routes.MapGet("/api/status", async context =>
            {
                var status = new
                {
                    IsRunning = _isRunning,
                    ServerUrl = _baseUrl,
                    StartTime = DateTime.UtcNow,
                    Version = "1.0.0"
                };

                await context.Response.WriteAsJsonAsync(status);
            });

            // Chat endpoint
            routes.MapPost("/api/chat", async (ChatRequest request, HttpContext context) =>
            {
                try
                {
                    await LogApiRequestAsync(context, "chat", request);

                    // This would integrate with the main MISA engine
                    var response = new ChatResponse
                    {
                        Message = $"Response to: {request.Message}",
                        Personality = request.Personality ?? "Girlfriend_Caring",
                        Timestamp = DateTime.UtcNow
                    };

                    await LogApiResponseAsync(context, "chat", response);
                    await context.Response.WriteAsJsonAsync(response);
                }
                catch (Exception ex)
                {
                    await HandleApiErrorAsync(context, ex, "chat");
                }
            });

            // Model management endpoints
            routes.MapGet("/api/models", async context =>
            {
                try
                {
                    var models = new[]
                    {
                        new { Name = "mixtral:8x7b", Status = "Installed", Size = "4.7GB" },
                        new { Name = "codellama:13b", Status = "Installed", Size = "7.4GB" },
                        new { Name = "dolphin-mistral:7b", Status = "Available", Size = "3.8GB" }
                    };

                    await context.Response.WriteAsJsonAsync(new { models });
                }
                catch (Exception ex)
                {
                    await HandleApiErrorAsync(context, ex, "models");
                }
            });

            // Memory endpoints
            routes.MapGet("/api/memory/search", async context =>
            {
                try
                {
                    var query = context.Request.Query["q"].ToString();
                    if (string.IsNullOrEmpty(query))
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new { error = "Query parameter 'q' is required" });
                        return;
                    }

                    var results = new[]
                    {
                        new { Id = "1", Content = "Sample memory result", Relevance = 0.95, Timestamp = DateTime.UtcNow }
                    };

                    await context.Response.WriteAsJsonAsync(new { query, results });
                }
                catch (Exception ex)
                {
                    await HandleApiErrorAsync(context, ex, "memory/search");
                }
            });

            routes.MapPost("/api/memory/store", async (MemoryStoreRequest request, HttpContext context) =>
            {
                try
                {
                    var memoryId = Guid.NewGuid().ToString("N");

                    await context.Response.WriteAsJsonAsync(new {
                        success = true,
                        memoryId,
                        message = "Memory stored successfully"
                    });
                }
                catch (Exception ex)
                {
                    await HandleApiErrorAsync(context, ex, "memory/store");
                }
            });

            // Screen sharing endpoints
            routes.MapPost("/api/screen/share", async context =>
            {
                try
                {
                    var sessionId = Guid.NewGuid().ToString("N");

                    await context.Response.WriteAsJsonAsync(new {
                        success = true,
                        sessionId,
                        signalingUrl = $"ws://localhost:8080/screen/{sessionId}"
                    });
                }
                catch (Exception ex)
                {
                    await HandleApiErrorAsync(context, ex, "screen/share");
                }
            });

            // Remote control endpoints
            routes.MapPost("/api/remote/control", async (RemoteControlRequest request, HttpContext context) =>
            {
                try
                {
                    await context.Response.WriteAsJsonAsync(new {
                        success = true,
                        message = "Remote control action processed"
                    });
                }
                catch (Exception ex)
                {
                    await HandleApiErrorAsync(context, ex, "remote/control");
                }
            });

            // File transfer endpoints
            routes.MapPost("/api/files/upload", async (HttpContext context) =>
            {
                try
                {
                    var file = context.Request.Form.Files.FirstOrDefault();
                    if (file == null)
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new { error = "No file provided" });
                        return;
                    }

                    var uploadPath = Path.Combine("uploads", file.FileName);
                    Directory.CreateDirectory("uploads");

                    using (var stream = new FileStream(uploadPath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    await context.Response.WriteAsJsonAsync(new {
                        success = true,
                        fileName = file.FileName,
                        size = file.Length,
                        uploadPath
                    });
                }
                catch (Exception ex)
                {
                    await HandleApiErrorAsync(context, ex, "files/upload");
                }
            });

            // Configuration endpoints
            routes.MapGet("/api/config", async context =>
            {
                try
                {
                    var config = new
                    {
                        Personality = new { Default = "Girlfriend_Caring", AutoSwitch = true },
                        Models = new { AutoDownload = true, MaxConcurrent = 3 },
                        Memory = new { CloudSync = false, MaxAgeDays = 365 }
                    };

                    await context.Response.WriteAsJsonAsync(config);
                }
                catch (Exception ex)
                {
                    await HandleApiErrorAsync(context, ex, "config");
                }
            });

            routes.MapPut("/api/config", async (ConfigUpdateRequest request, HttpContext context) =>
            {
                try
                {
                    await context.Response.WriteAsJsonAsync(new {
                        success = true,
                        message = "Configuration updated"
                    });
                }
                catch (Exception ex)
                {
                    await HandleApiErrorAsync(context, ex, "config");
                }
            });
        }

        private async Task LogApiRequestAsync(HttpContext context, string endpoint, object request)
        {
            OnApiRequest?.Invoke(this, new ApiRequestEventArgs
            {
                Endpoint = endpoint,
                Method = context.Request.Method,
                IpAddress = GetClientIpAddress(context),
                UserAgent = context.Request.Headers["User-Agent"].ToString(),
                Request = request,
                Timestamp = DateTime.UtcNow
            });

            _loggingService.LogApiCall(endpoint, context.Request.Method, 200, TimeSpan.Zero);
        }

        private async Task LogApiResponseAsync(HttpContext context, string endpoint, object response)
        {
            OnApiResponse?.Invoke(this, new ApiResponseEventArgs
            {
                Endpoint = endpoint,
                StatusCode = context.Response.StatusCode,
                Response = response,
                Timestamp = DateTime.UtcNow
            });
        }

        private async Task HandleApiErrorAsync(HttpContext context, Exception ex, string endpoint)
        {
            var statusCode = ex switch
            {
                ArgumentException => 400,
                UnauthorizedAccessException => 401,
                InvalidOperationException => 422,
                _ => 500
            };

            context.Response.StatusCode = statusCode;

            var errorResponse = new
            {
                error = ex.Message,
                endpoint,
                timestamp = DateTime.UtcNow,
                statusCode
            };

            OnError?.Invoke(this, $"API Error [{endpoint}]: {ex.Message}");
            _loggingService.LogError(ex, $"API Error: {endpoint}");

            await context.Response.WriteAsJsonAsync(errorResponse);
        }

        private string GetClientIpAddress(HttpContext context)
        {
            var xForwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(xForwardedFor))
            {
                return xForwardedFor.Split(',')[0].Trim();
            }

            var xRealIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrEmpty(xRealIp))
            {
                return xRealIp;
            }

            return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }

        public bool IsRunning => _isRunning;
        public string GetStatus() => _isRunning ? $"Running on {_baseUrl}" : "Stopped";

        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                if (!_isRunning) return false;

                using var httpClient = new HttpClient();
                var response = await httpClient.GetAsync($"{_baseUrl}/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }

    // API Request/Response Models
    public class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
        public string? Personality { get; set; }
        public string? DeviceId { get; set; }
        public Dictionary<string, object>? Context { get; set; }
    }

    public class ChatResponse
    {
        public string Message { get; set; } = string.Empty;
        public string Personality { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    public class MemoryStoreRequest
    {
        public string Content { get; set; } = string.Empty;
        public string Type { get; set; } = "Conversation";
        public List<string>? Tags { get; set; }
        public string? Category { get; set; }
    }

    public class RemoteControlRequest
    {
        public string SessionId { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public Dictionary<string, object>? Parameters { get; set; }
    }

    public class ConfigUpdateRequest
    {
        public Dictionary<string, object>? Personality { get; set; }
        public Dictionary<string, object>? Models { get; set; }
        public Dictionary<string, object>? Memory { get; set; }
        public Dictionary<string, object>? Communication { get; set; }
    }

    // Event Arguments
    public class ApiRequestEventArgs : EventArgs
    {
        public string Endpoint { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
        public object Request { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    public class ApiResponseEventArgs : EventArgs
    {
        public string Endpoint { get; set; } = string.Empty;
        public int StatusCode { get; set; }
        public object Response { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    // Custom Middleware
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly LoggingService _loggingService;

        public RequestLoggingMiddleware(RequestDelegate next, LoggingService loggingService)
        {
            _next = next;
            _loggingService = loggingService;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, $"HTTP Request failed: {context.Request.Path}");
                throw;
            }
            finally
            {
                var duration = DateTime.UtcNow - startTime;
                _loggingService.LogApiCall(
                    context.Request.Path,
                    context.Request.Method,
                    context.Response.StatusCode,
                    duration
                );
            }
        }
    }

    public class AuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly SecurityService _securityService;

        public AuthenticationMiddleware(RequestDelegate next, SecurityService securityService)
        {
            _next = next;
            _securityService = securityService;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var deviceId = context.Request.Headers["X-Device-ID"].FirstOrDefault();
            var signature = context.Request.Headers["X-Signature"].FirstOrDefault();
            var path = context.Request.Path;

            // Skip authentication for health and swagger endpoints
            if (path.StartsWithSegments("/health") || path.StartsWithSegments("/swagger"))
            {
                await _next(context);
                return;
            }

            if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(signature))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "Device authentication required" });
                return;
            }

            if (!_securityService.ValidateDeviceSignature(deviceId, signature))
            {
                await _securityService.LogSecurityEventAsync("APIAuthenticationFailed", deviceId: deviceId);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid device signature" });
                return;
            }

            await _next(context);
        }
    }

    public class RateLimitingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly Dictionary<string, List<DateTime>> _requests;
        private readonly object _lockObject = new object();

        public RateLimitingMiddleware(RequestDelegate next)
        {
            _next = next;
            _requests = new Dictionary<string, List<DateTime>>();
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var clientId = GetClientIdentifier(context);
            var now = DateTime.UtcNow;

            lock (_lockObject)
            {
                if (!_requests.ContainsKey(clientId))
                {
                    _requests[clientId] = new List<DateTime>();
                }

                // Remove old requests (older than 1 minute)
                _requests[clientId].RemoveAll(time => time < now.AddMinutes(-1));

                // Check rate limit (100 requests per minute)
                if (_requests[clientId].Count >= 100)
                {
                    context.Response.StatusCode = 429;
                    await context.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded" });
                    return;
                }

                _requests[clientId].Add(now);
            }

            await _next(context);
        }

        private string GetClientIdentifier(HttpContext context)
        {
            var deviceId = context.Request.Headers["X-Device-ID"].FirstOrDefault();
            return !string.IsNullOrEmpty(deviceId) ? deviceId : GetClientIpAddress(context);
        }

        private string GetClientIpAddress(HttpContext context)
        {
            var xForwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(xForwardedFor))
            {
                return xForwardedFor.Split(',')[0].Trim();
            }

            return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }
    }
}