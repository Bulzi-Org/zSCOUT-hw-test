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
}
