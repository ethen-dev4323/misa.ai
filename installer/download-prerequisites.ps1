# MISA AI - Prerequisite Download Script
# Downloads all required prerequisites for the installer

param(
    [string]$OutputDir = "prerequisites",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

Write-Host "MISA AI Prerequisite Downloader" -ForegroundColor Green
Write-Host "================================" -ForegroundColor Green

# Create output directory if it doesn't exist
if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    Write-Host "Created output directory: $OutputDir" -ForegroundColor Yellow
}

# Define downloads with URLs and expected file sizes for validation
$downloads = @(
    @{
        Name = ".NET 8 Runtime"
        Url = "https://download.microsoft.com/download/8/D/8/8D80C5F7-1B8E-4E7A-A5C6-7E2C7D4E1E6D/windowsdesktop-runtime-8.0.0-win-x64.exe"
        Filename = "dotnet-runtime-8.0.exe"
        ExpectedSize = 50MB
        Description = "Microsoft .NET 8 Desktop Runtime"
    },
    @{
        Name = "Visual C++ 2022 Redistributable"
        Url = "https://aka.ms/vs/17/release/vc_redist.x64.exe"
        Filename = "vcredist2022.exe"
        ExpectedSize = 25MB
        Description = "Microsoft Visual C++ 2022 Redistributable"
    },
    @{
        Name = "Ollama Setup"
        Url = "https://ollama.ai/download/OllamaSetup.exe"
        Filename = "ollama-setup.exe"
        ExpectedSize = 500MB
        Description = "Ollama AI Model Runner"
    }
)

foreach ($download in $downloads) {
    $outputPath = Join-Path $OutputDir $download.Filename
    $tempPath = "$outputPath.tmp"

    Write-Host "`nDownloading $($download.Name)..." -ForegroundColor Cyan
    Write-Host "URL: $($download.Url)" -ForegroundColor Gray
    Write-Host "Output: $outputPath" -ForegroundColor Gray

    # Skip if file exists and not forcing
    if ((Test-Path $outputPath) -and !$Force) {
        $existingSize = (Get-Item $outputPath).Length
        Write-Host "File already exists ($([math]::Round($existingSize / 1MB, 2)) MB)" -ForegroundColor Green

        # Validate file size
        if ($existingSize -lt ($download.ExpectedSize * 0.8)) {
            Write-Host "WARNING: Existing file appears to be incomplete. Re-downloading..." -ForegroundColor Yellow
        } else {
            Write-Host "Skipping $($download.Name) - file exists and appears complete" -ForegroundColor Green
            continue
        }
    }

    try {
        # Download with progress tracking
        $webClient = New-Object System.Net.WebClient
        $webClient.DownloadProgressChanged = {
            param($sender, $e)
            $percent = $e.ProgressPercentage
            $bytesReceived = $e.BytesReceived
            $totalBytes = $e.TotalBytesToReceive
            $receivedMB = [math]::Round($bytesReceived / 1MB, 2)
            $totalMB = [math]::Round($totalBytes / 1MB, 2)

            Write-Progress -Activity "Downloading $($download.Name)" -Status "$receivedMB MB / $totalMB MB ($percent%)" -PercentComplete $percent
        }

        $webClient.DownloadFileAsync($download.Url, $tempPath)

        # Wait for download to complete
        while ($webClient.IsBusy) {
            Start-Sleep -Milliseconds 100
        }

        Write-Progress -Activity "Downloading $($download.Name)" -Completed

        # Validate download
        if (!(Test-Path $tempPath)) {
            throw "Download failed - temp file not created"
        }

        $downloadedSize = (Get-Item $tempPath).Length
        Write-Host "Downloaded: $([math]::Round($downloadedSize / 1MB, 2)) MB" -ForegroundColor Green

        # Validate file size
        if ($downloadedSize -lt ($download.ExpectedSize * 0.8)) {
            throw "Downloaded file appears to be incomplete (expected at least $($download.ExpectedSize / 1MB) MB, got $([math]::Round($downloadedSize / 1MB, 2)) MB)"
        }

        # Move temp file to final location
        Move-Item $tempPath $outputPath -Force
        Write-Host "✓ Successfully downloaded $($download.Name)" -ForegroundColor Green

    } catch {
        Write-Host "✗ Failed to download $($download.Name): $($_.Exception.Message)" -ForegroundColor Red

        # Clean up temp file if it exists
        if (Test-Path $tempPath) {
            Remove-Item $tempPath -Force
        }

        # Continue with other downloads
        continue
    } finally {
        $webClient.Dispose()
    }
}

# Create installer resources
Write-Host "`nCreating installer resources..." -ForegroundColor Cyan

# Create license file
$licenseContent = @"
MISA AI - End User License Agreement
====================================

Copyright (c) 2024 MISA AI Technologies

This license agreement governs your use of MISA AI software and services.

1. LICENSE GRANT
MISA AI Technologies grants you a non-exclusive, non-transferable license to use MISA AI for personal and commercial purposes.

2. PERMITTED USES
- Use MISA AI for legitimate purposes
- Install on multiple devices you own
- Create AI-powered applications and content
- Use for business and commercial purposes

3. RESTRICTIONS
- Do not redistribute or sell MISA AI
- Do not reverse engineer or modify the software
- Do not use for illegal or harmful activities
- Do not violate privacy or data protection laws

4. PRIVACY AND DATA
MISA AI processes data locally by default. Cloud features require explicit consent. We are committed to protecting your privacy and data security.

5. UPDATES AND SUPPORT
MISA AI includes automatic updates and self-upgrade capabilities. Support is provided through our online documentation and community forums.

6. DISCLAIMER OF WARRANTY
MISA AI is provided "as is" without warranties of any kind. Use at your own risk.

7. LIMITATION OF LIABILITY
MISA AI Technologies shall not be liable for any damages arising from the use of this software.

By installing MISA AI, you agree to these terms and conditions.

For more information, visit https://misa.ai/terms
"@

Set-Content -Path "installer\resources\license.txt" -Value $licenseContent -Encoding UTF8
Write-Host "✓ Created license.txt" -ForegroundColor Green

# Create info-before file
$infoBeforeContent = @"
Welcome to MISA AI Installation Wizard
=====================================

MISA AI is an advanced artificial intelligence assistant that combines:
• Multiple personality modes (Girlfriend, Professional, Creative)
• Local AI processing with Ollama integration
• Cross-device remote control and screen sharing
• Automated project building and self-upgrade capabilities
• Background operation with continuous learning
• Cloud synchronization and memory management

System Requirements:
• Windows 10 or later (64-bit)
• 8GB RAM minimum (16GB+ recommended)
• 50GB free disk space (100GB+ recommended for AI models)
• Internet connection for initial setup and cloud features

Installation will:
1. Install required dependencies (.NET 8, Visual C++, Ollama)
2. Configure Windows firewall rules
3. Install MISA AI as a Windows service
4. Set up cross-device communication channels
5. Download essential AI models (may take several minutes)

Click Next to continue with the installation process.
"@

Set-Content -Path "installer\resources\info-before.txt" -Value $infoBeforeContent -Encoding UTF8
Write-Host "✓ Created info-before.txt" -ForegroundColor Green

# Create info-after file
$infoAfterContent = @"
MISA AI Installation Complete!
===============================

Congratulations! MISA AI has been successfully installed on your system.

What's Next?

1. First-Time Setup:
   • Launch MISA AI from the Start Menu
   • Complete the personality configuration wizard
   • Select your preferred AI models and settings

2. Mobile Setup:
   • Generate your Android APK from the desktop application
   • Install the APK on your Android device
   • Pair your devices using the QR code or device ID

3. Voice Commands:
   • Try saying "Hey Misa" to activate voice control
   • Experiment with different personality modes
   • Use remote control features from your mobile device

4. Background Features:
   • MISA AI runs automatically in the background
   • Configure screen capture and activity monitoring
   • Set up cloud synchronization for multi-device access

Resources:
• Documentation: Open MISA AI and click Help → Documentation
• Community: https://community.misa.ai
• Support: https://support.misa.ai
• Updates: MISA AI updates automatically (check Settings for options)

Troubleshooting:
• If MISA AI service doesn't start: Check Windows Services
• For model download issues: Check your internet connection
• For connection problems: Verify firewall settings

Thank you for choosing MISA AI!
"@

Set-Content -Path "installer\resources\info-after.txt" -Value $infoAfterContent -Encoding UTF8
Write-Host "✓ Created info-after.txt" -ForegroundColor Green

# Verify all downloads are present
Write-Host "`nVerifying downloads..." -ForegroundColor Cyan

$missingFiles = @()
foreach ($download in $downloads) {
    $filePath = Join-Path $OutputDir $download.Filename
    if (!(Test-Path $filePath)) {
        $missingFiles += $download.Filename
    } else {
        $size = (Get-Item $filePath).Length
        Write-Host "✓ $($download.Filename) ($([math]::Round($size / 1MB, 2)) MB)" -ForegroundColor Green
    }
}

if ($missingFiles.Count -gt 0) {
    Write-Host "`nWARNING: Some files are missing:" -ForegroundColor Red
    $missingFiles | ForEach-Object { Write-Host "  ✗ $_" -ForegroundColor Red }
    exit 1
}

Write-Host "`nAll prerequisites downloaded successfully!" -ForegroundColor Green
Write-Host "Files are ready in: $OutputDir" -ForegroundColor Green
Write-Host "Run the installer build script to create the final MISA AI installer." -ForegroundColor Yellow