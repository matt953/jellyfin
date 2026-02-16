# Jellyfin Docker Build with Custom jellyfin-ffmpeg
#
# This Dockerfile builds a complete Jellyfin server image using:
#   - jellyfin-server built from source
#   - jellyfin-web built from source
#   - custom-built jellyfin-ffmpeg binaries (your patches)
#
# Built by: build-docker-linux.sh (creates the staging context automatically)
# Context layout: jellyfin-server/, jellyfin-web/, ffmpeg/, Dockerfile

ARG DOTNET_VERSION=10.0
ARG NODEJS_VERSION=24
ARG OS_VERSION=trixie

###############################################################################
# Stage 1: Build jellyfin-web
###############################################################################
FROM node:${NODEJS_VERSION}-alpine AS web

RUN apk add --no-cache \
    autoconf g++ make libpng-dev gifsicle alpine-sdk \
    automake libtool gcc musl-dev nasm python3 git

WORKDIR /src
COPY jellyfin-web/ .

RUN npm ci --no-audit --unsafe-perm \
 && npm run build:production \
 && mv dist /web

###############################################################################
# Stage 2: Build jellyfin-server
###############################################################################
FROM debian:${OS_VERSION}-slim AS server

ARG DOTNET_VERSION
WORKDIR /src
COPY jellyfin-server/ .

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1

RUN apt-get update \
 && apt-get install --no-install-recommends --no-install-suggests -y \
    curl ca-certificates libicu76 \
 && curl -fsSL https://dot.net/v1/dotnet-install.sh \
    | bash /dev/stdin --channel ${DOTNET_VERSION} --install-dir /usr/local/bin

RUN dotnet publish Jellyfin.Server --configuration Release \
    --output="/server" --self-contained \
    -p:DebugSymbols=false -p:DebugType=none

###############################################################################
# Stage 3: Final combined image
###############################################################################
FROM debian:${OS_VERSION}-slim

# Intel OpenCL compute runtime versions (for HW-accelerated tone mapping)
ARG GMMLIB_VER=22.8.1
ARG IGC2_VER=2.18.5
ARG IGC2_BUILD=19820
ARG NEO_VER=25.35.35096.9
ARG IGC1_LEGACY_VER=1.0.17537.24
ARG NEO_LEGACY_VER=24.35.30872.36

ENV HEALTHCHECK_URL=http://localhost:8096/health

ENV DEBIAN_FRONTEND="noninteractive" \
    LC_ALL="en_US.UTF-8" \
    LANG="en_US.UTF-8" \
    LANGUAGE="en_US:en" \
    JELLYFIN_DATA_DIR="/config" \
    JELLYFIN_CACHE_DIR="/cache" \
    JELLYFIN_CONFIG_DIR="/config/config" \
    JELLYFIN_LOG_DIR="/config/log" \
    JELLYFIN_WEB_DIR="/jellyfin/jellyfin-web" \
    JELLYFIN_FFMPEG="/usr/lib/jellyfin-ffmpeg/ffmpeg"

# Required for fontconfig cache
ENV XDG_CACHE_HOME=${JELLYFIN_CACHE_DIR}

# https://github.com/dlemstra/Magick.NET/issues/707#issuecomment-785351620
ENV MALLOC_TRIM_THRESHOLD_=131072

# NVIDIA container runtime
ENV NVIDIA_VISIBLE_DEVICES="all"
ENV NVIDIA_DRIVER_CAPABILITIES="compute,video,utility"

# Install runtime dependencies
RUN apt-get update \
 && apt-get install --no-install-recommends --no-install-suggests -y \
    ca-certificates \
    curl \
    openssl \
    locales \
    libicu76 \
    libfontconfig1 \
    libfreetype6 \
    libjemalloc2 \
 && sed -i 's/# en_US.UTF-8 UTF-8/en_US.UTF-8 UTF-8/' /etc/locale.gen \
 && locale-gen \
 && apt-get clean autoclean -y \
 && apt-get autoremove -y \
 && rm -rf /var/cache/apt/archives* /var/lib/apt/lists/*

# Intel OpenCL runtime (amd64 only - for Intel QSV / OpenCL tone mapping)
RUN mkdir intel-compute-runtime && cd intel-compute-runtime \
 && curl -LO https://github.com/intel/compute-runtime/releases/download/${NEO_VER}/libigdgmm12_${GMMLIB_VER}_amd64.deb \
         -LO https://github.com/intel/intel-graphics-compiler/releases/download/v${IGC2_VER}/intel-igc-core-2_${IGC2_VER}+${IGC2_BUILD}_amd64.deb \
         -LO https://github.com/intel/intel-graphics-compiler/releases/download/v${IGC2_VER}/intel-igc-opencl-2_${IGC2_VER}+${IGC2_BUILD}_amd64.deb \
         -LO https://github.com/intel/compute-runtime/releases/download/${NEO_VER}/intel-opencl-icd_${NEO_VER}-0_amd64.deb \
         -LO https://github.com/intel/intel-graphics-compiler/releases/download/igc-${IGC1_LEGACY_VER}/intel-igc-core_${IGC1_LEGACY_VER}_amd64.deb \
         -LO https://github.com/intel/intel-graphics-compiler/releases/download/igc-${IGC1_LEGACY_VER}/intel-igc-opencl_${IGC1_LEGACY_VER}_amd64.deb \
         -LO https://github.com/intel/compute-runtime/releases/download/${NEO_LEGACY_VER}/intel-opencl-icd-legacy1_${NEO_LEGACY_VER}_amd64.deb \
 && apt-get update \
 && apt-get install --no-install-recommends --no-install-suggests -f -y ./*.deb \
 && cd .. && rm -rf intel-compute-runtime \
 && apt-get clean autoclean -y \
 && apt-get autoremove -y \
 && rm -rf /var/cache/apt/archives* /var/lib/apt/lists/*

# CJK fonts for subtitle rendering
RUN apt-get update \
 && apt-get install --no-install-recommends --no-install-suggests -y \
    fonts-wqy-zenhei \
    fonts-wqy-microhei \
    fonts-arphic-ukai \
    fonts-arphic-uming \
    fonts-noto-cjk \
    fonts-ipafont-mincho \
    fonts-ipafont-gothic \
    fonts-unfonts-core \
 && apt-get clean autoclean -y \
 && apt-get autoremove -y \
 && rm -rf /var/cache/apt/archives* /var/lib/apt/lists/*

# Setup jemalloc
RUN mkdir -p /usr/lib/jellyfin \
 && if [ -f /usr/lib/x86_64-linux-gnu/libjemalloc.so.2 ]; then \
        ln -sf /usr/lib/x86_64-linux-gnu/libjemalloc.so.2 /usr/lib/jellyfin/libjemalloc.so.2; \
    elif [ -f /usr/lib/aarch64-linux-gnu/libjemalloc.so.2 ]; then \
        ln -sf /usr/lib/aarch64-linux-gnu/libjemalloc.so.2 /usr/lib/jellyfin/libjemalloc.so.2; \
    fi
ENV LD_PRELOAD=/usr/lib/jellyfin/libjemalloc.so.2

# Copy custom-built jellyfin-ffmpeg (from pre-built portable tarball)
COPY ffmpeg/ /usr/lib/jellyfin-ffmpeg/
RUN chmod +x /usr/lib/jellyfin-ffmpeg/ffmpeg /usr/lib/jellyfin-ffmpeg/ffprobe 2>/dev/null || true

# Create data directories
RUN mkdir -p ${JELLYFIN_DATA_DIR} ${JELLYFIN_CACHE_DIR} \
 && chmod 777 ${JELLYFIN_DATA_DIR} ${JELLYFIN_CACHE_DIR}

# Copy server and web from build stages
COPY --from=server /server /jellyfin
COPY --from=web /web /jellyfin/jellyfin-web

EXPOSE 8096
VOLUME ${JELLYFIN_DATA_DIR} ${JELLYFIN_CACHE_DIR}
ENTRYPOINT ["/jellyfin/jellyfin"]

HEALTHCHECK --interval=30s --timeout=30s --start-period=10s --retries=3 \
    CMD curl --noproxy 'localhost' -Lk -fsS "${HEALTHCHECK_URL}" || exit 1
