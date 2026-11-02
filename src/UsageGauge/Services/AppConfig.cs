using System;

namespace UsageGauge.Services;

/// <summary>
/// %APPDATA%\UsageGauge\config.json içinde saklanan pencere yerleşim ve görünüm ayarları.
/// Asla kota, token, e-posta veya kimlik bilgisi içermez.
/// </summary>
public sealed record AppConfig
{
    public double WindowLeft { get; init; } = 80;
    public double WindowTop { get; init; } = 80;
    public string? MonitorDeviceName { get; init; }
    public double Opacity { get; init; } = 1.0;
}
