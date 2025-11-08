using MISA.Core.Engine;
using MISA.Core.Services;
using Serilog;

namespace MISA.Core
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Initialize logging
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File("logs/misa-.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            try
            {
                Log.Information("MISA AI starting up...");

                // Initialize configuration service
                var configService = new ConfigService();
                await configService.InitializeAsync();

                // Initialize security service
                var securityService = new SecurityService();
                await securityService.InitializeAsync();

                // Create and start MISA Engine
                var engine = new MISAEngine(configService, securityService);

                // Handle system shutdown gracefully
                Console.CancelKeyPress += async (sender, e) =>
                {
                    e.Cancel = true;
                    Log.Information("Shutdown signal received, stopping MISA Engine...");
                    await engine.StopAsync();
                    Environment.Exit(0);
                };

                await engine.StartAsync();

                Log.Information("MISA AI started successfully. Press Ctrl+C to shutdown.");

                // Keep the application running
                await Task.Delay(Timeout.Infinite);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly");
                Environment.Exit(1);
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}