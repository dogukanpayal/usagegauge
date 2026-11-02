using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UsageGauge.Models;
using UsageGauge.Providers.Process;

namespace UsageGauge.Providers;

/// <summary>
/// agy -p /usage --output-format json --print-timeout 10s komutu üzerinden
/// Antigravity Gemini ve Claude/GPT havuzlarının kota verilerini okuyan sağlayıcı.
/// response düz metnini asla kaynak olarak kullanmaz; yapılandırılmış command.data.groups alanını ayrıştırır.
/// </summary>
public sealed class AntigravityProvider : IAntigravityProvider
{
    private static readonly Regex ExplicitTimezoneRegex = new(@"(?:[Zz]|[+-]\d{2}(?::?\d{2})?)$", RegexOptions.Compiled);

    private readonly IProcessRunner _processRunner;
    private readonly string? _configuredExecutablePath;
    private readonly TimeSpan _timeout;

    public AntigravityProvider(
        IProcessRunner? processRunner = null,
        string? agyExecutablePath = null,
        TimeSpan? timeout = null)
    {
        _processRunner = processRunner ?? new StandardProcessRunner();
        _configuredExecutablePath = agyExecutablePath;
        _timeout = timeout ?? TimeSpan.FromSeconds(15);
    }

    public async Task<AntigravityQuotaResult> FetchQuotaAsync(CancellationToken cancellationToken = default)
    {
        string? executablePath = _configuredExecutablePath ?? CliPathResolver.ResolveAntigravityPath();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return AntigravityQuotaResult.CreateError(
                ProviderStatus.CliNotFound,
                "Antigravity CLI (agy.exe) sistemde bulunamadı. Lütfen agy kurulumunu doğrulayın.");
        }

        string[] arguments = ["-p", "/usage", "--output-format", "json", "--print-timeout", "10s"];

        try
        {
            ProcessRunResult runResult = await _processRunner.RunProcessAsync(
                executablePath,
                arguments,
                _timeout,
                cancellationToken);

            if (runResult.IsTimeout)
            {
                return AntigravityQuotaResult.CreateError(
                    ProviderStatus.Timeout,
                    $"Antigravity CLI {_timeout.TotalSeconds:F0} saniyelik zaman aşımına uğradı.");
            }

            if (runResult.ExitCode != 0 && string.IsNullOrWhiteSpace(runResult.StandardOutput))
            {
                var status = IsAuthError(runResult.StandardError)
                    ? ProviderStatus.AuthenticationRequired
                    : ProviderStatus.ProcessError;

                string safeMsg = status == ProviderStatus.AuthenticationRequired
                    ? "Antigravity oturumu açık değil veya yetkilendirme gerekiyor."
                    : $"Antigravity CLI hata kodu ile sonlandı (kod: {runResult.ExitCode}).";

                return AntigravityQuotaResult.CreateError(status, safeMsg);
            }

            return ParseAntigravityJson(runResult.StandardOutput);
        }
        catch (FileNotFoundException)
        {
            return AntigravityQuotaResult.CreateError(
                ProviderStatus.CliNotFound,
                "Antigravity CLI (agy.exe) sistemde bulunamadı.");
        }
        catch (OperationCanceledException)
        {
            return AntigravityQuotaResult.CreateError(
                ProviderStatus.Timeout,
                "Antigravity sorgusu zaman aşımına uğradı veya iptal edildi.");
        }
        catch (Exception)
        {
            return AntigravityQuotaResult.CreateError(
                ProviderStatus.ProcessError,
                "Antigravity CLI çalıştırılırken beklenmeyen hata oluştu.");
        }
    }

    /// <summary>
    /// Antigravity başsız JSON çıktısını ayrıştırır ve kota havuzlarını ID eşleştirmesiyle doğrular.
    /// Sentetik testler için doğrudan çağrılabilir.
    /// </summary>
    public static AntigravityQuotaResult ParseAntigravityJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return AntigravityQuotaResult.CreateError(
                ProviderStatus.InvalidResponse,
                "Antigravity CLI çıktısı boş.");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return AntigravityQuotaResult.CreateError(
                ProviderStatus.InvalidResponse,
                "Antigravity çıktısı geçerli JSON formatında değil.");
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return AntigravityQuotaResult.CreateError(
                    ProviderStatus.InvalidResponse,
                    "Antigravity kök JSON düğümü nesne değil.");
            }

            // 1. Status doğrulaması
            if (!root.TryGetProperty("status", out JsonElement statusProp) ||
                statusProp.ValueKind != JsonValueKind.String)
            {
                return AntigravityQuotaResult.CreateError(
                    ProviderStatus.InvalidResponse,
                    "Yanıtta geçerli 'status' alanı bulunamadı.");
            }

            string? statusStr = statusProp.GetString();
            if (!string.Equals(statusStr, "SUCCESS", StringComparison.OrdinalIgnoreCase))
            {
                string rawMsg = root.TryGetProperty("error", out JsonElement errProp) ? errProp.ToString() : string.Empty;
                var provStatus = IsAuthError(rawMsg) ? ProviderStatus.AuthenticationRequired : ProviderStatus.InvalidResponse;
                string safeMsg = provStatus == ProviderStatus.AuthenticationRequired
                    ? "Antigravity oturumu açık değil veya yetkilendirme gerekiyor."
                    : "Antigravity durumu başarılı değil.";
                return AntigravityQuotaResult.CreateError(provStatus, safeMsg);
            }

            // 2. Token ve Tur kontrolü (Eksikse, yanlış tipse veya negatifse geçersiz sayılır; > 0 ise ModelTurnDetected)
            if (!root.TryGetProperty("num_turns", out JsonElement turnsProp) ||
                turnsProp.ValueKind != JsonValueKind.Number ||
                !turnsProp.TryGetInt32(out int numTurns) ||
                numTurns < 0)
            {
                return AntigravityQuotaResult.CreateError(
                    ProviderStatus.InvalidResponse,
                    "Antigravity çıktısında geçerli 'num_turns' alanı bulunamadı.");
            }

            long totalTokens;
            if (root.TryGetProperty("usage", out JsonElement usageProp))
            {
                if (usageProp.ValueKind != JsonValueKind.Object ||
                    !usageProp.TryGetProperty("total_tokens", out JsonElement tokensProp) ||
                    tokensProp.ValueKind != JsonValueKind.Number ||
                    !tokensProp.TryGetInt64(out totalTokens) ||
                    totalTokens < 0)
                {
                    return AntigravityQuotaResult.CreateError(
                        ProviderStatus.InvalidResponse,
                        "Antigravity yanıtındaki 'usage.total_tokens' alanı geçersiz veya negatif.");
                }
            }
            else if (root.TryGetProperty("total_tokens", out JsonElement rootTokensProp))
            {
                if (rootTokensProp.ValueKind != JsonValueKind.Number ||
                    !rootTokensProp.TryGetInt64(out totalTokens) ||
                    totalTokens < 0)
                {
                    return AntigravityQuotaResult.CreateError(
                        ProviderStatus.InvalidResponse,
                        "Antigravity yanıtındaki 'total_tokens' alanı geçersiz veya negatif.");
                }
            }
            else
            {
                return AntigravityQuotaResult.CreateError(
                    ProviderStatus.InvalidResponse,
                    "Antigravity yanıtında 'total_tokens' veya 'usage.total_tokens' alanı bulunamadı.");
            }

            if (numTurns > 0 || totalTokens > 0)
            {
                return AntigravityQuotaResult.CreateError(
                    ProviderStatus.ModelTurnDetected,
                    "Model turu veya token harcaması tespit edildi; kota okuması durduruldu.",
                    numTurns,
                    totalTokens);
            }

            // 3. Yapılandırılmış command.data.groups nesnesi
            if (!root.TryGetProperty("command", out JsonElement cmdProp) ||
                cmdProp.ValueKind != JsonValueKind.Object ||
                !cmdProp.TryGetProperty("data", out JsonElement dataProp) ||
                dataProp.ValueKind != JsonValueKind.Object ||
                !dataProp.TryGetProperty("groups", out JsonElement groupsProp) ||
                groupsProp.ValueKind != JsonValueKind.Array)
            {
                return AntigravityQuotaResult.CreateError(
                    ProviderStatus.InvalidResponse,
                    "Yanıtta beklenen 'command.data.groups' dizisi bulunamadı.");
            }

            // Bucket ID eşleştirmesi: Sıra indeksine güvenilmez, gruplar ve bucket'lar taranır
            Dictionary<string, QuotaWindow> windowsById = new();
            Dictionary<string, string> bucketErrors = new();

            foreach (JsonElement group in groupsProp.EnumerateArray())
            {
                if (group.ValueKind != JsonValueKind.Object) continue;

                if (!group.TryGetProperty("buckets", out JsonElement bucketsArray) ||
                    bucketsArray.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement bucket in bucketsArray.EnumerateArray())
                {
                    if (bucket.ValueKind != JsonValueKind.Object) continue;

                    if (!bucket.TryGetProperty("id", out JsonElement idProp) ||
                        idProp.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string? id = idProp.GetString();
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    if (!bucket.TryGetProperty("remaining_fraction", out JsonElement fracProp) ||
                        fracProp.ValueKind != JsonValueKind.Number ||
                        !fracProp.TryGetDouble(out double fraction) ||
                        fraction < 0.0 || fraction > 1.0)
                    {
                        bucketErrors[id] = $"'{id}' havuzunda 'remaining_fraction' geçersiz veya aralık dışı.";
                        continue;
                    }

                    if (!bucket.TryGetProperty("reset_time", out JsonElement resetProp) ||
                        resetProp.ValueKind != JsonValueKind.String)
                    {
                        bucketErrors[id] = $"'{id}' havuzunda geçerli 'reset_time' alanı bulunamadı.";
                        continue;
                    }

                    string? resetStr = resetProp.GetString();
                    if (string.IsNullOrWhiteSpace(resetStr) ||
                        !ExplicitTimezoneRegex.IsMatch(resetStr.Trim()) ||
                        !DateTimeOffset.TryParse(resetStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset resetTime))
                    {
                        bucketErrors[id] = $"'{id}' havuzunda 'reset_time' açık saat dilimi içermiyor veya geçersiz.";
                        continue;
                    }

                    DateTimeOffset resetUtc = resetTime.ToUniversalTime();
                    int durationMins = id.EndsWith("-weekly", StringComparison.OrdinalIgnoreCase) ? 10080 : 300;
                    string windowTitle = durationMins == 10080 ? "Haftalık" : "5 Saatlik";

                    windowsById[id] = new QuotaWindow(id, windowTitle, durationMins, fraction * 100.0, resetUtc);
                }
            }

            // Gemini Grubu değerlendirmesi
            QuotaGroup geminiGroup;
            if (windowsById.TryGetValue("gemini-5h", out QuotaWindow? g5h) &&
                windowsById.TryGetValue("gemini-weekly", out QuotaWindow? gWeekly))
            {
                geminiGroup = new QuotaGroup("antigravity-gemini", "Antigravity Gemini", g5h, gWeekly, ProviderStatus.Success);
            }
            else
            {
                string errMsg = bucketErrors.TryGetValue("gemini-5h", out var e1) ? e1
                    : bucketErrors.TryGetValue("gemini-weekly", out var e2) ? e2
                    : "Gemini kota havuzu verileri eksik veya geçersiz.";
                geminiGroup = QuotaGroup.CreateError("antigravity-gemini", "Antigravity Gemini", ProviderStatus.InvalidResponse, errMsg);
            }

            // Claude / GPT Grubu değerlendirmesi
            QuotaGroup tpGroup;
            if (windowsById.TryGetValue("3p-5h", out QuotaWindow? p5h) &&
                windowsById.TryGetValue("3p-weekly", out QuotaWindow? pWeekly))
            {
                tpGroup = new QuotaGroup("antigravity-3p", "Antigravity Claude / GPT", p5h, pWeekly, ProviderStatus.Success);
            }
            else
            {
                string errMsg = bucketErrors.TryGetValue("3p-5h", out var e1) ? e1
                    : bucketErrors.TryGetValue("3p-weekly", out var e2) ? e2
                    : "Claude/GPT kota havuzu verileri eksik veya geçersiz.";
                tpGroup = QuotaGroup.CreateError("antigravity-3p", "Antigravity Claude / GPT", ProviderStatus.InvalidResponse, errMsg);
            }

            ProviderStatus overallStatus;
            string? overallErrorMessage;

            if (geminiGroup.IsSuccess && tpGroup.IsSuccess)
            {
                overallStatus = ProviderStatus.Success;
                overallErrorMessage = null;
            }
            else if (geminiGroup.IsSuccess || tpGroup.IsSuccess)
            {
                overallStatus = ProviderStatus.PartialSuccess;
                overallErrorMessage = "Bir kota havuzu başarıyla okundu, diğeri okunamadı.";
            }
            else
            {
                overallStatus = ProviderStatus.InvalidResponse;
                overallErrorMessage = "Antigravity kota havuzları okunamadı.";
            }

            return new AntigravityQuotaResult(
                geminiGroup,
                tpGroup,
                overallStatus,
                numTurns,
                totalTokens,
                overallErrorMessage,
                DateTimeOffset.UtcNow);
        }
    }

    private static bool IsAuthError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        string lower = message.ToLowerInvariant();
        return lower.Contains("auth") ||
               lower.Contains("unauthorized") ||
               lower.Contains("login") ||
               lower.Contains("sign in") ||
               lower.Contains("not logged in") ||
               lower.Contains("sign-in");
    }
}
