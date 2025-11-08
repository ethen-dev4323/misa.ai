# MISA AI Master Build Script
# Builds the complete MISA AI system with all components
# This script handles everything from compilation to final distribution
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "build",
    [switch]$Clean,
    [switch]$SkipTests,
    [switch]$SkipInstaller,
    [switch]$SkipAndroid,
    [switch]$SkipPrerequisites,
    [switch]$Sign,
    [switch]$Package,
    [string]$Version = "1.0.0"
)
$ErrorActionPreference = "Stop"
# Color output functions
function Write-ColorOutput($Message, $Color = "White") {
    switch ($Color) {
        "Red" { Write-Host $Message -ForegroundColor Red }
        "Green" { Write-Host $Message -ForegroundColor Green }
        "Yellow" { Write-Host $Message -ForegroundColor Yellow }
        "Cyan" { Write-Host $Message -ForegroundColor Cyan }
        "Magenta" { Write-Host $Message -ForegroundColor Magenta }
        default { Write-Host $Message }
    }
}
function Write-Success($Message) { Write-ColorOutput "✓ $Message" "Green" }
function Write-Error($Message) { Write-ColorOutput "✗ $Message" "Red" }
function Write-Warning($Message) { Write-ColorOutput "⚠ $Message" "Yellow" }
function Write-Info($Message) { Write-ColorOutput "ℹ $Message" "Cyan" }
# Set up environment
$StartTime = Get-Date
$RootDir = Get-Location
$BuildDir = Join-Path $RootDir $OutputDir
$DistributionDir = Join-Path $BuildDir "distribution"
Write-ColorOutput "MISA AI Build System" "Magenta"
Write-ColorOutput "====================" "Magenta"
Write-Host "Build Configuration:"
Write-Host "  - Configuration: $Configuration"
Write-Host "  - Output Directory: $OutputDir"
Write-Host "  - Version: $Version"
Write-Host "  - Root Directory: $RootDir"
Write-Host ""
# Validate environment
function Test-BuildEnvironment {
    Write-Info "Testing build environment..."
    # Check .NET SDK
    try {
        $dotnetVersion = & dotnet --version
        Write-Success ".NET SDK: $dotnetVersion"
    } catch {
        Write-Error ".NET SDK not found. Please install .NET 8 SDK."
        exit 1
    }
    # Check PowerShell version
    if ($PSVersionTable.PSVersion.Major -lt 5) {
        Write-Error "PowerShell 5.0 or higher is required."
        exit 1
    }
    Write-Success "PowerShell: $($PSVersionTable.PSVersion)"
    # Check for required tools
    $requiredTools = @("git", "node", "npm")
    foreach ($toolName in $requiredTools) {
        try {
            $toolVersion = & $toolName --version 2>$null
            if ($LASTEXITCODE -eq 0) {
                Write-Success "$toolName`: Available"
            } else {
                $versionCheck = & $toolName --version 2>&1
                Write-Success "$toolName`: Available"
            }
        } catch {
            Write-Warning "$toolName`: Not found (optional)"
        }
    }
    Write-Info "Environment validation complete"
}
# Clean previous builds
function Invoke-Clean {
    if (-not $Clean) { return }
    Write-Info "Cleaning previous builds..."
    if (Test-Path $BuildDir) {
        Write-Host "Removing build directory: $BuildDir"
        Remove-Item $BuildDir -Recurse -Force
    }
    # Clean .NET build outputs
    Get-ChildItem -Path $RootDir -Recurse -Directory -Name "bin" -ErrorAction SilentlyContinue | ForEach-Object {
        $binPath = Join-Path $RootDir $_
        if (Test-Path $binPath) {
            Write-Host "Removing: $binPath"
            Remove-Item $binPath -Recurse -Force
        }
    }
    Get-ChildItem -Path $RootDir -Recurse -Directory -Name "obj" -ErrorAction SilentlyContinue | ForEach-Object {
        $objPath = Join-Path $RootDir $_
        if (Test-Path $objPath) {
            Write-Host "Removing: $objPath"
            Remove-Item $objPath -Recurse -Force
        }
    }
    # Clean Android build outputs
    $androidBuildDir = Join-Path $RootDir "android\app\build"
    if (Test-Path $androidBuildDir) {
        Write-Host "Removing Android build directory: $androidBuildDir"
        Remove-Item $androidBuildDir -Recurse -Force
    }
    Write-Success "Clean completed"
}
# Download prerequisites
function Invoke-DownloadPrerequisites {
    if ($SkipPrerequisites) { return }
    Write-Info "Downloading prerequisites..."
    $prereqScript = Join-Path $RootDir "installer\download-prerequisites.ps1"
    if (Test-Path $prereqScript) {
        try {
            Write-Host "Running prerequisite downloader..."
            & $prereqScript -OutputDir "$($RootDir)\installer\prerequisites"
            if ($LASTEXITCODE -eq 0) {
                Write-Success "Prerequisites downloaded successfully"
            } else {
                Write-Error "Failed to download prerequisites"
                exit 1
            }
        } catch {
            Write-Error "Error running prerequisite downloader"
            exit 1
        }
    } else {
        Write-Warning "Prerequisite download script not found: $prereqScript"
    }
}
# Build .NET projects
function Invoke-BuildDotNet {
    Write-Info "Building .NET projects..."
    # Find all project files
    $projectFiles = Get-ChildItem -Path "$RootDir\src" -Recurse -Filter "*.csproj"
    if ($projectFiles.Count -eq 0) {
        Write-Error "No .NET project files found"
        exit 1
    }
    Write-Host "Found $($projectFiles.Count) project files"
    $successCount = 0
    $totalCount = $projectFiles.Count
    foreach ($projectFile in $projectFiles) {
        $projectName = Split-Path $projectFile -LeafBase
        Write-Host "Building $projectName ($($successCount + 1)/$totalCount)..."
        try {
            # Restore packages
            Write-Host "  Restoring packages..."
            & dotnet restore $projectFile.FullName --verbosity quiet
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Failed to restore packages for $projectName"
                continue
            }
            # Build project
            Write-Host "  Compiling..."
            & dotnet build $projectFile.FullName --configuration $Configuration --no-restore --verbosity minimal
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Failed to build $projectName"
                continue
            }
            # Publish if it's an executable project
            if ($projectFile.Name -match "MISA\.Core|.*\.exe|.*\.App") {
                Write-Host "  Publishing..."
                $publishDir = Join-Path (Split-Path $projectFile.FullName) "bin\$Configuration\net8.0-windows\publish"
                & dotnet publish $projectFile.FullName --configuration $Configuration --runtime win-x64 --self-contained false --output $publishDir --no-build
                if ($LASTEXITCODE -ne 0) {
                    Write-Error "Failed to publish $projectName"
                    continue
                }
            }
            Write-Success "$projectName built successfully"
            $successCount++
        } catch {
            Write-Error "Error building $projectName"
        }
    }
    if ($successCount -eq $totalCount) {
        Write-Success "All .NET projects built successfully ($successCount/$totalCount)"
    } else {
        Write-Error "Some .NET projects failed to build ($successCount/$totalCount)"
        exit 1
    }
}
# Run tests
function Invoke-RunTests {
    if ($SkipTests) { return }
    Write-Info "Running tests..."
    $testProjects = Get-ChildItem -Path "$RootDir\src" -Recurse -Filter "*Tests.csproj"
    if ($testProjects.Count -eq 0) {
        Write-Warning "No test projects found"
        return
    }
    $allTestsPassed = $true
    foreach ($testProject in $testProjects) {
        $testName = Split-Path $testProject.FullName -LeafBase
        Write-Host "Running tests: $testName"
        try {
            & dotnet test $testProject.FullName --configuration $Configuration --no-build --verbosity minimal --logger "console;verbosity=normal"
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Tests failed: $testName"
                $allTestsPassed = $false
            } else {
                Write-Success "Tests passed: $testName"
            }
        } catch {
            Write-Error "Error running tests: $testName"
            $allTestsPassed = $false
        }
    }
    if (-not $allTestsPassed) {
        Write-Error "Some tests failed"
        exit 1
    }
    Write-Success "All tests passed"
}
# Build Android APK
function Invoke-BuildAndroid {
    if ($SkipAndroid) { return }
    Write-Info "Building Android APK..."
    $androidDir = Join-Path $RootDir "android"
    if (-not (Test-Path $androidDir)) {
        Write-Error "Android directory not found: $androidDir"
        exit 1
    }
    Set-Location $androidDir
    try {
        # Check if Gradle wrapper exists
        $gradlewPath = Join-Path $androidDir "gradlew.bat"
        if (-not (Test-Path $gradlewPath)) {
            Write-Error "Gradle wrapper not found: $gradlewPath"
            Set-Location $RootDir
            exit 1
        }
        Write-Host "Building Android APK..."
        & $gradlewPath assembleRelease
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Android APK build failed"
            Set-Location $RootDir
            exit 1
        }
        # Check if APK was created
        $apkPath = Join-Path $androidDir "app\build\outputs\apk\release\app-release.apk"
        if (Test-Path $apkPath) {
            $apkSize = (Get-Item $apkPath).Length / 1MB
            Write-Success "Android APK built successfully ($([math]::Round($apkSize, 2)) MB)"
        } else {
            Write-Error "Android APK file not found after build"
            Set-Location $RootDir
            exit 1
        }
    } catch {
        Write-Error "Error building Android APK"
        Set-Location $RootDir
        exit 1
    } finally {
        Set-Location $RootDir
    }
}
# Create installer
function Invoke-CreateInstaller {
    if ($SkipInstaller) { return }
    Write-Info "Creating installer..."
    $installerScript = Join-Path $RootDir "installer\build-installer.ps1"
    if (Test-Path $installerScript) {
        try {
            Write-Host "Running installer build script..."
            & $installerScript -Configuration $Configuration -OutputDir $OutputDir
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Installer build failed"
                exit 1
            }
            Write-Success "Installer created successfully"
        } catch {
            Write-Error "Error running installer build script"
            exit 1
        }
    } else {
        Write-Warning "Installer build script not found: $installerScript"
    }
}
# Create distribution package
function Invoke-CreateDistribution {
    Write-Info "Creating distribution package..."
    # Create distribution directory
    if (-not (Test-Path $DistributionDir)) {
        New-Item -ItemType Directory -Path $DistributionDir -Force | Out-Null
    }
    # Copy Windows installer
    $installerPath = Get-ChildItem -Path $BuildDir -Filter "*.exe" | Sort-Object LastWriteTime | Select-Object -Last 1
    if ($installerPath) {
        $installerName = "misa-ai-installer-v$Version.exe"
        $installerDest = Join-Path $DistributionDir $installerName
        Copy-Item $installerPath.FullName $installerDest -Force
        Write-Success "Copied installer: $installerName"
    }
