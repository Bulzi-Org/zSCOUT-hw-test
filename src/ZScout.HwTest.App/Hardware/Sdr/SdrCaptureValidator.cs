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
    private const int DefaultSamplesPerCapture = 65_536;
    private const int DefaultRepeatsPerCandidate = 3;
    private const double SignalPeakSnrThresholdDb = 10.0;

    /// <summary>
    /// Uplink-focused profiles for 3G (UMTS), 4G/LTE, and 5G NR FR1.
    /// Frequencies are in Hz and constrained to uSDR operating range.
    /// </summary>
    private static readonly ScanProfile[] UplinkProfiles =
    [
        // 3G / WCDMA common uplink ranges
        new("3G", "Band1", 1_920_000_000, 1_980_000_000, [3_840_000, 5_000_000]),
        new("3G", "Band2", 1_850_000_000, 1_910_000_000, [3_840_000, 5_000_000]),
        new("3G", "Band4", 1_710_000_000, 1_755_000_000, [3_840_000, 5_000_000]),
        new("3G", "Band5", 824_000_000, 849_000_000, [3_840_000, 5_000_000]),
        new("3G", "Band8", 880_000_000, 915_000_000, [3_840_000, 5_000_000]),

        // 4G/LTE FDD/TDD common uplink ranges
        new("4G/LTE", "Band1", 1_920_000_000, 1_980_000_000, [5_000_000, 10_000_000]),
        new("4G/LTE", "Band2", 1_850_000_000, 1_910_000_000, [5_000_000, 10_000_000]),
        new("4G/LTE", "Band3", 1_710_000_000, 1_785_000_000, [5_000_000, 10_000_000, 20_000_000]),
        new("4G/LTE", "Band4", 1_710_000_000, 1_755_000_000, [5_000_000, 10_000_000]),
        new("4G/LTE", "Band5", 824_000_000, 849_000_000, [5_000_000, 10_000_000]),
        new("4G/LTE", "Band7", 2_500_000_000, 2_570_000_000, [10_000_000, 20_000_000]),
        new("4G/LTE", "Band8", 880_000_000, 915_000_000, [5_000_000, 10_000_000]),
        new("4G/LTE", "Band12", 699_000_000, 716_000_000, [5_000_000]),
        new("4G/LTE", "Band13", 777_000_000, 787_000_000, [5_000_000]),
        new("4G/LTE", "Band17", 704_000_000, 716_000_000, [5_000_000]),
        new("4G/LTE", "Band20", 832_000_000, 862_000_000, [5_000_000, 10_000_000]),
        new("4G/LTE", "Band25", 1_850_000_000, 1_915_000_000, [5_000_000, 10_000_000]),
        new("4G/LTE", "Band26", 814_000_000, 849_000_000, [5_000_000, 10_000_000]),
        new("4G/LTE", "Band28", 703_000_000, 748_000_000, [5_000_000, 10_000_000]),
        new("4G/LTE", "Band38", 2_570_000_000, 2_620_000_000, [10_000_000, 20_000_000]),
        new("4G/LTE", "Band40", 2_300_000_000, 2_400_000_000, [10_000_000, 20_000_000]),
        new("4G/LTE", "Band41", 2_496_000_000, 2_690_000_000, [10_000_000, 20_000_000]),
        new("4G/LTE", "Band66", 1_710_000_000, 1_780_000_000, [5_000_000, 10_000_000, 20_000_000]),
        new("4G/LTE", "Band71", 663_000_000, 698_000_000, [5_000_000, 10_000_000]),

        // 5G NR FR1 common uplink ranges (within <= 3.8 GHz constraint)
        new("5G", "n1", 1_920_000_000, 1_980_000_000, [5_000_000, 10_000_000, 20_000_000]),
        new("5G", "n2", 1_850_000_000, 1_910_000_000, [5_000_000, 10_000_000, 20_000_000]),
        new("5G", "n3", 1_710_000_000, 1_785_000_000, [5_000_000, 10_000_000, 20_000_000]),
        new("5G", "n5", 824_000_000, 849_000_000, [5_000_000, 10_000_000]),
        new("5G", "n7", 2_500_000_000, 2_570_000_000, [10_000_000, 20_000_000]),
        new("5G", "n8", 880_000_000, 915_000_000, [5_000_000, 10_000_000]),
        new("5G", "n20", 832_000_000, 862_000_000, [5_000_000, 10_000_000]),
        new("5G", "n25", 1_850_000_000, 1_915_000_000, [5_000_000, 10_000_000, 20_000_000]),
        new("5G", "n28", 703_000_000, 748_000_000, [5_000_000, 10_000_000]),
        new("5G", "n38", 2_570_000_000, 2_620_000_000, [10_000_000, 20_000_000]),
        new("5G", "n40", 2_300_000_000, 2_400_000_000, [10_000_000, 20_000_000]),
        new("5G", "n41", 2_496_000_000, 2_690_000_000, [10_000_000, 20_000_000]),
        new("5G", "n66", 1_710_000_000, 1_780_000_000, [5_000_000, 10_000_000, 20_000_000]),
        new("5G", "n71", 663_000_000, 698_000_000, [5_000_000, 10_000_000]),
        new("5G", "n77", 3_300_000_000, 3_800_000_000, [20_000_000]),
        new("5G", "n78", 3_300_000_000, 3_800_000_000, [20_000_000]),
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
        int maxCandidates = 0,
        int numSamples = DefaultSamplesPerCapture,
        CancellationToken ct = default)
    {
        numSamples = Math.Max(2_048, numSamples);
        var messages = new List<string>();
        var host = _config["Peripherals:Sdr:Host"] ?? "localhost";
        var port = int.TryParse(_config["Peripherals:Sdr:Port"], out var p) ? p : 5101;
        var repeatsPerCandidate = int.TryParse(_config["Peripherals:Sdr:AutoDiscoverRepeatsPerCandidate"], out var repeats)
            ? Math.Clamp(repeats, 1, 8)
            : DefaultRepeatsPerCandidate;
        var candidates = BuildCandidates(maxCandidates);
        messages.Add($"SDR Waveform Capture Validator starting (sdr-svc {host}:{port}, candidates={candidates.Count}, samples={numSamples}, repeats={repeatsPerCandidate})");

        IqSamples? chosenIq = null;
        ScanCandidate? chosenCandidate = null;
        AttemptMetrics? bestAttempt = null;

        for (int i = 0; i < candidates.Count; i++)
        {
            var cand = candidates[i];
            messages.Add($"[cand {i + 1}/{candidates.Count}] {cand.Tech} {cand.Band} center={cand.CenterHz / 1_000_000.0:F3}MHz bw={cand.BandwidthHz / 1_000_000.0:F1}MHz");

            for (int rep = 1; rep <= repeatsPerCandidate; rep++)
            {
                var capture = await CaptureRawAsync(cand.CenterHz, cand.BandwidthHz, numSamples, 12000, ct);
                if (capture.Samples is null || capture.Samples.Data.Length == 0)
                {
                    messages.Add($"  [rep {rep}/{repeatsPerCandidate}] no IQ ({capture.Diagnostic})");
                    continue;
                }

                var iq = capture.Samples;
                var bursts = DetectBursts(iq.Data);
                var metrics = ComputeAttemptMetrics(iq, bursts);

                messages.Add($"  [rep {rep}/{repeatsPerCandidate}] {capture.Diagnostic}");
                messages.Add($"     stats: floats={metrics.FloatCount} mean_abs={metrics.MeanAbs:F4} p95={metrics.P95Db:F1}dB peak={metrics.PeakDb:F1}dB noise={metrics.NoiseFloorDb:F1}dB peak_snr={metrics.PeakSnrDb:F1}dB bursts={metrics.BurstCount}");

                if (bestAttempt is null || metrics.PeakSnrDb > bestAttempt.PeakSnrDb)
                {
                    bestAttempt = metrics;
                }

                // Prefer explicit burst detection, then high peak-vs-noise contrast fallback.
                if (metrics.BurstCount > 0 || metrics.PeakSnrDb >= SignalPeakSnrThresholdDb)
                {
                    chosenIq = iq;
                    chosenCandidate = cand;
                    messages.Add($"SELECTED: {cand.Tech} {cand.Band} @ {cand.CenterHz / 1_000_000.0:F3}MHz/{cand.BandwidthHz / 1_000_000.0:F1}MHz (bursts={metrics.BurstCount}, peak_snr={metrics.PeakSnrDb:F1}dB)");
                    break;
                }
            }

            if (chosenIq is not null)
            {
                break;
            }
        }

        if (chosenIq is null || chosenCandidate is null)
        {
            if (bestAttempt is not null)
            {
                messages.Add($"Best observed energy: peak_snr={bestAttempt.PeakSnrDb:F1}dB p95={bestAttempt.P95Db:F1}dB bursts={bestAttempt.BurstCount}");
            }
            messages.Add("Auto-discover complete: no candidate showed detectable transmissions above threshold. Verify antenna, gain, front-end path, and increase timeout/sample count if needed.");
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
            chosenCandidate.CenterHz, chosenCandidate.BandwidthHz, hex,
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
    private async Task<CaptureAttempt> CaptureRawAsync(long centerFreqHz, long bandwidthHz, int numSamples, int timeoutMs, CancellationToken ct)
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
                return new CaptureAttempt(null, "404 Not Found on /api/rx/capture");
            }
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("SdrCaptureValidator: capture returned {Status} for {F}/{Bw}", response.StatusCode, centerFreqHz, bandwidthHz);
                var body = await response.Content.ReadAsStringAsync(ct);
                var compact = string.IsNullOrWhiteSpace(body)
                    ? "(empty body)"
                    : body.Replace('\n', ' ').Replace('\r', ' ').Trim();
                if (compact.Length > 160) compact = compact[..160] + "...";
                return new CaptureAttempt(null, $"HTTP {(int)response.StatusCode} {response.StatusCode}: {compact}");
            }

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var iq = await response.Content.ReadFromJsonAsync<IqSamples>(opts, ct);
            if (iq is null)
            {
                return new CaptureAttempt(null, "response JSON did not deserialize into IqSamples");
            }

            var floatCount = iq.Data?.Length ?? 0;
            var sampleRate = iq.SampleRateHz;
            var durationMs = sampleRate > 0 ? (floatCount / 2.0) / sampleRate * 1000.0 : 0.0;
            var detail = $"center={iq.CenterFreqHz / 1_000_000.0:F3}MHz sample_rate={sampleRate / 1_000_000.0:F3}MSPS floats={floatCount} duration~{durationMs:F2}ms";
            return new CaptureAttempt(iq, detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "SdrCaptureValidator: capture exception for {F}/{Bw}", centerFreqHz, bandwidthHz);
            return new CaptureAttempt(null, $"exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static List<ScanCandidate> BuildCandidates(int maxCandidates)
    {
        var all = new List<ScanCandidate>(2048);
        foreach (var profile in UplinkProfiles)
        {
            foreach (var bandwidth in profile.BandwidthsHz)
            {
                var step = Math.Max(2_000_000L, bandwidth / 2);
                var minCenter = profile.UplinkStartHz + (bandwidth / 2);
                var maxCenter = profile.UplinkEndHz - (bandwidth / 2);
                if (maxCenter < minCenter)
                {
                    continue;
                }

                for (long center = minCenter; center <= maxCenter; center += step)
                {
                    all.Add(new ScanCandidate(profile.Tech, profile.Band, center, bandwidth));
                }
            }
        }

        var dedup = all
            .DistinctBy(c => (c.CenterHz, c.BandwidthHz))
            .OrderBy(c => c.CenterHz)
            .ThenBy(c => c.BandwidthHz)
            .ToList();

        if (maxCandidates > 0 && dedup.Count > maxCandidates)
        {
            return dedup.Take(maxCandidates).ToList();
        }

        return dedup;
    }

    private static AttemptMetrics ComputeAttemptMetrics(IqSamples iq, IReadOnlyCollection<BurstInfo> bursts)
    {
        var data = iq.Data ?? [];
        if (data.Length < 2)
        {
            return new AttemptMetrics(0, 0, -120, -120, -120, 0, bursts.Count);
        }

        int complexCount = data.Length / 2;
        var powers = new double[complexCount];
        var sumAbs = 0.0;

        for (int i = 0; i < complexCount; i++)
        {
            var re = data[i * 2];
            var im = data[(i * 2) + 1];
            var p = ((double)re * re) + ((double)im * im);
            powers[i] = p;
            sumAbs += Math.Abs(re) + Math.Abs(im);
        }

        Array.Sort(powers);
        var noise = powers[(int)(0.10 * (complexCount - 1))];
        var p95 = powers[(int)(0.95 * (complexCount - 1))];
        var peak = powers[complexCount - 1];

        var noiseDb = 10.0 * Math.Log10(Math.Max(noise, 1e-20));
        var p95Db = 10.0 * Math.Log10(Math.Max(p95, 1e-20));
        var peakDb = 10.0 * Math.Log10(Math.Max(peak, 1e-20));
        var peakSnr = peakDb - noiseDb;
        var meanAbs = sumAbs / data.Length;

        return new AttemptMetrics(data.Length, meanAbs, p95Db, peakDb, noiseDb, peakSnr, bursts.Count);
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
    private sealed record ScanProfile(string Tech, string Band, long UplinkStartHz, long UplinkEndHz, long[] BandwidthsHz);
    private sealed record ScanCandidate(string Tech, string Band, long CenterHz, long BandwidthHz);
    private sealed record CaptureAttempt(IqSamples? Samples, string Diagnostic);
    private sealed record AttemptMetrics(int FloatCount, double MeanAbs, double P95Db, double PeakDb, double NoiseFloorDb, double PeakSnrDb, int BurstCount);
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