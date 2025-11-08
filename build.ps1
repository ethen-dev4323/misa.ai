# MISA AI Master Build Script (PowerShell)
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

function Write-Info([string]$m){ Write-Host "ℹ $m" -ForegroundColor Cyan }
function Write-Success([string]$m){ Write-Host "✓ $m" -ForegroundColor Green }
function Write-Err([string]$m){ Write-Host "✗ $m" -ForegroundColor Red }
function Write-Warn([string]$m){ Write-Host "⚠ $m" -ForegroundColor Yellow }

$StartTime = Get-Date
$RootDir = (Get-Location).Path
$BuildDir = Join-Path $RootDir $OutputDir
$DistributionDir = Join-Path $BuildDir "distribution"

Write-Host "MISA AI Build System" -ForegroundColor Magenta
Write-Host "====================" -ForegroundColor Magenta
Write-Host "Build Configuration:"
Write-Host "  - Configuration: $Configuration"
Write-Host "  - Output Directory: $OutputDir"
Write-Host "  - Version: $Version"
Write-Host "  - Root Directory: $RootDir"
Write-Host ""

function Test-BuildEnvironment {
    Write-Info "Testing build environment..."
    try {
        $dotnetVersion = & dotnet --version 2>$null
        Write-Success ".NET SDK: $dotnetVersion"
    } catch {
        Write-Err ".NET SDK not found. Install .NET 8 SDK."
        exit 1
    }
    if ($PSVersionTable.PSVersion.Major -lt 5) {
        Write-Err "PowerShell 5.0+ required."
        exit 1
    } else {
        Write-Success "PowerShell: $($PSVersionTable.PSVersion)"
    }

    $optionalTools = @("git","node","npm")
    foreach ($t in $optionalTools) {
        if (Get-Command $t -ErrorAction SilentlyContinue) {
            try { $v = & $t --version 2>$null; Write-Success "$t: $v" } catch { Write-Success "$t: available" }
        } else {
            Write-Warn "$t: not found (optional)"
        }
    }
    Write-Info "Environment validation complete"
}

function Invoke-Clean {
    if (-not $Clean) { return }
    Write-Info "Cleaning previous builds..."
    if (Test-Path $BuildDir) { Remove-Item $BuildDir -Recurse -Force -ErrorAction SilentlyContinue }
    Get-ChildItem -Path $RootDir -Recurse -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in @('bin','obj') } |
        ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    $androidBuild = Join-Path $RootDir "android\app\build"
    if (Test-Path $androidBuild) { Remove-Item $androidBuild -Recurse -Force -ErrorAction SilentlyContinue }
    Write-Success "Clean completed"
}

function Invoke-DownloadPrerequisites {
    if ($SkipPrerequisites) { return }
    Write-Info "Downloading prerequisites (if provided)..."
    $prereqScript = Join-Path $RootDir "installer\download-prerequisites.ps1"
    if (Test-Path $prereqScript) {
        try {
            & $prereqScript -OutputDir (Join-Path $RootDir "installer\prerequisites")
            Write-Success "Prerequisites downloader executed"
        } catch {
            Write-Warn "Prerequisite downloader returned non-zero exit code"
        }
    } else {
        Write-Warn "No prerequisite downloader found; install required tools manually"
    }
}

function Invoke-BuildDotNet {
    Write-Info "Building .NET projects..."
    $projectFiles = Get-ChildItem -Path (Join-Path $RootDir 'src') -Recurse -Filter '*.csproj' -File -ErrorAction SilentlyContinue
    if (-not $projectFiles) { Write-Err "No .NET project files found"; exit 1 }
    Write-Host "Found $($projectFiles.Count) project files"
    $success = 0
    foreach ($p in $projectFiles) {
        $name = $p.BaseName
        Write-Host "Building $name..."
        try {
            & dotnet restore $p.FullName --verbosity quiet
            if ($LASTEXITCODE -ne 0) { Write-Err "Restore failed for $name"; continue }
            & dotnet build $p.FullName --configuration $Configuration --no-restore --verbosity minimal
            if ($LASTEXITCODE -ne 0) { Write-Err "Build failed for $name"; continue }
            if ($p.Name -match 'MISA\.Core|.*\.App') {
                $publishDir = Join-Path $p.Directory.FullName (Join-Path "bin" (Join-Path $Configuration "net8.0\publish"))
                & dotnet publish $p.FullName --configuration $Configuration --output $publishDir --no-build
            }
            Write-Success "$name built"
            $success++
        } catch {
            Write-Err "Error while building $name"
        }
    }
    if ($success -ne $projectFiles.Count) { Write-Err "Some projects failed ($success/$($projectFiles.Count))"; exit 1 }
    Write-Success "All .NET projects built ($success/$($projectFiles.Count))"
}

function Invoke-RunTests {
    if ($SkipTests) { return }
    Write-Info "Running tests..."
    $tests = Get-ChildItem -Path (Join-Path $RootDir 'src') -Recurse -Filter '*Tests.csproj' -File -ErrorAction SilentlyContinue
    if (-not $tests) { Write-Warn "No test projects found"; return }
    $ok = $true
    foreach ($t in $tests) {
        $tn = $t.BaseName
        Write-Host "Testing $tn..."
        & dotnet test $t.FullName --configuration $Configuration --no-build --verbosity minimal --logger "console;verbosity=normal"
        if ($LASTEXITCODE -ne 0) { Write-Err "Tests failed: $tn"; $ok = $false }
        else { Write-Success "Tests passed: $tn" }
    }
    if (-not $ok) { Write-Err "Some tests failed"; exit 1 }
    Write-Success "All tests passed"
}

function Invoke-BuildAndroid {
    if ($SkipAndroid) { return }
    Write-Info "Building Android..."
    $androidDir = Join-Path $RootDir 'android'
    if (-not (Test-Path $androidDir)) { Write-Warn "android/ not present; skipping"; return }
    Push-Location $androidDir
    try {
        $gw = Get-ChildItem -Path . -Filter 'gradlew*' -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'gradlew(.bat)?$' } | Select-Object -First 1
        if (-not $gw) { Write-Err "Gradle wrapper not found"; Pop-Location; exit 1 }
        if ($gw.Name -eq 'gradlew') { & chmod +x $gw.FullName }
        & $gw.FullName assembleRelease
        if ($LASTEXITCODE -ne 0) { Write-Err "Android build failed"; Pop-Location; exit 1 }
        $apk = Join-Path $androidDir 'app\build\outputs\apk\release\app-release.apk'
        if (Test-Path $apk) { $sizeMB = [math]::Round((Get-Item $apk).Length/1MB,2); Write-Success "APK built: ${sizeMB} MB" } 
        else { Write-Err "APK not found"; Pop-Location; exit 1 }
    } catch {
        Write-Err "Error building Android APK"
        Pop-Location
        exit 1
    } finally { Pop-Location }
}

function Invoke-CreateInstaller {
    if ($SkipInstaller) { return }
    Write-Info "Creating installer (if installer scripts available)..."
    $installerScript = Join-Path $RootDir 'installer\build-installer.ps1'
    if (Test-Path $installerScript) {
        try { & $installerScript -Configuration $Configuration -OutputDir $OutputDir; Write-Success "Installer created" } catch { Write-Warn "Installer script failed" }
    } else {
        Write-Warn "No installer script found; creating simple distribution folder"
        New-Item -ItemType Directory -Path $DistributionDir -Force | Out-Null
    }
}

function Invoke-CreateDistribution {
    Write-Info "Creating distribution metadata..."
    New-Item -ItemType Directory -Path $DistributionDir -Force | Out-Null
    $versionObj = @{ version = $Version; buildDate = (Get-Date -AsUTC).ToString("s") + "Z"; configuration = $Configuration }
    $versionObj | ConvertTo-Json | Out-File -Encoding utf8 (Join-Path $DistributionDir 'version.json')
    Write-Success "Distribution directory prepared: $DistributionDir"
}

function Main {
    Write-Info "Starting build..."
    Test-BuildEnvironment
    Invoke-Clean
    Invoke-DownloadPrerequisites
    Invoke-BuildDotNet
    Invoke-RunTests
    Invoke-BuildAndroid
    Invoke-CreateInstaller
    Invoke-CreateDistribution
    Write-Success "Build completed"
}

Main
