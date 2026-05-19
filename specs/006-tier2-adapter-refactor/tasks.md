# Tasks: Tier 2 Service API Adapter Refactor

**Branch**: `006-tier2-adapter-refactor` | **Plan**: [plan.md](plan.md)

## Task 1: Add gRPC NuGet packages and proto infrastructure
- Add Grpc.Net.Client, Google.Protobuf, Grpc.Tools to Directory.Packages.props
- Add package references to ZScout.HwTest.App.csproj
- Create Protos/ directory with compass.proto, sdr.proto, mesh.proto
- Configure .csproj for proto file compilation
- Verify: `dotnet build zSCOUT-hw-test.slnx`

## Task 2: Add shared helpers (TcpHealthCheck, GrpcChannelFactory)
- Create TcpHealthCheck.cs — static async method to test TCP connectivity with timeout
- Create GrpcChannelFactory.cs — creates GrpcChannel with configurable endpoint
- Both in Hardware/Common/

## Task 3: Refactor GpsAdapter — TCP connectivity check
- Replace `pgrep -x gpsd` with TcpHealthCheck to localhost:2947
- Make host/port configurable via IConfiguration
- Preserve gpspipe -w streaming (add host flag for TCP target)
- Preserve GnssFixUpdate parsing, GpsFixAccumulator, HealthSnapshot

## Task 4: Rewrite CompassAdapter — gRPC client
- Replace I2C-based probing with gRPC client to compass-svc:5100
- Use CompassService.GetStatus() for availability check
- Use CompassService.GetHeading() for probe data
- Make endpoint configurable via IConfiguration
- Build HealthSnapshot with heading, xyz, temperature data

## Task 5: Rewrite SdrAdapter — gRPC client
- Replace SoapySDRUtil shell commands with gRPC client to sdr-svc:5101
- Use SdrService.GetStatus() for device availability
- Use SdrService.GetCapabilities() for probe data
- Make endpoint configurable via IConfiguration
- Build HealthSnapshot with device_found, capabilities data

## Task 6: Extend HalowAdapter — Add Tier B mesh gRPC
- Preserve existing Tier A (Layers 0-3) exactly as-is
- Add Tier B after Tier A passes: gRPC connect to mesh :5102
- Use MeshService.GetStatus() and MeshService.GetPeers()
- If mesh unavailable, report NotTested (not failure)
- Extend HealthSnapshot with Tier B fields
- Add NotTested value to PeripheralStatus enum if needed for mesh reporting

## Task 7: Refactor RunOrchestrator — incremental processing
- Replace Task.WhenAll with incremental result processing
- Save evidence and publish status as each adapter completes
- Assign per-adapter verdicts immediately on completion
- Compute overall run outcome after all adapters finish

## Task 8: Update Docker configuration
- Dockerfile: Remove i2c-tools from apt-get install, keep iw/usbutils/kmod/gpsd-clients
- docker-compose.yml: Remove privileged: true, remove /dev:/dev:ro volume
- Keep /sys:/sys:ro and --network host

## Task 9: Update tests
- Add GpsAdapter TCP-based tests
- Add CompassAdapter gRPC tests
- Add SdrAdapter gRPC tests
- Update HalowAdapter tests for Tier B
- Add RunOrchestrator incremental processing tests
- Verify: `dotnet test`

## Task 10: Final verification
- `dotnet build zSCOUT-hw-test.slnx` — zero warnings
- `dotnet test` — all tests pass
- `docker build -f deploy/Dockerfile -t zscout-hw-test .` — builds successfully
- Self code review for spec alignment
