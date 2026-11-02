using System.Threading;
using System.Threading.Tasks;
using UsageGauge.Models;

namespace UsageGauge.Providers;

/// <summary>
/// Antigravity (Gemini ve Claude/GPT havuzları) kotalarını okuyan sağlayıcı arayüzü.
/// </summary>
public interface IAntigravityProvider
{
    Task<AntigravityQuotaResult> FetchQuotaAsync(CancellationToken cancellationToken = default);
}
