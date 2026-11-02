using System;
using System.Threading;
using System.Threading.Tasks;

namespace UsageGauge.Providers.Process;

/// <summary>
/// stdio üzerinden çift yönlü satır tabanlı iletişim oturumu.
/// </summary>
public interface IStdioSession : IAsyncDisposable
{
    Task WriteLineAsync(string line, CancellationToken cancellationToken = default);
    Task<string?> ReadLineAsync(CancellationToken cancellationToken = default);
    Task CloseInputAsync();
}
