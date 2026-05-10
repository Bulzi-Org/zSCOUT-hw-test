using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Persistence;

/// <summary>
/// Enforces 30-day data retention boundaries.
/// </summary>
public sealed class RetentionPolicy
{
	private readonly TimeSpan _retentionWindow;

	public RetentionPolicy(IConfiguration config)
	{
		var days = config.GetValue("RetentionDays", 30);
		_retentionWindow = TimeSpan.FromDays(days);
	}

	public DateTimeOffset CutoffUtc => DateTimeOffset.UtcNow - _retentionWindow;

	public bool IsWithinRetention(DateTimeOffset? timestamp)
		=> timestamp.HasValue && timestamp.Value >= CutoffUtc;

	public bool IsWithinRetention(DateTimeOffset from, DateTimeOffset to)
		=> to >= CutoffUtc && from <= DateTimeOffset.UtcNow;

	public void ValidateExportRange(DateTimeOffset from, DateTimeOffset to)
	{
		if (from > to)
			throw new ArgumentException("Export range 'from' must be before 'to'.");
		if (to < CutoffUtc)
			throw new ArgumentException($"Export range is outside the {_retentionWindow.Days}-day retention window.");
	}
}
