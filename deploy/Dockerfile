# Multi-stage Dockerfile for zSCOUT hardware test dashboard
# Target: linux-arm64 (Raspberry Pi CM5)

# Stage 1: Build
# Use --platform=$BUILDPLATFORM so the SDK runs natively on the CI host (amd64)
# and cross-compiles for the target architecture (arm64) via -a $TARGETARCH,
# avoiding QEMU emulation entirely in the build stage.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Copy solution and projects
COPY zSCOUT-hw-test.slnx .
COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY src/ ./src/
# Include shared common library sources outside /src so fallback external
# ProjectReference path resolves without inheriting hw-test CPM settings.
COPY zSCOUT-common/src/ /zSCOUT-common/src/

# Restore App project only — tests/ is not copied into the build image
RUN dotnet restore src/ZScout.HwTest.App/ZScout.HwTest.App.csproj -a $TARGETARCH
RUN dotnet publish src/ZScout.HwTest.App/ZScout.HwTest.App.csproj \
    -a $TARGETARCH -c Release --no-restore -o /app/publish

# Stage 2: Runtime
# Using full aspnet image (not chiseled) — we need shell access for
# hardware tools (gpspipe, SoapySDRUtil, i2cdetect, etc.)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-arm64v8
WORKDIR /app

# Install hardware diagnostic tools needed at runtime
# Note: i2c-tools removed — compass now uses compass-svc REST API (FR-004)
# Note: gpsd-clients removed — GPS now uses gps-svc REST API (#35)
# Kept: iw/usbutils/kmod (HaLow Tier A), pciutils
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl iw usbutils kmod pciutils && \
    rm -rf /var/lib/apt/lists/*

# Copy published application from build stage
COPY --from=build /app/publish/ .

# Metadata
LABEL org.opencontainers.image.title="zSCOUT Hardware Test Dashboard"
LABEL org.opencontainers.image.description="Hardware communication test suite for CM5 + CM5-IO-BASE-B"
LABEL org.opencontainers.image.url="https://github.com/Bulzi-Org/zSCOUT-hw-test"

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
	CMD curl -f http://localhost:5000/health || exit 1

# Expose port for dashboard
EXPOSE 5000

# Run application
ENTRYPOINT ["dotnet", "ZScout.HwTest.App.dll"]
