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
    foreach ($tool in $requiredTools) {
        try {
            $toolVersion = & $tool --version 2>$null
            if ($LASTEXITCODE -eq 0) {
                Write-Success "$tool: Available"
            } else {
                $versionCheck = & $tool --version 2>&1
                Write-Success "$tool: Available"
            }
        } catch {
            Write-Warning "$tool: Not found (optional)"
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

    # Copy Android APK
    $apkPath = Join-Path $RootDir "android\app\build\outputs\apk\release\app-release.apk"
    if (Test-Path $apkPath) {
        $apkName = "misa-android-v$Version.apk"
        $apkDest = Join-Path $DistributionDir $apkName
        Copy-Item $apkPath $apkDest -Force
        Write-Success "Copied APK: $apkName"
    }

    # Create version info file
    $versionInfo = @{
        version = $Version
        buildDate = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        configuration = $Configuration
        dotnetVersion = & dotnet --version
        platform = $env:OS
        architecture = $env:PROCESSOR_ARCHITECTURE
        components = @(
            @{ name = "Core Engine"; version = "1.0.0" }
            @{ name = "Personality System"; version = "1.0.0" }
            @{ name = "WebRTC Remote Control"; version = "1.0.0" }
            @{ name = "Memory System"; version = "1.0.0" }
            @{ name = "Cloud Sync"; version = "1.0.0" }
            @{ name = "Android App"; version = "1.0.0" }
            @{ name = "Windows Installer"; version = "1.0.0" }
        )
    }

    $versionInfoPath = Join-Path $DistributionDir "version.json"
    $versionInfo | ConvertTo-Json -Depth 10 | Out-File -FilePath $versionInfoPath -Encoding UTF8

    # Create README for distribution
    $readmeContent = @"
# MISA AI v$Version

## Installation

### Windows
1. Run `misa-ai-installer-v$Version.exe` as Administrator
2. Follow the installation wizard
3. Launch MISA AI from the Start Menu

### Android
1. Transfer `misa-android-v$Version.apk` to your Android device
2. Enable installation from unknown sources
3. Install the APK
4. Launch MISA AI

## Features

- **AI Assistant**: Advanced conversational AI with 3 personality modes
- **Remote Control**: Control your computer from your phone
- **Screen Sharing**: View your computer screen on mobile
- **Memory System**: Smart memory with cloud synchronization
- **Self-Upgrade**: Automatic updates and maintenance
- **Cross-Device**: Seamless integration between desktop and mobile

## System Requirements

### Windows
- Windows 10 or later (64-bit)
- 8GB RAM minimum (16GB recommended)
- 50GB free disk space
- Internet connection for setup

### Android
- Android 7.0 or later
- 4GB RAM minimum
- 2GB free storage space

## Support

For support and documentation, visit https://misa.ai
"@

    $readmePath = Join-Path $DistributionDir "README.md"
    $readmeContent | Out-File -FilePath $readmePath -Encoding UTF8

    # Create checksums file
    $checksums = @()
    Get-ChildItem $DistributionDir | ForEach-Object {
        if ($_.Name -in @("README.md", "version.json")) { return }

        $hash = Get-FileHash $_.FullName -Algorithm SHA256
        $checksums += "$($hash.Hash.ToLower())  *$($_.Name)"
    }

    $checksumsPath = Join-Path $DistributionDir "checksums.sha256"
    $checksums | Out-File -FilePath $checksumsPath -Encoding UTF8

    Write-Success "Distribution package created in: $DistributionDir"
}

# Sign artifacts (if requested)
function Invoke-SignArtifacts {
    if (-not $Sign) { return }

    Write-Info "Signing artifacts..."

    $certPath = $env:CODE_SIGNING_CERT_PATH
    $certPassword = $env:CODE_SIGNING_CERT_PASSWORD

    if (-not $certPath -or -not $certPassword) {
        Write-Warning "Code signing credentials not found. Skipping signing."
        return
    }

    try {
        # Sign Windows installer
        $installerPath = Get-ChildItem -Path $DistributionDir -Filter "*.exe" | Select-Object -First 1
        if ($installerPath) {
            Write-Host "Signing installer: $($installerPath.Name)"
            & signtool sign /f $certPath /p $certPassword /fd SHA256 /t http://timestamp.digicert.com $installerPath.FullName
            if ($LASTEXITCODE -eq 0) {
                Write-Success "Installer signed successfully"
            } else {
                Write-Error "Failed to sign installer"
            }
        }

        # Sign Android APK (if keystore is available)
        $keystorePath = Join-Path $RootDir "android\keystore\misa-release.keystore"
        if (Test-Path $keystorePath) {
            $apkPath = Get-ChildItem -Path $DistributionDir -Filter "*.apk" | Select-Object -First 1
            if ($apkPath) {
                Write-Host "Signing APK: $($apkPath.Name)"
                & jarsigner -verbose -sigalg SHA1withRSA -digestalg SHA1 -keystore $keystorePath -storepass misa123456 -keypass misa123456 $apkPath.FullName misa-key
                if ($LASTEXITCODE -eq 0) {
                    Write-Success "APK signed successfully"
                } else {
                    Write-Error "Failed to sign APK"
                }
            }
        }
    } catch {
        Write-Error "Error during signing process"
    }
}

# Package for distribution
function Invoke-PackageDistribution {
    if (-not $Package) { return }

    Write-Info "Creating distribution packages..."

    $packageName = "misa-ai-v$Version-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    $packageDir = Join-Path $BuildDir $packageName
    $packageZip = Join-Path $BuildDir "$packageName.zip"

    if (Test-Path $packageDir) {
        Remove-Item $packageDir -Recurse -Force
    }

    # Create package directory
    New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

    # Copy distribution files
    Copy-Item $DistributionDir\* $packageDir -Recurse -Force

    # Create ZIP package
    Compress-Archive -Path $packageDir -DestinationPath $packageZip -Force

    # Clean up directory
    Remove-Item $packageDir -Recurse -Force

    $packageSize = (Get-Item $packageZip).Length / 1MB
    Write-Success "Distribution package created: $packageName.zip ($([math]::Round($packageSize, 2)) MB)"
}

# Generate build report
function Invoke-GenerateBuildReport {
    Write-Info "Generating build report..."

    $endTime = Get-Date
    $duration = $endTime - $StartTime

    $report = @{
        buildId = [System.Guid]::NewGuid().ToString("N")[..16]
        version = $Version
        configuration = $Configuration
        startTime = $StartTime
        endTime = $endTime
        duration = $duration.ToString("hh\:mm\:ss")
        status = if ($?) { "Success" } else { "Failed" }
        platform = $env:OS
        architecture = $env:PROCESSOR_ARCHITECTURE
        dotnetVersion = & dotnet --version
        gitCommit = & git rev-parse HEAD 2>$null
        gitBranch = & git rev-parse --abbrev-ref HEAD 2>$null
        components = @()
        artifacts = @()
    }

    # Add component information
    if (Test-Path "$RootDir\src\MISA.Core\bin\Release\net8.0-windows\MISA.Core.exe") {
        $report.components += @{ name = "MISA Core Engine"; status = "Built" }
    }

    if (Test-Path "$RootDir\android\app\build\outputs\apk\release\app-release.apk") {
        $report.components += @{ name = "Android Application"; status = "Built" }
    }

    # Add artifact information
    if (Test-Path $DistributionDir) {
        Get-ChildItem $DistributionDir | ForEach-Object {
            if ($_.Name -in @("README.md", "version.json", "checksums.sha256")) { return }

            $report.artifacts += @{
                name = $_.Name
                size = $([math]::Round($_.Length / 1MB, 2))
                path = $_.FullName
                created = $_.LastWriteTime
            }
        }
    }

    $reportPath = Join-Path $BuildDir "build-report.json"
    $report | ConvertTo-Json -Depth 10 | Out-File -FilePath $reportPath -Encoding UTF8

    Write-Success "Build report generated: $reportPath"
}

# Display build summary
function Invoke-DisplaySummary {
    $duration = (Get-Date) - $StartTime

    Write-ColorOutput "" "White"
    Write-ColorOutput "MISA AI Build Summary" "Magenta"
    Write-ColorOutput "==================" "Magenta"
    Write-Host "Version: $Version"
    Write-Host "Configuration: $Configuration"
    Write-Host "Duration: $($duration.ToString('hh\:mm\:ss'))"
    Write-Host "Output Directory: $OutputDir"

    if (Test-Path $DistributionDir) {
        $fileCount = (Get-ChildItem $DistributionDir).Count
        $totalSize = (Get-ChildItem $DistributionDir | Measure-Object -Property Length -Sum).Length / 1MB
        Write-Host "Distribution Files: $fileCount ($([math]::Round($totalSize, 2)) MB)"
    }

    if ($?) {
        Write-Success "Build completed successfully!"
        Write-ColorOutput "Distribution directory: $DistributionDir" "Green"
    } else {
        Write-Error "Build failed!"
        exit 1
    }

    Write-Host ""
    Write-Host "Next steps:"
    Write-Host "1. Test the installer on a clean Windows system"
    Write-Host "2. Install and verify Android APK"
    Write-Host "3. Test cross-device functionality"
    Write-Host "4. Deploy to distribution channels"
}

# Main execution
try {
    Write-ColorOutput "Starting MISA AI build process..." "Cyan"
    Write-Host ""

    Test-BuildEnvironment
    Invoke-Clean
    Invoke-DownloadPrerequisites
    Invoke-BuildDotNet
    Invoke-RunTests
    Invoke-BuildAndroid
    Invoke-CreateInstaller
    Invoke-CreateDistribution
    Invoke-SignArtifacts
    Invoke-PackageDistribution
    Invoke-GenerateBuildReport
    Invoke-DisplaySummary

} catch {
    Write-Error "Build failed with error: $_"
    exit 1
} finally {
    Write-Host ""
    Write-ColorOutput "Build process completed at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" "Cyan"
}