namespace ZScout.HwTest.App.Hardware.Sdr;

// TODO(zSCOUT-common#10): once shared Sdr contracts land in ZScout.Common, replace
// these local records with the canonical types from ZScout.Common.Sdr and remove
// this file.

/// <summary>
/// RX receiver configuration passed to POST /api/rx/configure on sdr-svc.
/// Mirrors the RxConfig contract defined in sdr-svc #26; will be superseded
/// by ZScout.Common.Sdr.RxConfig when zSCOUT-common#10 is merged.
/// </summary>
public sealed record SdrRxConfig(
    long CenterFreqHz,
    long SampleRateHz,
    Dictionary<string, double>? Gains = null);

/// <summary>
/// A block of complex IQ samples returned by GET /api/rx/samples on sdr-svc.
/// Each pair of floats in <see cref="Data"/> is one I+Q sample (interleaved).
/// Will be superseded by ZScout.Common.Sdr.SdrIqSamples when zSCOUT-common#10 is merged.
/// </summary>
public sealed record SdrIqSampleBlock(
    long CenterFreqHz,
    long SampleRateHz,
    float[] Data);
