using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UsageGauge.Providers.Process;

namespace UsageGauge.Tests.Mocks;

/// <summary>
/// Birim testler için sahte süreç çalıştırıcısı ve stdio simülatörü.
/// </summary>
public sealed class MockProcessRunner : IProcessRunner
{
    public Func<string, IReadOnlyList<string>, TimeSpan, ProcessRunResult>? ProcessRunHandler { get; set; }
    public Func<IStdioSession, Task>? SessionAction { get; set; }
    public Queue<string> MockStdioOutputLines { get; } = new();
    public List<string> WrittenLines { get; } = new();
    public bool SimulateCliNotFound { get; set; }
    public bool SimulateTimeout { get; set; }
    public bool IsInputClosed { get; private set; }

    public Task<ProcessRunResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (SimulateCliNotFound)
        {
            throw new FileNotFoundException($"Mock: CLI bulunamadı: {fileName}", fileName);
        }

        if (SimulateTimeout)
        {
            return Task.FromResult(ProcessRunResult.Timeout());
        }

        if (ProcessRunHandler != null)
        {
            return Task.FromResult(ProcessRunHandler(fileName, arguments, timeout));
        }

        return Task.FromResult(new ProcessRunResult(0, string.Empty, string.Empty));
    }

    public async Task<T> RunStdioSessionAsync<T>(
        string fileName,
        IReadOnlyList<string> arguments,
        Func<IStdioSession, Task<T>> sessionHandler,
        TimeSpan timeout,
        TimeSpan gracefulExitWait,
        CancellationToken cancellationToken = default)
    {
        if (SimulateCliNotFound)
        {
            throw new FileNotFoundException($"Mock: CLI bulunamadı: {fileName}", fileName);
        }

        if (SimulateTimeout)
        {
            throw new TimeoutException("Mock: stdio zaman aşımına uğradı.");
        }

        var session = new MockStdioSession(this);
        T result = await sessionHandler(session);
        await session.CloseInputAsync();
        return result;
    }

    private sealed class MockStdioSession : IStdioSession
    {
        private readonly MockProcessRunner _owner;

        public MockStdioSession(MockProcessRunner owner)
        {
            _owner = owner;
        }

        public Task WriteLineAsync(string line, CancellationToken cancellationToken = default)
        {
            _owner.WrittenLines.Add(line);
            return Task.CompletedTask;
        }

        public Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            if (_owner.MockStdioOutputLines.Count > 0)
            {
                return Task.FromResult<string?>(_owner.MockStdioOutputLines.Dequeue());
            }
            return Task.FromResult<string?>(null);
        }

        public Task CloseInputAsync()
        {
            _owner.IsInputClosed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _owner.IsInputClosed = true;
            return ValueTask.CompletedTask;
        }
    }
}
