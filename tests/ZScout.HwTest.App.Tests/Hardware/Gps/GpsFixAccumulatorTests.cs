using Xunit;
using ZScout.HwTest.App.Hardware.Gps;

namespace ZScout.HwTest.App.Tests.Hardware.Gps;

/// <summary>
/// Unit tests for <see cref="GpsFixAccumulator"/>.
/// Verifies fix qualification, session accumulation, and HealthSnapshot construction.
/// </summary>
public sealed class GpsFixAccumulatorTests
{
	// ── IsQualifying ──────────────────────────────────────────────────────────

	[Fact]
	public void IsQualifying_Mode3WithFullData_ReturnsTrue()
	{
		var fix = new GnssFixUpdate
		{
			Class = "TPV",
			Mode = 3,
			Latitude = 37.1,
			Longitude = -122.1,
			AltitudeM = 10.0,
			UtcTime = "2026-05-18T00:00:00Z"
		};

		Assert.True(GpsFixAccumulator.IsQualifying(fix));
	}

	[Fact]
	public void IsQualifying_Mode1_ReturnsFalse()
	{
		var fix = new GnssFixUpdate
		{
			Class = "TPV",
			Mode = 1,
			Latitude = 37.1,
			Longitude = -122.1,
			AltitudeM = 10.0,
			UtcTime = "2026-05-18T00:00:00Z"
		};

		Assert.False(GpsFixAccumulator.IsQualifying(fix));
	}

	[Fact]
	public void IsQualifying_ZeroLatitude_ReturnsFalse()
	{
		var fix = new GnssFixUpdate
		{
			Class = "TPV",
			Mode = 3,
			Latitude = 0.0,
			Longitude = -122.1,
			AltitudeM = 10.0,
			UtcTime = "2026-05-18T00:00:00Z"
		};

		Assert.False(GpsFixAccumulator.IsQualifying(fix));
	}

	[Fact]
	public void IsQualifying_NullAltitude_ReturnsFalse()
	{
		var fix = new GnssFixUpdate
		{
			Class = "TPV",
			Mode = 2,
			Latitude = 37.1,
			Longitude = -122.1,
			AltitudeM = null, // 2D fix — no altitude
			UtcTime = "2026-05-18T00:00:00Z"
		};

		Assert.False(GpsFixAccumulator.IsQualifying(fix));
	}

	[Fact]
	public void IsQualifying_NullUtcTime_ReturnsFalse()
	{
		var fix = new GnssFixUpdate
		{
			Class = "TPV",
			Mode = 3,
			Latitude = 37.1,
			Longitude = -122.1,
			AltitudeM = 10.0,
			UtcTime = null
		};

		Assert.False(GpsFixAccumulator.IsQualifying(fix));
	}

	[Fact]
	public void IsQualifying_SkyClass_ReturnsFalse()
	{
		var fix = new GnssFixUpdate { Class = "SKY" };

		Assert.False(GpsFixAccumulator.IsQualifying(fix));
	}

	// ── Update / accumulation ─────────────────────────────────────────────────

	[Fact]
	public void Update_MultipleNoFixTpvs_FixObtainedFalse_CountCorrect()
	{
		var acc = new GpsFixAccumulator();
		for (var i = 0; i < 5; i++)
		{
			acc.Update(new GnssFixUpdate { Class = "TPV", Mode = 1 });
		}

		Assert.False(acc.FixObtained);
		Assert.Equal(5, acc.TotalFixUpdates);
		Assert.Null(acc.BestFix);
	}

	[Fact]
	public void Update_QualifyingTpv_FixObtainedTrue_BestFixSet()
	{
		var acc = new GpsFixAccumulator();
		var fix = new GnssFixUpdate
		{
			Class = "TPV",
			Mode = 3,
			Latitude = 37.1,
			Longitude = -122.1,
			AltitudeM = 10.0,
			UtcTime = "2026-05-18T00:00:00Z"
		};

		acc.Update(fix);

		Assert.True(acc.FixObtained);
		Assert.Equal(1, acc.TotalFixUpdates);
		Assert.Same(fix, acc.BestFix);
	}

	[Fact]
	public void Update_SkyUpdate_LastSkyUpdateSet()
	{
		var acc = new GpsFixAccumulator();
		var sky = new GnssFixUpdate
		{
			Class = "SKY",
			SatellitesUsed = 8,
			SatellitesVisible = 12,
			MaxSnrDb = 42,
			MinSnrDb = 18
		};

		acc.Update(sky);

		Assert.Same(sky, acc.LastSkyUpdate);
		Assert.Equal(0, acc.TotalFixUpdates); // SKY does not increment TPV counter
	}

	[Fact]
	public void Update_QualifyingTpvThenNewQualifying_BestFixUpdated()
	{
		var acc = new GpsFixAccumulator();
		var first = new GnssFixUpdate { Class = "TPV", Mode = 2, Latitude = 37.0, Longitude = -122.0, AltitudeM = 5.0, UtcTime = "2026-05-18T00:00:00Z" };
		var second = new GnssFixUpdate { Class = "TPV", Mode = 3, Latitude = 37.1, Longitude = -122.1, AltitudeM = 10.0, UtcTime = "2026-05-18T00:01:00Z" };

		acc.Update(first);
		acc.Update(second);

		Assert.True(acc.FixObtained);
		Assert.Equal(2, acc.TotalFixUpdates);
		Assert.Same(second, acc.BestFix); // most recent qualifying fix wins
	}

	// ── BuildSnapshot ─────────────────────────────────────────────────────────

	[Fact]
	public void BuildSnapshot_WithFix_AllKeysPresent_WithValues()
	{
		var acc = new GpsFixAccumulator();
		acc.Update(new GnssFixUpdate
		{
			Class = "SKY",
			SatellitesUsed = 8,
			SatellitesVisible = 12,
			MaxSnrDb = 42,
			MinSnrDb = 18
		});
		acc.Update(new GnssFixUpdate
		{
			Class = "TPV",
			Mode = 3,
			Latitude = 37.123456,
			Longitude = -122.654321,
			AltitudeM = 42.5,
			UtcTime = "2026-05-18T00:45:00Z",
			SpeedMs = 0.1,
			Hdop = 1.2
		});

		var snapshot = acc.BuildSnapshot(gpsdRunning: true);

		// All 14 keys must be present
		Assert.Equal(14, snapshot.Count);
		Assert.Equal(true, snapshot["gpsd_running"]);
		Assert.Equal(true, snapshot["fix_obtained"]);
		Assert.Equal(3, snapshot["fix_quality"]);
		Assert.Equal(37.123456, snapshot["latitude"]);
		Assert.Equal(-122.654321, snapshot["longitude"]);
		Assert.Equal(42.5, snapshot["altitude_m"]);
		Assert.Equal("2026-05-18T00:45:00Z", snapshot["utc_time"]);
		Assert.Equal(8, snapshot["satellites_used"]);
		Assert.Equal(12, snapshot["satellites_visible"]);
		Assert.Equal(1.2, snapshot["hdop"]);
		Assert.Equal(42, snapshot["max_snr_db"]);
		Assert.Equal(18, snapshot["min_snr_db"]);
		Assert.NotNull(snapshot["speed_knots"]); // 0.1 m/s * 1.94384 ≈ 0.194
		Assert.Equal(1, snapshot["total_fix_updates"]);
	}

	[Fact]
	public void BuildSnapshot_WithoutFix_AllKeysPresent_NullOrZeroDefaults()
	{
		var acc = new GpsFixAccumulator();
		// No updates at all

		var snapshot = acc.BuildSnapshot(gpsdRunning: true);

		Assert.Equal(14, snapshot.Count);
		Assert.Equal(true, snapshot["gpsd_running"]);
		Assert.Equal(false, snapshot["fix_obtained"]);
		Assert.Equal(0, snapshot["fix_quality"]);
		Assert.Null(snapshot["latitude"]);
		Assert.Null(snapshot["longitude"]);
		Assert.Null(snapshot["altitude_m"]);
		Assert.Null(snapshot["utc_time"]);
		Assert.Equal(0, snapshot["satellites_used"]);
		Assert.Equal(0, snapshot["satellites_visible"]);
		Assert.Null(snapshot["hdop"]);
		Assert.Null(snapshot["max_snr_db"]);
		Assert.Null(snapshot["min_snr_db"]);
		Assert.Null(snapshot["speed_knots"]);
		Assert.Equal(0, snapshot["total_fix_updates"]);
	}

	[Fact]
	public void BuildSnapshot_GpsdNotRunning_GpsdRunningFalse()
	{
		var acc = new GpsFixAccumulator();

		var snapshot = acc.BuildSnapshot(gpsdRunning: false);

		Assert.Equal(false, snapshot["gpsd_running"]);
		Assert.Equal(false, snapshot["fix_obtained"]);
	}

	[Fact]
	public void BuildSnapshot_SpeedKnots_ConvertedCorrectly()
	{
		var acc = new GpsFixAccumulator();
		acc.Update(new GnssFixUpdate
		{
			Class = "TPV",
			Mode = 3,
			Latitude = 37.1,
			Longitude = -122.1,
			AltitudeM = 10.0,
			UtcTime = "2026-05-18T00:00:00Z",
			SpeedMs = 1.0 // 1 m/s = 1.94384 knots
		});

		var snapshot = acc.BuildSnapshot(gpsdRunning: true);

		var knots = Assert.IsType<double>(snapshot["speed_knots"]);
		Assert.Equal(1.94384, knots, precision: 4);
	}
}
