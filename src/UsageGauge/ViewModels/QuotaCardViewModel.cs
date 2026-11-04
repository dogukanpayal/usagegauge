using System;
using System.Windows.Media;
using UsageGauge.Models;
using UsageGauge.Services;

namespace UsageGauge.ViewModels;

public sealed class QuotaCardViewModel : ViewModelBase
{
    private string _groupId;
    private string _title;
    private string _statusText = "Bekleniyor...";
    private Brush _statusBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184)); // Slate 400
    private Brush _statusBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 148, 163, 184));
    private bool _isLoading = true;
    private bool _isStale;
    private string? _errorMessage;

    public string GroupId
    {
        get => _groupId;
        set => SetProperty(ref _groupId, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (SetProperty(ref _statusText, value))
            {
                OnPropertyChanged(nameof(StatusTooltipText));
            }
        }
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        set => SetProperty(ref _statusBrush, value);
    }

    public Brush StatusBadgeBackground
    {
        get => _statusBadgeBackground;
        set => SetProperty(ref _statusBadgeBackground, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool IsStale
    {
        get => _isStale;
        set => SetProperty(ref _isStale, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(StatusTooltipText));
            }
        }
    }

    public string StatusTooltipText => string.IsNullOrWhiteSpace(ErrorMessage)
        ? $"Durum: {StatusText}"
        : $"Durum: {StatusText}\nDetay: {ErrorMessage}";

    public QuotaWindowViewModel FiveHourWindow { get; }
    public QuotaWindowViewModel WeeklyWindow { get; }

    public QuotaCardViewModel(string groupId, string title)
    {
        _groupId = groupId;
        _title = title;
        FiveHourWindow = new QuotaWindowViewModel("5 Saatlik");
        WeeklyWindow = new QuotaWindowViewModel("Haftalık");
    }

    public void SetLoading(bool loading)
    {
        IsLoading = loading;
        if (loading)
        {
            StatusText = "⟳ Yenileniyor...";
            StatusBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)); // Sky 400
            StatusBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 56, 189, 248));
        }
    }

    public void UpdateFromGroup(QuotaGroup? group, DateTimeOffset nowUtc)
    {
        IsLoading = false;

        if (group == null)
        {
            StatusText = "⚠ Veri Yok";
            StatusBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            StatusBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 148, 163, 184));
            FiveHourWindow.UpdateFromWindow(null, false, nowUtc);
            WeeklyWindow.UpdateFromWindow(null, false, nowUtc);
            return;
        }

        IsStale = group.IsStale;
        ErrorMessage = group.ErrorMessage;

        // Pencereleri güncelle
        FiveHourWindow.UpdateFromWindow(group.FiveHourLimit, group.IsStale, nowUtc);
        WeeklyWindow.UpdateFromWindow(group.WeeklyLimit, group.IsStale, nowUtc);

        // Durum rozeti metni ve stilini ayarla
        ApplyStatusStyle(group.Status, group.IsStale, group.FetchedAtUtc);
    }

    private void ApplyStatusStyle(ProviderStatus status, bool isStale, DateTimeOffset fetchedAtUtc)
    {
        if (isStale)
        {
            string timeStr = QuotaTimeFormatter.FormatLastChecked(fetchedAtUtc);
            StatusText = $"⚠ Eski Veri ({timeStr})";
            StatusBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber 500
            StatusBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 245, 158, 11));
            return;
        }

        switch (status)
        {
            case ProviderStatus.Success:
                StatusText = "✓ Güncel";
                StatusBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // Green 500
                StatusBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 34, 197, 94));
                break;

            case ProviderStatus.AuthenticationRequired:
                StatusText = "🔑 Oturum Gerekli";
                StatusBrush = new SolidColorBrush(Color.FromRgb(249, 115, 22)); // Orange 500
                StatusBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 249, 115, 22));
                break;

            case ProviderStatus.CliNotFound:
                StatusText = "✕ CLI Bulunamadı";
                StatusBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red 500
                StatusBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 239, 68, 68));
                break;

            case ProviderStatus.ModelTurnDetected:
                StatusText = "⛔ Model Turu Engellendi";
                StatusBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red 500
                StatusBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 239, 68, 68));
                break;

            case ProviderStatus.Timeout:
                StatusText = "⏱ Zaman Aşımı";
                StatusBrush = new SolidColorBrush(Color.FromRgb(234, 179, 8)); // Yellow 500
                StatusBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 234, 179, 8));
                break;

            case ProviderStatus.PartialSuccess:
                StatusText = "⚡ Kısmi Veri";
                StatusBrush = new SolidColorBrush(Color.FromRgb(234, 179, 8));
                StatusBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 234, 179, 8));
                break;

            default:
                StatusText = "⚠ Okuma Hatası";
                StatusBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                StatusBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 239, 68, 68));
                break;
        }
    }

    public void TickCountdown(DateTimeOffset nowUtc)
    {
        FiveHourWindow.UpdateCountdown(nowUtc, _isStale);
        WeeklyWindow.UpdateCountdown(nowUtc, _isStale);
    }
}
