using System;
using System.IO;

namespace UsageGauge.Providers.Process;

/// <summary>
/// Sistemde kurulu codex ve agy ikililerinin dosya yollarını çözer.
/// </summary>
public static class CliPathResolver
{
    public static string? ResolveCodexPath()
    {
        return ResolveFromPath("codex") ?? ResolveFromKnownCodexLocations();
    }

    public static string? ResolveAntigravityPath()
    {
        return ResolveFromPath("agy") ?? ResolveFromKnownAntigravityLocations();
    }

    private static string? ResolveFromPath(string executableName)
    {
        string fileName = OperatingSystem.IsWindows() && !executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? executableName + ".exe"
            : executableName;

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv)) return null;

        char separator = OperatingSystem.IsWindows() ? ';' : ':';
        string[] paths = pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string dir in paths)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    string candidate = Path.Combine(dir, fileName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
                // İzin veya biçim hatalarını sessizce atla
            }
        }

        return null;
    }

    private static string? ResolveFromKnownCodexLocations()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // WinGet Paket konumu
        string wingetPackages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(wingetPackages))
        {
            try
            {
                string[] matchDirs = Directory.GetDirectories(wingetPackages, "OpenAI.Codex*");
                foreach (string dir in matchDirs)
                {
                    string candidate = Path.Combine(dir, "codex.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch { }
        }

        string[] candidates =
        [
            Path.Combine(localAppData, "Programs", "codex", "codex.exe"),
            Path.Combine(userProfile, ".codex", "bin", "codex.exe")
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static string? ResolveFromKnownAntigravityLocations()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] candidates =
        [
            Path.Combine(localAppData, "agy", "bin", "agy.exe"),
            Path.Combine(userProfile, ".gemini", "antigravity", "bin", "agy.exe"),
            Path.Combine(localAppData, "Programs", "antigravity", "bin", "agy.exe")
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
