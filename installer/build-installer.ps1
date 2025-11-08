# MISA AI - Installer Build Script
# Builds the complete installer with all prerequisites and dependencies

param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "build",
    [switch]$Clean,
    [switch]$SkipBuild,
    [switch]$SkipPrerequisites,
    [switch]$SkipModels
)

$ErrorActionPreference = "Stop"

Write-Host "MISA AI Installer Builder" -ForegroundColor Green
Write-Host "=========================" -ForegroundColor Green

# Set up paths
$RootDir = Get-Location
$SrcDir = Join-Path $RootDir "src"
$InstallerDir = Join-Path $RootDir "installer"
$OutputDir = Join-Path $RootDir $OutputDir

Write-Host "Root Directory: $RootDir" -ForegroundColor Gray
Write-Host "Source Directory: $SrcDir" -ForegroundColor Gray
Write-Host "Installer Directory: $InstallerDir" -ForegroundColor Gray
Write-Host "Output Directory: $OutputDir" -ForegroundColor Gray

# Clean previous builds if requested
if ($Clean) {
    Write-Host "`nCleaning previous builds..." -ForegroundColor Yellow
    if (Test-Path $OutputDir) {
        Remove-Item $OutputDir -Recurse -Force
        Write-Host "✓ Cleaned output directory" -ForegroundColor Green
    }

    # Clean build outputs
    Get-ChildItem -Path $SrcDir -Recurse -Directory -Name "bin" -ErrorAction SilentlyContinue | ForEach-Object {
        $binPath = Join-Path $SrcDir $_
        if (Test-Path $binPath) {
            Remove-Item $binPath -Recurse -Force
            Write-Host "✓ Removed $binPath" -ForegroundColor Green
        }
    }
}

# Create output directories
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
New-Item -ItemType Directory -Path "$OutputDir\distribution" -Force | Out-Null

# Build projects if not skipped
if (!$SkipBuild) {
    Write-Host "`nBuilding MISA AI projects..." -ForegroundColor Cyan

    # Find all project files
    $projectFiles = Get-ChildItem -Path $SrcDir -Recurse -Filter "*.csproj"

    foreach ($projectFile in $projectFiles) {
        $projectName = $projectFile.Name
        $projectPath = $projectFile.FullName

        Write-Host "Building $projectName..." -ForegroundColor Yellow

        try {
            # Build the project
            $buildResult = dotnet build $projectPath --configuration $Configuration --no-restore --verbosity minimal

            if ($LASTEXITCODE -ne 0) {
                throw "Build failed for $projectName"
            }

            Write-Host "✓ Built $projectName" -ForegroundColor Green
        } catch {
            Write-Host "✗ Failed to build $projectName`: $_" -ForegroundColor Red
            exit 1
        }
    }

    # Publish the main application
    $mainProject = Join-Path $SrcDir "MISA.Core\MISA.Core.csproj"
    if (Test-Path $mainProject) {
        Write-Host "Publishing main application..." -ForegroundColor Yellow

        try {
            $publishDir = Join-Path $SrcDir "MISA.Core\bin\$Configuration\net8.0-windows\publish"
            $publishResult = dotnet publish $mainProject --configuration $Configuration --runtime win-x64 --self-contained false --output $publishDir

            if ($LASTEXITCODE -ne 0) {
                throw "Publish failed for main application"
            }

            Write-Host "✓ Published main application" -ForegroundColor Green
        } catch {
            Write-Host "✗ Failed to publish main application: $_" -ForegroundColor Red
            exit 1
        }
    }
}

# Download prerequisites if not skipped
if (!$SkipPrerequisites) {
    Write-Host "`nDownloading prerequisites..." -ForegroundColor Cyan

    $prereqScript = Join-Path $InstallerDir "download-prerequisites.ps1"
    if (Test-Path $prereqScript) {
        try {
            & $prereqScript -OutputDir "$InstallerDir\prerequisites"
            Write-Host "✓ Prerequisites downloaded" -ForegroundColor Green
        } catch {
            Write-Host "✗ Failed to download prerequisites: $_" -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "✗ Prerequisite download script not found" -ForegroundColor Red
        exit 1
    }
}

# Setup essential AI models if not skipped
if (!$SkipModels) {
    Write-Host "`nSetting up AI models..." -ForegroundColor Cyan

    $modelsDir = Join-Path $RootDir "models"
    New-Item -ItemType Directory -Path $modelsDir -Force | Out-Null

    # Create models README
    $modelsReadme = @"
# MISA AI Models Directory

This directory will contain AI models downloaded during first run.

## Essential Models (Auto-downloaded)
- **mixtral:8x7b** - General conversation and complex reasoning
- **codellama:13b** - Code generation and technical assistance
- **dolphin-mistral:7b** - Creative tasks and brainstorming
- **wizardcoder:3b** - Quick coding assistance

## Model Storage
- Models are stored in Ollama's default location (~/.ollama/models on Windows)
- Automatic model management and optimization
- Support for quantized models to reduce storage requirements

## Model Selection
MISA AI automatically selects the best model based on:
- Task complexity and type
- Available system resources
- User preferences
- Performance requirements

## Custom Models
You can add custom models through:
1. MISA AI Settings → Model Management
2. Command line: ollama pull <model-name>
3. Model files in this directory

For more information, see the MISA AI documentation.
"@

    Set-Content -Path "$modelsDir\README.md" -Value $modelsReadme -Encoding UTF8
    Write-Host "✓ Created models directory and documentation" -ForegroundColor Green
}

# Build the installer
Write-Host "`nBuilding Inno Setup installer..." -ForegroundColor Cyan

# Check for Inno Setup
$innoSetupPath = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
if (!$innoSetupPath) {
    # Try common installation paths
    $commonPaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\iscc.exe",
        "${env:ProgramFiles}\Inno Setup 6\iscc.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 5\iscc.exe",
        "${env:ProgramFiles}\Inno Setup 5\iscc.exe"
    )

    foreach ($path in $commonPaths) {
        if (Test-Path $path) {
            $innoSetupPath = $path
            break
        }
    }

    if (!$innoSetupPath) {
        Write-Host "✗ Inno Setup not found. Please install Inno Setup 6 or later." -ForegroundColor Red
        Write-Host "Download from: https://jrsoftware.org/isinfo.php" -ForegroundColor Yellow
        exit 1
    }
}

Write-Host "Using Inno Setup at: $($innoSetupPath.Source)" -ForegroundColor Gray

# Compile the installer
$installerScript = Join-Path $InstallerDir "misa-installer.iss"
$installerOutput = Join-Path $OutputDir "misa-ai-installer.exe"

try {
    $process = Start-Process -FilePath $innoSetupPath.Source -ArgumentList "/O$OutputDir", "/Fmisa-ai-installer", $installerScript -Wait -PassThru -NoNewWindow

    if ($process.ExitCode -ne 0) {
        throw "Inno Setup compilation failed with exit code $($process.ExitCode)"
    }

    Write-Host "✓ Installer built successfully" -ForegroundColor Green
} catch {
    Write-Host "✗ Failed to build installer: $_" -ForegroundColor Red
    exit 1
}

# Verify installer was created
if (!(Test-Path $installerOutput)) {
    Write-Host "✗ Installer file not found at expected location: $installerOutput" -ForegroundColor Red
    exit 1
}

# Copy installer to distribution directory
$installerSize = (Get-Item $installerOutput).Length
$distributionInstaller = Join-Path "$OutputDir\distribution" "misa-ai-installer-v1.0.0.exe"
Copy-Item $installerOutput $distributionInstaller -Force

Write-Host "✓ Installer copied to distribution directory" -ForegroundColor Green

# Create build summary
$summaryFile = Join-Path "$OutputDir\distribution" "build-summary.txt"
$summary = @"
MISA AI Installer Build Summary
===============================
Build Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
Configuration: $Configuration
Installer Size: $([math]::Round($installerSize / 1MB, 2)) MB

Files Created:
- misa-ai-installer-v1.0.0.exe ($(Split-Path $distributionInstaller -Leaf))

Prerequisites Included:
- Microsoft .NET 8 Runtime
- Visual C++ 2022 Redistributable
- Ollama AI Model Runner

Installation Requirements:
- Windows 10 or later (64-bit)
- 8GB RAM minimum (16GB+ recommended)
- 50GB free disk space (100GB+ recommended)
- Internet connection for initial setup

Next Steps:
1. Test the installer on a clean Windows system
2. Verify all features work after installation
3. Create distribution package for release
4. Update website and documentation

Build completed successfully!
"@

Set-Content -Path $summaryFile -Value $summary -Encoding UTF8

# Display final results
Write-Host "`n" + ("=" * 60) -ForegroundColor Green
Write-Host "MISA AI INSTALLER BUILD COMPLETE!" -ForegroundColor Green
Write-Host "=" * 60 -ForegroundColor Green
Write-Host "Installer Location: $distributionInstaller" -ForegroundColor Yellow
Write-Host "Installer Size: $([math]::Round($installerSize / 1MB, 2)) MB" -ForegroundColor Yellow
Write-Host "Build Summary: $summaryFile" -ForegroundColor Yellow
Write-Host "`nReady for distribution and testing!" -ForegroundColor Green

# Offer to open the output directory
$response = Read-Host "`nOpen output directory? (Y/N)"
if ($response -eq "Y" -or $response -eq "y") {
    Start-Process $OutputDir
}