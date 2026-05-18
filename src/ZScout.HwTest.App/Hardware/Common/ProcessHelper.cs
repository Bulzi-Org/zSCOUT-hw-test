using System.Diagnostics;
using ZScout.HwTest.Contracts.Models;

namespace ZScout.HwTest.App.Hardware.Common;

/// <summary>
/// Shared helper for running native tool subprocesses with stdout/stderr capture.
/// </summary>
public static class ProcessHelper
{
	public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

	public static async Task<ProcessResult> RunAsync(
		string executable,
		string arguments,
		int timeoutMs = 10_000,
		CancellationToken ct = default)
	{
		using var process = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = executable,
				Arguments = arguments,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			}
		};

		process.Start();

		using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(timeoutMs);

		try
		{
			var stdout = await process.StandardOutput.ReadToEndAsync(cts.Token);
			var stderr = await process.StandardError.ReadToEndAsync(cts.Token);
			await process.WaitForExitAsync(cts.Token);
			return new ProcessResult(process.ExitCode, stdout, stderr);
		}
		catch (OperationCanceledException)
		{
			try { process.Kill(entireProcessTree: true); } catch { }
			return new ProcessResult(-1, string.Empty, "Process timed out or was cancelled.");
		}
	}

	/// <summary>
	/// Starts <paramref name="executable"/> and reads its stdout line by line,
	/// invoking <paramref name="lineHandler"/> for each non-empty line until the
	/// <paramref name="ct"/> is cancelled or the process exits.
	/// The process is always killed on exit (cancellation or natural EOF).
	/// </summary>
	/// <param name="executable">The executable to run.</param>
	/// <param name="arguments">Command-line arguments.</param>
	/// <param name="lineHandler">Async callback invoked for each line of stdout.</param>
	/// <param name="ct">Cancellation token; cancellation terminates the process.</param>
	/// <returns>The process stderr output (for diagnostics).</returns>
	public static async Task<string> StreamLinesAsync(
		string executable,
		string arguments,
		Func<string, CancellationToken, Task> lineHandler,
		CancellationToken ct = default)
	{
		using var process = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = executable,
				Arguments = arguments,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			}
		};

		process.Start();

		// Drain stderr asynchronously so it does not block stdout reads
		var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

		try
		{
			while (!ct.IsCancellationRequested)
			{
				var line = await process.StandardOutput.ReadLineAsync(ct);
				if (line is null) break; // process exited / EOF
				if (!string.IsNullOrWhiteSpace(line))
					await lineHandler(line, ct);
			}
		}
		catch (OperationCanceledException)
		{
			// Expected on Stop — fall through to kill
		}
		finally
		{
			try { process.Kill(entireProcessTree: true); } catch { }
		}

		return await stderrTask;
	}
}
