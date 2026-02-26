#!/bin/bash
# Jellyfin Development Environment Launcher
# This script runs Jellyfin server with local jellyfin-web and jellyfin-ffmpeg

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECTS_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
JELLYFIN_DIR="$SCRIPT_DIR"
JELLYFIN_WEB_DIR="$PROJECTS_DIR/jellyfin-web/dist"
JELLYFIN_FFMPEG_DIR="$PROJECTS_DIR/jellyfin-ffmpeg/builder/artifacts"
LOG_FILE="$PROJECTS_DIR/jellyfin-dev.log"

# Check if jellyfin-web is built
if [ ! -d "$JELLYFIN_WEB_DIR" ]; then
    echo "Error: jellyfin-web not built. Run: cd jellyfin-web && npm install && npm run build:development"
    exit 1
fi

# Find the ffmpeg binary
FFMPEG_PATH=""
if [ -d "$JELLYFIN_FFMPEG_DIR" ]; then
    # Look for extracted ffmpeg or extract from tar.xz
    if [ -f "$JELLYFIN_FFMPEG_DIR/ffmpeg" ]; then
        FFMPEG_PATH="$JELLYFIN_FFMPEG_DIR/ffmpeg"
    else
        # Try to extract from tar.xz if it exists
        TARBALL=$(find "$JELLYFIN_FFMPEG_DIR" -name "*.tar.xz" -type f 2>/dev/null | head -1)
        if [ -n "$TARBALL" ]; then
            echo "Extracting jellyfin-ffmpeg from $TARBALL..."
            tar -xJf "$TARBALL" -C "$JELLYFIN_FFMPEG_DIR"
            FFMPEG_PATH="$JELLYFIN_FFMPEG_DIR/ffmpeg"
        fi
    fi
fi

if [ -z "$FFMPEG_PATH" ] || [ ! -f "$FFMPEG_PATH" ]; then
    echo "Warning: jellyfin-ffmpeg not found. Falling back to system ffmpeg."
    FFMPEG_PATH=$(which ffmpeg 2>/dev/null || echo "")
    if [ -z "$FFMPEG_PATH" ]; then
        echo "Error: No ffmpeg found. Please build jellyfin-ffmpeg or install ffmpeg."
        exit 1
    fi
fi

echo "============================================"
echo "Jellyfin Development Environment"
echo "============================================"
echo "Jellyfin Server: $JELLYFIN_DIR"
echo "Jellyfin Web:    $JELLYFIN_WEB_DIR"
echo "FFmpeg:          $FFMPEG_PATH"
echo "Log File:        $LOG_FILE"
echo "============================================"
echo ""
echo "Starting Jellyfin server..."
echo "Access at: http://localhost:8096"
echo "API Docs:  http://localhost:8096/api-docs/swagger/index.html"
echo ""
echo "Logs are being written to: $LOG_FILE"
echo ""

cd "$JELLYFIN_DIR"
echo "Clean building to ensure all DLLs are in sync..."
dotnet clean -c Debug > /dev/null 2>&1
dotnet build -c Debug > /dev/null 2>&1
echo "Build complete. Starting server..."
dotnet run --project Jellyfin.Server --no-build -- \
    --webdir "$JELLYFIN_WEB_DIR" \
    --ffmpeg "$FFMPEG_PATH" \
    2>&1 | tee "$LOG_FILE"
