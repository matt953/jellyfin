#!/bin/bash
# build-docker-linux.sh - Build Jellyfin Docker image with custom jellyfin-ffmpeg
#
# Usage:
#   ./build-docker-linux.sh                  Build everything (ffmpeg + Docker image)
#   ./build-docker-linux.sh --skip-ffmpeg    Skip ffmpeg build (use existing artifacts)
#   ./build-docker-linux.sh --ffmpeg-only    Only build ffmpeg portable, skip Docker image
#
# Prerequisites:
#   - Docker with buildx support
#   - Repos cloned at:
#       ../jellyfin-ffmpeg/
#       ../jellyfin-web/

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECTS_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

JELLYFIN_DIR="$PROJECTS_DIR/jellyfin"
JELLYFIN_WEB_DIR="$PROJECTS_DIR/jellyfin-web"
JELLYFIN_FFMPEG_DIR="$PROJECTS_DIR/jellyfin-ffmpeg"

IMAGE_NAME="${JELLYFIN_IMAGE_NAME:-jellyfin-dev}"
SKIP_FFMPEG=false
FFMPEG_ONLY=false

# Parse arguments
for arg in "$@"; do
    case $arg in
        --skip-ffmpeg)  SKIP_FFMPEG=true ;;
        --ffmpeg-only)  FFMPEG_ONLY=true ;;
        --help|-h)
            echo "Usage: $0 [--skip-ffmpeg] [--ffmpeg-only]"
            echo ""
            echo "Options:"
            echo "  --skip-ffmpeg    Skip jellyfin-ffmpeg build, use existing artifacts"
            echo "  --ffmpeg-only    Only build jellyfin-ffmpeg, skip Docker image"
            echo ""
            echo "Environment:"
            echo "  JELLYFIN_IMAGE_NAME    Docker image name (default: jellyfin-dev)"
            exit 0
            ;;
    esac
done

echo "============================================"
echo " Jellyfin Docker Image Builder"
echo "============================================"
echo ""
echo " Server:  $JELLYFIN_DIR"
echo " Web:     $JELLYFIN_WEB_DIR"
echo " FFmpeg:  $JELLYFIN_FFMPEG_DIR"
echo " Image:   $IMAGE_NAME"
echo ""

# Validate repos exist
for dir in "$JELLYFIN_DIR" "$JELLYFIN_WEB_DIR" "$JELLYFIN_FFMPEG_DIR"; do
    if [ ! -d "$dir" ]; then
        echo "Error: Directory not found: $dir"
        exit 1
    fi
done

###############################################################################
# Step 1: Build jellyfin-ffmpeg portable
###############################################################################
FFMPEG_TARBALL=""

if [ "$SKIP_FFMPEG" = false ]; then
    # Check if artifacts already exist
    FFMPEG_TARBALL=$(find "$JELLYFIN_FFMPEG_DIR/builder/artifacts" -name "*portable_linux64-gpl.tar.xz" -type f 2>/dev/null | sort -V | tail -1)

    if [ -n "$FFMPEG_TARBALL" ]; then
        echo "Found existing ffmpeg build: $(basename "$FFMPEG_TARBALL")"
        echo "Use --skip-ffmpeg to reuse, or delete artifacts/ to rebuild."
        echo ""
        read -r -p "Rebuild ffmpeg? [y/N] " response
        if [[ "$response" =~ ^[Yy]$ ]]; then
            FFMPEG_TARBALL=""
        fi
    fi

    if [ -z "$FFMPEG_TARBALL" ]; then
        echo ""
        echo "============================================"
        echo " Building jellyfin-ffmpeg portable..."
        echo " (This will take a while on first run)"
        echo "============================================"
        echo ""

        cd "$JELLYFIN_FFMPEG_DIR/builder"

        echo ">>> Step 1a: Building Docker images with dependencies..."
        ./makeimage.sh linux64 gpl

        echo ""
        echo ">>> Step 1b: Building ffmpeg..."
        ./build_fedora.sh linux64 gpl

        FFMPEG_TARBALL=$(find "$JELLYFIN_FFMPEG_DIR/builder/artifacts" -name "*portable_linux64-gpl.tar.xz" -type f | sort -V | tail -1)

        if [ -z "$FFMPEG_TARBALL" ]; then
            echo "Error: ffmpeg build failed - no tarball produced"
            exit 1
        fi

        echo ""
        echo "FFmpeg build complete: $(basename "$FFMPEG_TARBALL")"
    fi
else
    FFMPEG_TARBALL=$(find "$JELLYFIN_FFMPEG_DIR/builder/artifacts" -name "*portable_linux64-gpl.tar.xz" -type f 2>/dev/null | sort -V | tail -1)
    if [ -z "$FFMPEG_TARBALL" ]; then
        echo "Error: --skip-ffmpeg specified but no ffmpeg artifacts found."
        echo "Run without --skip-ffmpeg first, or build manually:"
        echo "  cd $JELLYFIN_FFMPEG_DIR/builder"
        echo "  ./makeimage.sh linux64 gpl"
        echo "  ./build.sh linux64 gpl"
        exit 1
    fi
    echo "Using existing ffmpeg: $(basename "$FFMPEG_TARBALL")"
fi

if [ "$FFMPEG_ONLY" = true ]; then
    echo ""
    echo "============================================"
    echo " FFmpeg build complete (--ffmpeg-only)"
    echo " Artifact: $FFMPEG_TARBALL"
    echo "============================================"
    exit 0
fi

###############################################################################
# Step 2: Create staging directory (minimal Docker build context)
###############################################################################
echo ""
echo "============================================"
echo " Preparing Docker build context..."
echo "============================================"

STAGING=$(mktemp -d "${TMPDIR:-/tmp}/jellyfin-docker-build.XXXXXX")
cleanup() {
    echo "Cleaning up staging directory..."
    rm -rf "$STAGING"
}
trap cleanup EXIT

# Copy Dockerfile
cp "$JELLYFIN_DIR/Dockerfile" "$STAGING/"

# Copy server source (exclude .git, build artifacts, dev files)
echo "  Copying jellyfin-server source..."
cp -r "$JELLYFIN_DIR" "$STAGING/jellyfin-server"
rm -rf "$STAGING/jellyfin-server/.git" \
       "$STAGING/jellyfin-server/.github" \
       "$STAGING/jellyfin-server/.devcontainer"
# Remove .NET build outputs from all project dirs
find "$STAGING/jellyfin-server" -type d \( -name bin -o -name obj \) -exec rm -rf {} + 2>/dev/null || true

# Copy web source (exclude .git, node_modules)
echo "  Copying jellyfin-web source..."
cp -r "$JELLYFIN_WEB_DIR" "$STAGING/jellyfin-web"
rm -rf "$STAGING/jellyfin-web/.git" \
       "$STAGING/jellyfin-web/.github" \
       "$STAGING/jellyfin-web/node_modules"

# Extract ffmpeg binaries
echo "  Extracting ffmpeg binaries from $(basename "$FFMPEG_TARBALL")..."
mkdir -p "$STAGING/ffmpeg"
tar -xJf "$FFMPEG_TARBALL" -C "$STAGING/ffmpeg/"

echo ""
echo "Build context ready at: $STAGING"
echo "  $(du -sh "$STAGING" | cut -f1) total"
echo ""

###############################################################################
# Step 3: Build Docker image
###############################################################################
echo "============================================"
echo " Building Docker image: $IMAGE_NAME"
echo "============================================"
echo ""

docker build \
    --progress=plain \
    -t "$IMAGE_NAME" \
    "$STAGING"

echo ""
echo "============================================"
echo " Build complete!"
echo "============================================"
echo ""
echo " Image: $IMAGE_NAME"
echo " FFmpeg: $(basename "$FFMPEG_TARBALL")"
echo ""
echo " Run with:"
echo "   ./run-jellyfin-dev.sh docker"
echo ""
echo " Or manually:"
echo "   docker run -d --name jellyfin \\"
echo "     -p 8096:8096 \\"
echo "     -v jellyfin-config:/config \\"
echo "     -v jellyfin-cache:/cache \\"
echo "     -v /path/to/media:/media:ro \\"
echo "     --device /dev/dri:/dev/dri \\"
echo "     $IMAGE_NAME"
echo ""
