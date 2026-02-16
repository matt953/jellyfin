# Jellyfin Local Development Build Guide (Linux/Fedora)

## Project Layout

```
~/Projects/
├── jellyfin/                  # Server (C#/.NET) - YOU ARE HERE
│   ├── Dockerfile             # Custom Docker build (uses your ffmpeg)
│   ├── build-docker-linux.sh        # Builds complete Docker image
│   ├── run-jellyfin-dev.sh    # Launcher (Docker or native mode)
│   └── BUILD.md               # This file
├── jellyfin-web/              # Web UI (TypeScript/React)
├── jellyfin-ffmpeg/           # FFmpeg with custom patches
└── jellyfin-packaging/        # Official packaging repo (reference)
```

---

## Quick Start (Docker - Recommended)

Build a Docker image containing your custom jellyfin-ffmpeg, server, and web UI:

```bash
# Build everything (ffmpeg + Docker image)
cd ~/Projects/jellyfin
./build-docker-linux.sh

# Run it
./run-jellyfin-dev.sh docker
```

Access at: http://localhost:8096

This builds jellyfin-ffmpeg portable inside Docker, then creates a Jellyfin image with your patched ffmpeg baked in.

---

## Docker Build Details

### Prerequisites

- Docker with BuildKit support (`docker buildx` available)
- ~20 GB disk space (for ffmpeg dependency images + build context)

### Step 1: Build jellyfin-ffmpeg portable

The `build-docker-linux.sh` script does this automatically, but you can also run it manually:

```bash
cd ~/Projects/jellyfin-ffmpeg/builder

# Build the Docker image containing all pre-compiled dependencies
./makeimage.sh linux64 gpl

# Build ffmpeg inside that image
./build.sh linux64 gpl
```

Output: `builder/artifacts/jellyfin-ffmpeg_<version>_portable_linux64-gpl.tar.xz`

This tarball contains the `ffmpeg` and `ffprobe` binaries with all your custom patches.

### Step 2: Build the Docker image

```bash
cd ~/Projects/jellyfin
./build-docker-linux.sh --skip-ffmpeg   # Skip ffmpeg if already built
```

The script creates a minimal staging directory, copies server/web sources and ffmpeg binaries, and runs `docker build`. The multi-stage Dockerfile:
1. Builds `jellyfin-web` with Node.js
2. Builds `jellyfin-server` with .NET SDK
3. Creates a Debian slim final image with your custom ffmpeg, Intel OpenCL runtime, and CJK fonts

### Step 3: Run the container

```bash
# Start
./run-jellyfin-dev.sh docker

# View logs
./run-jellyfin-dev.sh docker logs

# Stop
./run-jellyfin-dev.sh docker stop
```

Or run manually with more control:

```bash
docker run -d --name jellyfin-dev \
    -p 8096:8096 \
    -v jellyfin-config:/config \
    -v jellyfin-cache:/cache \
    -v ~/Media:/media:ro \
    --device /dev/dri:/dev/dri \
    jellyfin-dev
```

### GPU Passthrough

| GPU | Docker Flag |
|-----|-------------|
| Intel / AMD (VA-API) | `--device /dev/dri:/dev/dri` |
| NVIDIA | `--gpus all` (requires nvidia-container-toolkit) |

The `run-jellyfin-dev.sh docker` command auto-detects available GPUs.

### Rebuild After Changes

| Changed | Command |
|---------|---------|
| jellyfin-ffmpeg patches | `./build-docker-linux.sh` (full rebuild) |
| jellyfin-server code | `./build-docker-linux.sh --skip-ffmpeg` |
| jellyfin-web code | `./build-docker-linux.sh --skip-ffmpeg` |

---

## Native Development (Alternative)

Run directly on Fedora without Docker. Faster iteration for server/web changes, but requires installing dependencies locally.

### Prerequisites

```bash
# .NET SDK 10.0
sudo dnf install dotnet-sdk-10.0

# Node.js (for building jellyfin-web)
sudo dnf install nodejs npm
# Or use fnm for version management:
#   curl -fsSL https://fnm.vercel.app/install | bash
#   fnm install 22

# System ffmpeg (fallback - won't have your custom patches)
sudo dnf install ffmpeg-free
```

### Build and Run

```bash
# 1. Build jellyfin-web
cd ~/Projects/jellyfin-web
npm install
npm run build:development

# 2. Run jellyfin-server (uses dotnet run)
cd ~/Projects/jellyfin
./run-jellyfin-dev.sh
```

The native launcher will:
- Use custom-built ffmpeg from `jellyfin-ffmpeg/builder/artifacts/` if available
- Fall back to system ffmpeg otherwise

### Using Custom FFmpeg Natively

If you've built the portable ffmpeg (via `build-docker-linux.sh --ffmpeg-only` or the builder scripts), the tarball in `jellyfin-ffmpeg/builder/artifacts/` is auto-extracted and used.

```bash
# Build only ffmpeg portable (no Docker image)
./build-docker-linux.sh --ffmpeg-only

# Then run natively with the custom ffmpeg
./run-jellyfin-dev.sh
```

### Runtime Updates

| Component | Hot Reload | Action |
|-----------|------------|--------|
| jellyfin-web | Yes | Rebuild web, refresh browser (Ctrl+Shift+R) |
| jellyfin-server | No | Ctrl+C, re-run `./run-jellyfin-dev.sh` |
| jellyfin-ffmpeg | No | Rebuild ffmpeg, restart server |

---

## Rebuilding Individual Components

### Jellyfin Server

```bash
cd ~/Projects/jellyfin
dotnet build                    # Incremental build
dotnet clean && dotnet build    # Clean rebuild
dotnet test                     # Run tests
```

### Jellyfin Web

```bash
cd ~/Projects/jellyfin-web
npm run build:development       # Dev build (faster, with source maps)
npm run build:production        # Production build (minified)
```

### Jellyfin FFmpeg

```bash
# Full rebuild (builds all dependencies + ffmpeg in Docker)
cd ~/Projects/jellyfin-ffmpeg/builder
./makeimage.sh linux64 gpl
./build.sh linux64 gpl

# Output: artifacts/jellyfin-ffmpeg_*_portable_linux64-gpl.tar.xz
```

To iterate on ffmpeg patches without rebuilding dependencies:
1. Dependencies are cached in the Docker image from `makeimage.sh`
2. Only `build.sh` needs to re-run (applies patches + compiles ffmpeg)

To clean and rebuild:
```bash
cd ~/Projects/jellyfin-ffmpeg
quilt pop -af 2>/dev/null || true
rm -f patches
rm -rf builder/ffbuild builder/artifacts
```

---

## Useful Paths

| Description | Path |
|-------------|------|
| Server source | `~/Projects/jellyfin/` |
| Web source | `~/Projects/jellyfin-web/` |
| FFmpeg source | `~/Projects/jellyfin-ffmpeg/` |
| FFmpeg artifacts | `~/Projects/jellyfin-ffmpeg/builder/artifacts/` |
| FFmpeg patches | `~/Projects/jellyfin-ffmpeg/debian/patches/` |
| Docker config volume | `docker volume inspect jellyfin-config` |
| Native data dir | `~/.local/share/jellyfin/` |
| Native config | `~/.config/jellyfin/` |
| Native cache | `~/.cache/jellyfin/` |
| Native logs | `~/.config/jellyfin/log/` |

---

## Troubleshooting

**Port 8096 already in use:**
```bash
# Kill native process
pkill -f "Jellyfin.Server"
# Or stop Docker container
./run-jellyfin-dev.sh docker stop
```

**Docker build fails at web stage:**
- Ensure `jellyfin-web` is cloned: `git clone https://github.com/jellyfin/jellyfin-web ~/Projects/jellyfin-web`

**FFmpeg makeimage.sh fails:**
- Ensure Docker BuildKit is enabled: `docker buildx version`
- May need significant disk space (~15 GB) for the dependency images

**"No space left on device" during Docker build:**
```bash
docker system prune -a    # Remove unused images/containers
```

**Reset Jellyfin data (Docker):**
```bash
docker volume rm jellyfin-config jellyfin-cache
```

**Reset Jellyfin data (Native):**
```bash
rm -rf ~/.local/share/jellyfin ~/.config/jellyfin ~/.cache/jellyfin
```
