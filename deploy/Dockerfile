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

# Restore and build
RUN dotnet restore
RUN dotnet build -c Release --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-arm64v8
WORKDIR /app

# Copy built application from build stage
COPY --from=build /src/src/ZScout.HwTest.App/bin/Release/net10.0/publish/ .

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
