using System.Threading;
using System.Threading.Tasks;
using UsageGauge.Models;

namespace UsageGauge.Providers;

/// <summary>
/// ChatGPT Work / Codex kotalarını okuyan sağlayıcı arayüzü.
/// </summary>
public interface IChatGptProvider
{
    Task<ChatGptQuotaResult> FetchQuotaAsync(CancellationToken cancellationToken = default);
}
