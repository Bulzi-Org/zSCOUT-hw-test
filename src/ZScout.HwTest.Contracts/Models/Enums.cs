namespace ZScout.HwTest.Contracts.Models;

public enum PeripheralId { Gps, Sdr, Halow, Compass }

public enum PeripheralTransport { Usb, I2c, Pcie, Net }

public enum PeripheralStatus { Unknown, Ready, Degraded, Unavailable }

public enum RunMode { Host, Container }

public enum RunStatus
{
	Queued,
	Running,
	AwaitingVerdict,
	Completed,
	Failed,
	Stopped,
	Rejected
}

public enum VerdictOutcome { Pass, Fail }

public enum OverallOutcome { Pass, Fail, Inconclusive }

public enum StreamType { GpsNmea, SdrInfo, HalowMetrics, CompassHeading }

public enum ExportFormat { ZipJson }

public enum ExportStatus { Queued, Ready, Failed }
