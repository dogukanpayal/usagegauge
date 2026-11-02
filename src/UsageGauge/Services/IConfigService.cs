using System;

namespace UsageGauge.Services;

public interface IConfigService
{
    string ConfigFilePath { get; }
    AppConfig LoadConfig();
    void SaveConfig(AppConfig config);
}
