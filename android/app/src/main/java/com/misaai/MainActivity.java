package com.misaai;

import android.Manifest;
import android.content.pm.PackageManager;
import android.os.Bundle;
import android.view.View;
import android.widget.Button;
import android.widget.Toast;
import androidx.annotation.NonNull;
import androidx.appcompat.app.AppCompatActivity;
import androidx.core.app.ActivityCompat;
import androidx.core.content.ContextCompat;
import android.content.Intent;
import android.net.Uri;
import android.provider.Settings;
import android.widget.TextView;
import com.misaai.services.MISAService;
import com.misaai.utils.DeviceManager;
import com.misaai.ui.RemoteControlFragment;
import com.misaai.ui.ChatFragment;
import com.misaai.network.WebRTCClient;
import com.misaai.network.APIClient;

/**
 * Main Activity for MISA AI Android Application
 * Provides interface for remote control, AI interaction, and device pairing
 */
public class MainActivity extends AppCompatActivity {

    private static final String TAG = "MainActivity";
    private static final int PERMISSION_REQUEST_CODE = 1001;

    // UI Components
    private Button btnRemoteControl;
    private Button btnChatAI;
    private Button btnSettings;
    private Button btnPairDevice;
    private TextView tvStatus;
    private TextView tvDeviceInfo;

    // Services and Managers
    private MISAService misaService;
    private DeviceManager deviceManager;
    private WebRTCClient webRTCClient;
    private APIClient apiClient;

    // Permissions
    private static final String[] REQUIRED_PERMISSIONS = {
        Manifest.permission.CAMERA,
        Manifest.permission.RECORD_AUDIO,
        Manifest.permission.WRITE_EXTERNAL_STORAGE,
        Manifest.permission.READ_EXTERNAL_STORAGE,
        Manifest.permission.INTERNET,
        Manifest.permission.ACCESS_NETWORK_STATE,
        Manifest.permission.ACCESS_WIFI_STATE,
        Manifest.permission.FOREGROUND_SERVICE
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        initializeServices();
        initializeUI();
        checkPermissions();
        setupEventListeners();

        // Initialize MISA connection
        initializeMISAConnection();
    }

    private void initializeServices() {
        try {
            deviceManager = new DeviceManager(this);
            apiClient = new APIClient(this);
            webRTCClient = new WebRTCClient(this);
            misaService = new MISAService(this, apiClient, webRTCClient);
        } catch (Exception e) {
            showError("Failed to initialize services: " + e.getMessage());
        }
    }

    private void initializeUI() {
        btnRemoteControl = findViewById(R.id.btnRemoteControl);
        btnChatAI = findViewById(R.id.btnChatAI);
        btnSettings = findViewById(R.id.btnSettings);
        btnPairDevice = findViewById(R.id.btnPairDevice);
        tvStatus = findViewById(R.id.tvStatus);
        tvDeviceInfo = findViewById(R.id.tvDeviceInfo);

        // Set initial device info
        updateDeviceInfo();
    }

    private void checkPermissions() {
        if (!hasAllPermissions()) {
            requestPermissions();
        } else {
            onPermissionsGranted();
        }
    }

    private boolean hasAllPermissions() {
        for (String permission : REQUIRED_PERMISSIONS) {
            if (ContextCompat.checkSelfPermission(this, permission) != PackageManager.PERMISSION_GRANTED) {
                return false;
            }
        }
        return true;
    }

    private void requestPermissions() {
        ActivityCompat.requestPermissions(this, REQUIRED_PERMISSIONS, PERMISSION_REQUEST_CODE);
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, @NonNull String[] permissions, @NonNull int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode == PERMISSION_REQUEST_CODE) {
            boolean allGranted = true;
            for (int result : grantResults) {
                if (result != PackageManager.PERMISSION_GRANTED) {
                    allGranted = false;
                    break;
                }
            }

            if (allGranted) {
                onPermissionsGranted();
            } else {
                showPermissionDeniedMessage();
            }
        }
    }

    private void onPermissionsGranted() {
        updateStatus("Permissions granted - Ready to connect");
        // Initialize services that require permissions
        misaService.initializeWithPermissions();
    }

    private void showPermissionDeniedMessage() {
        new androidx.appcompat.app.AlertDialog.Builder(this)
            .setTitle("Permissions Required")
            .setMessage("MISA AI requires all permissions to function properly. Please grant permissions in Settings.")
            .setPositiveButton("Settings", (dialog, which) -> {
                Intent intent = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS);
                Uri uri = Uri.fromParts("package", getPackageName(), null);
                intent.setData(uri);
                startActivity(intent);
            })
            .setNegativeButton("Exit", (dialog, which) -> finish())
            .show();
    }

    private void setupEventListeners() {
        btnRemoteControl.setOnClickListener(v -> openRemoteControl());
        btnChatAI.setOnClickListener(v -> openChatAI());
        btnSettings.setOnClickListener(v -> openSettings());
        btnPairDevice.setOnClickListener(v -> pairWithDesktop());
    }

    private void openRemoteControl() {
        try {
            if (misaService.isConnected()) {
                RemoteControlFragment fragment = RemoteControlFragment.newInstance();
                getSupportFragmentManager()
                    .beginTransaction()
                    .replace(R.id.fragmentContainer, fragment)
                    .addToBackStack(null)
                    .commit();
            } else {
                showError("Not connected to MISA desktop. Please pair with desktop first.");
            }
        } catch (Exception e) {
            showError("Failed to open remote control: " + e.getMessage());
        }
    }

    private void openChatAI() {
        try {
            ChatFragment fragment = ChatFragment.newInstance();
            getSupportFragmentManager()
                .beginTransaction()
                .replace(R.id.fragmentContainer, fragment)
                .addToBackStack(null)
                .commit();
        } catch (Exception e) {
            showError("Failed to open AI chat: " + e.getMessage());
        }
    }

    private void openSettings() {
        try {
            Intent intent = new Intent(this, SettingsActivity.class);
            startActivity(intent);
        } catch (Exception e) {
            showError("Failed to open settings: " + e.getMessage());
        }
    }

    private void pairWithDesktop() {
        try {
            // Start device pairing process
            deviceManager.startPairingProcess(new DeviceManager.PairingCallback() {
                @Override
                public void onPairingStarted() {
                    updateStatus("Scanning for MISA desktop...");
                }

                @Override
                public void onDeviceFound(String deviceId, String deviceName) {
                    runOnUiThread(() -> showDeviceFoundDialog(deviceId, deviceName));
                }

                @Override
                public void onPairingSuccess(String deviceId) {
                    runOnUiThread(() -> {
                        updateStatus("Successfully paired with desktop");
                        Toast.makeText(MainActivity.this, "Pairing successful!", Toast.LENGTH_SHORT).show();
                    });
                }

                @Override
                public void onPairingFailed(String error) {
                    runOnUiThread(() -> showError("Pairing failed: " + error));
                }
            });
        } catch (Exception e) {
            showError("Failed to start pairing: " + e.getMessage());
        }
    }

    private void showDeviceFoundDialog(String deviceId, String deviceName) {
        new androidx.appcompat.app.AlertDialog.Builder(this)
            .setTitle("MISA Desktop Found")
            .setMessage("Found MISA desktop: " + deviceName + "\nDevice ID: " + deviceId)
            .setPositiveButton("Connect", (dialog, which) -> {
                deviceManager.pairWithDevice(deviceId);
                updateStatus("Connecting to desktop...");
            })
            .setNegativeButton("Cancel", null)
            .show();
    }

    private void initializeMISAConnection() {
        try {
            // Get desktop connection info from QR code or manual entry
            // This would typically come from a QR code scan or user input
            String desktopEndpoint = getDesktopEndpointFromIntent();

            if (desktopEndpoint != null) {
                misaService.connectToDesktop(desktopEndpoint, new MISAService.ConnectionCallback() {
                    @Override
                    public void onConnected() {
                        runOnUiThread(() -> {
                            updateStatus("Connected to MISA desktop");
                            Toast.makeText(MainActivity.this, "Successfully connected to desktop!", Toast.LENGTH_SHORT).show();
                        });
                    }

                    @Override
                    public void onConnectionFailed(String error) {
                        runOnUiThread(() -> {
                            updateStatus("Connection failed: " + error);
                            showError("Failed to connect to desktop: " + error);
                        });
                    }

                    @Override
                    public void onDisconnected() {
                        runOnUiThread(() -> {
                            updateStatus("Disconnected from desktop");
                            Toast.makeText(MainActivity.this, "Disconnected from desktop", Toast.LENGTH_SHORT).show();
                        });
                    }
                });
            }
        } catch (Exception e) {
            showError("Failed to initialize MISA connection: " + e.getMessage());
        }
    }

    private String getDesktopEndpointFromIntent() {
        // Extract desktop endpoint from intent (from QR code scan or deep link)
        Intent intent = getIntent();
        if (intent != null && intent.getData() != null) {
            Uri data = intent.getData();
            if ("misa".equals(data.getScheme()) && "connect".equals(data.getHost())) {
                return data.getQueryParameter("endpoint");
            }
        }

        // For testing, return a default endpoint
        // In production, this would come from user input or QR code
        return "ws://192.168.1.100:8080";
    }

    private void updateDeviceInfo() {
        try {
            String deviceId = deviceManager.getDeviceId();
            String deviceName = deviceManager.getDeviceName();
            String osVersion = android.os.Build.VERSION.RELEASE;
            String manufacturer = android.os.Build.MANUFACTURER;
            String model = android.os.Build.MODEL;

            String info = String.format("Device: %s %s\nOS: Android %s\nID: %s",
                manufacturer, model, osVersion, deviceId.substring(0, 8) + "...");

            tvDeviceInfo.setText(info);
        } catch (Exception e) {
            tvDeviceInfo.setText("Device info unavailable");
        }
    }

    private void updateStatus(String status) {
        tvStatus.setText(status);
    }

    private void showError(String error) {
        Toast.makeText(this, error, Toast.LENGTH_LONG).show();
        updateStatus("Error: " + error);
    }

    @Override
    protected void onResume() {
        super.onResume();
        updateDeviceInfo();

        // Check MISA connection status
        if (misaService != null) {
            if (misaService.isConnected()) {
                updateStatus("Connected to MISA desktop");
            } else {
                updateStatus("Not connected to desktop");
            }
        }
    }

    @Override
    protected void onPause() {
        super.onPause();

        // Pause background operations if needed
        if (misaService != null) {
            misaService.onBackground();
        }
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();

        // Cleanup resources
        if (misaService != null) {
            misaService.cleanup();
        }
        if (deviceManager != null) {
            deviceManager.cleanup();
        }
        if (webRTCClient != null) {
            webRTCClient.cleanup();
        }
    }

    @Override
    public void onBackPressed() {
        // Handle back button press
        if (getSupportFragmentManager().getBackStackEntryCount() > 0) {
            getSupportFragmentManager().popBackStack();
        } else {
            new androidx.appcompat.app.AlertDialog.Builder(this)
                .setTitle("Exit MISA AI?")
                .setMessage("Are you sure you want to exit MISA AI?")
                .setPositiveButton("Exit", (dialog, which) -> finish())
                .setNegativeButton("Cancel", null)
                .show();
        }
    }
}