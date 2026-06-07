using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ZScout.Common.Sdr;
using ZScout.HwTest.App.Hardware.Gps;

namespace ZScout.HwTest.App.Hardware.Sdr;

/// <summary>
/// Focused on-demand "SDR Waveform Capture Validator" (Phase B of #74 / parent #19).
/// - Curated list of common cell uplink (LTE/5G FR1) center freq + bw candidates.
/// - Auto-discover: try candidates via sdr-svc /api/rx/capture until one with detectable transmissions (bursts).
/// - Detailed raw capture at chosen f/bw.
/// - Client-side burst detection on float IQ.
/// - Per-transmission RSSI (10*log10(mean(I²+Q²))) + SNR (vs noise floor est).
/// - GPS fix (from gps-svc /api/fix at capture time) + wall time per tx.
/// - HEX sample of raw waveform prefix.
/// - Aggregate stats: tx count, RSSI count+range, SNR count+range.
/// 
/// Produces evidence that arbitrary (supported) f/bw raw capture works end-to-end.
/// On-demand (button in UI), not continuous. Graceful handling if capture 404 during sdr-svc rollout.
/// Reuses SdrClient (when extended), existing gps-svc paths, IHttpClientFactory, config, and power math patterns from ValidateIqBlock.
/// </summary>
public sealed class SdrCaptureValidator
{
    /// <summary>Curated short list of common LTE/5G FR1 uplink centers + typical BWs (Hz). Easy to extend. All inside uSDR RX range.</summary>
    private static readonly (long CenterHz, long BandwidthHz)[] Candidates =
    [
        (703_000_000, 5_000_000),   // LTE B28 / low-band approx
        (832_000_000, 10_000_000),  // LTE B20 uplink
        (880_000_000, 5_000_000),   // LTE B8
        (1_710_000_000, 10_000_000),// LTE B3
        (1_920_000_000, 5_000_000), // LTE B1 / B2 area
        (2_500_000_000, 20_000_000),// mid-band / 5G FR1
        (3_400_000_000, 20_000_000),// 5G n78 FR1 area (if supported)
    ];

    private readonly ILogger<SdrCaptureValidator> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly SdrClient _sdrClient;

    public SdrCaptureValidator(
        ILogger<SdrCaptureValidator> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        SdrClient sdrClient)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _sdrClient = sdrClient;
    }

    /// <summary>
    /// Runs the auto-discover + capture validator.
    /// Returns detailed result with chosen f/bw (or null), HEX, stats, per-tx (with GPS/time), messages.
    /// </summary>
    public async Task<CaptureResult> RunAutoDiscoverAndCaptureAsync(
        int maxCandidates = 6,
        int numSamples = 4096,
        CancellationToken ct = default)
    {
        var messages = new List<string>();
        var host = _config["Peripherals:Sdr:Host"] ?? "localhost";
        var port = int.TryParse(_config["Peripherals:Sdr:Port"], out var p) ? p : 5101;
        messages.Add($"SDR Waveform Capture Validator starting (sdr-svc {host}:{port}, max {maxCandidates} candidates)");

        IqSamples? chosenIq = null;
        long chosenF = 0, chosenBw = 0;

        int tried = 0;
        foreach (var (f, bw) in Candidates.Take(maxCandidates))
        {
            tried++;
            messages.Add($"[cand {tried}] Trying center={f / 1_000_000.0:F3} MHz bw={bw / 1_000_000.0:F1} MHz ...");
            var iq = await CaptureRawAsync(f, bw, numSamples, 8000, ct);
            if (iq is null || iq.Data.Length == 0)
            {
                messages.Add("  -> no data / unreachable (skipped)");
                continue;
            }

            var bursts = DetectBursts(iq.Data);
            messages.Add($"  -> bursts={bursts.Count} (energy present)");
            if (bursts.Count > 0)
            {
                chosenIq = iq;
                chosenF = f;
                chosenBw = bw;
                messages.Add($"SELECTED: active data at {f / 1_000_000.0:F3} MHz / {bw / 1_000_000.0:F1} MHz");
                break;
            }
        }

        if (chosenIq is null)
        {
            messages.Add("Auto-discover complete: no candidate showed detectable transmissions (all captures reached sdr-svc but channels were quiet or below threshold).");
            return new CaptureResult(
                null, null, null,
                0, 0, null, null,
                0, null, null,
                new List<PerTxInfo>(),
                messages,
                null,
                DateTimeOffset.UtcNow);
        }

        // Detailed analysis on the chosen capture
        var burstsFinal = DetectBursts(chosenIq.Data, threshDb: -26, minLenSamples: 8);
        var gps = await GetGpsFixAsync(ct);
        var capTime = DateTimeOffset.UtcNow;
        var hex = ComputeHexPrefix(chosenIq.Data, maxBytes: 48);

        var rssiVals = burstsFinal.Select(b => b.Rssi).ToList();
        var snrVals = burstsFinal.Select(b => b.Snr).ToList();

        var perTx = burstsFinal
            .Select(b => new PerTxInfo(b.Rssi, b.Snr, gps, capTime, b.StartSample, b.LengthSamples))
            .ToList();

        messages.Add($"Analysis: tx_count={burstsFinal.Count} rssi(n={rssiVals.Count} range={(rssiVals.Count>0 ? rssiVals.Min() : 0):F1}..{(rssiVals.Count>0 ? rssiVals.Max() : 0):F1}) snr(n={snrVals.Count} range={(snrVals.Count>0 ? snrVals.Min() : 0):F1}..{(snrVals.Count>0 ? snrVals.Max() : 0):F1})");
        messages.Add($"HEX prefix (first ~{hex.Length/2} bytes of IQ): {hex}");
        if (gps is not null)
            messages.Add($"GPS at capture: mode={gps.Mode} lat={(gps.Latitude?.ToString("F5") ?? "n/a")} lon={(gps.Longitude?.ToString("F5") ?? "n/a")} sats={gps.SatellitesUsed}");

        return new CaptureResult(
            chosenF, chosenBw, hex,
            burstsFinal.Count,
            rssiVals.Count, rssiVals.Count > 0 ? rssiVals.Min() : null, rssiVals.Count > 0 ? rssiVals.Max() : null,
            snrVals.Count, snrVals.Count > 0 ? snrVals.Min() : null, snrVals.Count > 0 ? snrVals.Max() : null,
            perTx,
            messages,
            gps,
            capTime);
    }

    /// <summary>
    /// Calls the new stateless capture endpoint (or graceful during rollout).
    /// Prefers /api/rx/capture?center_freq_hz=...&bandwidth_hz=...&num_samples=...
    /// Falls back/returns null on 404 or error (does not throw to caller for discovery).
    /// </summary>
    private async Task<IqSamples?> CaptureRawAsync(long centerFreqHz, long bandwidthHz, int numSamples, int timeoutMs, CancellationToken ct)
    {
        var host = _config["Peripherals:Sdr:Host"] ?? "localhost";
        var port = int.TryParse(_config["Peripherals:Sdr:Port"], out var p) ? p : 5101;
        try
        {
            var client = _httpClientFactory.CreateClient("SdrSvc");
            client.BaseAddress = new Uri($"http://{host}:{port}");
            client.Timeout = TimeSpan.FromMilliseconds(timeoutMs);

            var url = $"/api/rx/capture?center_freq_hz={centerFreqHz}&bandwidth_hz={bandwidthHz}&num_samples={numSamples}";
            using var response = await client.GetAsync(url, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("SdrCaptureValidator: /api/rx/capture 404 (sdr-svc#32 rollout not complete) for {F}/{Bw}", centerFreqHz, bandwidthHz);
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("SdrCaptureValidator: capture returned {Status} for {F}/{Bw}", response.StatusCode, centerFreqHz, bandwidthHz);
                return null;
            }

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var iq = await response.Content.ReadFromJsonAsync<IqSamples>(opts, ct);
            return iq;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "SdrCaptureValidator: capture exception for {F}/{Bw}", centerFreqHz, bandwidthHz);
            return null;
        }
    }

    private async Task<GpsFix?> GetGpsFixAsync(CancellationToken ct)
    {
        var host = _config["Peripherals:Gps:Host"] ?? "localhost";
        var restPort = int.TryParse(_config["Peripherals:Gps:RestPort"], out var rp) ? rp : 5200;
        try
        {
            var client = _httpClientFactory.CreateClient("GpsSvc");
            client.BaseAddress = new Uri($"http://{host}:{restPort}");
            client.Timeout = TimeSpan.FromSeconds(3);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(2500);
            return await client.GetFromJsonAsync<GpsFix>("/api/fix", cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "SdrCaptureValidator: gps fix at capture time unavailable");
            return null;
        }
    }

    /// <summary>
    /// Client-side burst detection on interleaved float IQ.
    /// Simple energy threshold segmentation. Returns bursts with start/len (in floats), RSSI, SNR.
    /// </summary>
    internal static List<BurstInfo> DetectBursts(float[] data, double threshDb = -26, int minLenSamples = 8)
    {
        if (data is null || data.Length < 4) return new List<BurstInfo>();

        int nComplex = data.Length / 2;
        var powers = new double[nComplex];
        double sumP = 0;
        double minP = double.MaxValue;

        for (int i = 0; i < nComplex; i++)
        {
            float re = data[2 * i];
            float im = data[2 * i + 1];
            double p = (double)re * re + (double)im * im;
            powers[i] = p;
            sumP += p;
            if (p < minP) minP = p;
        }

        double noiseEst = (minP > 1e-20) ? minP : (sumP / Math.Max(nComplex, 1) * 0.1);
        double threshP = Math.Pow(10.0, threshDb / 10.0);
        if (threshP < noiseEst * 1.8) threshP = noiseEst * 1.8; // stay above noise floor

        var bursts = new List<BurstInfo>();
        int start = -1;
        for (int i = 0; i < nComplex; i++)
        {
            if (powers[i] >= threshP)
            {
                if (start < 0) start = i;
            }
            else if (start >= 0)
            {
                int len = i - start;
                if (len >= minLenSamples)
                {
                    double meanP = 0;
                    for (int j = start; j < i; j++) meanP += powers[j];
                    meanP /= len;
                    double rssi = 10.0 * Math.Log10(Math.Max(meanP, 1e-20));
                    double nfDb = 10.0 * Math.Log10(Math.Max(noiseEst, 1e-20));
                    double snr = rssi - nfDb;
                    bursts.Add(new BurstInfo(start * 2, len * 2, rssi, snr));
                }
                start = -1;
            }
        }
        if (start >= 0)
        {
            int len = nComplex - start;
            if (len >= minLenSamples)
            {
                double meanP = 0;
                for (int j = start; j < nComplex; j++) meanP += powers[j];
                meanP /= len;
                double rssi = 10.0 * Math.Log10(Math.Max(meanP, 1e-20));
                double nfDb = 10.0 * Math.Log10(Math.Max(noiseEst, 1e-20));
                double snr = rssi - nfDb;
                bursts.Add(new BurstInfo(start * 2, len * 2, rssi, snr));
            }
        }
        return bursts;
    }

    /// <summary>
    /// HEX dump (lowercase, no separators) of raw float IQ byte prefix (little-endian float bytes).
    /// </summary>
    internal static string ComputeHexPrefix(float[] data, int maxBytes = 32)
    {
        if (data is null || data.Length == 0 || maxBytes <= 0) return string.Empty;
        int floats = Math.Min(data.Length, maxBytes / 4);
        byte[] bytes = new byte[floats * 4];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // Small DTOs for result (kept internal for test access + UI binding)
    internal record BurstInfo(int StartSample, int LengthSamples, double Rssi, double Snr);
}

/// <summary>
/// Result of auto-discover + capture validator. Suitable for UI display and telemetry.
/// </summary>
public sealed record CaptureResult(
    long? CenterFreqHz,
    long? BandwidthHz,
    string? HexSample,
    int TransmissionCount,
    int RssiCount,
    double? RssiMin,
    double? RssiMax,
    int SnrCount,
    double? SnrMin,
    double? SnrMax,
    List<PerTxInfo> PerTransmission,
    List<string> Messages,
    GpsFix? GpsAtCapture,
    DateTimeOffset CaptureTimeUtc);

/// <summary>
/// Per-transmission details (for optional table in UI).
/// </summary>
public sealed record PerTxInfo(double Rssi, double Snr, GpsFix? Gps, DateTimeOffset Time, int StartSample, int LengthSamples);