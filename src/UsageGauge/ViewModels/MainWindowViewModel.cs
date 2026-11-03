using System;
using System.Threading.Tasks;
using System.Windows.Input;
using UsageGauge.Models;
using UsageGauge.Services;

namespace UsageGauge.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly QuotaScheduler _scheduler;
    private readonly Func<DateTimeOffset> _utcNow;
    private string _lastCheckedText = "Son: --:--";
    private bool _isAnyRefreshing;
    private bool _isDisposed;

    public QuotaCardViewModel ChatGptCard { get; }
    public QuotaCardViewModel GeminiCard { get; }

    public string LastCheckedText
    {
        get => _lastCheckedText;
        set => SetProperty(ref _lastCheckedText, value);
    }

    public bool IsAnyRefreshing
    {
        get => _isAnyRefreshing;
        set => SetProperty(ref _isAnyRefreshing, value);
    }

    public QuotaScheduler Scheduler => _scheduler;

    public MainWindowViewModel(QuotaScheduler? scheduler = null, Func<DateTimeOffset>? utcNow = null)
    {
        _scheduler = scheduler ?? new QuotaScheduler();
        _utcNow = utcNow ?? _scheduler.UtcNow;

        ChatGptCard = new QuotaCardViewModel("chatgpt-codex", "ChatGPT (Work/Codex)");
        GeminiCard = new QuotaCardViewModel("antigravity-gemini", "Antigravity (Gemini)");

        _scheduler.ChatGptUpdated += OnChatGptUpdated;
        _scheduler.AntigravityUpdated += OnAntigravityUpdated;
        _scheduler.RefreshStateChanged += OnRefreshStateChanged;
    }

    public async Task StartInitialRefreshAsync()
    {
        ChatGptCard.SetLoading(true);
        GeminiCard.SetLoading(true);

        await _scheduler.RefreshAllAsync(isManual: false);
    }

    public async Task TriggerManualRefreshAsync()
    {
        if (IsAnyRefreshing) return;

        ChatGptCard.SetLoading(true);

        if (!_scheduler.IsAntigravityHalted)
        {
            GeminiCard.SetLoading(true);
            await _scheduler.RefreshAllAsync(isManual: true);
        }
        else
        {
            // Antigravity durdurulmuşsa (ModelTurnDetected) yalnızca ChatGPT yenilenir; Antigravity engelli rozetini korur
            await _scheduler.RefreshChatGptAsync(isManual: true);
        }
    }

    private void OnChatGptUpdated(ChatGptQuotaResult result)
    {
        DateTimeOffset now = _utcNow();
        ChatGptCard.UpdateFromGroup(result.CodexGroup, now);
        UpdateLastCheckedDisplay();
    }

    private void OnAntigravityUpdated(AntigravityQuotaResult result)
    {
        DateTimeOffset now = _utcNow();
        GeminiCard.UpdateFromGroup(result.GeminiGroup, now);
        UpdateLastCheckedDisplay();
    }

    private void OnRefreshStateChanged(bool isRefreshing)
    {
        IsAnyRefreshing = isRefreshing;
    }

    private void UpdateLastCheckedDisplay()
    {
        if (_scheduler.LastSuccessfulCheckUtc.HasValue)
        {
            LastCheckedText = $"Son: {QuotaTimeFormatter.FormatLastChecked(_scheduler.LastSuccessfulCheckUtc)}";
        }
        else
        {
            LastCheckedText = "Son: --:--";
        }
    }

    public void Tick(DateTimeOffset nowUtc)
    {
        _scheduler.Tick(nowUtc);

        ChatGptCard.TickCountdown(nowUtc);
        GeminiCard.TickCountdown(nowUtc);
    }

    public async Task<bool> DisposeAsync(TimeSpan timeout = default)
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _scheduler.ChatGptUpdated -= OnChatGptUpdated;
            _scheduler.AntigravityUpdated -= OnAntigravityUpdated;
            _scheduler.RefreshStateChanged -= OnRefreshStateChanged;
            return await _scheduler.DisposeAsync(timeout);
        }
        return true;
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _scheduler.ChatGptUpdated -= OnChatGptUpdated;
            _scheduler.AntigravityUpdated -= OnAntigravityUpdated;
            _scheduler.RefreshStateChanged -= OnRefreshStateChanged;
            _scheduler.Dispose();
        }
    }
}
