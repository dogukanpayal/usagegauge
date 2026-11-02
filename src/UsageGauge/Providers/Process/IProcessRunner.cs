using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UsageGauge.Providers.Process;

/// <summary>
/// Dış süreç çalıştırma ve stdio oturum yönetimi arayüzü.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessRunResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<T> RunStdioSessionAsync<T>(
        string fileName,
        IReadOnlyList<string> arguments,
        Func<IStdioSession, Task<T>> sessionHandler,
        TimeSpan timeout,
        TimeSpan gracefulExitWait,
        CancellationToken cancellationToken = default);
}
