using System;
using System.Threading;
using System.Threading.Tasks;
using UsageGauge.Providers.Process;
using Xunit;

namespace UsageGauge.Tests.Providers;

public class StandardProcessRunnerTests
{
    [Fact]
    public async Task RunProcessAsync_WithHeavyStderr_DrainsWithoutDeadlocking()
    {
        var runner = new StandardProcessRunner();
        // 70.000 karakterlik stderr çıktısı (Windows varsayılan 4KB-64KB boru tamponunu doldurur)
        string[] args = ["-NoProfile", "-Command", "[Console]::Error.Write('E' * 70000); [Console]::Out.WriteLine('OK')"];

        ProcessRunResult result = await runner.RunProcessAsync(
            "powershell.exe",
            args,
            TimeSpan.FromSeconds(10));

        Assert.False(result.IsTimeout);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("OK", result.StandardOutput);
        Assert.True(result.StandardError.Length >= 70000);
    }

    [Fact]
    public async Task RunStdioSessionAsync_WithHeavyStderrBeforeStdout_ReadsResponseWithoutDeadlock()
    {
        var runner = new StandardProcessRunner();
        // Süreç: Stdin'den satır oku -> Stderr'e 80.000 karakter bas (boru tamponunu taşır) -> Stdout'a yanıt satırı yaz -> Stdin kapanana kadar bekle
        string script = "$line = [Console]::In.ReadLine(); [Console]::Error.Write('E' * 80000); [Console]::Out.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":\"pong\"}'); $null = [Console]::In.ReadLine();";
        string[] args = ["-NoProfile", "-Command", script];

        string response = await runner.RunStdioSessionAsync<string>(
            "powershell.exe",
            args,
            async session =>
            {
                await session.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}");
                string? line = await session.ReadLineAsync();
                return line ?? string.Empty;
            },
            timeout: TimeSpan.FromSeconds(10),
            gracefulExitWait: TimeSpan.FromSeconds(2));

        Assert.Contains("pong", response);
    }

    [Fact]
    public async Task RunProcessAsync_WhenProcessExceedsTimeout_KillsProcessAndReturnsTimeout()
    {
        var runner = new StandardProcessRunner();
        string[] args = ["-NoProfile", "-Command", "Start-Sleep -Seconds 10"];

        ProcessRunResult result = await runner.RunProcessAsync(
            "powershell.exe",
            args,
            TimeSpan.FromMilliseconds(600));

        Assert.True(result.IsTimeout);
    }

    [Fact]
    public async Task RunProcessAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var runner = new StandardProcessRunner();
        string[] args = ["-NoProfile", "-Command", "Start-Sleep -Seconds 10"];

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await runner.RunProcessAsync(
                "powershell.exe",
                args,
                TimeSpan.FromSeconds(10),
                cts.Token);
        });
    }

    [Fact]
    public async Task RunStdioSessionAsync_WhenSessionHandlerTimesOut_ThrowsTimeoutExceptionAndCleansUp()
    {
        var runner = new StandardProcessRunner();
        string[] args = ["-NoProfile", "-Command", "Start-Sleep -Seconds 10"];

        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await runner.RunStdioSessionAsync<string>(
                "powershell.exe",
                args,
                async session =>
                {
                    // Uzun süre bloklanacak bir işlem simülasyonu
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    return "bitti";
                },
                timeout: TimeSpan.FromMilliseconds(500),
                gracefulExitWait: TimeSpan.FromMilliseconds(200));
        });
    }
}
