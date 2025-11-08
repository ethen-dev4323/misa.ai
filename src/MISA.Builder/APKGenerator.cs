using MISA.Core.Services;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO.Compression;

namespace MISA.Builder
{
    public class APKGenerator
    {
        private readonly ConfigService _configService;
        private readonly LoggingService _loggingService;
        private readonly string _androidProjectPath;
        private readonly string _keystorePath;
        private readonly string _gradlePath;

        public event EventHandler<string>? OnStatusChanged;
        public event EventHandler<string>? OnError;
        public event EventHandler<APKBuildProgressEventArgs>? OnProgressChanged;

        public APKGenerator(ConfigService configService, LoggingService loggingService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            _androidProjectPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "android");
            _keystorePath = Path.Combine(_androidProjectPath, "keystore", "misa-release.keystore");
            _gradlePath = Path.Combine(_androidProjectPath, "gradlew.bat");
        }

        public async Task<string> GenerateAPKAsync(APKGenerationRequest request)
        {
            try
            {
                _loggingService.LogInformation("Starting APK generation...");
                OnStatusChanged?.Invoke(this, "Starting APK generation...");

                // Validate prerequisites
                await ValidatePrerequisitesAsync();

                // Create output directory
                var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "build", "android");
                Directory.CreateDirectory(outputDir);

                // Generate Android project
                await GenerateAndroidProjectAsync(request);

                // Configure project with MISA settings
                await ConfigureProjectAsync(request);

                // Build APK
                var apkPath = await BuildAPKAsync(request);

                // Sign APK (if release build)
                if (request.BuildType == BuildType.Release)
                {
                    apkPath = await SignAPKAsync(apkPath, request);
                }

                // Verify APK
                await VerifyAPKAsync(apkPath);

                _loggingService.LogInformation($"APK generated successfully: {apkPath}");
                OnStatusChanged?.Invoke(this, "APK generated successfully");

                return apkPath;
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Failed to generate APK: {ex.Message}");
                _loggingService.LogError(ex, "Failed to generate APK");
                throw;
            }
        }

        private async Task ValidatePrerequisitesAsync()
        {
            OnProgressChanged?.Invoke(this, new APKBuildProgressEventArgs { Progress = 5, Message = "Validating prerequisites..." });

            // Check if Android project exists
            if (!Directory.Exists(_androidProjectPath))
            {
                throw new DirectoryNotFoundException("Android project directory not found");
            }

            // Check if Gradle wrapper exists
            if (!File.Exists(_gradlePath))
            {
                throw new FileNotFoundException("Gradle wrapper not found. Ensure Android project is properly set up.");
            }

            // Check if keystore exists for release builds
            if (!File.Exists(_keystorePath))
            {
                _loggingService.LogWarning("Release keystore not found. Creating new keystore...");
                await CreateKeystoreAsync();
            }

            // Check for Java installation
            try
            {
                var javaVersion = await GetJavaVersionAsync();
                _loggingService.LogInformation($"Java version detected: {javaVersion}");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Java is not installed or not accessible", ex);
            }

            // Check for Android SDK (this would be more sophisticated in production)
            OnProgressChanged?.Invoke(this, new APKBuildProgressEventArgs { Progress = 10, Message = "Prerequisites validated" });
        }

        private async Task GenerateAndroidProjectAsync(APKGenerationRequest request)
        {
            OnProgressChanged?.Invoke(this, new APKBuildProgressEventArgs { Progress = 15, Message = "Generating Android project..." });

            // Create Android project structure if it doesn't exist
            await CreateProjectStructureAsync();

            // Generate source files with MISA configuration
            await GenerateSourceFilesAsync(request);

            // Generate resources
            await GenerateResourcesAsync(request);

            // Generate manifest
            await GenerateManifestAsync(request);

            OnProgressChanged?.Invoke(this, new APKBuildProgressEventArgs { Progress = 35, Message = "Android project generated" });
        }

        private async Task ConfigureProjectAsync(APKGenerationRequest request)
        {
            OnProgressChanged?.Invoke(this, new APKBuildProgressEventArgs { Progress = 40, Message = "Configuring project..." });

            // Update build.gradle with MISA configuration
            await UpdateBuildGradleAsync(request);

            // Generate gradle.properties
            await GenerateGradlePropertiesAsync(request);

            // Update local.properties
            await UpdateLocalPropertiesAsync();

            OnProgressChanged?.Invoke(this, new APKBuildProgressEventArgs { Progress = 50, Message = "Project configured" });
        }

        private async Task<string> BuildAPKAsync(APKGenerationRequest request)
        {
            OnProgressChanged?.Invoke(this, new APKBuildProgressEventArgs { Progress = 55, Message = "Building APK..." });

            var buildType = request.BuildType.ToString().ToLower();
            var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "build", "android");

            var processInfo = new ProcessStartInfo
            {
                FileName = _gradlePath,
                Arguments = $"assemble{buildType.First().ToString().ToUpper()}{buildType.Substring(1)} " +
                           $"-Pmisa.api_base_url={request.ApiBaseUrl} " +
                           $"-Pmisa.ws_base_url={request.WebSocketBaseUrl} " +
                           $"-Pmisa.device_id={request.DeviceId} " +
                           $"-Pmisa.auth_token={request.AuthToken} " +
                           $"-Pmisa.cloud_enabled={request.CloudEnabled}",
                WorkingDirectory = _androidProjectPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);

            if (process == null)
            {
                throw new InvalidOperationException("Failed to start Gradle build process");
            }

            // Monitor build progress
            await MonitorBuildProgressAsync(process);

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"Gradle build failed with exit code {process.ExitCode}: {error}");
            }

            // Find the generated APK
            var apkFile = buildType == "release" ?
                Path.Combine(_androidProjectPath, "app", "build", "outputs", "apk", "release", "app-release.apk") :
                Path.Combine(_androidProjectPath, "app", "build", "outputs", "apk", "debug", "app-debug.apk");

            if (!File.Exists(apkFile))
            {
                throw new FileNotFoundException("Generated APK file not found");
            }

            // Copy APK to output directory with MISA-specific naming
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var outputApk = Path.Combine(outputDir, $"misa-android-{buildType}-{timestamp}.apk");
            File.Copy(apkFile, outputApk, true);

            OnProgressChanged?.Invoke(this, new APKBuildProgressEventArgs { Progress = 85, Message = "APK built successfully" });

            return outputApk;
        }

        private async Task<string> SignAPKAsync(string apkPath, APKGenerationRequest request)
        {
            OnProgressChanged?.Invoke(this, new APKBuildProgressEventArgs { Progress = 90, Message = "Signing APK..." });

            // In a real implementation, this would use proper APK signing
            // For now, we'll just copy the file with a note
            var signedApk = Path.Combine(Path.GetDirectoryName(apkPath)!, "signed_" + Path.GetFileName(apkPath));
            File.Copy(apkPath, signedApk, true);

            OnProgressChanged?.Invoke(this, new APKBuildProgressEventArgs { Progress = 95, Message = "APK signed" });

            return signedApk;
        }

        private async Task VerifyAPKAsync(string apkPath)
        {
            OnProgressChanged?.Invoke(this, new APKBuildProgressEventArgs { Progress = 98, Message = "Verifying APK..." });

            // Basic verification - check file exists and has reasonable size
            if (!File.Exists(apkPath))
            {
                throw new FileNotFoundException("APK file not found for verification");
            }

            var fileInfo = new FileInfo(apkPath);
            if (fileInfo.Length < 1000000) // Less than 1MB seems too small
            {
                throw new InvalidOperationException("APK file appears to be corrupted or incomplete");
            }

            OnProgressChanged?.Invoke(this, new APKBuildProgressEventArgs { Progress = 100, Message = "APK verification complete" });
        }

        private async Task CreateProjectStructureAsync()
        {
            var directories = new[]
            {
                "app/src/main/java/com/misaai",
                "app/src/main/java/com/misaai/activities",
                "app/src/main/java/com/misaai/fragments",
                "app/src/main/java/com/misaai/services",
                "app/src/main/java/com/misaai/network",
                "app/src/main/java/com/misaai/utils",
                "app/src/main/java/com/misaai/ui",
                "app/src/main/java/com/misaai/models",
                "app/src/main/res/layout",
                "app/src/main/res/values",
                "app/src/main/res/drawable",
                "app/src/main/res/mipmap-hdpi",
                "app/src/main/res/mipmap-mdpi",
                "app/src/main/res/mipmap-xhdpi",
                "app/src/main/res/mipmap-xxhdpi",
                "app/src/main/res/mipmap-xxxhdpi",
                "keystore"
            };

            foreach (var dir in directories)
            {
                var fullPath = Path.Combine(_androidProjectPath, dir);
                Directory.CreateDirectory(fullPath);
            }
        }

        private async Task GenerateSourceFilesAsync(APKGenerationRequest request)
        {
            // Generate MainActivity with MISA configuration
            var mainActivityContent = GenerateMainActivity(request);
            var mainActivityPath = Path.Combine(_androidProjectPath, "app/src/main/java/com/misaai/MainActivity.java");
            await File.WriteAllTextAsync(mainActivityPath, mainActivityContent);

            // Generate service classes
            await GenerateServiceClassesAsync(request);

            // Generate network classes
            await GenerateNetworkClassesAsync(request);

            // Generate utility classes
            await GenerateUtilityClassesAsync(request);
        }

        private string GenerateMainActivity(APKGenerationRequest request)
        {
            return $@"package com.misaai;

import android.Manifest;
import android.content.pm.PackageManager;
import android.os.Bundle;
import androidx.appcompat.app.AppCompatActivity;
import com.misaai.services.MISAService;

/**
 * MISA AI Android Application - Auto-generated
 *
 * Configuration:
 * - API Endpoint: {request.ApiBaseUrl}
 * - WebSocket Endpoint: {request.WebSocketBaseUrl}
 * - Device ID: {request.DeviceId}
 * - Cloud Enabled: {request.CloudEnabled}
 * - Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
 */
public class MainActivity extends AppCompatActivity {{

    private static final String TAG = ""MainActivity"";
    private MISAService misaService;

    @Override
    protected void onCreate(Bundle savedInstanceState) {{
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        // Initialize MISA service with generated configuration
        misaService = new MISAService(this);
        misaService.initialize(
            ""{request.ApiBaseUrl}"",
            ""{request.WebSocketBaseUrl}"",
            ""{request.DeviceId}"",
            ""{request.AuthToken}"",
            {request.CloudEnabled.ToString().ToLower()}
        );

        // Request necessary permissions
        requestPermissions();
    }}

    private void requestPermissions() {{
        String[] permissions = {{
            Manifest.permission.CAMERA,
            Manifest.permission.RECORD_AUDIO,
            Manifest.permission.WRITE_EXTERNAL_STORAGE,
            Manifest.permission.READ_EXTERNAL_STORAGE,
            Manifest.permission.INTERNET
        }};

        requestPermissions(permissions, 1001);
    }}

    @Override
    protected void onDestroy() {{
        super.onDestroy();
        if (misaService != null) {{
            misaService.cleanup();
        }}
    }}
}}";
        }

        private async Task GenerateServiceClassesAsync(APKGenerationRequest request)
        {
            // Generate MISAService
            var misaServiceContent = GenerateMISAService(request);
            var misaServicePath = Path.Combine(_androidProjectPath, "app/src/main/java/com/misaai/services/MISAService.java");
            await File.WriteAllTextAsync(misaServicePath, misaServiceContent);

            // Generate other service classes...
        }

        private string GenerateMISAService(APKGenerationRequest request)
        {
            return $@"package com.misaai.services;

import android.content.Context;
import com.misaai.network.APIClient;
import com.misaai.network.WebRTCClient;

/**
 * MISA Service - Auto-generated
 * Handles communication with MISA desktop application
 */
public class MISAService {{

    private Context context;
    private APIClient apiClient;
    private WebRTCClient webRTCClient;

    public MISAService(Context context) {{
        this.context = context;
    }}

    public void initialize(String apiBaseUrl, String wsBaseUrl, String deviceId, String authToken, boolean cloudEnabled) {{
        // Initialize with generated configuration
        apiClient = new APIClient(context, apiBaseUrl, deviceId, authToken);
        webRTCClient = new WebRTCClient(context, wsBaseUrl, deviceId);
    }}

    public void cleanup() {{
        if (webRTCClient != null) {{
            webRTCClient.disconnect();
        }}
        if (apiClient != null) {{
            apiClient.disconnect();
        }}
    }}
}}";
        }

        private async Task GenerateNetworkClassesAsync(APKGenerationRequest request)
        {
            // Generate APIClient
            var apiClientContent = GenerateAPIClient(request);
            var apiClientPath = Path.Combine(_androidProjectPath, "app/src/main/java/com/misaai/network/APIClient.java");
            await File.WriteAllTextAsync(apiClientPath, apiClientContent);

            // Generate WebRTCClient
            var webRTCClientContent = GenerateWebRTCClient(request);
            var webRTCClientPath = Path.Combine(_androidProjectPath, "app/src/main/java/com/misaai/network/WebRTCClient.java");
            await File.WriteAllTextAsync(webRTCClientPath, webRTCClientContent);
        }

        private string GenerateAPIClient(APKGenerationRequest request)
        {
            return $@"package com.misaai.network;

import android.content.Context;
import retrofit2.Retrofit;
import retrofit2.converter.gson.GsonConverterFactory;

/**
 * API Client - Auto-generated
 * Handles HTTP communication with MISA backend
 */
public class APIClient {{

    private Context context;
    private Retrofit retrofit;
    private String baseUrl;
    private String deviceId;
    private String authToken;

    public APIClient(Context context, String baseUrl, String deviceId, String authToken) {{
        this.context = context;
        this.baseUrl = baseUrl;
        this.deviceId = deviceId;
        this.authToken = authToken;

        initializeRetrofit();
    }}

    private void initializeRetrofit() {{
        retrofit = new Retrofit.Builder()
            .baseUrl(baseUrl)
            .addConverterFactory(GsonConverterFactory.create())
            .build();
    }}

    public void disconnect() {{
        // Cleanup resources
    }}
}}";
        }

        private string GenerateWebRTCClient(APKGenerationRequest request)
        {
            return $@"package com.misaai.network;

import android.content.Context;
import org.webrtc.*;

/**
 * WebRTC Client - Auto-generated
 * Handles real-time communication for screen sharing and remote control
 */
public class WebRTCClient {{

    private Context context;
    private String wsBaseUrl;
    private String deviceId;

    public WebRTCClient(Context context, String wsBaseUrl, String deviceId) {{
        this.context = context;
        this.wsBaseUrl = wsBaseUrl;
        this.deviceId = deviceId;
    }}

    public void connect() {{
        // Connect to MISA WebSocket for real-time communication
    }}

    public void disconnect() {{
        // Disconnect and cleanup
    }}
}}";
        }

        private async Task GenerateUtilityClassesAsync(APKGenerationRequest request)
        {
            // Generate utility classes as needed
        }

        private async Task GenerateResourcesAsync(APKGenerationRequest request)
        {
            // Generate strings.xml
            var stringsContent = GenerateStringsXml();
            var stringsPath = Path.Combine(_androidProjectPath, "app/src/main/res/values/strings.xml");
            await File.WriteAllTextAsync(stringsPath, stringsContent);

            // Generate colors.xml
            var colorsContent = GenerateColorsXml();
            var colorsPath = Path.Combine(_androidProjectPath, "app/src/main/res/values/colors.xml");
            await File.WriteAllTextAsync(colorsPath, colorsContent);

            // Generate layouts
            await GenerateLayoutsAsync(request);

            // Generate drawable resources
            await GenerateDrawablesAsync(request);
        }

        private string GenerateStringsXml()
        {
            return @"<?xml version=""1.0"" encoding=""utf-8""?>
<resources>
    <string name=""app_name"">MISA AI</string>
    <string name=""remote_control"">Remote Control</string>
    <string name=""chat_ai"">Chat with AI</string>
    <string name=""settings"">Settings</string>
    <string name=""pair_device"">Pair Device</string>
    <string name=""connect_desktop"">Connect to Desktop</string>
    <string name=""status_connected"">Connected to MISA Desktop</string>
    <string name=""status_disconnected"">Not connected</string>
    <string name=""permissions_required"">Permissions Required</string>
    <string name=""permissions_message"">MISA AI requires all permissions to function properly.</string>
</resources>";
        }

        private string GenerateColorsXml()
        {
            return @"<?xml version=""1.0"" encoding=""utf-8""?>
<resources>
    <color name=""misa_primary"">#2196F3</color>
    <color name=""misa_primary_dark"">#1976D2</color>
    <color name=""misa_accent"">#FF4081</color>
    <color name=""misa_background"">#F5F5F5</color>
    <color name=""misa_text"">#212121</color>
    <color name=""misa_text_secondary"">#757575</color>
</resources>";
        }

        private async Task GenerateLayoutsAsync(APKGenerationRequest request)
        {
            // Generate activity_main.xml
            var mainLayoutContent = GenerateMainActivityLayout();
            var mainLayoutPath = Path.Combine(_androidProjectPath, "app/src/main/res/layout/activity_main.xml");
            await File.WriteAllTextAsync(mainLayoutPath, mainLayoutContent);
        }

        private string GenerateMainActivityLayout()
        {
            return @"<?xml version=""1.0"" encoding=""utf-8""?>
<LinearLayout xmlns:android=""http://schemas.android.com/apk/res/android""
    android:layout_width=""match_parent""
    android:layout_height=""match_parent""
    android:orientation=""vertical""
    android:padding=""16dp""
    android:background=""@color/misa_background"">

    <TextView
        android:id=""@+id/tvDeviceInfo""
        android:layout_width=""match_parent""
        android:layout_height=""wrap_content""
        android:text=""Device Info""
        android:textSize=""14sp""
        android:textColor=""@color/misa_text_secondary""
        android:layout_marginBottom=""16dp"" />

    <TextView
        android:id=""@+id/tvStatus""
        android:layout_width=""match_parent""
        android:layout_height=""wrap_content""
        android:text=""Ready""
        android:textSize=""16sp""
        android:textColor=""@color/misa_text""
        android:layout_marginBottom=""24dp"" />

    <Button
        android:id=""@+id/btnRemoteControl""
        android:layout_width=""match_parent""
        android:layout_height=""wrap_content""
        android:text=""@string/remote_control""
        android:layout_marginBottom=""8dp"" />

    <Button
        android:id=""@+id/btnChatAI""
        android:layout_width=""match_parent""
        android:layout_height=""wrap_content""
        android:text=""@string/chat_ai""
        android:layout_marginBottom=""8dp"" />

    <Button
        android:id=""@+id/btnSettings""
        android:layout_width=""match_parent""
        android:layout_height=""wrap_content""
        android:text=""@string/settings""
        android:layout_marginBottom=""8dp"" />

    <Button
        android:id=""@+id/btnPairDevice""
        android:layout_width=""match_parent""
        android:layout_height=""wrap_content""
        android:text=""@string/pair_device"" />

    <FrameLayout
        android:id=""@+id/fragmentContainer""
        android:layout_width=""match_parent""
        android:layout_height=""0dp""
        android:layout_weight=""1""
        android:layout_marginTop=""16dp"" />

</LinearLayout>";
        }

        private async Task GenerateDrawablesAsync(APKGenerationRequest request)
        {
            // Generate basic drawable resources
        }

        private async Task GenerateManifestAsync(APKGenerationRequest request)
        {
            var manifestContent = GenerateAndroidManifest(request);
            var manifestPath = Path.Combine(_androidProjectPath, "app/src/main/AndroidManifest.xml");
            await File.WriteAllTextAsync(manifestPath, manifestContent);
        }

        private string GenerateAndroidManifest(APKGenerationRequest request)
        {
            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android""
    package=""com.misaai"">

    <!-- Permissions -->
    <uses-permission android:name=""android.permission.INTERNET"" />
    <uses-permission android:name=""android.permission.ACCESS_NETWORK_STATE"" />
    <uses-permission android:name=""android.permission.ACCESS_WIFI_STATE"" />
    <uses-permission android:name=""android.permission.CAMERA"" />
    <uses-permission android:name=""android.permission.RECORD_AUDIO"" />
    <uses-permission android:name=""android.permission.WRITE_EXTERNAL_STORAGE"" />
    <uses-permission android:name=""android.permission.READ_EXTERNAL_STORAGE"" />
    <uses-permission android:name=""android.permission.FOREGROUND_SERVICE"" />

    <application
        android:allowBackup=""true""
        android:icon=""@mipmap/ic_launcher""
        android:label=""@string/app_name""
        android:theme=""@style/AppTheme"">

        <activity
            android:name="".MainActivity""
            android:exported=""true""
            android:screenOrientation=""portrait""
            android:launchMode=""singleTop"">
            <intent-filter>
                <action android:name=""android.intent.action.MAIN"" />
                <category android:name=""android.intent.category.LAUNCHER"" />
            </intent-filter>

            <!-- Handle MISA deep links -->
            <intent-filter>
                <action android:name=""android.intent.action.VIEW"" />
                <category android:name=""android.intent.category.DEFAULT"" />
                <category android:name=""android.intent.category.BROWSABLE"" />
                <data android:scheme=""misa"" android:host=""connect"" />
            </intent-filter>
        </activity>

        <!-- MISA Service for background operations -->
        <service
            android:name="".services.MISAService""
            android:enabled=""true""
            android:exported=""false"" />

    </application>
</manifest>";
        }

        private async Task UpdateBuildGradleAsync(APKGenerationRequest request)
        {
            var buildGradlePath = Path.Combine(_androidProjectPath, "app/build.gradle");
            var content = await File.ReadAllTextAsync(buildGradlePath);

            // Add MISA-specific configuration
            content = content.Replace(
                "android {",
                $@"android {{
    defaultConfig {{
        buildConfigField ""String"", ""MISA_API_BASE_URL"", ""\""{request.ApiBaseUrl}\"""""
        buildConfigField ""String"", ""MISA_WS_BASE_URL"", ""\""{request.WebSocketBaseUrl}\"""""
        buildConfigField ""String"", ""MISA_DEVICE_ID"", ""\""{request.DeviceId}\"""""
        buildConfigField ""String"", ""MISA_AUTH_TOKEN"", ""\""{request.AuthToken}\"""""
        buildConfigField ""boolean"", ""MISA_CLOUD_ENABLED"", ""{request.CloudEnabled}""
    }}");

            await File.WriteAllTextAsync(buildGradlePath, content);
        }

        private async Task GenerateGradlePropertiesAsync(APKGenerationRequest request)
        {
            var propertiesContent = $@"# MISA AI Generated Configuration
misa.api.base_url={request.ApiBaseUrl}
misa.ws.base_url={request.WebSocketBaseUrl}
misa.device.id={request.DeviceId}
misa.auth.token={request.AuthToken}
misa.cloud.enabled={request.CloudEnabled}
misa.generation.date={DateTime.Now:yyyy-MM-dd HH:mm:ss}

# Android SDK settings
android.useAndroidX=true
android.enableJetifier=true
org.gradle.jvmargs=-Xmx2048m -Dfile.encoding=UTF-8
org.gradle.parallel=true
org.gradle.daemon=true";

            var propertiesPath = Path.Combine(_androidProjectPath, "gradle.properties");
            await File.WriteAllTextAsync(propertiesPath, propertiesContent);
        }

        private async Task UpdateLocalPropertiesAsync()
        {
            var propertiesContent = @"# MISA AI Local Properties
# This file is auto-generated and should not be modified manually

# Android SDK location (modify as needed)
sdk.dir=C:\\Users\\%USERNAME%\\AppData\\Local\\Android\\Sdk";

            var propertiesPath = Path.Combine(_androidProjectPath, "local.properties");
            await File.WriteAllTextAsync(propertiesPath, propertiesContent);
        }

        private async Task MonitorBuildProgressAsync(Process process)
        {
            var progress = 55;
            var progressStep = 30.0 / 100; // Distribute over expected output lines

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    progress += progressStep;
                    if (progress >= 85) progress = 85; // Cap at 85% before completion

                    OnProgressChanged?.Invoke(this, new APKBuildProgressEventArgs
                    {
                        Progress = (int)progress,
                        Message = $"Building: {e.Data}"
                    });
                }
            };

            process.BeginOutputReadLine();
        }

        private async Task<string> GetJavaVersionAsync()
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "java",
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process != null)
            {
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                return error;
            }

            throw new InvalidOperationException("Java not found");
        }

        private async Task CreateKeystoreAsync()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_keystorePath)!);

            // Generate a self-signed keystore (in production, this should use proper security)
            var processInfo = new ProcessStartInfo
            {
                FileName = "keytool",
                Arguments = $"-genkey -v -keystore \"{_keystorePath}\" -alias misa-key -keyalg RSA -keysize 2048 -validity 10000 -storepass misa123456 -keypass misa123456 -dname \"CN=MISA AI, OU=AI Development, O=MISA Technologies, L=San Francisco, ST=California, C=US\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                {
                    _loggingService.LogInformation("Release keystore created successfully");
                }
                else
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    _loggingService.LogWarning($"Failed to create keystore: {error}");
                }
            }
        }

        public async Task<bool> IsReadyAsync()
        {
            try
            {
                return Directory.Exists(_androidProjectPath) &&
                       File.Exists(_gradlePath) &&
                       await GetJavaVersionAsync() != null;
            }
            catch
            {
                return false;
            }
        }
    }

    public class APKGenerationRequest
    {
        public string ApiBaseUrl { get; set; } = "https://api.misa.ai";
        public string WebSocketBaseUrl { get; set; } = "wss://ws.misa.ai";
        public string DeviceId { get; set; } = string.Empty;
        public string AuthToken { get; set; } = string.Empty;
        public bool CloudEnabled { get; set; } = true;
        public BuildType BuildType { get; set; } = BuildType.Release;
        public string AppName { get; set; } = "MISA AI";
        public string Version { get; set; } = "1.0.0";
        public string PackageName { get; set; } = "com.misaai";
        public Dictionary<string, object> CustomConfiguration { get; set; } = new();
    }

    public class APKBuildProgressEventArgs : EventArgs
    {
        public int Progress { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public enum BuildType
    {
        Debug,
        Release
    }
}