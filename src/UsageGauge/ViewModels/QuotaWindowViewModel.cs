using System;
using System.Windows.Media;
using UsageGauge.Models;
using UsageGauge.Services;

namespace UsageGauge.ViewModels;

public sealed class QuotaWindowViewModel : ViewModelBase
{
    private string _title = string.Empty;
    private double _remainingPercent;
    private string _remainingPercentText = "--%";
    private string _countdownText = "Sıfırlanma: --";
    private Brush _progressBrush = Brushes.SlateGray;
    private DateTimeOffset? _resetsAtUtc;
    private bool _isExpired;
    private bool _hasData;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public double RemainingPercent
    {
        get => _remainingPercent;
        set
        {
            if (SetProperty(ref _remainingPercent, value))
            {
                UpdateProgressBrush();
            }
        }
    }

    public string RemainingPercentText
    {
        get => _remainingPercentText;
        set => SetProperty(ref _remainingPercentText, value);
    }

    public string CountdownText
    {
        get => _countdownText;
        set => SetProperty(ref _countdownText, value);
    }

    public Brush ProgressBrush
    {
        get => _progressBrush;
        private set => SetProperty(ref _progressBrush, value);
    }

    public DateTimeOffset? ResetsAtUtc
    {
        get => _resetsAtUtc;
        set => SetProperty(ref _resetsAtUtc, value);
    }

    public bool IsExpired
    {
        get => _isExpired;
        set => SetProperty(ref _isExpired, value);
    }

    public bool HasData
    {
        get => _hasData;
        set => SetProperty(ref _hasData, value);
    }

    public QuotaWindowViewModel(string title)
    {
        Title = title;
    }

    public void UpdateFromWindow(QuotaWindow? window, bool isStale, DateTimeOffset nowUtc)
    {
        if (window == null)
        {
            HasData = false;
            RemainingPercent = 0;
            RemainingPercentText = "--%";
            CountdownText = "Sıfırlanma: --";
            IsExpired = false;
            ResetsAtUtc = null;
            return;
        }

        HasData = true;
        RemainingPercent = Math.Clamp(window.RemainingPercent, 0.0, 100.0);
        RemainingPercentText = $"%{Math.Round(RemainingPercent):F0}";
        ResetsAtUtc = window.ResetsAtUtc;

        UpdateCountdown(nowUtc, isStale);
    }

    public void UpdateCountdown(DateTimeOffset nowUtc, bool isStale = false)
    {
        if (!ResetsAtUtc.HasValue)
        {
            CountdownText = "Sıfırlanma: --";
            IsExpired = false;
            return;
        }

        TimeSpan diff = ResetsAtUtc.Value - nowUtc;
        if (diff <= TimeSpan.Zero)
        {
            CountdownText = QuotaTimeFormatter.ExpiredText;
            IsExpired = true;
        }
        else
        {
            CountdownText = QuotaTimeFormatter.FormatCountdown(ResetsAtUtc, nowUtc);
            IsExpired = false;
        }

        UpdateProgressBrush(isStale);
    }

    private void UpdateProgressBrush(bool isStale = false)
    {
        if (!HasData)
        {
            ProgressBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)); // Slate 600
            return;
        }

        if (isStale)
        {
            ProgressBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6)); // Amber 600 (Eski Veri)
            return;
        }

        if (_remainingPercent >= 50.0)
        {
            ProgressBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // Green 500
        }
        else if (_remainingPercent >= 20.0)
        {
            ProgressBrush = new SolidColorBrush(Color.FromRgb(234, 179, 8)); // Yellow/Amber 500
        }
        else
        {
            ProgressBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red 500
        }
    }
}
