using System.Text.Json.Serialization;

namespace ZScout.HwTest.App.Hardware.Gps;

/// <summary>
/// Immutable record representing a GPS fix from the gps-svc REST API.
/// Deserialized from GET /api/fix (snapshot) and GET /api/stream/fixes (SSE stream).
/// Replaces the internal <c>GnssFixUpdate</c> model that was specific to gpsd JSON output.
/// </summary>
public sealed record GpsFix
{
	/// <summary>Fix mode: 0=unknown, 1=no-fix, 2=2D, 3=3D.</summary>
	[JsonPropertyName("mode")]
	public int Mode { get; init; }

	/// <summary>Latitude in decimal degrees. Null if not available.</summary>
	[JsonPropertyName("latitude")]
	public double? Latitude { get; init; }

	/// <summary>Longitude in decimal degrees. Null if not available.</summary>
	[JsonPropertyName("longitude")]
	public double? Longitude { get; init; }

	/// <summary>Altitude above MSL in metres. Null if not available.</summary>
	[JsonPropertyName("altitudeM")]
	public double? AltitudeM { get; init; }

	/// <summary>UTC timestamp string (ISO-8601). Null if not available.</summary>
	[JsonPropertyName("utcTime")]
	public string? UtcTime { get; init; }

	/// <summary>Speed over ground in m/s. Null if not reported.</summary>
	[JsonPropertyName("speedMs")]
	public double? SpeedMs { get; init; }

	/// <summary>Track/heading in degrees true. Null if not reported.</summary>
	[JsonPropertyName("track")]
	public double? Track { get; init; }

	/// <summary>Horizontal dilution of precision. Null if not reported.</summary>
	[JsonPropertyName("hdop")]
	public double? Hdop { get; init; }

	/// <summary>Number of satellites used in the fix.</summary>
	[JsonPropertyName("satellitesUsed")]
	public int SatellitesUsed { get; init; }

	/// <summary>Total number of satellites in view.</summary>
	[JsonPropertyName("satellitesVisible")]
	public int SatellitesVisible { get; init; }

	/// <summary>Maximum satellite signal strength in dBHz. Null if no satellites.</summary>
	[JsonPropertyName("maxSnrDb")]
	public int? MaxSnrDb { get; init; }

	/// <summary>Minimum non-zero satellite signal strength in dBHz. Null if no satellites.</summary>
	[JsonPropertyName("minSnrDb")]
	public int? MinSnrDb { get; init; }

	/// <summary>
	/// Server-side qualifying fix determination: mode≥2, non-zero lat/lon/alt, valid time.
	/// </summary>
	[JsonPropertyName("hasQualifyingFix")]
	public bool HasQualifyingFix { get; init; }
}
