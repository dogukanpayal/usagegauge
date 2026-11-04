using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace UsageGauge.Services;

/// <summary>
/// UsageGauge uygulamasının tekil örnek (single-instance) olarak çalışmasını ve
/// bildirim alanına gizliyken ikinci kez başlatıldığında mevcut örneğin öne getirilmesini sağlayan servis.
/// Named Mutex ile örnek varlığını kontrol eder, NamedPipe ile mevcut örneğe "göster" sinyali iletir.
/// Kullanıcı oturumu, parola veya token okumaz.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    public static int CurrentSessionId => System.Diagnostics.Process.GetCurrentProcess().SessionId;

    public static string GetDefaultMutexName(int? sessionId = null) =>
        $@"Local\UsageGauge_SingleInstance_Mutex_Session_{sessionId ?? CurrentSessionId}";

    public static string GetDefaultPipeName(int? sessionId = null) =>
        $"UsageGauge_SingleInstance_Pipe_Session_{sessionId ?? CurrentSessionId}";

    public static string DefaultMutexName => GetDefaultMutexName();
    public static string DefaultPipeName => GetDefaultPipeName();

    private readonly Mutex? _mutex;
    private readonly bool _isFirstInstance;
    private readonly string _pipeName;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private bool _isDisposed;

    /// <summary>
    /// Bu sürecin geçerli oturumdaki ilk örnek olup olmadığını belirtir.
    /// </summary>
    public bool IsFirstInstance => _isFirstInstance;

    public SingleInstanceService(string? customMutexName = null, string? customPipeName = null)
    {
        string mutexName = customMutexName ?? GetDefaultMutexName();
        _pipeName = customPipeName ?? GetDefaultPipeName();

        try
        {
            _mutex = new Mutex(true, mutexName, out bool createdNew);
            _isFirstInstance = createdNew;
        }
        catch (AbandonedMutexException)
        {
            // Önceki süreç anormal sonlandıysa sahipliği devral
            _isFirstInstance = true;
        }
        catch
        {
            _isFirstInstance = false;
        }
    }

    /// <summary>
    /// İlk örnek için arka planda NamedPipe sunucusunu başlatır.
    /// İkinci bir örnekten "SHOW" sinyali geldiğinde onShowRequested geri çağrısını tetikler.
    /// </summary>
    public void StartListening(Action onShowRequested)
    {
        if (!_isFirstInstance || _isDisposed) return;
        if (onShowRequested == null) throw new ArgumentNullException(nameof(onShowRequested));

        _cts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenLoopAsync(_pipeName, onShowRequested, _cts.Token));
    }

    private static async Task ListenLoopAsync(string pipeName, Action onShowRequested, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                // Dinleyici bağlı fakat komut yazmayan istemcide süresiz bloklanmamak için
                // komut okuma işlemine azami 2.5 saniyelik zaman aşımı tanımla.
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                readCts.CancelAfter(TimeSpan.FromMilliseconds(2500));

                byte[] buffer = new byte[1];
                int bytesRead = await server.ReadAsync(buffer.AsMemory(0, 1), readCts.Token).ConfigureAwait(false);
                if (bytesRead > 0 && buffer[0] == 1)
                {
                    try
                    {
                        onShowRequested();
                    }
                    catch
                    {
                        // UI tetikleme hatası sunucu dinleme döngüsünü sonlandırmamalı
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }
                // readCts zaman aşımı: bağlantıyı sonlandır ve bir sonraki bağlantıyı kabul et
            }
            catch
            {
                // Kısa bir bekleme ve döngüye devam
                try { await Task.Delay(100, ct).ConfigureAwait(false); } catch { break; }
            }
            finally
            {
                if (server != null)
                {
                    try
                    {
                        if (server.IsConnected)
                        {
                            server.Disconnect();
                        }
                    }
                    catch { }
                    server.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// İkinci bir örnekten çağrılır. Mevcut ilk örneğe "SHOW" (1) sinyali gönderir.
    /// </summary>
    public static bool TrySignalExistingInstance(string? customPipeName = null, int timeoutMs = 1200)
    {
        string pipeName = customPipeName ?? GetDefaultPipeName();
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.None);
            client.Connect(timeoutMs);
            client.WriteByte(1);
            client.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// İkinci bir örnekten çağrılır. Sınırlı tekrar ve bekleme ile mevcut örneğe sinyal iletir.
    /// </summary>
    public static bool SignalExistingInstanceWithRetry(
        string? customPipeName = null,
        int maxRetries = 3,
        int retryDelayMs = 250,
        int timeoutPerAttemptMs = 1000)
    {
        string pipeName = customPipeName ?? GetDefaultPipeName();
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            if (TrySignalExistingInstance(pipeName, timeoutPerAttemptMs))
            {
                return true;
            }

            if (attempt < maxRetries && retryDelayMs > 0)
            {
                Thread.Sleep(retryDelayMs);
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
        catch { }

        try
        {
            _listenerTask?.Wait(500);
        }
        catch { }

        if (_mutex != null && _isFirstInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch { }
            try
            {
                _mutex.Dispose();
            }
            catch { }
        }
    }
}
