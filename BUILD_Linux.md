# Building Jellyfin + Custom jellyfin-ffmpeg on Linux

Tested on Fedora 43 x86_64, Docker 29.1.5.

## Status

| Script | Tested? | Notes |
|--------|---------|-------|
| `makeimage.sh linux64 gpl` | Yes | Works as-is on all distros |
| `build.sh linux64 gpl` | Yes | Upstream script, works on Debian/Ubuntu |
| `build_fedora.sh linux64 gpl` | Yes | Fedora/RHEL version (SELinux + no dpkg) |
| `build-docker-linux.sh --skip-ffmpeg` | Yes | Builds full Jellyfin Docker image |
| `Dockerfile` | Yes | Multi-stage build (web + server + custom ffmpeg) |
| `run-jellyfin-dev.sh docker` | Yes | Start/stop/logs all work |

## Prerequisites

```bash
# Fedora
sudo dnf install -y docker moby-engine docker-buildx
sudo systemctl enable --now docker
sudo usermod -aG docker $USER

# Debian/Ubuntu
sudo apt-get install -y docker.io docker-buildx
sudo usermod -aG docker $USER
```

After adding yourself to the `docker` group, either log out and back in, or use
`sg docker -c "command"` to run Docker commands in the current session without
re-logging. All commands in this guide use `sg docker -c` for Fedora.

## Directory Layout

All three repos must be siblings:

```
~/Projects/
    jellyfin/            # this repo
    jellyfin-ffmpeg/     # custom ffmpeg
    jellyfin-web/        # web UI
```

## Build Steps

### 1. Clone repos

```bash
cd ~/Projects
git clone https://github.com/jellyfin/jellyfin.git
git clone https://github.com/jellyfin/jellyfin-web.git
git clone https://github.com/jellyfin/jellyfin-ffmpeg.git
```

### 2. Build ffmpeg toolchain images (one-time, ~30-60 min)

```bash
cd ~/Projects/jellyfin-ffmpeg/builder
sg docker -c "./makeimage.sh linux64 gpl"
```

### 3. Build ffmpeg (~10-20 min)

**Fedora/RHEL (SELinux):**
```bash
cd ~/Projects/jellyfin-ffmpeg/builder
sg docker -c "./build_fedora.sh linux64 gpl"
```

**Debian/Ubuntu:**
```bash
cd ~/Projects/jellyfin-ffmpeg/builder
./build.sh linux64 gpl
```

Output: `builder/artifacts/jellyfin-ffmpeg_<version>_portable_linux64-gpl.tar.xz`

### 4. Build Docker image (~5-15 min)

```bash
cd ~/Projects/jellyfin
sg docker -c "./build-docker-linux.sh --skip-ffmpeg"
```

This builds the `jellyfin-dev` Docker image containing:
- jellyfin-server (built from source via .NET)
- jellyfin-web (built from source via Node.js)
- your custom ffmpeg binaries from step 3

### 5. Run

**Fedora/RHEL (SELinux + NAS mounts):**
```bash
cd ~/Projects/jellyfin
sg docker -c "./run-jellyfin-dev_docker_fedora.sh"
# Access at http://localhost:8096

# Stop/logs:
sg docker -c "./run-jellyfin-dev_docker_fedora.sh stop"
sg docker -c "./run-jellyfin-dev_docker_fedora.sh logs"
```

**Debian/Ubuntu:**
```bash
cd ~/Projects/jellyfin
./run-jellyfin-dev.sh docker
# Access at http://localhost:8096
```

Or manually:
```bash
docker run -d --name jellyfin \
    -p 8096:8096 \
    -v jellyfin-config:/config \
    -v jellyfin-cache:/cache \
    -v /path/to/media:/media:ro \
    --device /dev/dri:/dev/dri \
    jellyfin-dev
```

> **Note:** On Fedora, use `sg docker -c "command"` to run Docker commands
> if your shell session doesn't have the `docker` group active (e.g. after
> `usermod -aG docker $USER` without logging out/in).

## Why build_fedora.sh?

The upstream `build.sh` doesn't work on Fedora/RHEL because:

1. **SELinux** blocks Docker volume mounts unless `:z` suffix is added
2. **`dpkg-parsechangelog`** doesn't exist outside Debian (used for version parsing)

`build_fedora.sh` is a standalone copy of `build.sh` with these fixes.
The upstream `build.sh` is left untouched and works on Debian/Ubuntu as-is.

## Troubleshooting

- **Docker permission denied**: Use `sg docker -c "command"` or log out/in after adding yourself to the `docker` group.
- **GitHub 502 during makeimage.sh**: Transient — just re-run.
- **Rebuilding after changing ffmpeg patches**: Delete `builder/artifacts/`, re-run step 3. No need to re-run `makeimage.sh`.
- **Rebuilding after changing server/web code**: Just re-run step 4 (`sg docker -c "./build-docker-linux.sh --skip-ffmpeg"`).
