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

	/// <summary>The most recent qualifying fix (mode≥2, non-zero lat/lon/alt/time).</summary>
	public GpsFix? BestFix { get; private set; }

	/// <summary>The most recent fix update (for satellite visibility data).</summary>
	public GpsFix? LastFix { get; private set; }

	/// <summary>Total number of fix updates received during the session.</summary>
	public int TotalFixUpdates { get; private set; }

	/// <summary>
	/// Integrates a newly received <see cref="GpsFix"/> from the gps-svc SSE stream.
	/// Increments the counter and updates fix state if qualifying.
	/// </summary>
	public void Update(GpsFix fix)
	{
		TotalFixUpdates++;
		LastFix = fix;

		if (IsQualifying(fix))
		{
			FixObtained = true;
			BestFix = fix;
		}
	}

	/// <summary>
	/// Returns true if the fix contains a complete, non-zero GNSS fix:
	/// mode≥2, latitude≠0, longitude≠0, altitude is non-null, and UTC time is non-empty.
	/// Also considers the server-side <see cref="GpsFix.HasQualifyingFix"/> flag.
	/// </summary>
	public static bool IsQualifying(GpsFix fix)
	{
		if (fix.HasQualifyingFix) return true;
		if (fix.Mode < 2) return false;
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
	/// <param name="serviceReachable">Whether gps-svc was confirmed reachable at probe start.</param>
	public Dictionary<string, object?> BuildSnapshot(bool serviceReachable)
	{
		var fix = BestFix;
		var latest = LastFix;
		double? speedKnots = fix?.SpeedMs is double s ? s * MsToKnots : null;

		return new Dictionary<string, object?>
		{
			["gpsdRunning"] = serviceReachable,
			["fixObtained"] = FixObtained,
			["fixQuality"] = (object?)(fix?.Mode ?? 0),
			["latitude"] = fix?.Latitude,
			["longitude"] = fix?.Longitude,
			["altitudeM"] = fix?.AltitudeM,
			["utcTime"] = fix?.UtcTime,
			["satellitesUsed"] = latest?.SatellitesUsed ?? 0,
			["satellitesVisible"] = latest?.SatellitesVisible ?? 0,
			["hdop"] = fix?.Hdop,
			["maxSnrDb"] = latest?.MaxSnrDb,
			["minSnrDb"] = latest?.MinSnrDb,
			["speedKnots"] = speedKnots,
			["totalFixUpdates"] = TotalFixUpdates
		};
	}
}
