namespace UsageGauge.Models;

/// <summary>
/// Kota sağlayıcılarının çalışma ve hata durumları.
/// </summary>
public enum ProviderStatus
{
    /// <summary>
    /// Kota başarıyla okundu ve doğrulandı.
    /// </summary>
    Success,

    /// <summary>
    /// Gruptaki havuzlardan biri başarılı, diğeri hatalı (kısmi başarı).
    /// </summary>
    PartialSuccess,

    /// <summary>
    /// Gerekli CLI ikilisi (codex veya agy) sistemde bulunamadı.
    /// </summary>
    CliNotFound,

    /// <summary>
    /// Sağlayıcı oturumu açık değil veya süresi dolmuş.
    /// </summary>
    AuthenticationRequired,

    /// <summary>
    /// İşlem tanımlanan süre (15 saniye) içinde tamamlanamadı.
    /// </summary>
    Timeout,

    /// <summary>
    /// Beklenmeyen model turu veya token harcaması tespit edildi; güvenlik gereği durduruldu.
    /// </summary>
    ModelTurnDetected,

    /// <summary>
    /// Süreç çalışırken hata verdi veya sıfır dışı çıkış kodu döndü.
    /// </summary>
    ProcessError,

    /// <summary>
    /// JSON formatı bozuk, beklenen şema değişmiş veya zorunlu alanlar/ID'ler eksik.
    /// </summary>
    InvalidResponse
}
