using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OPTISENS.App.Services;
using OPTISENS.Core.Config;

namespace OPTISENS.App.ViewModels;

public partial class ArenaSettingsViewModel : ViewModelBase
{
    [ObservableProperty] private double _fovDeg = 103.0;
    [ObservableProperty] private string _crosshairStyle = "Cross";
    [ObservableProperty] private string _crosshairColorHex = "#00E5FF";
    [ObservableProperty] private double _crosshairScale = 1.0;
    [ObservableProperty] private double _soundVolume = 0.8;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private IBrush _previewBrush;

    public ArenaSettingsViewModel()
    {
        var settings = AppSettingsStore.Load();
        FovDeg = Math.Clamp(settings.ArenaFovDeg, 90.0, 120.0);
        CrosshairStyle = settings.CrosshairStyle is "Dot" or "CircleDot" or "Circle-Dot" ? settings.CrosshairStyle : "Cross";
        CrosshairColorHex = settings.CrosshairColorHex;
        CrosshairScale = Math.Clamp(settings.CrosshairScale, 0.6, 1.6);
        SoundVolume = Math.Clamp(settings.SoundVolume, 0.0, 1.0);
        IsMuted = settings.SoundMuted;
        _previewBrush = ParseBrush(CrosshairColorHex);
    }

    public string FovReadout => $"{FovDeg:F0}°";
    public string VolumeReadout => $"{SoundVolume * 100:F0}%";
    public string ScaleReadout => $"{CrosshairScale:F1}x";

    public bool IsCrossSelected => CrosshairStyle == "Cross";
    public bool IsDotSelected => CrosshairStyle == "Dot";
    public bool IsCircleDotSelected => CrosshairStyle is "CircleDot" or "Circle-Dot";

    partial void OnFovDegChanged(double value) => OnPropertyChanged(nameof(FovReadout));
    partial void OnSoundVolumeChanged(double value)
    {
        OnPropertyChanged(nameof(VolumeReadout));
        SoundEffectsService.SetVolume(value);
    }

    partial void OnCrosshairScaleChanged(double value) => OnPropertyChanged(nameof(ScaleReadout));

    partial void OnCrosshairColorHexChanged(string value)
    {
        PreviewBrush = ParseBrush(value);
    }

    partial void OnCrosshairStyleChanged(string value)
    {
        OnPropertyChanged(nameof(IsCrossSelected));
        OnPropertyChanged(nameof(IsDotSelected));
        OnPropertyChanged(nameof(IsCircleDotSelected));
    }

    partial void OnIsMutedChanged(bool value) => SoundEffectsService.Muted = value;

    public event Action? SaveAndBackRequested;

    [RelayCommand]
    private void SelectStyle(string style) => CrosshairStyle = style;

    [RelayCommand]
    private void SelectColor(string hex) => CrosshairColorHex = hex;

    [RelayCommand]
    private void TestSound()
    {
        if (IsMuted) return;
        SoundEffectsService.PlayHit(3);
    }

    [RelayCommand]
    private void SaveAndBack()
    {
        try
        {
            var settings = AppSettingsStore.Load();
            AppSettingsStore.Save(settings with
            {
                ArenaFovDeg = FovDeg,
                CrosshairStyle = CrosshairStyle,
                CrosshairColorHex = CrosshairColorHex,
                CrosshairScale = CrosshairScale,
                SoundVolume = SoundVolume,
                SoundMuted = IsMuted
            });
        }
        catch { /* best effort persistence */ }
        SaveAndBackRequested?.Invoke();
    }

    private static IBrush ParseBrush(string? hex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
                return new SolidColorBrush(Color.Parse(hex));
        }
        catch { /* fall through to default */ }
        return new SolidColorBrush(Color.Parse("#00E5FF"));
    }
}
