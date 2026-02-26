# Jellyfin Local Development Build Guide

## Prerequisites

| Requirement | Version | Install |
|-------------|---------|---------|
| .NET SDK | 10.0+ | `brew install dotnet` or [dotnet.microsoft.com](https://dotnet.microsoft.com/download) |
| Node.js | 24+ | `brew install fnm && fnm install 24` |
| FFmpeg build deps | - | See [jellyfin-ffmpeg/builder/Buildmac.md](../jellyfin-ffmpeg/builder/Buildmac.md) |

## Project Locations

```
~/Projects/
├── jellyfin/                  # Server (C#/.NET) - YOU ARE HERE
│   ├── run-jellyfin-dev_mac.sh    # Launcher 
│   └── BUILD.md               # This file
├── jellyfin-web/              # Web UI (TypeScript/React)
├── jellyfin-ffmpeg/           # FFmpeg with custom patches
└── jellyfin-packaging/        # Official packaging repo (reference)
```

## Quick Start

```bash
~/Projects/jellyfin/run-jellyfin-dev_mac.sh
```

Access at: http://localhost:8096

---

## Rebuilding Projects

### Jellyfin Server

```bash
cd ~/Projects/jellyfin
dotnet build
```

Clean rebuild:
```bash
dotnet clean && dotnet build
```

Run tests:
```bash
dotnet test
```

### Jellyfin Web

```bash
eval "$(fnm env)" && fnm use 24
cd ~/Projects/jellyfin-web
npm run build:development
```

### Jellyfin FFmpeg

See [jellyfin-ffmpeg/builder/Buildmac.md](../jellyfin-ffmpeg/builder/Buildmac.md) for full instructions.

```bash
cd ~/Projects/jellyfin-ffmpeg
quilt pop -af 2>/dev/null || true
rm -f patches
rm -rf builder/build
rm -rf /opt/ffbuild/prefix/*
rm -rf /opt/ffbuild/bin/*

cd builder && export PATH="/opt/ffbuild/bin:/opt/homebrew/opt/nasm@2/bin:$PATH" && ./buildmac.sh arm64

# Extract the binaries
cd artifacts && tar -xJf *.tar.xz

or partial rebuild
cd ~/Projects/jellyfin-ffmpeg && make -j$(sysctl -n hw.ncpu)
```

---

## Runtime Updates

| Component | Hot Reload | Action Required |
|-----------|------------|-----------------|
| **jellyfin-web** | Yes | Rebuild web, refresh browser |
| **jellyfin-server** | No | Rebuild server, restart process |
| **jellyfin-ffmpeg** | No | Rebuild ffmpeg, restart server |

### Web UI Changes (No Server Restart)

1. Make changes to `jellyfin-web/`
2. Rebuild:
   ```bash
   cd ~/Projects/jellyfin-web && npm run build:development
   ```
3. Refresh browser (Ctrl+Shift+R for hard refresh)

### Server Changes

1. Stop the running server (Ctrl+C)
2. Make changes to `jellyfin/`
3. Restart:
   ```bash
   ~/Projects/jellyfin/run-jellyfin-dev_mac.sh
   ```

### FFmpeg Changes

1. Stop the running server (Ctrl+C)
2. Make changes to `jellyfin-ffmpeg/`
3. Rebuild FFmpeg (see [Buildmac.md](../jellyfin-ffmpeg/builder/Buildmac.md))
4. Restart server:
   ```bash
   ~/Projects/jellyfin/run-jellyfin-dev_mac.sh
   ```

---

## Manual Run Command

```bash
cd ~/Projects/jellyfin
dotnet run --project Jellyfin.Server -- \
    --webdir ~/Projects/jellyfin-web/dist \
    --ffmpeg ~/Projects/jellyfin-ffmpeg/builder/artifacts/ffmpeg
```

---

## Useful Paths

| Description | Path |
|-------------|------|
| Server binary | `Jellyfin.Server/bin/Debug/net10.0/` |
| Web dist | `../jellyfin-web/dist/` |
| FFmpeg binary | `../jellyfin-ffmpeg/builder/artifacts/ffmpeg` |
| Data directory | `~/Library/Application Support/jellyfin/` |
| Config | `~/Library/Application Support/jellyfin/config/` |
| Logs | `~/Library/Application Support/jellyfin/log/` |
| Cache | `~/Library/Caches/jellyfin/` |

---

## Troubleshooting

**Port 8096 already in use:**
```bash
pkill -f "Jellyfin.Server"
```

**Node version mismatch:**
```bash
eval "$(fnm env)" && fnm use 24
```

**FFmpeg build issues:**
See [jellyfin-ffmpeg/builder/Buildmac.md](../jellyfin-ffmpeg/builder/Buildmac.md)
