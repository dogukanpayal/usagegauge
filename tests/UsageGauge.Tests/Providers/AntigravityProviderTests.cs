using System;
using System.Threading.Tasks;
using UsageGauge.Models;
using UsageGauge.Providers;
using UsageGauge.Providers.Process;
using UsageGauge.Tests.Mocks;
using Xunit;

namespace UsageGauge.Tests.Providers;

public class AntigravityProviderTests
{
    private const string ValidAntigravityJson = """
    {
      "conversation_id": "",
      "status": "SUCCESS",
      "response": "Gemini Models\tWeekly Limit Remaining\t74%\t2026-10-27T13:41:07Z\nGemini Models\tFive Hour Limit Remaining\t96%\t2026-10-24T18:32:05Z\nClaude and GPT models\tWeekly Limit Remaining\t100%\t2026-10-31T14:04:24Z\nClaude and GPT models\tFive Hour Limit Remaining\t100%\t2026-10-24T19:04:24Z\n",
      "duration_seconds": 0,
      "num_turns": 0,
      "usage": {
        "input_tokens": 0,
        "output_tokens": 0,
        "thinking_tokens": 0,
        "cache_read_tokens": 0,
        "total_tokens": 0
      },
      "command": {
        "name": "usage",
        "data": {
          "description": "Within each group, models share a weekly limit and a 5-hour limit.",
          "groups": [
            {
              "name": "Gemini Models",
              "buckets": [
                {
                  "id": "gemini-weekly",
                  "name": "Weekly Limit Remaining",
                  "window": "weekly",
                  "remaining_fraction": 0.73699951171875,
                  "reset_time": "2026-10-27T13:41:07Z"
                },
                {
                  "id": "gemini-5h",
                  "name": "Five Hour Limit Remaining",
                  "window": "5h",
                  "remaining_fraction": 0.9639928340911865,
                  "reset_time": "2026-10-24T18:32:05Z"
                }
              ]
            },
            {
              "name": "Claude and GPT models",
              "buckets": [
                {
                  "id": "3p-weekly",
                  "name": "Weekly Limit Remaining",
                  "window": "weekly",
                  "remaining_fraction": 1.0,
                  "reset_time": "2026-10-31T14:04:24Z"
                },
                {
                  "id": "3p-5h",
                  "name": "Five Hour Limit Remaining",
                  "window": "5h",
                  "remaining_fraction": 0.85,
                  "reset_time": "2026-10-24T19:04:24Z"
                }
              ]
            }
          ]
        }
      }
    }
    """;

    [Fact]
    public void ParseAntigravityJson_WithValidJson_ExtractsAllFourWindowsAccurately()
    {
        AntigravityQuotaResult result = AntigravityProvider.ParseAntigravityJson(ValidAntigravityJson);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProviderStatus.Success, result.Status);
        Assert.Equal(0, result.NumTurns);
        Assert.Equal(0, result.TotalTokens);

        // Gemini Grubu
        QuotaGroup gemini = result.GeminiGroup;
        Assert.NotNull(gemini.FiveHourLimit);
        Assert.NotNull(gemini.WeeklyLimit);
        Assert.Equal("gemini-5h", gemini.FiveHourLimit.Id);
        Assert.Equal(96.4, gemini.FiveHourLimit.RemainingPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-10-24T18:32:05Z").ToUniversalTime(), gemini.FiveHourLimit.ResetsAtUtc);

        Assert.Equal("gemini-weekly", gemini.WeeklyLimit.Id);
        Assert.Equal(73.7, gemini.WeeklyLimit.RemainingPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-10-27T13:41:07Z").ToUniversalTime(), gemini.WeeklyLimit.ResetsAtUtc);

        // Claude / GPT Grubu
        QuotaGroup tp = result.ThirdPartyGroup;
        Assert.NotNull(tp.FiveHourLimit);
        Assert.NotNull(tp.WeeklyLimit);
        Assert.Equal("3p-5h", tp.FiveHourLimit.Id);
        Assert.Equal(85.0, tp.FiveHourLimit.RemainingPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-10-24T19:04:24Z").ToUniversalTime(), tp.FiveHourLimit.ResetsAtUtc);

        Assert.Equal("3p-weekly", tp.WeeklyLimit.Id);
        Assert.Equal(100.0, tp.WeeklyLimit.RemainingPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-10-31T14:04:24Z").ToUniversalTime(), tp.WeeklyLimit.ResetsAtUtc);
    }

    [Fact]
    public void ParseAntigravityJson_WhenNumTurnsGreaterThanZero_ReturnsModelTurnDetected()
    {
        string jsonWithTurn = ValidAntigravityJson.Replace("\"num_turns\": 0", "\"num_turns\": 1");

        AntigravityQuotaResult result = AntigravityProvider.ParseAntigravityJson(jsonWithTurn);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.ModelTurnDetected, result.Status);
        Assert.Equal(1, result.NumTurns);
        Assert.Contains("Model turu veya token harcaması tespit edildi", result.ErrorMessage);
    }

    [Fact]
    public void ParseAntigravityJson_WhenTotalTokensGreaterThanZero_ReturnsModelTurnDetected()
    {
        string jsonWithTokens = ValidAntigravityJson.Replace("\"total_tokens\": 0", "\"total_tokens\": 42");

        AntigravityQuotaResult result = AntigravityProvider.ParseAntigravityJson(jsonWithTokens);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.ModelTurnDetected, result.Status);
        Assert.Equal(42, result.TotalTokens);
        Assert.Contains("Model turu veya token harcaması tespit edildi", result.ErrorMessage);
    }

    [Fact]
    public void ParseAntigravityJson_WhenNumTurnsMissing_ReturnsInvalidResponse()
    {
        string jsonMissingTurns = """
        {
          "status": "SUCCESS",
          "total_tokens": 0,
          "command": { "data": { "groups": [] } }
        }
        """;

        AntigravityQuotaResult result = AntigravityProvider.ParseAntigravityJson(jsonMissingTurns);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.InvalidResponse, result.Status);
        Assert.Contains("num_turns", result.ErrorMessage);
    }

    [Fact]
    public void ParseAntigravityJson_WhenTotalTokensMissing_ReturnsInvalidResponse()
    {
        string jsonMissingTokens = """
        {
          "status": "SUCCESS",
          "num_turns": 0,
          "command": { "data": { "groups": [] } }
        }
        """;

        AntigravityQuotaResult result = AntigravityProvider.ParseAntigravityJson(jsonMissingTokens);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.InvalidResponse, result.Status);
        Assert.Contains("total_tokens", result.ErrorMessage);
    }

    [Fact]
    public void ParseAntigravityJson_WhenNumTurnsNegative_ReturnsInvalidResponse()
    {
        string jsonNegativeTurn = ValidAntigravityJson.Replace("\"num_turns\": 0", "\"num_turns\": -1");

        AntigravityQuotaResult result = AntigravityProvider.ParseAntigravityJson(jsonNegativeTurn);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.InvalidResponse, result.Status);
    }

    [Fact]
    public void ParseAntigravityJson_WhenTotalTokensNegative_ReturnsInvalidResponse()
    {
        string jsonNegativeTokens = ValidAntigravityJson.Replace("\"total_tokens\": 0", "\"total_tokens\": -10");

        AntigravityQuotaResult result = AntigravityProvider.ParseAntigravityJson(jsonNegativeTokens);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.InvalidResponse, result.Status);
    }

    [Fact]
    public void ParseAntigravityJson_WhenResetTimeHasNoTimezoneOffset_RejectsTimestamp()
    {
        // "2026-10-24T18:32:05" içinde 'Z' veya '+HH:mm' ofseti yok
        string jsonNoTz = ValidAntigravityJson.Replace("2026-10-24T18:32:05Z", "2026-10-24T18:32:05");

        AntigravityQuotaResult result = AntigravityProvider.ParseAntigravityJson(jsonNoTz);

        // Gemini havuzunda geçersiz saat dilimi hatası oluşmalı, 3p ise başarılı kalmalı (PartialSuccess)
        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.PartialSuccess, result.Status);
        Assert.False(result.GeminiGroup.IsSuccess);
        Assert.True(result.ThirdPartyGroup.IsSuccess);
    }

    [Fact]
    public void ParseAntigravityJson_WhenResetTimeHasPositiveOffset_ParsesToCorrectUtc()
    {
        // 21:32:05+03:00 -> UTC 18:32:05Z olmalıdır
        string jsonWithOffset = ValidAntigravityJson.Replace("2026-10-24T18:32:05Z", "2026-10-24T21:32:05+03:00");

        AntigravityQuotaResult result = AntigravityProvider.ParseAntigravityJson(jsonWithOffset);

        Assert.True(result.IsSuccess);
        Assert.Equal(DateTimeOffset.Parse("2026-10-24T18:32:05Z").ToUniversalTime(), result.GeminiGroup.FiveHourLimit!.ResetsAtUtc);
    }

    [Fact]
    public void ParseAntigravityJson_When3pGroupFails_GeminiGroupPreservedAsPartialSuccess()
    {
        // 3p-weekly bucket'ı eksik olan JSON
        string missing3pWeeklyJson = """
        {
          "status": "SUCCESS",
          "num_turns": 0,
          "total_tokens": 0,
          "command": {
            "name": "usage",
            "data": {
              "groups": [
                {
                  "name": "Gemini Models",
                  "buckets": [
                    { "id": "gemini-weekly", "remaining_fraction": 0.8, "reset_time": "2026-10-27T13:41:07Z" },
                    { "id": "gemini-5h", "remaining_fraction": 0.9, "reset_time": "2026-10-24T18:32:05Z" }
                  ]
                },
                {
                  "name": "Claude and GPT models",
                  "buckets": [
                    { "id": "3p-5h", "remaining_fraction": 1.0, "reset_time": "2026-10-24T19:04:24Z" }
                  ]
                }
              ]
            }
          }
        }
        """;

        AntigravityQuotaResult result = AntigravityProvider.ParseAntigravityJson(missing3pWeeklyJson);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.PartialSuccess, result.Status);
        Assert.True(result.GeminiGroup.IsSuccess);
        Assert.False(result.ThirdPartyGroup.IsSuccess);
        Assert.Equal(90.0, result.GeminiGroup.FiveHourLimit!.RemainingPercent);
    }

    [Fact]
    public void ParseAntigravityJson_WhenCorruptedJsonTypesEncountered_ReturnsInvalidResponseWithoutThrowing()
    {
        string corruptedJson = """
        {
          "status": "SUCCESS",
          "num_turns": "not-a-number",
          "command": []
        }
        """;

        AntigravityQuotaResult result = AntigravityProvider.ParseAntigravityJson(corruptedJson);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.InvalidResponse, result.Status);
    }

    [Fact]
    public void ParseAntigravityJson_WhenStatusNotSuccess_ReturnsInvalidResponse()
    {
        string errorStatusJson = """
        {
          "status": "ERROR",
          "error": "Failed to fetch usage from backend service."
        }
        """;

        AntigravityQuotaResult result = AntigravityProvider.ParseAntigravityJson(errorStatusJson);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.InvalidResponse, result.Status);
    }

    [Fact]
    public async Task FetchQuotaAsync_WhenCliNotFound_ReturnsCliNotFoundStatus()
    {
        var mockRunner = new MockProcessRunner { SimulateCliNotFound = true };
        var provider = new AntigravityProvider(mockRunner, agyExecutablePath: "missing_agy.exe");

        AntigravityQuotaResult result = await provider.FetchQuotaAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.CliNotFound, result.Status);
    }

    [Fact]
    public async Task FetchQuotaAsync_WhenTimeoutOccurs_ReturnsTimeoutStatus()
    {
        var mockRunner = new MockProcessRunner { SimulateTimeout = true };
        var provider = new AntigravityProvider(mockRunner, agyExecutablePath: "mock_agy.exe");

        AntigravityQuotaResult result = await provider.FetchQuotaAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.Timeout, result.Status);
    }

    [Fact]
    public async Task FetchQuotaAsync_WhenExitCodeNonZero_SanitizesErrorMessage()
    {
        var mockRunner = new MockProcessRunner
        {
            ProcessRunHandler = (_, _, _) => new ProcessRunResult(1, string.Empty, "SensitiveTokenSecret-12345 /home/user/path")
        };
        var provider = new AntigravityProvider(mockRunner, agyExecutablePath: "mock_agy.exe");

        AntigravityQuotaResult result = await provider.FetchQuotaAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderStatus.ProcessError, result.Status);
        Assert.DoesNotContain("SensitiveTokenSecret-12345", result.ErrorMessage);
        Assert.DoesNotContain("/home/user/path", result.ErrorMessage);
    }
}
