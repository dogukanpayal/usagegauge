using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UsageGauge.Providers.Process;

/// <summary>
/// Güvenli, kilitlenmeyen ve iptal/zaman aşımı uyumlu standart dış süreç yöneticisi.
/// ProcessStartInfo.ArgumentList kullanır; süreç ağacını güvenle sonlandırır.
/// </summary>
public sealed class StandardProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new System.Diagnostics.Process { StartInfo = psi };

        try
        {
            if (!process.Start())
            {
                return new ProcessRunResult(-1, string.Empty, "Süreç başlatılamadı.");
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2) // Dosya bulunamadı
        {
            throw new FileNotFoundException($"Belirtilen CLI bulunamadı: {fileName}", fileName, ex);
        }

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);
        Task exitTask = process.WaitForExitAsync(linkedCts.Token);

        Task completedTask = await Task.WhenAny(exitTask, Task.Delay(timeout, linkedCts.Token));

        if (completedTask != exitTask || timeoutCts.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
            TryKillProcessTree(process);

            if (cancellationToken.IsCancellationRequested && !timeoutCts.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            string partialOut = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty;
            string partialErr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty;
            return ProcessRunResult.Timeout(partialOut, partialErr);
        }

        await Task.WhenAll(stdoutTask, stderrTask);
        return new ProcessRunResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    public async Task<T> RunStdioSessionAsync<T>(
        string fileName,
        IReadOnlyList<string> arguments,
        Func<IStdioSession, Task<T>> sessionHandler,
        TimeSpan timeout,
        TimeSpan gracefulExitWait,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new System.Diagnostics.Process { StartInfo = psi };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("stdio süreci başlatılamadı.");
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            throw new FileNotFoundException($"Belirtilen CLI bulunamadı: {fileName}", fileName, ex);
        }

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        // Stderr akışını süreç başlar başlamaz eşzamansız boşalt (Windows boru belleği taşmasını ve deadlock'u önle)
        _ = Task.Run(async () =>
        {
            try
            {
                char[] buffer = new char[4096];
                while (!linkedCts.Token.IsCancellationRequested)
                {
                    int read = await process.StandardError.ReadAsync(buffer.AsMemory(), linkedCts.Token);
                    if (read == 0) break;
                }
            }
            catch
            {
                // Süreç sonlandırıldığında veya iptal edildiğinde akış kapanması normaldir
            }
        }, CancellationToken.None);

        var session = new StdioProcessSession(process, linkedCts.Token);

        try
        {
            // 15 saniyelik toplam süre sınırını sessionHandler çalışırken doğrudan zorla
            Task<T> handlerTask = sessionHandler(session);
            Task delayTask = Task.Delay(timeout, linkedCts.Token);

            Task completedTask = await Task.WhenAny(handlerTask, delayTask);

            if (completedTask != handlerTask)
            {
                TryKillProcessTree(process);

                if (cancellationToken.IsCancellationRequested && !timeoutCts.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                throw new TimeoutException($"stdio oturumu {timeout.TotalSeconds:F0} saniyelik zaman aşımına uğradı.");
            }

            T result = await handlerTask;

            // Başarılı yanıt sonrasında stdin'i kapat ve en fazla gracefulExitWait (2 sn) düzgün çıkışı bekle
            await session.CloseInputAsync();

            try
            {
                using var exitCts = new CancellationTokenSource(gracefulExitWait);
                using var exitLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(exitCts.Token, cancellationToken);
                await process.WaitForExitAsync(exitLinkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                TryKillProcessTree(process);
            }
            finally
            {
                TryKillProcessTree(process);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !timeoutCts.IsCancellationRequested)
        {
            TryKillProcessTree(process);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            throw new TimeoutException($"stdio oturumu {timeout.TotalSeconds:F0} saniyelik zaman aşımına uğradı.");
        }
        catch
        {
            TryKillProcessTree(process);
            throw;
        }
    }

    private static void TryKillProcessTree(System.Diagnostics.Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Süreç zaten kapanmışsa veya yetki hatasında yutulur
        }
    }

    private sealed class StdioProcessSession : IStdioSession
    {
        private readonly System.Diagnostics.Process _process;
        private readonly StreamWriter _writer;
        private readonly StreamReader _reader;
        private readonly CancellationToken _defaultToken;
        private bool _isClosed;

        public StdioProcessSession(System.Diagnostics.Process process, CancellationToken defaultToken)
        {
            _process = process;
            _writer = process.StandardInput;
            _reader = process.StandardOutput;
            _defaultToken = defaultToken;
        }

        public async Task WriteLineAsync(string line, CancellationToken cancellationToken = default)
        {
            if (_isClosed) throw new ObjectDisposedException(nameof(StdioProcessSession));
            CancellationToken token = cancellationToken == default ? _defaultToken : cancellationToken;
            await _writer.WriteLineAsync(line.AsMemory(), token);
            await _writer.FlushAsync(token);
        }

        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            if (_isClosed) return null;
            CancellationToken token = cancellationToken == default ? _defaultToken : cancellationToken;
            return await _reader.ReadLineAsync(token);
        }

        public Task CloseInputAsync()
        {
            if (!_isClosed)
            {
                _isClosed = true;
                try
                {
                    _writer.Close();
                }
                catch
                {
                }
            }
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (!_isClosed)
            {
                _isClosed = true;
                try
                {
                    _writer.Dispose();
                }
                catch
                {
                }
            }
            return ValueTask.CompletedTask;
        }
    }
}
