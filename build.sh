#!/usr/bin/env bash

# MISA AI Build Script for Linux/macOS
# This script builds the complete MISA AI system
# Portable and defensive: works on GNU and BSD/macOS where possible.

set -euo pipefail

# Color output functions
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
NC='\033[0m' # No Color

log_info()    { echo -e "${CYAN}ℹ $1${NC}"; }
log_success() { echo -e "${GREEN}✓ $1${NC}"; }
log_error()   { echo -e "${RED}✗ $1${NC}"; }
log_warn()    { echo -e "${YELLOW}⚠ $1${NC}"; }

# Get script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$SCRIPT_DIR"

# Portability helpers
file_size_bytes() {
  local file="$1"
  if [ ! -f "$file" ]; then echo 0; return; fi
  if stat --version >/dev/null 2>&1; then
    stat -c %s "$file"
  else
    stat -f %z "$file"
  fi
}

format_time_iso() {
  local epoch="$1"
  # GNU date supports -d, macOS uses -r
  if date -u -d "@${epoch}" +"%Y-%m-%dT%H:%M:%SZ" >/dev/null 2>&1; then
    date -u -d "@${epoch}" +"%Y-%m-%dT%H:%M:%SZ"
  else
    date -u -r "${epoch}" +"%Y-%m-%dT%H:%M:%SZ"
  fi
}

gen_id() {
  if command -v uuidgen >/dev/null 2>&1; then
    uuidgen | tr -d '-' | cut -c1-16
  elif command -v openssl >/dev/null 2>&1; then
    openssl rand -hex 8
  else
    echo "$(date +%s)$RANDOM"
  fi
}

# Default options
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
    --configuration) CONFIGURATION="$2"; shift 2;;
    --output-dir)    OUTPUT_DIR="$2"; shift 2;;
    --clean)         CLEAN=true; shift;;
    --skip-tests)    SKIP_TESTS=true; shift;;
    --skip-installer)SKIP_INSTALLER=true; shift;;
    --skip-android)  SKIP_ANDROID=true; shift;;
    --skip-prerequisites) SKIP_PREREQUISITES=true; shift;;
    --sign)          SIGN=true; shift;;
    --package)       PACKAGE=true; shift;;
    --version)       VERSION="$2"; shift 2;;
    *) log_error "Unknown option: $1"; exit 1;;
  esac
done

START_TIME=$(date +%s)

log_info "MISA AI Build System (Unix)"
log_info "========================="
log_info "  Configuration: $CONFIGURATION"
log_info "  Output Directory: $OUTPUT_DIR"
log_info "  Version: $VERSION"
log_info "  Root Directory: $ROOT_DIR"
echo

validate_environment() {
  log_info "Validating build environment..."

  if command -v dotnet >/dev/null 2>&1; then
    DOTNET_VERSION=$(dotnet --version 2>/dev/null || echo "unknown")
    log_success ".NET SDK: $DOTNET_VERSION"
  else
    log_error ".NET SDK not found. Please install .NET 8 SDK."
    exit 1
  fi

  if command -v node >/dev/null 2>&1; then
    NODE_VERSION=$(node --version 2>/dev/null || echo "unknown")
    log_success "Node.js: $NODE_VERSION"
  else
    log_warn "Node.js: Not found (optional)"
  fi

  if command -v npm >/dev/null 2>&1; then
    NPM_VERSION=$(npm --version 2>/dev/null || echo "unknown")
    log_success "npm: $NPM_VERSION"
  else
    log_warn "npm: Not found (optional)"
  fi

  log_info "Environment validation complete"
}

clean_build() {
  if [ "$CLEAN" != "true" ]; then return; fi
  log_info "Cleaning previous builds..."

  if [ -d "$OUTPUT_DIR" ]; then
    log_info "Removing build directory: $OUTPUT_DIR"
    rm -rf "$OUTPUT_DIR"
  fi

  # Remove bin/obj under src
  find "$ROOT_DIR/src" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null || true

  if [ -d "$ROOT_DIR/android/app/build" ]; then
    log_info "Removing Android build directory"
    rm -rf "$ROOT_DIR/android/app/build"
  fi

  log_success "Clean completed"
}

download_prerequisites() {
  if [ "$SKIP_PREREQUISITES" = "true" ]; then return; fi
  log_info "Downloading prerequisites..."
  mkdir -p "$ROOT_DIR/installer/prerequisites"
  log_info "Prerequisites directory prepared. On Unix use package manager to install required tools."
  log_success "Prerequisites check completed"
}

build_dotnet() {
  log_info "Building .NET projects..."
  mapfile -t PROJECT_FILES < <(find "$ROOT_DIR/src" -name "*.csproj" 2>/dev/null | sort)
  if [ ${#PROJECT_FILES[@]} -eq 0 ]; then
    log_error "No .NET project files found under $ROOT_DIR/src"
    exit 1
  fi

  PROJECT_COUNT=${#PROJECT_FILES[@]}
  log_info "Found $PROJECT_COUNT project files"

  SUCCESS_COUNT=0
  for project_file in "${PROJECT_FILES[@]}"; do
    PROJECT_NAME=$(basename "$project_file" .csproj)
    log_info "Building $PROJECT_NAME ..."

    echo "  Restoring packages..."
    if ! dotnet restore "$project_file" --verbosity quiet; then
      log_error "Failed to restore packages for $PROJECT_NAME"
      continue
    fi

    echo "  Compiling..."
    if ! dotnet build "$project_file" --configuration "$CONFIGURATION" --no-restore --verbosity minimal; then
      log_error "Failed to build $PROJECT_NAME"
      continue
    fi

    # Best-effort publish for likely executable projects
    if [[ "$project_file" == *"MISA.Core"* || "$project_file" == *".App"* ]]; then
      echo "  Publishing..."
      PUBLISH_DIR="$(dirname "$project_file")/bin/$CONFIGURATION/net8.0/publish"
      if ! dotnet publish "$project_file" --configuration "$CONFIGURATION" --output "$PUBLISH_DIR" --no-build; then
        log_warn "Publish failed for $PROJECT_NAME (continuing)"
      fi
    fi

    log_success "$PROJECT_NAME built successfully"
    SUCCESS_COUNT=$((SUCCESS_COUNT+1))
  done

  if [ $SUCCESS_COUNT -ne $PROJECT_COUNT ]; then
    log_error "Some .NET projects failed to build ($SUCCESS_COUNT/$PROJECT_COUNT)"
    exit 1
  fi
  log_success "All .NET projects built successfully ($SUCCESS_COUNT/$PROJECT_COUNT)"
}

run_tests() {
  if [ "$SKIP_TESTS" = "true" ]; then return; fi
  log_info "Running tests..."
  mapfile -t TEST_PROJECTS < <(find "$ROOT_DIR/src" -name "*Tests.csproj" 2>/dev/null | sort)
  if [ ${#TEST_PROJECTS[@]} -eq 0 ]; then
    log_warn "No test projects found"
    return
  fi

  ALL_PASSED=true
  for test_proj in "${TEST_PROJECTS[@]}"; do
    TEST_NAME=$(basename "$test_proj" .csproj)
    log_info "Running tests for $TEST_NAME"
    if ! dotnet test "$test_proj" --configuration "$CONFIGURATION" --no-build --verbosity minimal --logger "console;verbosity=normal"; then
      log_error "Tests failed: $TEST_NAME"
      ALL_PASSED=false
    else
      log_success "Tests passed: $TEST_NAME"
    fi
  done

  if [ "$ALL_PASSED" = "false" ]; then
    log_error "Some tests failed"
    exit 1
  fi
  log_success "All tests passed"
}

build_android() {
  if [ "$SKIP_ANDROID" = "true" ]; then return; fi
  log_info "Building Android APK..."
  ANDROID_DIR="$ROOT_DIR/android"
  if [ ! -d "$ANDROID_DIR" ]; then
    log_warn "Android directory not found; skipping Android build"
    return
  fi
  cd "$ANDROID_DIR" || exit 1

  if [ -f "./gradlew" ]; then
    chmod +x ./gradlew
    if ! ./gradlew assembleRelease; then
      BUILD_RESULT=$?
      cd "$ROOT_DIR" || exit 1
      log_error "Gradle assembleRelease failed (exit $BUILD_RESULT)"
      exit 1
    fi
  elif [ -f "./gradlew.bat" ]; then
    if ! ./gradlew.bat assembleRelease; then
      cd "$ROOT_DIR" || exit 1
      log_error "Gradle assembleRelease (bat) failed"
      exit 1
    fi
  else
    cd "$ROOT_DIR" || exit 1
    log_error "Gradle wrapper not found in android/ (add gradlew)"
    exit 1
  fi

  cd "$ROOT_DIR" || exit 1
  APK_PATH="$ANDROID_DIR/app/build/outputs/apk/release/app-release.apk"
  if [ -f "$APK_PATH" ]; then
    APK_SIZE_BYTES=$(file_size_bytes "$APK_PATH")
    APK_SIZE_MB=$((APK_SIZE_BYTES/1024/1024))
    log_success "Android APK built successfully (${APK_SIZE_MB} MB)"
  else
    log_error "Android APK not found after build"
    exit 1
  fi
}

create_installer() {
  if [ "$SKIP_INSTALLER" = "true" ]; then return; fi
  log_info "Creating installer/distribution..."

  DIST_DIR="$ROOT_DIR/$OUTPUT_DIR/distribution"
  mkdir -p "$DIST_DIR"

  # copy publish artifacts if present
  if [ -d "$ROOT_DIR/src/MISA.Core/bin/$CONFIGURATION/net8.0/publish" ]; then
    cp -r "$ROOT_DIR/src/MISA.Core/bin/$CONFIGURATION/net8.0/publish" "$DIST_DIR/misa-core-publish"
    log_success "Copied MISA Core publish folder"
  fi

  if [ -f "$ROOT_DIR/android/app/build/outputs/apk/release/app-release.apk" ]; then
    cp "$ROOT_DIR/android/app/build/outputs/apk/release/app-release.apk" "$DIST_DIR/misa-android-v$VERSION.apk"
    log_success "Copied Android APK"
  fi

  # simple installer script for Unix
  cat > "$DIST_DIR/install.sh" <<'EOF'
#!/usr/bin/env bash
set -e
INSTALL_DIR="$HOME/.local/bin"
mkdir -p "$INSTALL_DIR"
if [ -f "misa-core-publish/MISA.Core.dll" ]; then
  cp -r misa-core-publish "$INSTALL_DIR/misa-core"
  cat > "$INSTALL_DIR/misa-ai" <<LAUNCH
#!/usr/bin/env bash
DIR="$(cd "$(dirname "\${BASH_SOURCE[0]}")" && pwd)/misa-core"
exec dotnet "$DIR/MISA.Core.dll" "$@"
LAUNCH
  chmod +x "$INSTALL_DIR/misa-ai"
  echo "Installed misa-ai to $INSTALL_DIR"
fi
echo "Installation script finished"
EOF
  chmod +x "$DIST_DIR/install.sh"
  log_success "Created installation script"

  mkdir -p "$ROOT_DIR/$OUTPUT_DIR"
  PACKAGE_NAME="misa-ai-v$VERSION-$(date +%Y%m%d-%H%M%S).tar.gz"
  tar -C "$ROOT_DIR/$OUTPUT_DIR" -czf "$ROOT_DIR/$OUTPUT_DIR/$PACKAGE_NAME" distribution
  PACKAGE_SIZE_BYTES=$(file_size_bytes "$ROOT_DIR/$OUTPUT_DIR/$PACKAGE_NAME")
  PACKAGE_SIZE_MB=$((PACKAGE_SIZE_BYTES/1024/1024))
  log_success "Created package: $PACKAGE_NAME (${PACKAGE_SIZE_MB} MB)"
}

create_distribution() {
  log_info "Preparing distribution metadata..."
  DIST_DIR="$ROOT_DIR/$OUTPUT_DIR/distribution"
  mkdir -p "$DIST_DIR"
  cat > "$DIST_DIR/version.json" <<EOF
{
  "version": "$VERSION",
  "buildDate": "$(format_time_iso "$START_TIME")",
  "configuration": "$CONFIGURATION",
  "platform": "$(uname -s)",
  "architecture": "$(uname -m)",
  "dotnetVersion": "$(dotnet --version 2>/dev/null || echo 'unknown')"
}
EOF
  log_success "Distribution metadata created"
}

sign_artifacts() {
  if [ "$SIGN" = "true" ]; then
    log_warn "Signing requested but not implemented in this script"
  fi
}

package_distribution() {
  if [ "$PACKAGE" = "true" ]; then
    log_info "Packaging distribution..."
    PACKAGE_NAME="misa-ai-v$VERSION-$(date +%Y%m%d-%H%M%S)-dist.tar.gz"
    mkdir -p "$ROOT_DIR/$OUTPUT_DIR"
    tar -C "$ROOT_DIR/$OUTPUT_DIR" -czf "$ROOT_DIR/$OUTPUT_DIR/$PACKAGE_NAME" distribution
    log_success "Packaged distribution: $PACKAGE_NAME"
  fi
}

generate_build_report() {
  log_info "Generating build report..."
  END_TIME=$(date +%s)
  DURATION=$((END_TIME - START_TIME))
  BUILD_ID=$(gen_id)
  mkdir -p "$ROOT_DIR/$OUTPUT_DIR"
  cat > "$ROOT_DIR/$OUTPUT_DIR/build-report.json" <<EOF
{
  "buildId": "$BUILD_ID",
  "version": "$VERSION",
  "configuration": "$CONFIGURATION",
  "startTime": "$(format_time_iso "$START_TIME")",
  "endTime": "$(format_time_iso "$END_TIME")",
  "durationSeconds": $DURATION,
  "status": "Success",
  "platform": "$(uname -s)"
}
EOF
  log_success "Build report written to $ROOT_DIR/$OUTPUT_DIR/build-report.json"
}

display_summary() {
  END_TIME=${END_TIME:-$(date +%s)}
  DURATION=$((END_TIME - START_TIME))
  echo
  log_info "MISA AI Build Summary"
  echo "  Version: $VERSION"
  echo "  Configuration: $CONFIGURATION"
  echo "  Duration: $((DURATION/3600))h $(((DURATION%3600)/60))m $((DURATION%60))s"
  echo "  Output Directory: $OUTPUT_DIR"
  if [ -d "$ROOT_DIR/$OUTPUT_DIR/distribution" ]; then
    FILE_COUNT=$(find "$ROOT_DIR/$OUTPUT_DIR/distribution" -type f | wc -l)
    echo "  Distribution Files: $FILE_COUNT"
  fi
}

main() {
  log_info "Starting MISA AI build process..."
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
  log_success "Build finished"
}

main
