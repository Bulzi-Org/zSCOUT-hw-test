using Xunit;
using ZScout.HwTest.App.Hardware.Gps;

namespace ZScout.HwTest.App.Tests.Hardware.Gps;

/// <summary>
/// Unit tests for <see cref="GnssJsonParser"/>.
/// Verifies parsing of TPV and SKY gpsd JSON objects,
/// and graceful handling of malformed or unrecognised lines.
/// </summary>
public sealed class GnssFixParserTests
{
	// ── TPV parsing ───────────────────────────────────────────────────────────

	[Fact]
	public void Parse_WellFormedTpv_AllFieldsPopulated()
	{
		const string line = """{"class":"TPV","mode":3,"time":"2026-05-18T00:45:00.000Z","lat":37.123456,"lon":-122.654321,"alt":42.5,"speed":0.05,"track":180.0,"hdop":1.2}""";

		var result = GnssJsonParser.Parse(line);

		Assert.NotNull(result);
		Assert.Equal("TPV", result.Class);
		Assert.Equal(3, result.Mode);
		Assert.Equal(37.123456, result.Latitude!.Value, precision: 6);
		Assert.Equal(-122.654321, result.Longitude!.Value, precision: 6);
		Assert.Equal(42.5, result.AltitudeM!.Value, precision: 2);
		Assert.Equal("2026-05-18T00:45:00.000Z", result.UtcTime);
		Assert.Equal(0.05, result.SpeedMs!.Value, precision: 3);
		Assert.Equal(180.0, result.Track!.Value, precision: 1);
		Assert.Equal(1.2, result.Hdop!.Value, precision: 2);
	}

	[Fact]
	public void Parse_TpvWithMissingOptionalFields_NullablesAreNull()
	{
		const string line = """{"class":"TPV","mode":1}""";

		var result = GnssJsonParser.Parse(line);

		Assert.NotNull(result);
		Assert.Equal("TPV", result.Class);
		Assert.Equal(1, result.Mode);
		Assert.Null(result.Latitude);
		Assert.Null(result.Longitude);
		Assert.Null(result.AltitudeM);
		Assert.Null(result.UtcTime);
		Assert.Null(result.SpeedMs);
		Assert.Null(result.Hdop);
	}

	[Fact]
	public void Parse_TpvMode1_DoesNotQualifyAsAFix()
	{
		const string line = """{"class":"TPV","mode":1,"lat":37.1,"lon":-122.1,"alt":10.0,"time":"2026-05-18T00:00:00Z"}""";

		var result = GnssJsonParser.Parse(line);

		Assert.NotNull(result);
		Assert.False(GpsFixAccumulator.IsQualifying(result));
	}

	[Fact]
	public void Parse_TpvMode3FullFix_Qualifies()
	{
		const string line = """{"class":"TPV","mode":3,"lat":37.1,"lon":-122.1,"alt":10.0,"time":"2026-05-18T00:00:00Z"}""";

		var result = GnssJsonParser.Parse(line);

		Assert.NotNull(result);
		Assert.True(GpsFixAccumulator.IsQualifying(result));
	}

	// ── SKY parsing ───────────────────────────────────────────────────────────

	[Fact]
	public void Parse_SkyWithSatellites_CountsAndSnrCorrect()
	{
		const string line = """{"class":"SKY","satellites":[{"PRN":5,"used":true,"ss":42},{"PRN":12,"used":false,"ss":18},{"PRN":7,"used":true,"ss":35}]}""";

		var result = GnssJsonParser.Parse(line);

		Assert.NotNull(result);
		Assert.Equal("SKY", result.Class);
		Assert.Equal(2, result.SatellitesUsed);
		Assert.Equal(3, result.SatellitesVisible);
		Assert.Equal(42, result.MaxSnrDb);
		Assert.Equal(18, result.MinSnrDb);
	}

	[Fact]
	public void Parse_SkyWithNoSatellites_ZeroCountsAndNullSnr()
	{
		const string line = """{"class":"SKY","satellites":[]}""";

		var result = GnssJsonParser.Parse(line);

		Assert.NotNull(result);
		Assert.Equal(0, result.SatellitesUsed);
		Assert.Equal(0, result.SatellitesVisible);
		Assert.Null(result.MaxSnrDb);
		Assert.Null(result.MinSnrDb);
	}

	[Fact]
	public void Parse_SkyWithMissingSatellitesArray_ZeroCountsAndNullSnr()
	{
		const string line = """{"class":"SKY"}""";

		var result = GnssJsonParser.Parse(line);

		Assert.NotNull(result);
		Assert.Equal(0, result.SatellitesUsed);
		Assert.Equal(0, result.SatellitesVisible);
	}

	// ── Error handling ────────────────────────────────────────────────────────

	[Fact]
	public void Parse_MalformedJson_ReturnsNull()
	{
		const string line = "not-json-at-all{broken";

		var result = GnssJsonParser.Parse(line);

		Assert.Null(result);
	}

	[Fact]
	public void Parse_EmptyString_ReturnsNull()
	{
		var result = GnssJsonParser.Parse(string.Empty);

		Assert.Null(result);
	}

	[Fact]
	public void Parse_NullWhitespace_ReturnsNull()
	{
		var result = GnssJsonParser.Parse("   ");

		Assert.Null(result);
	}

	[Fact]
	public void Parse_NonTpvSkyClass_ReturnsNull()
	{
		const string line = """{"class":"VERSION","release":"3.24"}""";

		var result = GnssJsonParser.Parse(line);

		Assert.Null(result);
	}

	[Fact]
	public void Parse_JsonWithoutClassField_ReturnsNull()
	{
		const string line = """{"mode":3,"lat":37.1}""";

		var result = GnssJsonParser.Parse(line);

		Assert.Null(result);
	}

	[Fact]
	public void Parse_SkyWithSsZero_ExcludedFromMinSnr()
	{
		// A satellite with ss=0 should be excluded from min SNR calculation
		const string line = """{"class":"SKY","satellites":[{"PRN":5,"used":true,"ss":40},{"PRN":12,"used":false,"ss":0}]}""";

		var result = GnssJsonParser.Parse(line);

		Assert.NotNull(result);
		Assert.Equal(40, result.MaxSnrDb);
		Assert.Equal(40, result.MinSnrDb); // ss=0 excluded from min
	}
}
