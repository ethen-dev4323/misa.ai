@echo off
REM MISA AI Build Script for Windows
REM This is a wrapper around the PowerShell build script

echo Starting MISA AI build...
echo.

REM Check if PowerShell is available
where pwsh >nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo Using PowerShell Core...
    pwsh -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
) else (
    where powershell >nul 2>&1
    if %ERRORLEVEL% equ 0 (
        echo Using Windows PowerShell...
        powershell -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
    ) else (
        echo ERROR: PowerShell is not installed or not in PATH
        echo Please install PowerShell to run the build script
        pause
        exit /b 1
    )
)

if %ERRORLEVEL% neq 0 (
    echo.
    echo Build failed with exit code %ERRORLEVEL%
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo Build process completed!
pause