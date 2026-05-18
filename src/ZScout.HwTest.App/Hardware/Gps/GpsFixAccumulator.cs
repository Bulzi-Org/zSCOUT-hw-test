namespace ZScout.HwTest.App.Hardware.Gps;

/// <summary>
/// Session-scoped accumulator for GPS fix data during a streaming probe.
/// Tracks whether a qualifying GNSS fix was obtained and aggregates statistics
/// for population of the 14-field GPS <see cref="ZScout.HwTest.Contracts.Models.HealthSnapshot"/>.
/// T016: Data accumulation for fix-based verdict and comprehensive evidence.
/// </summary>
public sealed class GpsFixAccumulator
{
	private const double MsToKnots = 1.94384;

	/// <summary>True once at least one qualifying fix has been observed.</summary>
	public bool FixObtained { get; private set; }

	/// <summary>The most recent qualifying TPV update (mode≥2, non-zero lat/lon/alt/time).</summary>
	public GnssFixUpdate? BestFix { get; private set; }

	/// <summary>The most recent SKY update (satellite view).</summary>
	public GnssFixUpdate? LastSkyUpdate { get; private set; }

	/// <summary>Total number of TPV JSON lines received during the session.</summary>
	public int TotalFixUpdates { get; private set; }

	/// <summary>
	/// Integrates a newly parsed <see cref="GnssFixUpdate"/> into the accumulator.
	/// TPV objects update fix state and increment the counter.
	/// SKY objects update satellite visibility data.
	/// </summary>
	public void Update(GnssFixUpdate fix)
	{
		if (fix.Class == "TPV")
		{
			TotalFixUpdates++;
			if (IsQualifying(fix))
			{
				FixObtained = true;
				BestFix = fix;
			}
		}
		else if (fix.Class == "SKY")
		{
			LastSkyUpdate = fix;
		}
	}

	/// <summary>
	/// Returns true if the TPV update contains a complete, non-zero GNSS fix:
	/// mode≥2, latitude≠0, longitude≠0, altitude is non-null, and UTC time is non-empty.
	/// </summary>
	public static bool IsQualifying(GnssFixUpdate fix)
	{
		if (fix.Class != "TPV") return false;
		if ((fix.Mode ?? 0) < 2) return false;
		if ((fix.Latitude ?? 0.0) == 0.0) return false;
		if ((fix.Longitude ?? 0.0) == 0.0) return false;
		if (fix.AltitudeM is null) return false;
		if (string.IsNullOrEmpty(fix.UtcTime)) return false;
		return true;
	}

	/// <summary>
	/// Builds the 14-field GPS HealthSnapshot dictionary.
	/// All keys are always present; values are null or 0 when not observed.
	/// </summary>
	/// <param name="gpsdRunning">Whether gpsd was confirmed running at probe start.</param>
	public Dictionary<string, object?> BuildSnapshot(bool gpsdRunning)
	{
		var sky = LastSkyUpdate;
		var fix = BestFix;
		double? speedKnots = fix?.SpeedMs is double s ? s * MsToKnots : null;

		return new Dictionary<string, object?>
		{
			["gpsd_running"] = gpsdRunning,
			["fix_obtained"] = FixObtained,
			["fix_quality"] = (object?)(fix?.Mode ?? 0),
			["latitude"] = fix?.Latitude,
			["longitude"] = fix?.Longitude,
			["altitude_m"] = fix?.AltitudeM,
			["utc_time"] = fix?.UtcTime,
			["satellites_used"] = sky?.SatellitesUsed ?? 0,
			["satellites_visible"] = sky?.SatellitesVisible ?? 0,
			["hdop"] = fix?.Hdop,
			["max_snr_db"] = sky?.MaxSnrDb,
			["min_snr_db"] = sky?.MinSnrDb,
			["speed_knots"] = speedKnots,
			["total_fix_updates"] = TotalFixUpdates
		};
	}
}
