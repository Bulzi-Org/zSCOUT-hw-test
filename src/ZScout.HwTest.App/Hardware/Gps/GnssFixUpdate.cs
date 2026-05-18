using System.Text.Json;

namespace ZScout.HwTest.App.Hardware.Gps;

/// <summary>
/// Immutable record representing one parsed gpsd JSON line (TPV or SKY class).
/// T016: GPS streaming data model for fix accumulation and HealthSnapshot population.
/// </summary>
public sealed record GnssFixUpdate
{
	/// <summary>gpsd JSON class name: "TPV", "SKY", or other.</summary>
	public required string Class { get; init; }

	// ── TPV fields ────────────────────────────────────────────────────────────

	/// <summary>Fix mode: 0=unknown, 1=no-fix, 2=2D, 3=3D.</summary>
	public int? Mode { get; init; }

	/// <summary>Latitude in decimal degrees. Null if not available.</summary>
	public double? Latitude { get; init; }

	/// <summary>Longitude in decimal degrees. Null if not available.</summary>
	public double? Longitude { get; init; }

	/// <summary>Altitude above MSL in metres. Null if not available.</summary>
	public double? AltitudeM { get; init; }

	/// <summary>UTC timestamp string from gpsd (ISO-8601). Null if not available.</summary>
	public string? UtcTime { get; init; }

	/// <summary>Speed over ground in m/s. Null if not reported.</summary>
	public double? SpeedMs { get; init; }

	/// <summary>Track/heading in degrees true. Null if not reported.</summary>
	public double? Track { get; init; }

	/// <summary>Horizontal dilution of precision. Null if not reported.</summary>
	public double? Hdop { get; init; }

	// ── SKY / satellite fields ────────────────────────────────────────────────

	/// <summary>Number of satellites used in fix (from SKY.satellites where used=true).</summary>
	public int SatellitesUsed { get; init; }

	/// <summary>Total number of satellites in view (from SKY.satellites count).</summary>
	public int SatellitesVisible { get; init; }

	/// <summary>Maximum satellite signal strength in dBHz from the SKY update. Null if no satellites.</summary>
	public int? MaxSnrDb { get; init; }

	/// <summary>Minimum non-zero satellite signal strength in dBHz from the SKY update. Null if no satellites.</summary>
	public int? MinSnrDb { get; init; }
}

/// <summary>
/// Parser for gpsd JSON lines produced by <c>gpspipe -w</c>.
/// Handles TPV and SKY class objects; returns null for all other lines.
/// Never throws — malformed input returns null.
/// </summary>
public static class GnssJsonParser
{

	/// <summary>
	/// Parses a single gpsd JSON line into a <see cref="GnssFixUpdate"/>.
	/// Returns null if the line is not parseable or not a recognised class.
	/// </summary>
	public static GnssFixUpdate? Parse(string line)
	{
		if (string.IsNullOrWhiteSpace(line))
			return null;

		try
		{
			using var doc = JsonDocument.Parse(line);
			var root = doc.RootElement;

			if (!root.TryGetProperty("class", out var classEl))
				return null;

			var cls = classEl.GetString();
			return cls switch
			{
				"TPV" => ParseTpv(root),
				"SKY" => ParseSky(root),
				_ => null
			};
		}
		catch
		{
			// Malformed JSON or unexpected structure — skip silently
			return null;
		}
	}

	private static GnssFixUpdate ParseTpv(JsonElement root)
	{
		return new GnssFixUpdate
		{
			Class = "TPV",
			Mode = TryGetInt(root, "mode"),
			Latitude = TryGetDouble(root, "lat"),
			Longitude = TryGetDouble(root, "lon"),
			AltitudeM = TryGetDouble(root, "alt"),
			UtcTime = TryGetString(root, "time"),
			SpeedMs = TryGetDouble(root, "speed"),
			Track = TryGetDouble(root, "track"),
			Hdop = TryGetDouble(root, "hdop")
		};
	}

	private static GnssFixUpdate ParseSky(JsonElement root)
	{
		var satellites = new List<(bool Used, int Ss)>();

		if (root.TryGetProperty("satellites", out var satsEl) &&
			satsEl.ValueKind == JsonValueKind.Array)
		{
			foreach (var sat in satsEl.EnumerateArray())
			{
				var used = sat.TryGetProperty("used", out var usedEl) && usedEl.GetBoolean();
				var ss = sat.TryGetProperty("ss", out var ssEl)
					? ssEl.GetInt32()
					: 0;
				satellites.Add((used, ss));
			}
		}

		var usedCount = satellites.Count(s => s.Used);
		var visibleCount = satellites.Count;
		var snrValues = satellites.Where(s => s.Ss > 0).Select(s => s.Ss).ToList();

		return new GnssFixUpdate
		{
			Class = "SKY",
			SatellitesUsed = usedCount,
			SatellitesVisible = visibleCount,
			MaxSnrDb = snrValues.Count > 0 ? snrValues.Max() : null,
			MinSnrDb = snrValues.Count > 0 ? snrValues.Min() : null
		};
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private static double? TryGetDouble(JsonElement el, string name)
	{
		if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number)
			return prop.GetDouble();
		return null;
	}

	private static int? TryGetInt(JsonElement el, string name)
	{
		if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number)
			return prop.GetInt32();
		return null;
	}

	private static string? TryGetString(JsonElement el, string name)
	{
		if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
			return prop.GetString();
		return null;
	}
}
