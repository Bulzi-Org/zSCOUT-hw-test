# Multi-stage Dockerfile for zSCOUT hardware test dashboard
# Target: linux-arm64 (Raspberry Pi CM5)

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and projects
COPY zSCOUT-hw-test.slnx .
COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY src/ ./src/

# Restore App project only — tests/ is not copied into the build image
RUN dotnet restore src/ZScout.HwTest.App/ZScout.HwTest.App.csproj
RUN dotnet publish src/ZScout.HwTest.App/ZScout.HwTest.App.csproj \
    -c Release --no-restore -o /app/publish

# Stage 2: Runtime
# Using full aspnet image (not chiseled) — we need shell access for
# hardware tools (gpspipe, SoapySDRUtil, i2cdetect, etc.)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-arm64v8
WORKDIR /app

# Install hardware diagnostic tools needed at runtime
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl gpsd-clients i2c-tools usbutils pciutils && \
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
