using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UsageGauge.Models;
using UsageGauge.Providers.Process;

namespace UsageGauge.Providers;

/// <summary>
/// codex app-server --stdio üzerinden ChatGPT Work / Codex kota bilgilerini okuyan sağlayıcı.
/// Hiçbir model çağrısı yapmaz; yalnızca initialize -> initialized -> account/rateLimits/read protokolünü yürütür.
/// </summary>
public sealed class ChatGptProvider : IChatGptProvider
{
    private readonly IProcessRunner _processRunner;
    private readonly string? _configuredExecutablePath;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _gracefulExitWait;

    public ChatGptProvider(
        IProcessRunner? processRunner = null,
        string? codexExecutablePath = null,
        TimeSpan? timeout = null,
        TimeSpan? gracefulExitWait = null)
    {
        _processRunner = processRunner ?? new StandardProcessRunner();
        _configuredExecutablePath = codexExecutablePath;
        _timeout = timeout ?? TimeSpan.FromSeconds(15);
        _gracefulExitWait = gracefulExitWait ?? TimeSpan.FromMilliseconds(2000);
    }

    public async Task<ChatGptQuotaResult> FetchQuotaAsync(CancellationToken cancellationToken = default)
    {
        string? executablePath = _configuredExecutablePath ?? CliPathResolver.ResolveCodexPath();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return ChatGptQuotaResult.CreateError(
                ProviderStatus.CliNotFound,
                "Codex CLI (codex.exe) sistemde bulunamadı. Lütfen Codex CLI kurulumunu doğrulayın.");
        }

        string[] arguments = ["app-server", "--stdio"];

        try
        {
            return await _processRunner.RunStdioSessionAsync(
                executablePath,
                arguments,
                ExecuteJsonRpcSessionAsync,
                _timeout,
                _gracefulExitWait,
                cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return ChatGptQuotaResult.CreateError(
                ProviderStatus.CliNotFound,
                "Codex CLI (codex.exe) sistemde bulunamadı.");
        }
        catch (TimeoutException)
        {
            return ChatGptQuotaResult.CreateError(
                ProviderStatus.Timeout,
                $"Codex App Server {_timeout.TotalSeconds:F0} saniyelik zaman aşımına uğradı.");
        }
        catch (OperationCanceledException)
        {
            return ChatGptQuotaResult.CreateError(
                ProviderStatus.Timeout,
                "Codex sorgusu zaman aşımına uğradı veya iptal edildi.");
        }
        catch (Exception)
        {
            return ChatGptQuotaResult.CreateError(
                ProviderStatus.ProcessError,
                "Codex App Server ile iletişim sırasında hata oluştu.");
        }
    }

    private static async Task<ChatGptQuotaResult> ExecuteJsonRpcSessionAsync(IStdioSession session)
    {
        // 1. İstek: initialize (id: 1)
        string initRequest = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                clientInfo = new
                {
                    name = "usagegauge",
                    title = "UsageGauge",
                    version = "0.1.0"
                },
                capabilities = new
                {
                    experimentalApi = true
                }
            }
        });

        await session.WriteLineAsync(initRequest);

        // id: 1 yanıtını bekle (aradaki bildirimleri güvenle atla)
        JsonDocument initResponseDoc = await ReadResponseWithIdAsync(session, expectedId: 1);
        using (initResponseDoc)
        {
            if (initResponseDoc.RootElement.TryGetProperty("error", out JsonElement initError))
            {
                string rawErrMsg = GetErrorMessage(initError);
                var status = IsAuthError(rawErrMsg) ? ProviderStatus.AuthenticationRequired : ProviderStatus.InvalidResponse;
                string safeMsg = status == ProviderStatus.AuthenticationRequired
                    ? "Codex oturumu açık değil veya yetkilendirme gerekiyor."
                    : "Codex başlangıç (initialize) aşamasında hata bildirdi.";
                return ChatGptQuotaResult.CreateError(status, safeMsg);
            }
        }

        // 2. Bildirim: initialized
        string initializedNotification = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "initialized"
        });

        await session.WriteLineAsync(initializedNotification);

        // 3. İstek: account/rateLimits/read (id: 2)
        string readLimitsRequest = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "account/rateLimits/read",
            @params = new { }
        });

        await session.WriteLineAsync(readLimitsRequest);

        // id: 2 yanıtını bekle
        JsonDocument readResponseDoc = await ReadResponseWithIdAsync(session, expectedId: 2);
        using (readResponseDoc)
        {
            return ParseRateLimitsDocument(readResponseDoc.RootElement);
        }
    }

    private static async Task<JsonDocument> ReadResponseWithIdAsync(IStdioSession session, int expectedId)
    {
        while (true)
        {
            string? line = await session.ReadLineAsync();
            if (line == null)
            {
                throw new InvalidOperationException($"Beklenen JSON-RPC yanıtı (id: {expectedId}) alınamadan stdio akışı kapandı.");
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch
            {
                // JSON olmayan ara çıktı veya log satırlarını güvenle atla
                continue;
            }

            JsonElement root = doc.RootElement;

            // Yanıt id kontrolü
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("id", out JsonElement idProp) &&
                idProp.ValueKind == JsonValueKind.Number)
            {
                if (idProp.GetInt32() == expectedId)
                {
                    return doc; // Eşleşen yanıt bulundu
                }
            }

            // id eşleşmediyse (örneğin bildirim veya farklı id) bu belgeyi serbest bırak ve okumaya devam et
            doc.Dispose();
        }
    }

    /// <summary>
    /// JSON-RPC yanıt gövdesini ayrıştırır ve 300 ile 10080 dakikalık pencereleri doğrular.
    /// Sentetik testler için doğrudan çağrılabilir.
    /// </summary>
    public static ChatGptQuotaResult ParseRateLimitsJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ChatGptQuotaResult.CreateError(
                ProviderStatus.InvalidResponse,
                "Codex yanıtı boş.");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return ChatGptQuotaResult.CreateError(
                ProviderStatus.InvalidResponse,
                "Codex yanıtı geçerli JSON formatında değil.");
        }

        using (doc)
        {
            return ParseRateLimitsDocument(doc.RootElement);
        }
    }

    public static ChatGptQuotaResult ParseRateLimitsDocument(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return ChatGptQuotaResult.CreateError(
                ProviderStatus.InvalidResponse,
                "Codex JSON kök düğümü nesne değil.");
        }

        // 1. Üst düzey JSON-RPC hata denetimi
        if (root.TryGetProperty("error", out JsonElement errorElement))
        {
            string rawErrMsg = GetErrorMessage(errorElement);
            var status = IsAuthError(rawErrMsg) ? ProviderStatus.AuthenticationRequired : ProviderStatus.InvalidResponse;
            string safeMsg = status == ProviderStatus.AuthenticationRequired
                ? "Codex oturumu açık değil veya yetkilendirme gerekiyor."
                : "Codex kota sorgusunda (rateLimits/read) hata bildirdi.";
            return ChatGptQuotaResult.CreateError(status, safeMsg);
        }

        if (!root.TryGetProperty("result", out JsonElement resultElement) ||
            resultElement.ValueKind != JsonValueKind.Object)
        {
            return ChatGptQuotaResult.CreateError(
                ProviderStatus.InvalidResponse,
                "JSON-RPC yanıtında 'result' alanı bulunamadı.");
        }

        // rateLimitsByLimitId kontrolü
        if (!resultElement.TryGetProperty("rateLimitsByLimitId", out JsonElement rateLimitsByLimitId) ||
            rateLimitsByLimitId.ValueKind != JsonValueKind.Object)
        {
            return ChatGptQuotaResult.CreateError(
                ProviderStatus.InvalidResponse,
                "Yanıtta 'rateLimitsByLimitId' nesnesi bulunamadı.");
        }

        if (!rateLimitsByLimitId.TryGetProperty("codex", out JsonElement codexElement))
        {
            return ChatGptQuotaResult.CreateError(
                ProviderStatus.InvalidResponse,
                "'rateLimitsByLimitId.codex' nesnesi bulunamadı.");
        }

        // codex nesnesi içerisindeki tüm pencereleri topla (primary, secondary veya dizi öğeleri)
        List<JsonElement> candidateWindows = new();

        if (codexElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in codexElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object) candidateWindows.Add(item);
            }
        }
        else if (codexElement.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty prop in codexElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Object &&
                    prop.Value.TryGetProperty("windowDurationMins", out _))
                {
                    candidateWindows.Add(prop.Value);
                }
                else if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in prop.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object) candidateWindows.Add(item);
                    }
                }
            }
        }

        QuotaWindow? fiveHourWindow = null;
        QuotaWindow? weeklyWindow = null;

        foreach (JsonElement win in candidateWindows)
        {
            if (!win.TryGetProperty("windowDurationMins", out JsonElement durationProp) ||
                durationProp.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            int durationMins = durationProp.GetInt32();

            if (!win.TryGetProperty("usedPercent", out JsonElement usedProp) ||
                usedProp.ValueKind != JsonValueKind.Number)
            {
                return ChatGptQuotaResult.CreateError(
                    ProviderStatus.InvalidResponse,
                    $"{durationMins} dakikalık pencerede geçerli 'usedPercent' alanı bulunamadı.");
            }

            double usedPercent = usedProp.GetDouble();
            if (usedPercent < 0.0 || usedPercent > 100.0)
            {
                return ChatGptQuotaResult.CreateError(
                    ProviderStatus.InvalidResponse,
                    $"Geçersiz usedPercent değeri: {usedPercent} (0 ile 100 arasında olmalıdır).");
            }

            if (!win.TryGetProperty("resetsAt", out JsonElement resetsProp) ||
                resetsProp.ValueKind != JsonValueKind.Number)
            {
                return ChatGptQuotaResult.CreateError(
                    ProviderStatus.InvalidResponse,
                    $"{durationMins} dakikalık pencerede geçerli 'resetsAt' alanı bulunamadı.");
            }

            long resetsAtSeconds = resetsProp.GetInt64();
            if (resetsAtSeconds <= 0)
            {
                return ChatGptQuotaResult.CreateError(
                    ProviderStatus.InvalidResponse,
                    $"Geçersiz resetsAt Unix zaman damgası: {resetsAtSeconds}");
            }

            DateTimeOffset resetsAtUtc;
            try
            {
                resetsAtUtc = DateTimeOffset.FromUnixTimeSeconds(resetsAtSeconds).ToUniversalTime();
            }
            catch
            {
                return ChatGptQuotaResult.CreateError(
                    ProviderStatus.InvalidResponse,
                    "resetsAt zaman damgası geçerli bir tarihe dönüştürülemedi.");
            }

            double remainingPercent = Math.Clamp(100.0 - usedPercent, 0.0, 100.0);

            if (durationMins == 300)
            {
                fiveHourWindow = new QuotaWindow("codex-5h", "5 Saatlik", 300, remainingPercent, resetsAtUtc);
            }
            else if (durationMins == 10080)
            {
                weeklyWindow = new QuotaWindow("codex-weekly", "Haftalık", 10080, remainingPercent, resetsAtUtc);
            }
        }

        if (fiveHourWindow == null)
        {
            return ChatGptQuotaResult.CreateError(
                ProviderStatus.InvalidResponse,
                "Codex yanıtında 5 saatlik kota penceresi (windowDurationMins: 300) bulunamadı.");
        }

        if (weeklyWindow == null)
        {
            return ChatGptQuotaResult.CreateError(
                ProviderStatus.InvalidResponse,
                "Codex yanıtında haftalık kota penceresi (windowDurationMins: 10080) bulunamadı.");
        }

        var group = new QuotaGroup(
            "chatgpt-codex",
            "ChatGPT Work / Codex",
            fiveHourWindow,
            weeklyWindow,
            ProviderStatus.Success,
            errorMessage: null,
            fetchedAtUtc: DateTimeOffset.UtcNow);

        return new ChatGptQuotaResult(group, ProviderStatus.Success);
    }

    private static string GetErrorMessage(JsonElement errorElement)
    {
        if (errorElement.ValueKind == JsonValueKind.Object &&
            errorElement.TryGetProperty("message", out JsonElement msgElement))
        {
            return msgElement.GetString() ?? errorElement.ToString();
        }
        return errorElement.ToString();
    }

    private static bool IsAuthError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        string lower = message.ToLowerInvariant();
        return lower.Contains("auth") ||
               lower.Contains("unauthorized") ||
               lower.Contains("login") ||
               lower.Contains("sign in") ||
               lower.Contains("token") ||
               lower.Contains("not logged in");
    }
}
