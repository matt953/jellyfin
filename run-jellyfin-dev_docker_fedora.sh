#!/bin/bash
# run-jellyfin-dev_docker_fedora.sh - Run Jellyfin Docker container on Fedora
#
# Fedora-specific: disables SELinux confinement (needed for CIFS/NFS NAS mounts)
# and mounts NAS media directories.
#
# Usage:
#   ./run-jellyfin-dev_docker_fedora.sh          Start container
#   ./run-jellyfin-dev_docker_fedora.sh stop      Stop container
#   ./run-jellyfin-dev_docker_fedora.sh logs      Tail container logs

set -e

IMAGE_NAME="${JELLYFIN_IMAGE_NAME:-jellyfin-dev}"
CONTAINER_NAME="${JELLYFIN_CONTAINER_NAME:-jellyfin-dev}"

MEDIA_DIRS=(
    /mnt/nas/Media
    /mnt/nas/Movies
    /mnt/nas/Shows
    /mnt/nas/VR
)

action="${1:-start}"

case "$action" in
    stop)
        echo "Stopping $CONTAINER_NAME..."
        docker stop "$CONTAINER_NAME" 2>/dev/null || true
        docker rm "$CONTAINER_NAME" 2>/dev/null || true
        echo "Stopped."
        exit 0
        ;;
    logs)
        docker logs -f "$CONTAINER_NAME"
        exit 0
        ;;
    start|"")
        ;;
    *)
        echo "Usage: $0 [stop|logs]"
        exit 1
        ;;
esac

# Check if image exists
if ! docker image inspect "$IMAGE_NAME" &>/dev/null; then
    echo "Error: Docker image '$IMAGE_NAME' not found."
    echo "Build it first: ./build-docker-linux.sh --skip-ffmpeg"
    exit 1
fi

# Stop existing container if running
if docker container inspect "$CONTAINER_NAME" &>/dev/null; then
    echo "Stopping existing container..."
    docker stop "$CONTAINER_NAME" 2>/dev/null || true
    docker rm "$CONTAINER_NAME" 2>/dev/null || true
fi

echo "============================================"
echo " Jellyfin Development Environment (Docker)"
echo " Fedora / SELinux / NAS mounts"
echo "============================================"
echo " Image:     $IMAGE_NAME"
echo " Container: $CONTAINER_NAME"
echo " Media:"
for dir in "${MEDIA_DIRS[@]}"; do
    echo "   $dir"
done
echo "============================================"
echo ""
echo " Access at: http://localhost:8096"
echo " API Docs:  http://localhost:8096/api-docs/swagger/index.html"
echo ""

# GPU detection
GPU_ARGS=()
if [ -d /dev/dri ]; then
    GPU_ARGS+=(--device /dev/dri:/dev/dri)
    echo " GPU:       /dev/dri (Intel/AMD VA-API)"
fi
if command -v nvidia-smi &>/dev/null; then
    GPU_ARGS+=(--gpus all)
    echo " GPU:       NVIDIA (nvidia-container-toolkit)"
fi
echo ""

# Build media volume args (skip missing dirs)
MEDIA_ARGS=()
for dir in "${MEDIA_DIRS[@]}"; do
    if [ -d "$dir" ]; then
        MEDIA_ARGS+=(-v "${dir}:${dir}")
    else
        echo "Warning: $dir not found, skipping"
    fi
done

# --security-opt label=disable: required on Fedora because SELinux cannot
# relabel CIFS/NFS network mounts with :z. This disables SELinux confinement
# for this container only.
docker run -d \
    --name "$CONTAINER_NAME" \
    --security-opt label=disable \
    -p 8096:8096 \
    -v jellyfin-config:/config \
    -v jellyfin-cache:/cache \
    "${MEDIA_ARGS[@]}" \
    "${GPU_ARGS[@]}" \
    --restart unless-stopped \
    "$IMAGE_NAME"

echo "Container started. Logs: $0 logs"
