using System;
using System.Threading.Tasks;
using UsageGauge.Models;
using UsageGauge.Providers;
using UsageGauge.Tests.Mocks;
using Xunit;

namespace UsageGauge.Tests.Providers;

public class ChatGptProviderTests
{
    private const string ValidRateLimitsJson = """
    {
      "jsonrpc": "2.0",
      "id": 2,
      "result": {
        "rateLimitsByLimitId": {
          "codex": {
            "limitId": "codex",
            "primary": {
              "usedPercent": 15.0,
              "windowDurationMins": 300,
              "resetsAt": 1772412800
            },
            "secondary": {
              "usedPercent": 45.5,
              "windowDurationMins": 10080,
              "resetsAt": 1772912800
            }
          }
        }
      }
    }
    """;

    [Fact]
    public void ParseRateLimitsJson_WithValidWindows_ReturnsExpectedPercentagesAndUtc()
    {
        ChatGptQuotaResult result = ChatGptProvider.ParseRateLimitsJson(ValidRateLimitsJson);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProviderStatus.Success, result.Status);

        QuotaGroup group = result.CodexGroup;
        Assert.NotNull(group.FiveHourLimit);
        Assert.NotNull(group.WeeklyLimit);

        // 5 Saatlik pencere: usedPercent = 15.0 -> Kalan = 85.0%
        Assert.Equal("codex-5h", group.FiveHourLimit.Id);
        Assert.Equal(300, group.FiveHourLimit.WindowDurationMinutes);
        Assert.Equal(85.0, group.FiveHourLimit.RemainingPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1772412800).ToUniversalTime(), group.FiveHourLimit.ResetsAtUtc);

        // Haftalık pencere: usedPercent = 45.5 -> Kalan = 54.5%
        Assert.Equal("codex-weekly", group.WeeklyLimit.Id);
        Assert.Equal(10080, group.WeeklyLimit.WindowDurationMinutes);
        Assert.Equal(54.5, group.WeeklyLimit.RemainingPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1772912800).ToUniversalTime(), group.WeeklyLimit.ResetsAtUtc);
    }

    [Fact]
    public void ParseRateLimitsJson_WithArrayWindowStructure_ParsesSuccessfully()
    {
        string arrayJson = """
        {
          "jsonrpc": "2.0",
          "id": 2,
          "result": {
            "rateLimitsByLimitId": {
              "codex": [
                { "windowDurationMins": 10080, "usedPercent": 10.0, "resetsAt": 1772912800 },
                { "windowDurationMins": 300, "usedPercent": 0.0, "resetsAt": 1772412800 }
              ]
            }
          }
        }
        """;

        ChatGptQuotaResult result = ChatGptProvider.ParseRateLimitsJson(arrayJson);

        Assert.True(result.IsSuccess);
        Assert.Equal(100.0, result.CodexGroup.FiveHourLimit!.RemainingPercent);
        Assert.Equal(90.0, result.CodexGroup.WeeklyLimit!.RemainingPercent);
    }

    [Fact]
    public void ParseRateLimitsJson_WhenMissingFiveHourWindow_ReturnsInvalidResponse()
    {
        string missing5hJson = """
        {
          "jsonrpc": "2.0",
          "id": 2,
          "result": {
            "rateLimitsByLimitId": {
              "codex": {
                "secondary": {
                  "usedPercent": 10.0,
                  "windowDurationMins": 10080,
                  "resetsAt": 1772912800
                }
              }
            }
          }
        }
        """;

        ChatGptQuotaResult result = ChatGptProvider.ParseRateLimitsJson(missing5hJson);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.InvalidResponse, result.Status);
        Assert.Contains("300", result.ErrorMessage);
    }

    [Fact]
    public void ParseRateLimitsJson_WhenMissingWeeklyWindow_ReturnsInvalidResponse()
    {
        string missingWeeklyJson = """
        {
          "jsonrpc": "2.0",
          "id": 2,
          "result": {
            "rateLimitsByLimitId": {
              "codex": {
                "primary": {
                  "usedPercent": 10.0,
                  "windowDurationMins": 300,
                  "resetsAt": 1772412800
                }
              }
            }
          }
        }
        """;

        ChatGptQuotaResult result = ChatGptProvider.ParseRateLimitsJson(missingWeeklyJson);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.InvalidResponse, result.Status);
        Assert.Contains("10080", result.ErrorMessage);
    }

    [Fact]
    public void ParseRateLimitsJson_WithInvalidUsedPercent_ReturnsInvalidResponse()
    {
        string invalidPercentJson = """
        {
          "jsonrpc": "2.0",
          "id": 2,
          "result": {
            "rateLimitsByLimitId": {
              "codex": {
                "primary": { "windowDurationMins": 300, "usedPercent": 150.0, "resetsAt": 1772412800 },
                "secondary": { "windowDurationMins": 10080, "usedPercent": 0.0, "resetsAt": 1772912800 }
              }
            }
          }
        }
        """;

        ChatGptQuotaResult result = ChatGptProvider.ParseRateLimitsJson(invalidPercentJson);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.InvalidResponse, result.Status);
    }

    [Fact]
    public void ParseRateLimitsJson_WithInvalidResetsAt_ReturnsInvalidResponse()
    {
        string invalidResetsJson = """
        {
          "jsonrpc": "2.0",
          "id": 2,
          "result": {
            "rateLimitsByLimitId": {
              "codex": {
                "primary": { "windowDurationMins": 300, "usedPercent": 10.0, "resetsAt": -50 },
                "secondary": { "windowDurationMins": 10080, "usedPercent": 0.0, "resetsAt": 1772912800 }
              }
            }
          }
        }
        """;

        ChatGptQuotaResult result = ChatGptProvider.ParseRateLimitsJson(invalidResetsJson);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.InvalidResponse, result.Status);
    }

    [Fact]
    public void ParseRateLimitsJson_WithAuthErrorMessage_ReturnsAuthenticationRequired()
    {
        string authErrorJson = """
        {
          "jsonrpc": "2.0",
          "id": 2,
          "error": {
            "code": -32001,
            "message": "User is not logged in. Please run codex login."
          }
        }
        """;

        ChatGptQuotaResult result = ChatGptProvider.ParseRateLimitsJson(authErrorJson);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.AuthenticationRequired, result.Status);
    }

    [Fact]
    public void ParseRateLimitsJson_WithSensitiveError_SanitizesErrorMessage()
    {
        string sensitiveErrorJson = """
        {
          "jsonrpc": "2.0",
          "id": 2,
          "error": {
            "code": -32000,
            "message": "Internal error occurred for user john.doe@company.corp at /var/secrets/key"
          }
        }
        """;

        ChatGptQuotaResult result = ChatGptProvider.ParseRateLimitsJson(sensitiveErrorJson);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.InvalidResponse, result.Status);
        Assert.DoesNotContain("john.doe@company.corp", result.ErrorMessage);
        Assert.DoesNotContain("/var/secrets/key", result.ErrorMessage);
    }

    [Fact]
    public async Task FetchQuotaAsync_WithInterleavedNotifications_SkipsNotificationsAndClosesStdin()
    {
        var mockRunner = new MockProcessRunner();
        mockRunner.MockStdioOutputLines.Enqueue("""{"jsonrpc":"2.0","method":"window/log","params":{"message":"server ready"}}""");
        mockRunner.MockStdioOutputLines.Enqueue("""{"jsonrpc":"2.0","id":1,"result":{"capabilities":{}}}""");
        mockRunner.MockStdioOutputLines.Enqueue("""{"jsonrpc":"2.0","method":"session/telemetry","params":{}}""");
        mockRunner.MockStdioOutputLines.Enqueue(ValidRateLimitsJson);

        var provider = new ChatGptProvider(mockRunner, codexExecutablePath: "mock_codex.exe");
        ChatGptQuotaResult result = await provider.FetchQuotaAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(ProviderStatus.Success, result.Status);
        Assert.Equal(85.0, result.CodexGroup.FiveHourLimit!.RemainingPercent);
        Assert.Equal(54.5, result.CodexGroup.WeeklyLimit!.RemainingPercent);

        // Doğrulama: stdin kapatıldı mı (graceful exit)
        Assert.True(mockRunner.IsInputClosed);

        // Gönderilen istekler: initialize (id:1), initialized, account/rateLimits/read (id:2)
        Assert.Equal(3, mockRunner.WrittenLines.Count);
        Assert.Contains("\"id\":1", mockRunner.WrittenLines[0]);
        Assert.Contains("initialize", mockRunner.WrittenLines[0]);
        Assert.Contains("initialized", mockRunner.WrittenLines[1]);
        Assert.Contains("\"id\":2", mockRunner.WrittenLines[2]);
        Assert.Contains("account/rateLimits/read", mockRunner.WrittenLines[2]);
    }

    [Fact]
    public async Task FetchQuotaAsync_WhenCliNotFound_ReturnsCliNotFoundStatus()
    {
        var mockRunner = new MockProcessRunner { SimulateCliNotFound = true };
        var provider = new ChatGptProvider(mockRunner, codexExecutablePath: "missing_codex.exe");

        ChatGptQuotaResult result = await provider.FetchQuotaAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.CliNotFound, result.Status);
    }

    [Fact]
    public async Task FetchQuotaAsync_WhenTimeoutOccurs_ReturnsTimeoutStatus()
    {
        var mockRunner = new MockProcessRunner { SimulateTimeout = true };
        var provider = new ChatGptProvider(mockRunner, codexExecutablePath: "mock_codex.exe");

        ChatGptQuotaResult result = await provider.FetchQuotaAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.Timeout, result.Status);
    }
}
