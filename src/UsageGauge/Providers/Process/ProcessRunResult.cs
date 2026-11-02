namespace UsageGauge.Providers.Process;

/// <summary>
/// Dış süreç çalıştırma sonucunu temsil eder.
/// </summary>
public sealed record ProcessRunResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; }
    public string StandardError { get; init; }
    public bool IsTimeout { get; init; }
    public bool IsSuccess => !IsTimeout && ExitCode == 0;

    public ProcessRunResult(int exitCode, string standardOutput, string standardError, bool isTimeout = false)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput ?? string.Empty;
        StandardError = standardError ?? string.Empty;
        IsTimeout = isTimeout;
    }

    public static ProcessRunResult Timeout(string partialStdout = "", string partialStderr = "")
    {
        return new ProcessRunResult(-1, partialStdout, partialStderr, isTimeout: true);
    }
}
