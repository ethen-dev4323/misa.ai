#!/bin/bash

# MISA AI Build Script for Linux/macOS
# This script builds the complete MISA AI system

set -e  # Exit on any error

# Color output functions
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
NC='\033[0m' # No Color

log_info() {
    echo -e "${CYAN}ℹ $1${NC}"
}

log_success() {
    echo -e "${GREEN}✓ $1${NC}"
}

log_error() {
    echo -e "${RED}✗ $1${NC}"
}

log_warning() {
    echo -e "${YELLOW}⚠ $1${NC}"
}

# Get script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$SCRIPT_DIR"

# Parse command line arguments
CONFIGURATION="Release"
OUTPUT_DIR="build"
CLEAN=false
SKIP_TESTS=false
SKIP_INSTALLER=false
SKIP_ANDROID=false
SKIP_PREREQUISITES=false
SIGN=false
PACKAGE=false
VERSION="1.0.0"

while [[ $# -gt 0 ]]; do
    case $1 in
        --configuration)
            CONFIGURATION="$2"
            shift 2
            ;;
        --output-dir)
            OUTPUT_DIR="$2"
            shift 2
            ;;
        --clean)
            CLEAN=true
            shift
            ;;
        --skip-tests)
            SKIP_TESTS=true
            shift
            ;;
        --skip-installer)
            SKIP_INSTALLER=true
            shift
            ;;
        --skip-android)
            SKIP_ANDROID=true
            shift
            ;;
        --skip-prerequisites)
            SKIP_PREREQUISITES=true
            shift
            ;;
        --sign)
            SIGN=true
            shift
            ;;
        --package)
            PACKAGE=true
            shift
            ;;
        --version)
            VERSION="$2"
            shift 2
            ;;
        *)
            log_error "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Build start time
START_TIME=$(date +%s)

log_info "MISA AI Build System (Unix)"
log_info "========================="
log_info "Build Configuration:"
log_info "  - Configuration: $CONFIGURATION"
log_info "  - Output Directory: $OUTPUT_DIR"
log_info "  - Version: $VERSION"
log_info "  - Root Directory: $ROOT_DIR"
echo

# Validate environment
validate_environment() {
    log_info "Validating build environment..."

    # Check .NET SDK
    if command -v dotnet &> /dev/null; then
        DOTNET_VERSION=$(dotnet --version)
        log_success ".NET SDK: $DOTNET_VERSION"
    else
        log_error ".NET SDK not found. Please install .NET 8 SDK."
        exit 1
    fi

    # Check Node.js (optional)
    if command -v node &> /dev/null; then
        NODE_VERSION=$(node --version)
        log_success "Node.js: $NODE_VERSION"
    else
        log_warning "Node.js: Not found (optional)"
    fi

    # Check npm (optional)
    if command -v npm &> /dev/null; then
        NPM_VERSION=$(npm --version)
        log_success "npm: $NPM_VERSION"
    else
        log_warning "npm: Not found (optional)"
    fi

    log_info "Environment validation complete"
}

# Clean previous builds
clean_build() {
    if [ "$CLEAN" = false ]; then
        return
    fi

    log_info "Cleaning previous builds..."

    # Remove build directory
    if [ -d "$OUTPUT_DIR" ]; then
        log_info "Removing build directory: $OUTPUT_DIR"
        rm -rf "$OUTPUT_DIR"
    fi

    # Clean .NET build outputs
    find "$ROOT_DIR/src" -type d -name "bin" -exec rm -rf {} + 2>/dev/null || true
    find "$ROOT_DIR/src" -type d -name "obj" -exec rm -rf {} + 2>/dev/null || true

    # Clean Android build outputs
    if [ -d "$ROOT_DIR/android/app/build" ]; then
        log_info "Removing Android build directory"
        rm -rf "$ROOT_DIR/android/app/build"
    fi

    log_success "Clean completed"
}

# Download prerequisites
download_prerequisites() {
    if [ "$SKIP_PREREQUISITES" = true ]; then
        return
    fi

    log_info "Downloading prerequisites..."

    # Create prerequisites directory if it doesn't exist
    PREREQ_DIR="$ROOT_DIR/installer/prerequisites"
    mkdir -p "$PREREQ_DIR"

    # Download .NET 8 Runtime (Windows-style script won't work on Unix)
    if [[ "$OSTYPE" == "msys" ]] || [[ "$OSTYPE" == "cygwin" ]]; then
        # Windows environment
        log_info "Skipping prerequisite downloads (use build.ps1 on Windows)"
        return
    fi

    # For Linux/macOS, we assume prerequisites are managed by package managers
    log_success "Prerequisites check completed (package manager managed)"
}

# Build .NET projects
build_dotnet() {
    log_info "Building .NET projects..."

    # Find all project files
    PROJECT_FILES=$(find "$ROOT_DIR/src" -name "*.csproj" | sort)

    if [ -z "$PROJECT_FILES" ]; then
        log_error "No .NET project files found"
        exit 1
    fi

    PROJECT_COUNT=$(echo "$PROJECT_FILES" | wc -l)
    log_info "Found $PROJECT_COUNT project files"

    SUCCESS_COUNT=0
    TOTAL_COUNT=$PROJECT_COUNT

    while IFS= read -r project_file; do
        PROJECT_NAME=$(basename "$project_file" .csproj)
        log_info "Building $PROJECT_NAME ($((SUCCESS_COUNT + 1))/$TOTAL_COUNT)..."

        # Restore packages
        echo "  Restoring packages..."
        dotnet restore "$project_file" --verbosity quiet
        if [ $? -ne 0 ]; then
            log_error "Failed to restore packages for $PROJECT_NAME"
            continue
        fi

        # Build project
        echo "  Compiling..."
        dotnet build "$project_file" --configuration "$CONFIGURATION" --no-restore --verbosity minimal
        if [ $? -neq 0 ]; then
            log_error "Failed to build $PROJECT_NAME"
            continue
        fi

        # Publish if it's an executable project
        if [[ "$project_file" == *"MISA.Core"* ]] || [[ "$project_file" == *".exe"* ]] || [[ "$project_file" == *".App"* ]]; then
            echo "  Publishing..."
            PUBLISH_DIR="$(dirname "$project_file")/bin/$CONFIGURATION/net8.0/publish"
            dotnet publish "$project_file" --configuration "$CONFIGURATION" --runtime win-x64 --self-contained false --output "$PUBLISH_DIR" --no-build
            if [ $? -neq 0 ]; then
                log_error "Failed to publish $PROJECT_NAME"
                continue
            fi
        fi

        log_success "$PROJECT_NAME built successfully"
        SUCCESS_COUNT=$((SUCCESS_COUNT + 1))
    done <<< "$PROJECT_FILES"

    if [ $SUCCESS_COUNT -eq $TOTAL_COUNT ]; then
        log_success "All .NET projects built successfully ($SUCCESS_COUNT/$TOTAL_COUNT)"
    else
        log_error "Some .NET projects failed to build ($SUCCESS_COUNT/$TOTAL_COUNT)"
        exit 1
    fi
}

# Run tests
run_tests() {
    if [ "$SKIP_TESTS" = true ]; then
        return
    fi

    log_info "Running tests..."

    TEST_PROJECTS=$(find "$ROOT_DIR/src" -name "*Tests.csproj" | sort)

    if [ -z "$TEST_PROJECTS" ]; then
        log_warning "No test projects found"
        return
    fi

    ALL_TESTS_PASSED=true

    while IFS= read -r test_project; do
        TEST_NAME=$(basename "$test_project" .csproj)
        log_info "Running tests: $TEST_NAME"

        dotnet test "$test_project" --configuration "$CONFIGURATION" --no-build --verbosity minimal --logger "console;verbosity=normal"
        if [ $? -neq 0 ]; then
            log_error "Tests failed: $TEST_NAME"
            ALL_TESTS_PASSED=false
        else
            log_success "Tests passed: $TEST_NAME"
        fi
    done <<< "$TEST_PROJECTS"

    if [ "$ALL_TESTS_PASSED" = false ]; then
        log_error "Some tests failed"
        exit 1
    fi

    log_success "All tests passed"
}

# Build Android APK
build_android() {
    if [ "$SKIP_ANDROID" = true ]; then
        return
    fi

    log_info "Building Android APK..."

    ANDROID_DIR="$ROOT_DIR/android"
    if [ ! -d "$ANDROID_DIR" ]; then
        log_error "Android directory not found: $ANDROID_DIR"
        exit 1
    fi

    cd "$ANDROID_DIR" || exit 1

    # Check for Gradle wrapper
    if [ ! -f "gradlew" ] && [ ! -f "gradlew.bat" ]; then
        log_error "Gradle wrapper not found"
        cd "$ROOT_DIR" || exit 1
        exit 1
    fi

    # Make gradlew executable on Unix-like systems
    if [ -f "gradlew" ]; then
        chmod +x gradlew
        ./gradlew assembleRelease
    else
        # Try using gradlew.bat on Cygwin/MSYS
        ./gradlew.bat assembleRelease
    fi

    BUILD_RESULT=$?

    cd "$ROOT_DIR" || exit 1

    if [ $BUILD_RESULT -neq 0 ]; then
        log_error "Android APK build failed"
        exit 1
    fi

    # Check if APK was created
    APK_PATH="$ANDROID_DIR/app/build/outputs/apk/release/app-release.apk"
    if [ -f "$APK_PATH" ]; then
        APK_SIZE=$(stat -f%z "$APK_PATH" | cut -d' ' -f2)
        APK_SIZE_MB=$((APK_SIZE / 1024 / 1024))
        log_success "Android APK built successfully ($APK_SIZE_MB MB)"
    else
        log_error "Android APK file not found after build"
        exit 1
    fi
}

# Create installer
create_installer() {
    if [ "$SKIP_INSTALLER" = true ]; then
        return
    fi

    log_info "Creating installer..."

    # On Unix-like systems, Inno Setup is not available
    # We'll create a simple tar package instead
    log_info "Creating deployment package instead of installer (Unix platform)"

    DIST_DIR="$ROOT_DIR/$OUTPUT_DIR/distribution"
    mkdir -p "$DIST_DIR"

    # Copy built artifacts
    if [ -f "$ROOT_DIR/src/MISA.Core/bin/$CONFIGURATION/net8.0-windows/MISA.Core.exe" ]; then
        cp "$ROOT_DIR/src/MISA.Core/bin/$CONFIGURATION/net8.0-windows/MISA.Core.exe" "$DIST_DIR/"
        log_success "Copied MISA Core executable"
    fi

    if [ -f "$ANDROID_DIR/app/build/outputs/apk/release/app-release.apk" ]; then
        cp "$ANDROID_DIR/app/build/outputs/apk/release/app-release.apk" "$DIST_DIR/misa-android-v$VERSION.apk"
        log_success "Copied Android APK"
    fi

    # Create installation script
    cat > "$DIST_DIR/install.sh" << 'EOF'
#!/bin/bash

# MISA AI Installation Script for Unix-like systems

set -e

INSTALL_DIR="$HOME/.local/bin"
APP_NAME="misa-ai"

echo "Installing MISA AI..."

# Create installation directory
mkdir -p "$INSTALL_DIR"

# Copy executable
if [ -f "MISA.Core.exe" ]; then
    cp MISA.Core.exe "$INSTALL_DIR/misa-ai"
    chmod +x "$INSTALL_DIR/misa-ai"
    echo "Installed MISA AI executable to $INSTALL_DIR"
fi

# Create desktop entry
if [ -d "$HOME/.local/share/applications" ]; then
    cat > "$HOME/.local/share/applications/misa-ai.desktop" << EOF_EOF
[Desktop Entry]
Name=MISA AI
Comment=Advanced AI Assistant with Multiple Personalities
Exec=$INSTALL_DIR/misa-ai
Icon=$INSTALL_DIR/misa-ai
Terminal=false
Type=Application
Categories=Utility;Development;
EOF_EOF
    echo "Created desktop entry"
fi

# Add to PATH (if not already there)
if ! echo "$PATH" | grep -q "$INSTALL_DIR"; then
    echo 'export PATH="$PATH:'$INSTALL_DIR'" >> "$HOME/.bashrc"
    echo "Added to PATH in ~/.bashrc"
fi

echo "Installation completed!"
echo "Run 'misa-ai' to start MISA AI"
EOF

    chmod +x "$DIST_DIR/install.sh"
    log_success "Created installation script"

    # Create tar package
    PACKAGE_NAME="misa-ai-v$VERSION-$(date +%Y%m%d-%H%M%S).tar.gz"
    tar -czf "$ROOT_DIR/$OUTPUT_DIR/$PACKAGE_NAME" -C "$ROOT_DIR" "$OUTPUT_DIR/distribution"

    PACKAGE_SIZE=$(stat -f%z "$ROOT_DIR/$OUTPUT_DIR/$PACKAGE_NAME" | cut -d' ' -f2)
    PACKAGE_SIZE_MB=$((PACKAGE_SIZE / 1024 / 1024))
    log_success "Created deployment package: $PACKAGE_NAME ($PACKAGE_SIZE_MB MB)"
}

# Create distribution package
create_distribution() {
    log_info "Creating distribution package..."

    DIST_DIR="$ROOT_DIR/$OUTPUT_DIR/distribution"
    mkdir -p "$DIST_DIR"

    # Copy built artifacts
    if [ -f "$ROOT_DIR/src/MISA.Core/bin/$CONFIGURATION/net8.0-windows/MISA.Core.exe" ]; then
        cp "$ROOT_DIR/src/MISA.Core/bin/$CONFIGURATION/net8.0-windows/MISA.Core.exe" "$DIST_DIR/misa-ai.exe"
    fi

    if [ -f "$ANDROID_DIR/app/build/outputs/apk/release/app-release.apk" ]; then
        cp "$ANDROID_DIR/app/build/outputs/apk/release/app-release.apk" "$DIST_DIR/misa-android.apk"
    fi

    # Create version info
    cat > "$DIST_DIR/version.json" << EOF
{
    "version": "$VERSION",
    "buildDate": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
    "configuration": "$CONFIGURATION",
    "platform": "$OSTYPE",
    "architecture": "$(uname -m)",
    "dotnetVersion": "$(dotnet --version)",
    "components": [
        {
            "name": "Core Engine",
            "version": "1.0.0"
        },
        {
            "name": "Personality System",
            "version": "1.0.0"
        },
        {
            "name": "WebRTC Remote Control",
            "version": "1.0.0"
        },
        {
            "name": "Memory System",
            "version": "1.0.0"
        },
        {
            "name": "Cloud Sync",
            "version": "1.0.0"
        },
        {
            "name": "Android App",
            "version": "1.0.0"
        }
    ]
}
EOF

    # Create README
    cat > "$DIST_DIR/README.md" << EOF
# MISA AI v$VERSION

## Installation

### Unix-like Systems (Linux/macOS)

#### Method 1: Install Script
\`\`\`bash
# Run the installation script
./install.sh
\`\`\`

#### Method 2: Manual Installation
1. Copy \`misa-ai\` to your preferred installation directory
2. Make it executable: \`chmod +x misa-ai\`
3. Add to your PATH if desired

### Windows

1. Run \`misa-ai-installer-v$VERSION.exe` as Administrator
2. Follow the installation wizard

### Android

1. Transfer \`misa-android.apk\` to your Android device
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

### Unix-like Systems
- .NET 8 Runtime
- 8GB RAM minimum (16GB recommended)
- 50GB free disk space

### Windows
- Windows 10 or later (64-bit)
- 8GB RAM minimum (16GB recommended)
- 50GB free disk space

### Android
- Android 7.0 or later
- 4GB RAM minimum
- 2GB free storage space

## Support

For support and documentation, visit https://misa.ai
EOF

    log_success "Distribution package created in: $DIST_DIR"
}

# Sign artifacts (if requested)
sign_artifacts() {
    if [ "$SIGN" = false ]; then
        return
    fi

    log_info "Signing artifacts..."

    # Check for code signing certificate
    if [ -z "$CODE_SIGNING_CERT_PATH" ] || [ -z "$CODE_SIGNING_CERT_PASSWORD" ]; then
        log_warning "Code signing credentials not found. Skipping signing."
        return
    fi

    # Sign executable if available
    if [ -f "$DIST_DIR/misa-ai.exe" ]; then
        log_info "Signing executable..."
        # This would use your platform's signing tool
        # For now, just mention that signing would happen here
        log_warning "Code signing configured but not implemented for this platform"
    fi
}

# Package for distribution
package_distribution() {
    if [ "$PACKAGE" = false ]; then
        return
    fi

    log_info "Creating distribution package..."

    PACKAGE_NAME="misa-ai-v$VERSION-$(date +%Y%m%d-%H%M%S)"
    PACKAGE_PATH="$ROOT_DIR/$OUTPUT_DIR/$PACKAGE_NAME.tar.gz"

    tar -czf "$PACKAGE_PATH" -C "$ROOT_DIR" "$OUTPUT_DIR/distribution"

    PACKAGE_SIZE=$(stat -f%z "$PACKAGE_PATH" | cut -d' ' -f2)
    PACKAGE_SIZE_MB=$((PACKAGE_SIZE / 1024 / 1024))
    log_success "Distribution package created: $PACKAGE_NAME ($PACKAGE_SIZE_MB MB)"
}

# Generate build report
generate_build_report() {
    log_info "Generating build report..."

    END_TIME=$(date +%s)
    DURATION=$((END_TIME - START_TIME))

    cat > "$ROOT_DIR/$OUTPUT_DIR/build-report.json" << EOF
{
    "buildId": "$(uuidgen | head -c 16)",
    "version": "$VERSION",
    "configuration": "$CONFIGURATION",
    "startTime": "$(date -d@$START_TIME -u +%Y-%m-%dT%H:%M:%SZ)",
    "endTime": "$(date -d@$END_TIME -u +%Y-%m-%dT%H:%M:%SZ)",
    "duration": "$DURATION",
    "status": "Success",
    "platform": "$OSTYPE",
    "architecture": "$(uname -m)",
    "dotnetVersion": "$(dotnet --version)",
    "gitCommit": "$(git rev-parse HEAD 2>/dev/null || echo 'unknown')",
    "gitBranch": "$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo 'unknown')",
    "components": [],
    "artifacts": []
}
EOF

    log_success "Build report generated: $ROOT_DIR/$OUTPUT_DIR/build-report.json"
}

# Display build summary
display_summary() {
    DURATION=$((END_TIME - START_TIME))

    echo
    log_info "MISA AI Build Summary"
    log_info "=================="
    echo "Version: $VERSION"
    echo "Configuration: $CONFIGURATION"
    echo "Duration: $(($DURATION / 3600))h $(((DURATION % 3600) / 60))m $((DURATION % 60))s"
    echo "Output Directory: $OUTPUT_DIR"

    if [ -d "$DIST_DIR" ]; then
        FILE_COUNT=$(find "$DIST_DIR" -type f | wc -l)
        TOTAL_SIZE=$(find "$DIST_DIR" -type f -exec du -b {} + 2>/dev/null | awk '{sum += $1} END {print sum/1024/1024}')
        echo "Distribution Files: $FILE_COUNT ($TOTAL_SIZE MB)"
    fi

    echo
    echo "Next steps:"
    echo "1. Test the installer on a clean system"
    echo "2. Install and verify Android APK"
    echo "3. Test cross-device functionality"
    echo "4. Deploy to distribution channels"
}

# Main execution
main() {
    log_info "Starting MISA AI build process..."
    echo

    validate_environment
    clean_build
    download_prerequisites
    build_dotnet
    run_tests
    build_android
    create_installer
    create_distribution
    sign_artifacts
    package_distribution
    generate_build_report
    display_summary

    exit 0
}

# Execute main function
main