using System.Diagnostics;
using System.Text.Json;

namespace PurchaseMemory.Infrastructure.Recognition;

/// <summary>
/// Runs the Apple sidecar binary and reads its single JSON document from stdout.
/// Shared by every stage that talks to the sidecar (<c>extract</c> today,
/// <c>geometry</c> for the OCR-rows route) so the timeout, the kill-on-timeout
/// behaviour and the error-reading rules cannot drift apart between them.
/// </summary>
internal static class AppleSidecarProcess
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string ResolvePath(string configuredPath) =>
        Path.IsPathFullyQualified(configuredPath)
            ? configuredPath
            : Path.GetFullPath(configuredPath, Directory.GetCurrentDirectory());

    public static async Task<SidecarProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("The Apple receipt sidecar process could not be started.");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linkedSource.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            // Both reads are already running; killing the process ends them, but they
            // must still be observed or their faults become unobserved task exceptions.
            await ObserveAsync(standardOutput, standardError);
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }

        return new SidecarProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static async Task ObserveAsync(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The stream faults are diagnostics for a process we already killed;
            // the caller gets the timeout, which is the real cause.
        }
    }

    /// <summary>
    /// Reads the sidecar's own error message. The sidecar keeps stdout as JSON
    /// even when it fails, but a startup failure can happen before its encoder
    /// runs, so stderr is the fallback.
    /// </summary>
    public static string ReadError(SidecarProcessResult result)
    {
        try
        {
            var error = JsonSerializer.Deserialize<AppleSidecarError>(result.StandardOutput, JsonOptions)?.Error;
            if (!string.IsNullOrWhiteSpace(error))
            {
                return error;
            }
        }
        catch (JsonException)
        {
            // Fall through to stderr.
        }

        return string.IsNullOrWhiteSpace(result.StandardError)
            ? "Apple receipt sidecar exited without a diagnostic message."
            : result.StandardError.Trim();
    }
}

internal sealed record SidecarProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal sealed class AppleSidecarError
{
    public string? Error { get; init; }
}
